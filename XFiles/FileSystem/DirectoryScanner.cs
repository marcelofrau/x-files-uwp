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

        [DllImport("kernel32.dll")]
        private static extern uint GetLogicalDrives();

        #endregion

        public static bool FileExists(string path)
        {
            return NativeFs.FileExists(path);
        }

        /// <summary>
        /// Open a file for reading via P/Invoke (bypasses UWP sandbox).
        /// Returns null if file cannot be opened. Caller owns the stream.
        /// </summary>
        public static Stream OpenFileRead(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return NativeFs.OpenReadStream(path);
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

            if (NativeFs.TryEnumerate(path, out List<FsEntry> entries, out win32Error, out FsBackend backend))
            {
                foreach (var entry in entries)
                    names.Add(entry.Name);
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
            IntPtr hFile = NativeFs.OpenFile(path, 0x80000000 /*GENERIC_READ*/,
                0x00000001 /*FILE_SHARE_READ*/, 3 /*OPEN_EXISTING*/, 0x80 /*FILE_ATTRIBUTE_NORMAL*/,
                out FsBackend backend, out int error);
            if (hFile == new IntPtr(-1))
                return error;
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
        /// If P/Invoke fails, also tries Windows.Storage as a diagnostic fallback.
        /// </summary>
        private static string ProbeDriveFindFirst(string driveLetter)
        {
            string drivePath = driveLetter + ":\\";
            if (NativeFs.TryEnumerate(drivePath, out List<FsEntry> entries, out int err, out FsBackend backend))
            {
                string first = entries.Count > 0 ? entries[0].Name : "(empty)";
                return $"{NativeFs.BackendName(backend)} OK, {entries.Count} entries, first: '{first}'";
            }

            return $"all backends failed (last Win32 error {err})";
        }

        private static bool DriveReadableFromResult(string result)
            => !result.StartsWith("all backends failed", StringComparison.Ordinal);

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
                // NativeFs tries *FromApp, then plain Win32, then Windows.Storage — the
                // *FromApp APIs apply the UWP app-container rules and can deny a volume
                // that the plain kernel32 APIs (full-trust token) read fine.
                if (!NativeFs.TryEnumerate(path, out List<FsEntry> found, out int err, out FsBackend backend))
                {
                    Log.Warn("Directory enumeration failed for '{Path}' (Win32 error {Error}) — '..' entry only", path, err);
                    return;
                }

                var dirs = new List<FileEntry>();
                var files = new List<FileEntry>();

                foreach (var item in found)
                {
                    token.ThrowIfCancellationRequested();

                    string fullPath = Path.Combine(path, item.Name);

                    var entry = new FileEntry
                    {
                        Name = item.Name,
                        FullPath = fullPath,
                        IsDirectory = item.IsDirectory,
                        SizeBytes = item.Size,
                        IsArchive = !item.IsDirectory && ArchiveExtensions.Contains(Path.GetExtension(item.Name)),
                        IsChiptune = !item.IsDirectory && Audio.RetroAudioPlayer.IsChiptuneFile(item.Name)
                    };

                    if (item.IsDirectory) dirs.Add(entry);
                    else files.Add(entry);
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
