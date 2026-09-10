using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Threading.Tasks;

namespace XFiles.FileSystem
{
    /// <summary>
    /// Snapshot of a directory-tree statistics scan. Values are cumulative sums of
    /// every file/folder visited so far. Use it for the Properties dialog: an
    /// incomplete snapshot means the scan hit its cap or was cancelled, so the UI
    /// should render the values with a "partial" marker.
    /// </summary>
    public struct DirectoryStatsSnapshot
    {
        public long FileCount;
        public long FolderCount;
        public long TotalBytes;
        public bool Complete;
        public bool Cancelled;

        /// <summary>True when the scan ended before exhausting the tree (cap or cancel).</summary>
        public bool IsPartial => !Complete;
    }

    /// <summary>
    /// Recursive directory size/file/folder counters that can report partial
    /// snapshots while walking. Runs on a background thread; the caller provides
    /// an IProgress sink (marshals to the UI thread automatically) for the
    /// incremental/placeholder UX. Pure + P/Invoke only — linkable into the
    /// desktop test project, where the *FromApp variants work against real temp
    /// directories.
    /// </summary>
    public static class DirectoryStatsCalculator
    {
        private const long ReportEveryEntries = 256;
        private const uint FIND_FIRST_EX_LARGE_FETCH = 0x00000002;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
        private const int INVALID_HANDLE_VALUE = -1;

        public enum FINDEX_INFO_LEVELS { FindExInfoStandard = 0 }
        public enum FINDEX_SEARCH_OPS { FindExSearchNameMatch = 0 }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct WIN32_FIND_DATA
        {
            public uint dwFileAttributes;
            public FILETIME ftCreationTime;
            public FILETIME ftLastAccessTime;
            public FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }

        [DllImport("api-ms-win-core-file-fromapp-l1-1-0.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindFirstFileExFromAppW(
            string lpFileName,
            FINDEX_INFO_LEVELS fInfoLevelId,
            out WIN32_FIND_DATA lpFindFileData,
            FINDEX_SEARCH_OPS fSearchOp,
            IntPtr lpSearchFilter,
            uint dwAdditionalFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool FindNextFileW(IntPtr hFindFile, out WIN32_FIND_DATA lpFindFileData);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FindClose(IntPtr hFindFile);

        /// <summary>
        /// Recursively scans <paramref name="path"/> summing file sizes and counts.
        /// Runs on a background thread (never blocks the UI), reports partial
        /// snapshots through <paramref name="reporter"/> every ~256 entries, honors
        /// <paramref name="token"/> cancellation between entries and stops after
        /// <paramref name="maxEntries"/> (Complete=false when capped).
        /// </summary>
        public static async Task<DirectoryStatsSnapshot> ComputeAsync(
            string path,
            IProgress<DirectoryStatsSnapshot> reporter = null,
            CancellationToken token = default,
            long maxEntries = long.MaxValue)
        {
            if (string.IsNullOrEmpty(path))
            {
                return new DirectoryStatsSnapshot { Complete = true };
            }

            var snapshot = new DirectoryStatsSnapshot();
            var ctx = new ScanContext { Reporter = reporter, Token = token, MaxEntries = maxEntries };
            await Task.Run(() =>
            {
                try
                {
                    WalkDirectory(path, ref snapshot, ref ctx);
                    snapshot.Complete = !snapshot.Cancelled && !ctx.Capped;
                }
                catch (OperationCanceledException)
                {
                    snapshot.Cancelled = true;
                }
            });
            try { reporter?.Report(snapshot); } catch { }
            return snapshot;
        }

        private struct ScanContext
        {
            public IProgress<DirectoryStatsSnapshot> Reporter;
            public CancellationToken Token;
            public long MaxEntries;
            public long EntriesScanned;
            public long PendingSinceReport;
            public bool Capped;
        }

        private static void WalkDirectory(string dir, ref DirectoryStatsSnapshot stats, ref ScanContext ctx)
        {
            ctx.Token.ThrowIfCancellationRequested();

            IntPtr hFind = FindFirstFileExFromAppW(
                dir + "\\*",
                FINDEX_INFO_LEVELS.FindExInfoStandard,
                out WIN32_FIND_DATA findData,
                FINDEX_SEARCH_OPS.FindExSearchNameMatch,
                IntPtr.Zero,
                FIND_FIRST_EX_LARGE_FETCH);

            if (hFind == new IntPtr(INVALID_HANDLE_VALUE))
            {
                return;
            }

            try
            {
                bool first = true;
                do
                {
                    if (!first)
                    {
                        ctx.Token.ThrowIfCancellationRequested();
                        if (ctx.MaxEntries != long.MaxValue && ctx.EntriesScanned >= ctx.MaxEntries)
                        {
                            ctx.Capped = true;
                            return;
                        }
                    }
                    first = false;

                    string name = findData.cFileName;
                    if (name == "." || name == "..") continue;

                    ctx.EntriesScanned++;
                    ctx.PendingSinceReport++;

                    bool isDir = (findData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
                    if (isDir)
                    {
                        stats.FolderCount++;
                    }
                    else
                    {
                        stats.FileCount++;
                        stats.TotalBytes += (((long)findData.nFileSizeHigh) << 32) | findData.nFileSizeLow;
                    }

                    if (ctx.PendingSinceReport >= ReportEveryEntries)
                    {
                        ctx.PendingSinceReport = 0;
                        ReportPartial(stats, ref ctx);
                    }

                    if (isDir)
                    {
                        WalkDirectory(dir + "\\" + name, ref stats, ref ctx);
                        if (ctx.Capped || stats.Cancelled) return;
                    }
                }
                while (FindNextFileW(hFind, out findData));
            }
            finally
            {
                FindClose(hFind);
            }
        }

        private static void ReportPartial(DirectoryStatsSnapshot stats, ref ScanContext ctx)
        {
            try { ctx.Reporter?.Report(stats); } catch { }
        }
    }
}