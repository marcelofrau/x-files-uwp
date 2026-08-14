using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using XFiles.Audio;
using XFiles.Settings;

namespace XFiles.FileSystem
{
    public static class DirectoryScanner
    {
        private static readonly HashSet<string> ArchiveExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".zip", ".7z", ".rar"
            };

        #region P/Invoke

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WIN32_FIND_DATA
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

        private const uint FIND_FIRST_EX_LARGE_FETCH = 0x00000002;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
        private const uint FILE_ATTRIBUTE_SYSTEM = 0x04;
        private const int INVALID_HANDLE_VALUE = -1;

        public enum FINDEX_INFO_LEVELS { FindExInfoStandard = 0 }
        public enum FINDEX_SEARCH_OPS { FindExSearchNameMatch = 0 }

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

        [DllImport("kernel32.dll")]
        private static extern uint GetLogicalDrives();

        [DllImport("api-ms-win-core-file-fromapp-l1-1-0.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile2FromAppW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            uint dwCreationDisposition,
            IntPtr lpSecurityAttributes);

        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint OPEN_EXISTING = 3;

        #endregion

        public static bool FileExists(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            IntPtr hFind = FindFirstFileExFromAppW(path, FINDEX_INFO_LEVELS.FindExInfoStandard,
                out _, FINDEX_SEARCH_OPS.FindExSearchNameMatch, IntPtr.Zero, 0);
            if (hFind == new IntPtr(INVALID_HANDLE_VALUE)) return false;
            FindClose(hFind);
            return true;
        }

        /// <summary>
        /// Open a file for reading via P/Invoke (bypasses UWP sandbox).
        /// Returns null if file cannot be opened. Caller owns the stream.
        /// </summary>
        public static Stream OpenFileRead(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            IntPtr hFile = CreateFile2FromAppW(path, GENERIC_READ, FILE_SHARE_READ,
                OPEN_EXISTING, IntPtr.Zero);
            if (hFile == new IntPtr(INVALID_HANDLE_VALUE)) return null;
            var handle = new Microsoft.Win32.SafeHandles.SafeFileHandle(hFile, ownsHandle: true);
            return new FileStream(handle, FileAccess.Read, bufferSize: 4096, isAsync: false);
        }

        /// <summary>
        /// Diagnostic: enumerate immediate child names of a directory via P/Invoke.
        /// Returns empty list on failure; check win32Error for the reason.
        /// "." and ".." are excluded.
        /// </summary>
        public static List<string> EnumerateDirectoryNames(string path, out int win32Error)
        {
            win32Error = 0;
            var names = new List<string>();
            if (string.IsNullOrEmpty(path)) { win32Error = 87; return names; } // ERROR_INVALID_PARAMETER

            string pattern = path.EndsWith("\\") || path.EndsWith("/")
                ? path + "*"
                : path + "\\*";

            IntPtr hFind = FindFirstFileExFromAppW(pattern, FINDEX_INFO_LEVELS.FindExInfoStandard,
                out WIN32_FIND_DATA findData, FINDEX_SEARCH_OPS.FindExSearchNameMatch,
                IntPtr.Zero, FIND_FIRST_EX_LARGE_FETCH);

            if (hFind == new IntPtr(INVALID_HANDLE_VALUE))
            {
                win32Error = Marshal.GetLastWin32Error();
                return names;
            }

            try
            {
                do
                {
                    string name = findData.cFileName;
                    if (name == "." || name == "..") continue;
                    names.Add(name);
                }
                while (FindNextFileW(hFind, out findData));
            }
            finally
            {
                FindClose(hFind);
            }

            return names;
        }

        /// <summary>
        /// Diagnostic: try to open a file for read via P/Invoke.
        /// Returns 0 on success, or the Win32 error code.
        /// </summary>
        public static int TestFileReadable(string path)
        {
            if (string.IsNullOrEmpty(path)) return 87; // ERROR_INVALID_PARAMETER
            IntPtr hFile = CreateFile2FromAppW(path, GENERIC_READ, FILE_SHARE_READ,
                OPEN_EXISTING, IntPtr.Zero);
            if (hFile == new IntPtr(INVALID_HANDLE_VALUE))
                return Marshal.GetLastWin32Error();
            var handle = new Microsoft.Win32.SafeHandles.SafeFileHandle(hFile, ownsHandle: true);
            handle.Dispose();
            return 0;
        }

        public static async Task<List<FileEntry>> ScanAsync(string path, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(path))
                return ScanRoot();

            return await ScanDirectoryAsync(path, token);
        }

