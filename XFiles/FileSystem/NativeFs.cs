using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
#if WINDOWS_UWP
using Windows.Storage;
#endif

namespace XFiles.FileSystem
{
    /// <summary>
    /// Which Win32 access path works for a given location.
    ///
    /// On Xbox the *FromApp* APIs enforce the UWP app-container security model:
    /// a full-trust app still gets ERROR_ACCESS_DENIED (error 5) on volumes the
    /// OS hides from the container (field report from an Xbox Series X: drive
    /// E: denied for both *FromApp and Windows.Storage, while the same files
    /// were readable from other UWP apps). The plain kernel32 variants use the
    /// process token instead of the app-container identity, so they can succeed
    /// where *FromApp fails.
    ///
    /// Every filesystem access goes through this class, which tries the backends
    /// in order and remembers which one worked for each volume root.
    /// </summary>
    internal enum FsBackend
    {
        /// <summary>Not probed yet.</summary>
        Unknown = 0,

        /// <summary>api-ms-win-core-file-fromapp-l1-1-0.dll (*FromAppW) - app-container aware.</summary>
        FromApp = 1,

        /// <summary>kernel32.dll plain variant - uses the full-trust process token.</summary>
        Plain = 2,

        /// <summary>Windows.Storage (broadFileSystemAccess) - last resort.</summary>
        WinRt = 3
    }

    /// <summary>
    /// One entry produced by a directory enumeration.
    /// </summary>
    internal struct FsEntry
    {
        public string Name;
        public bool IsDirectory;
        public long Size;
        public DateTime LastWriteTimeUtc;
    }

    /// <summary>
    /// Central filesystem access layer with per-volume backend fallback.
    ///
    /// Backend order per path: the backend that last worked for the volume
    /// root, then *FromApp, then plain Win32, then Windows.Storage. The winner
    /// is cached per root ("E:\", "\\server\share") so the whole app uses a
    /// consistent API for a given volume.
    /// </summary>
    internal static class NativeFs
    {
        #region P/Invoke

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WIN32_FIND_DATA
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }

        private enum FINDEX_INFO_LEVELS { FindExInfoStandard = 0 }
        private enum FINDEX_SEARCH_OPS { FindExSearchNameMatch = 0 }

