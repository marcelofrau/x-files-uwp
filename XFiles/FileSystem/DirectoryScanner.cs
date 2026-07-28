using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

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

        public static async Task<List<FileEntry>> ScanAsync(string path, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(path))
                return ScanRoot();

            return await ScanDirectoryAsync(path, token);
        }

        private static List<FileEntry> ScanRoot()
        {
            Log.Verb("Scanning root — enumerating logical drives via GetLogicalDrives");
            var entries = new List<FileEntry>();

            uint drives = GetLogicalDrives();
            for (int i = 0; i < 26; i++)
            {
                if ((drives & (1 << i)) != 0)
                {
                    string driveLetter = $"{(char)('A' + i)}:\\";
                    entries.Add(new FileEntry
                    {
                        Name = driveLetter,
                        FullPath = driveLetter,
                        IsDirectory = true,
                        IsDrive = true
                    });
                    Log.Verb("  Drive found: {Drive}", driveLetter);
                }
            }

            try
            {
                string localPath = ApplicationData.Current.LocalFolder.Path;
                entries.Insert(0, new FileEntry
                {
                    Name = "[App Data]",
                    FullPath = localPath,
                    IsDirectory = true
                });
                Log.Verb("  [App Data] entry added: {Path}", localPath);
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
                            IsArchive = !isDir && ArchiveExtensions.Contains(Path.GetExtension(name))
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

                entries.AddRange(dirs);
                entries.AddRange(files);
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