        /// <summary>
        /// Enumerates local drive letters only - no AppData, favorites or portal
        /// entries. Used by the move destination dialog.
        /// </summary>
        /// <summary>
        /// Xbox Dev Mode exposes system XVDs under fixed drive letters (S: =
        /// Settings.xvd, Q: = user home, etc.). GetLogicalDrives may not return
        /// all of them to a UWP app container, so probe the known set explicitly.
        /// </summary>
        private static readonly string[] KnownXboxDrives =
        {
            "C", "D", "E", "G", "J", "L", "M", "N", "O", "Q", "S", "T", "U", "V", "X", "Y"
        };

        private static readonly object DriveCacheLock = new object();
        private static readonly Dictionary<string, bool> DriveAccessCache = new Dictionary<string, bool>();
        private static int _probeWarmStarted;

        public static List<FileEntry> ScanDrivesOnly()
        {
            Log.Verb("Scanning drives - enumerating logical drives via GetLogicalDrives");

            uint drives = GetLogicalDrives();
            var present = new List<string>();
            for (int i = 0; i < 26; i++)
            {
                if ((drives & (1 << i)) != 0)
                    present.Add(((char)('A' + i)).ToString());
            }

            Log.Info("Drive scan: GetLogicalDrives returned [{0}]", string.Join(",", present));

            bool hideInaccessible = XFilesSettings.HideEmptyDrivesCached;
            var visible = hideInaccessible
                ? FilterAccessible(present)
                : present;

            // Root scans run on the UI thread and a denied probe costs ~210ms on
            // Xbox, so never probe here. If the background warm hasn't landed
            // yet, show everything this pass and let the next scan apply the
            // filter once the cache fills.
            if (hideInaccessible && visible.Count == 0 && present.Count > 0)
                visible = present;

            WarmDriveProbesAsync();

            var entries = new List<FileEntry>();
            foreach (string letter in visible)
            {
                string driveLetter = letter + ":\\";
                entries.Add(new FileEntry
                {
                    Name = driveLetter,
                    FullPath = driveLetter,
                    IsDirectory = true,
                    IsDrive = true
                });
                Log.Verb("  Drive found: {Drive}", driveLetter);
            }

            return entries;
        }

        /// <summary>
        /// Probes drive accessibility on a background thread (never the UI
        /// thread) and caches the results for the session. Idempotent — at most
        /// one warm runs. Kicked off from App.OnLaunched and from root scans.
        /// Covers the drives GetLogicalDrives returned plus the known Xbox
        /// system set it may have missed (XBVault-style hardcoded list).
        /// </summary>
        public static void WarmDriveProbesAsync()
        {
            if (Interlocked.Exchange(ref _probeWarmStarted, 1) != 0)
                return;

            Task.Run(() =>
            {
                try
                {
                    var letters = new List<string>(KnownXboxDrives);
                    uint drives = GetLogicalDrives();
                    for (int i = 0; i < 26; i++)
                    {
                        if ((drives & (1 << i)) != 0)
                            letters.Add(((char)('A' + i)).ToString());
                    }

                    ProbeDrivesConcurrent(letters.Distinct());
                }
                catch (Exception ex)
                {
                    Log.Warn("DirectoryScanner: drive probe warm failed", ex);
                }
            });
        }

        /// <summary>
        /// Returns the subset of drive letters that a FindFirstFileExFromAppW
        /// call can enumerate (i.e. non-empty, non-access-denied).
        /// </summary>
        private static List<string> FilterAccessible(List<string> letters)
        {
            lock (DriveCacheLock)
                return letters.Where(l => DriveAccessCache.TryGetValue(l, out bool ok) && ok).ToList();
        }

        /// <summary>
        /// Probe one drive letter with FindFirstFileExFromAppW. Returns a short
        /// human-readable summary (OK + first entry, or the Win32 error code).
        /// </summary>
        private static string ProbeDriveFindFirst(string driveLetter)
        {
            string pattern = driveLetter + ":\\*";
            IntPtr hFind = FindFirstFileExFromAppW(pattern, FINDEX_INFO_LEVELS.FindExInfoStandard,
                out WIN32_FIND_DATA findData, FINDEX_SEARCH_OPS.FindExSearchNameMatch,
                IntPtr.Zero, FIND_FIRST_EX_LARGE_FETCH);
            if (hFind == new IntPtr(INVALID_HANDLE_VALUE))
            {
                int err = Marshal.GetLastWin32Error();
                return $"FindFirstFileExFromAppW failed (error {err})";
            }
            string first = findData.cFileName;
            FindClose(hFind);
            return $"FindFirstFileExFromAppW OK, first entry '{first}'";
        }