        [DllImport("api-ms-win-core-file-fromapp-l1-1-0.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindFirstFileExFromAppW(
            string lpFileName,
            FINDEX_INFO_LEVELS fInfoLevelId,
            out WIN32_FIND_DATA lpFindFileData,
            FINDEX_SEARCH_OPS fSearchOp,
            IntPtr lpSearchFilter,
            uint dwAdditionalFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindFirstFileExW(
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

        [DllImport("api-ms-win-core-file-fromapp-l1-1-0.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileFromAppW(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition,
            uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileW(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition,
            uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        private const uint FIND_FIRST_EX_LARGE_FETCH = 0x00000002;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        private const uint FILE_FLAG_SEQUENTIAL_SCAN = 0x08000000;
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint FILE_SHARE_DELETE = 0x00000004;
        private const uint OPEN_EXISTING = 3;
        private const uint CREATE_ALWAYS = 2;

        private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

        #endregion

        /// <summary>
        /// Winning backend per volume root ("E:\", "\\server\share").
        /// </summary>
        private static readonly ConcurrentDictionary<string, FsBackend> _backends =
            new ConcurrentDictionary<string, FsBackend>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Volume root key for a path, used to cache the working backend.
        /// Returns empty string for paths without a volume root (relative paths,
        /// sandbox paths) - those always probe from scratch.
        /// </summary>
        public static string RootKey(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;

            // "E:\foo\bar" -> "E:\"
            if (path.Length >= 2 && path[1] == ':')
            {
                string drive = path.Substring(0, 2).ToUpperInvariant();
                return drive + "\\";
            }

            // UNC "\\server\share\dir" -> "\\server\share"
            if (path.StartsWith(@"\\", StringComparison.Ordinal))
            {
                int idx = path.IndexOf('\\', 2);
                if (idx > 0)
                {
                    int idx2 = path.IndexOf('\\', idx + 1);
                    if (idx2 > 0) return path.Substring(0, idx2).ToUpperInvariant();
                    return path.ToUpperInvariant();
                }
            }

            return string.Empty;
        }

        public static FsBackend PreferredBackend(string path)
        {
            string key = RootKey(path);
            if (key.Length == 0) return FsBackend.Unknown;
            return _backends.TryGetValue(key, out FsBackend backend) ? backend : FsBackend.Unknown;
        }

        public static void RememberBackend(string path, FsBackend backend)
        {
            string key = RootKey(path);
            if (key.Length == 0) return;
            _backends[key] = backend;
        }

        /// <summary>
        /// Backends to try, in order: the one that worked for this volume last
        /// time, then *FromApp, then plain Win32, then Windows.Storage.
        /// </summary>
        private static IEnumerable<FsBackend> BackendOrder(string path)
        {
            FsBackend preferred = PreferredBackend(path);
            if (preferred != FsBackend.Unknown) yield return preferred;
            if (preferred != FsBackend.FromApp) yield return FsBackend.FromApp;
            if (preferred != FsBackend.Plain) yield return FsBackend.Plain;
            yield return FsBackend.WinRt;
        }

        public static string BackendName(FsBackend backend)
        {
            switch (backend)
            {
                case FsBackend.FromApp: return "FromApp";
                case FsBackend.Plain: return "PlainWin32";
                case FsBackend.WinRt: return "WinRt";
                default: return "Unknown";
            }
        }

        #region Enumeration

        /// <summary>
        /// Enumerate the direct children of a directory. Tries every backend
        /// until one succeeds. Returns false when no backend can read the
        /// directory; <paramref name="win32Error"/> then holds the error of the
        /// last P/Invoke attempt.
        /// </summary>
        public static bool TryEnumerate(string path, out List<FsEntry> entries, out int win32Error, out FsBackend backend)
        {
            entries = null;
            win32Error = 0;
            backend = FsBackend.Unknown;

            if (string.IsNullOrEmpty(path)) { win32Error = 87; return false; }

            string pattern = path.EndsWith("\\") || path.EndsWith("/")
                ? path + "*"
                : path + "\\*";

            foreach (FsBackend candidate in BackendOrder(path))
            {
                if (candidate == FsBackend.WinRt)
                {
                    // Skip WinRT broker RPC when the path does not exist (ERROR_PATH_NOT_FOUND=3,
                    // ERROR_BAD_NETPATH=53, ERROR_INVALID_DRIVE=15).
                    if (win32Error == 3 || win32Error == 15 || win32Error == 53)
                        continue;

                    if (TryEnumerateWinRt(path, out List<FsEntry> rtEntries))
                    {
                        entries = rtEntries;
                        backend = FsBackend.WinRt;
                        RememberBackend(path, backend);
                        return true;
                    }
                    continue;
                }

                bool fromApp = candidate == FsBackend.FromApp;
                if (TryEnumerateFindFirst(pattern, fromApp, out List<FsEntry> found, out win32Error))
                {
                    entries = found;
                    backend = candidate;
                    RememberBackend(path, backend);
                    return true;
                }
                Log.Dbg("NativeFs.TryEnumerate: {Backend} failed for '{Path}' (Win32 error {Error})",
                    BackendName(candidate), path, win32Error);
            }

            return false;
        }

        private static bool TryEnumerateFindFirst(string pattern, bool fromApp, out List<FsEntry> entries, out int error)
        {
            entries = new List<FsEntry>();
            error = 0;

            WIN32_FIND_DATA findData;
            IntPtr hFind = fromApp
                ? FindFirstFileExFromAppW(pattern, FINDEX_INFO_LEVELS.FindExInfoStandard,
                    out findData, FINDEX_SEARCH_OPS.FindExSearchNameMatch, IntPtr.Zero, FIND_FIRST_EX_LARGE_FETCH)
                : FindFirstFileExW(pattern, FINDEX_INFO_LEVELS.FindExInfoStandard,
                    out findData, FINDEX_SEARCH_OPS.FindExSearchNameMatch, IntPtr.Zero, FIND_FIRST_EX_LARGE_FETCH);

            if (hFind == InvalidHandleValue)
            {
                error = Marshal.GetLastWin32Error();
                return false;
            }

            try
            {
                do
                {
                    string name = findData.cFileName;
                    if (name == "." || name == "..") continue;

                    bool isDir = (findData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
                    long size = isDir ? 0 : ((long)findData.nFileSizeHigh << 32) | findData.nFileSizeLow;
                    long fileTime = ((long)findData.ftLastWriteTime.dwHighDateTime << 32)
                                    | (uint)findData.ftLastWriteTime.dwLowDateTime;

                    entries.Add(new FsEntry
                    {
                        Name = name,
                        IsDirectory = isDir,
                        Size = size,
                        LastWriteTimeUtc = DateTime.FromFileTimeUtc(fileTime)
                    });
                }
                while (FindNextFileW(hFind, out findData));
            }
            finally
            {
                FindClose(hFind);
            }

            return true;
        }

        /// <summary>
        /// Last-resort enumeration through Windows.Storage (broadFileSystemAccess).
        /// Blocking - call from a background thread only.
        /// </summary>
        private static bool TryEnumerateWinRt(string path, out List<FsEntry> entries)
        {
            entries = null;
#if WINDOWS_UWP
            try
            {
                var folder = StorageFolder.GetFolderFromPathAsync(path).GetAwaiter().GetResult();
                var folders = folder.GetFoldersAsync().GetAwaiter().GetResult();
                var files = folder.GetFilesAsync().GetAwaiter().GetResult();

                var list = new List<FsEntry>(folders.Count + files.Count);
                foreach (var f in folders)
                {
                    list.Add(new FsEntry
                    {
                        Name = f.Name,
                        IsDirectory = true,
                        Size = 0,
                        LastWriteTimeUtc = DateTime.MinValue
                    });
                }
                foreach (var f in files)
                {
                    long size = 0;
                    try { size = (long)f.GetBasicPropertiesAsync().GetAwaiter().GetResult().Size; } catch { }
                    list.Add(new FsEntry
                    {
                        Name = f.Name,
                        IsDirectory = false,
                        Size = size,
                        LastWriteTimeUtc = DateTime.MinValue
                    });
                }

                entries = list;
                return true;
            }
            catch (Exception ex)
            {
                Log.Dbg("NativeFs: WinRT enumeration failed for '{Path}': {Error}", path, ex.Message);
                return false;
            }
#else
            return false;
#endif
        }

        #endregion

        #region File access

        /// <summary>
        /// Check whether a file exists. Tries every backend.
        /// </summary>
        public static bool FileExists(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            foreach (FsBackend candidate in BackendOrder(path))
            {
                if (candidate == FsBackend.WinRt)
                {
#if WINDOWS_UWP
                    try
                    {
                        var file = StorageFile.GetFileFromPathAsync(path).GetAwaiter().GetResult();
                        if (file != null) return true;
                    }
                    catch { }
#endif
                    continue;
                }

                WIN32_FIND_DATA findData;
                IntPtr hFind = candidate == FsBackend.FromApp
                    ? FindFirstFileExFromAppW(path, FINDEX_INFO_LEVELS.FindExInfoStandard,
                        out findData, FINDEX_SEARCH_OPS.FindExSearchNameMatch, IntPtr.Zero, 0)
                    : FindFirstFileExW(path, FINDEX_INFO_LEVELS.FindExInfoStandard,
                        out findData, FINDEX_SEARCH_OPS.FindExSearchNameMatch, IntPtr.Zero, 0);

                if (hFind == InvalidHandleValue) continue;

                FindClose(hFind);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Open a file handle, trying every backend. Returns INVALID_HANDLE_VALUE
        /// when no backend can open the file; <paramref name="error"/> holds the
        /// last Win32 error code.
        /// </summary>
        public static IntPtr OpenFile(
            string path, uint desiredAccess, uint shareMode, uint creationDisposition,
            uint flagsAndAttributes, out FsBackend backend, out int error)
        {
            backend = FsBackend.Unknown;
            error = 0;

            if (string.IsNullOrEmpty(path)) { error = 87; return InvalidHandleValue; }

            foreach (FsBackend candidate in BackendOrder(path))
            {
                if (candidate == FsBackend.WinRt) continue; // WinRT has no raw handle API

                IntPtr hFile = candidate == FsBackend.FromApp
                    ? CreateFileFromAppW(path, desiredAccess, shareMode, IntPtr.Zero,
                        creationDisposition, flagsAndAttributes, IntPtr.Zero)
                    : CreateFileW(path, desiredAccess, shareMode, IntPtr.Zero,
                        creationDisposition, flagsAndAttributes, IntPtr.Zero);

                if (hFile != InvalidHandleValue)
                {
                    backend = candidate;
                    error = 0;
                    RememberBackend(path, candidate);
                    return hFile;
                }

                error = Marshal.GetLastWin32Error();
            }

            return InvalidHandleValue;
        }

        /// <summary>
        /// Open a file for reading as a .NET stream (buffered). Returns null on failure.
        /// Caller owns the stream.
        /// </summary>
        public static Stream OpenReadStream(string path)
        {
            IntPtr hFile = OpenFile(path, GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_SEQUENTIAL_SCAN,
                out FsBackend backend, out int error);
            if (hFile == InvalidHandleValue)
            {
                Log.Warn("NativeFs.OpenReadStream: cannot open {Path}, Win32 error={Error}", path, error);
                return null;
            }
            Log.Verb("NativeFs.OpenReadStream: {Path} via {Backend}", path, BackendName(backend));
            var handle = new Microsoft.Win32.SafeHandles.SafeFileHandle(hFile, ownsHandle: true);
            return new FileStream(handle, FileAccess.Read, bufferSize: 4096, isAsync: false);
        }

        #endregion

        /// <summary>
        /// Probe whether a directory can be listed, and with which backend.
        /// Returns a human-readable summary for the log.
        /// </summary>
        public static string ProbeDirectory(string path)
        {
            if (TryEnumerate(path, out List<FsEntry> entries, out int error, out FsBackend backend))
            {
                string first = entries.Count > 0 ? entries[0].Name : "(empty)";
                return $"{BackendName(backend)} OK, {entries.Count} entries, first: '{first}'";
            }

            return $"all backends failed (last Win32 error {error})";
        }
    }
}