        private static bool DriveReadableFromResult(string result)
            => !result.StartsWith("FindFirstFileExFromAppW failed", StringComparison.Ordinal);

        /// <summary>
        /// Probe uncached drive letters (in parallel — a denied probe costs
        /// ~210ms on Xbox, so serializing 16 of them makes the root scan ~3.4s
        /// slow). Results and the per-drive diagnostic lines are logged once per
        /// session and cached; a drive plugged mid-session appears in
        /// GetLogicalDrives with an empty cache slot, so it is probed then.
        /// </summary>
        private static void ProbeDrivesConcurrent(IEnumerable<string> letters)
        {
            var candidates = new List<string>();
            lock (DriveCacheLock)
            {
                foreach (string letter in letters)
                    if (!DriveAccessCache.ContainsKey(letter))
                        candidates.Add(letter);
            }

            if (candidates.Count == 0)
                return;

            var results = new ConcurrentDictionary<string, bool>();
            Parallel.ForEach(candidates, letter =>
            {
                string result = ProbeDriveFindFirst(letter);
                results[letter] = DriveReadableFromResult(result);
                Log.Info("Drive probe {0}:\\: {1}", letter, result);
            });

            lock (DriveCacheLock)
                foreach (var kv in results)
                    DriveAccessCache[kv.Key] = kv.Value;
        }

        private static List<FileEntry> ScanRoot()
        {
            var entries = ScanDrivesOnly();

            try
            {
                string localPath = ApplicationData.Current.LocalFolder.Path;
                entries.Insert(0, new FileEntry
                {
                    Name = "AppData",
                    FullPath = localPath,
                    IsDirectory = true
                });
                Log.Verb("  AppData entry added: {Path}", localPath);
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to get LocalFolder — skipping [App Data] entry", ex);
            }

            Log.Info("Root scan complete — {Count} entries total", entries.Count);
            return entries;
        }

        private static async Task<List<FileEntry>> ScanDirectoryAsync(string path, CancellationToken token)
        {
            Log.Verb("Scanning directory: {Path}", path);
            var entries = new List<FileEntry>();

            string parent = Directory.GetParent(path)?.FullName;
            entries.Add(new FileEntry { Name = "..", FullPath = parent, IsDirectory = true });

            await Task.Run(() =>
            {
                string searchPath = Path.Combine(path, "*");
                IntPtr hFind = FindFirstFileExFromAppW(
                    searchPath,
                    FINDEX_INFO_LEVELS.FindExInfoStandard,
                    out WIN32_FIND_DATA findData,
                    FINDEX_SEARCH_OPS.FindExSearchNameMatch,
                    IntPtr.Zero,
                    FIND_FIRST_EX_LARGE_FETCH);

                if (hFind == new IntPtr(INVALID_HANDLE_VALUE))
                {
                    int err = Marshal.GetLastWin32Error();
                    Log.Warn("FindFirstFileExFromAppW failed for '{Path}' (error {Error}) — '..' entry only", path, err);
                    return;
                }

                var dirs = new List<FileEntry>();
                var files = new List<FileEntry>();

                try
                {
                    do
                    {
                        token.ThrowIfCancellationRequested();

                        string name = findData.cFileName;
                        if (name == "." || name == "..") continue;

                        bool isDir = (findData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
                        long size = isDir ? 0 : ((long)findData.nFileSizeHigh << 32) | findData.nFileSizeLow;
                        string fullPath = Path.Combine(path, name);

                        var entry = new FileEntry
                        {
                            Name = name,
                            FullPath = fullPath,
                            IsDirectory = isDir,
                            SizeBytes = size,
                            IsArchive = !isDir && ArchiveExtensions.Contains(Path.GetExtension(name)),
                            IsChiptune = !isDir && Audio.RetroAudioPlayer.IsChiptuneFile(name)
                        };

                        if (isDir) dirs.Add(entry);
                        else files.Add(entry);
                    }
                    while (FindNextFileW(hFind, out findData));
                }
                finally
                {
                    FindClose(hFind);
                }

                // Deterministic order: folders first, then files, each alphabetical.
                // Never clear "entries" here — it already carries the ".." parent entry
                // at index 0 that callers (FolderBrowserDialog, ColumnNavigator) rely on.
                DirectoryEntryOrder.AppendSorted(entries, dirs, files);
            });

            Log.Verb("Scan '{Path}' complete — {Total} entries", path, entries.Count);
            return entries;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }
}
