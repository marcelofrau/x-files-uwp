using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace XFiles.FileSystem
{
    /// <summary>
    /// Outcome of an archive open attempt — tells the UI whether to prompt for
    /// a password or to report a generic failure.
    /// </summary>
    public enum ArchiveOpenStatus
    {
        /// <summary>Archive opened.</summary>
        Ok = 0,

        /// <summary>Archive is encrypted and the password is missing or wrong.</summary>
        PasswordRequired = 1,

        /// <summary>Archive could not be opened (missing, empty, corrupt, unsupported).</summary>
        Failed = 2
    }

    /// <summary>
    /// Browse compressed archives (zip/7z/rar) as virtual folders.
    /// Uses SharpCompress for format-agnostic reading.
    ///
    /// Encrypted archives: the password is supplied by the caller (or cached
    /// from an earlier successful open) and passed to SharpCompress through
    /// <see cref="ReaderOptions.Password"/>. 7z/rar with an encrypted header
    /// fail at open time; zip entries fail when their stream is opened.
    /// </summary>
    public class ArchiveBrowser : IDisposable
    {
        private sealed class ArchiveIndex
        {
            public readonly Dictionary<string, IArchiveEntry> EntriesByPath =
                new Dictionary<string, IArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            public readonly List<(string path, bool isDir, long size, DateTimeOffset? modified)> AllEntryPaths =
                new List<(string path, bool isDir, long size, DateTimeOffset? modified)>();
            public readonly HashSet<string> AllDirs =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private readonly Dictionary<string, IArchive> _archiveCache = new Dictionary<string, IArchive>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<IArchive, ArchiveIndex> _indexCache = new Dictionary<IArchive, ArchiveIndex>();

        /// <summary>
        /// Passwords entered by the user, keyed by archive path. Kept in memory
        /// only (never logged, never persisted) so a second operation on the
        /// same archive does not prompt again.
        /// </summary>
        private readonly Dictionary<string, string> _passwords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly object _lock = new object();

        /// <summary>
        /// Check if file is a supported archive by extension.
        /// </summary>
        public static bool IsArchiveFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            string ext = Path.GetExtension(filePath);
            return string.Equals(ext, ".zip", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ext, ".7z", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ext, ".rar", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ext, ".rsn", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Remember the password for an archive so later operations on it do not
        /// prompt again. Pass null/empty to forget it.
        /// </summary>
        public void SetPassword(string archivePath, string password)
        {
            if (string.IsNullOrEmpty(archivePath)) return;
            lock (_lock)
            {
                if (string.IsNullOrEmpty(password))
                    _passwords.Remove(archivePath);
                else
                    _passwords[archivePath] = password;
            }
        }

        public void ClearPassword(string archivePath)
        {
            SetPassword(archivePath, null);
        }

        /// <summary>
        /// List entries (files and folders) at internal path within archive.
        /// internalPath == "" lists archive root.
        /// Handles archives both with and without explicit directory entries
        /// by inferring directories from file paths.
        /// Returns an empty list when the archive cannot be opened.
        /// </summary>
        public IReadOnlyList<FileEntry> ListEntries(string archivePath, string internalPath)
        {
            return ListEntriesCore(archivePath, internalPath, null, out ArchiveOpenStatus _);
        }

        /// <summary>
        /// List entries with password support. <paramref name="status"/> reports
        /// whether the archive needed a password (missing or wrong) so the caller
        /// can prompt and retry.
        /// </summary>
        public IReadOnlyList<FileEntry> ListEntries(
            string archivePath, string internalPath, string password, out ArchiveOpenStatus status)
        {
            return ListEntriesCore(archivePath, internalPath, password, out status);
        }

        private IReadOnlyList<FileEntry> ListEntriesCore(
            string archivePath, string internalPath, string password, out ArchiveOpenStatus status)
        {
            status = ArchiveOpenStatus.Ok;
            var entries = new List<FileEntry>();

            IArchive archive = GetOrCreateArchive(archivePath, password, out status);
            if (archive == null) return entries;

            try
            {
                internalPath = NormalizeInternalPath(internalPath);
                ArchiveIndex idx = GetOrBuildIndex(archive);
                var allEntryPaths = idx.AllEntryPaths;
                var allDirs = idx.AllDirs;

                // Now list items at the requested level
                var seenEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Add directories at this level
                foreach (string dirPath in allDirs)
                {
                    string normalizedDir = dirPath.TrimEnd('/');
                    string parentDir = GetParentPath(normalizedDir);
                    string dirName = GetFileName(normalizedDir);

                    if (string.IsNullOrEmpty(dirName)) continue;

                    bool matches;
                    if (string.IsNullOrEmpty(internalPath))
                    {
                        matches = string.IsNullOrEmpty(parentDir);
                    }
                    else
                    {
                        matches = string.Equals(parentDir, internalPath, StringComparison.OrdinalIgnoreCase);
                    }

                    if (!matches) continue;

                    if (seenEntries.Add(dirName))
                    {
                        entries.Add(new FileEntry
                        {
                            Name = dirName,
                            SizeBytes = 0,
                            IsDirectory = true,
                            FullPath = $"{archivePath}|{normalizedDir}",
                            ArchiveRootPath = archivePath,
                            ArchiveInternalPath = normalizedDir
                        });
                    }
                }

                // Add files at this level
                foreach (var item in allEntryPaths)
                {
                    if (item.isDir) continue;

                    string entryDir = GetParentPath(item.path);
                    string fileName = GetFileName(item.path);

                    bool matches;
                    if (string.IsNullOrEmpty(entryDir))
                    {
                        matches = string.IsNullOrEmpty(internalPath);
                    }
                    else
                    {
                        matches = string.Equals(entryDir, internalPath, StringComparison.OrdinalIgnoreCase);
                    }

                    if (!matches) continue;

                    if (seenEntries.Add(fileName))
                    {
                        entries.Add(new FileEntry
                        {
                            Name = fileName,
                            SizeBytes = item.size,
                            LastModified = item.modified,
                            IsDirectory = false,
                            FullPath = $"{archivePath}|{item.path}",
                            ArchiveRootPath = archivePath,
                            ArchiveInternalPath = item.path
                        });
                    }
                }

                entries = entries
                    .OrderBy(e => e.IsDirectory ? 0 : 1)
                    .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                Log.Dbg("ArchiveBrowser.ListEntries: archive={Archive} internal={Internal} count={Count}",
                    archivePath, internalPath ?? "", entries.Count);
            }
            catch (CryptographicException ex)
            {
                status = ArchiveOpenStatus.PasswordRequired;
                Log.Dbg("ArchiveBrowser.ListEntries: password required for {Archive}: {Error}", archivePath, ex.Message);
                return new List<FileEntry>();
            }
            catch (Exception ex)
            {
                Log.Dbg("ArchiveBrowser.ListEntries failed: {Error}", ex.Message);
                if (status == ArchiveOpenStatus.Ok) status = ArchiveOpenStatus.Failed;
            }

            return entries;
        }

        private static string GetParentPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            int lastSlash = path.LastIndexOf('/');
            return lastSlash >= 0 ? path.Substring(0, lastSlash) : "";
        }

        /// <summary>
        /// Recursive stats (file count, folder count, total bytes) for the entry
        /// subtree under <paramref name="internalPath"/> — computed entirely from
        /// the in-memory entry list, zero I/O. Returns all-zeroes when the archive
        /// cannot be opened.
        /// </summary>
        public ArchiveSubtreeStats.Stats ComputeSubtreeStats(string archivePath, string internalPath, string password = null)
        {
            IArchive archive = GetOrCreateArchive(archivePath, password, out ArchiveOpenStatus _);
            if (archive == null) return default;

            try
            {
                string normalized = string.IsNullOrEmpty(internalPath)
                    ? ""
                    : internalPath.Replace('\\', '/').Trim('/');

                var flat = new List<(string path, bool isDir, long size)>();
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Key)) continue;
                    string p = entry.Key.Replace('\\', '/').Trim('/');
                    if (string.IsNullOrEmpty(p)) continue;
                    flat.Add((p, entry.IsDirectory, (long)entry.Size));
                }

                var stats = ArchiveSubtreeStats.Compute(flat, normalized);
                Log.Dbg("ArchiveBrowser.ComputeSubtreeStats: archive={Archive} internal={Internal} files={Files} folders={Folders} bytes={Bytes}",
                    archivePath, normalized, stats.FileCount, stats.FolderCount, stats.TotalBytes);
                return stats;
            }
            catch (Exception ex)
            {
                Log.Warn("ArchiveBrowser.ComputeSubtreeStats failed: {Error}", ex.Message);
                return default;
            }
        }

        private static string GetFileName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            int lastSlash = path.LastIndexOf('/');
            return lastSlash >= 0 ? path.Substring(lastSlash + 1) : path;
        }

        /// <summary>
        /// List subdirectories at internal path within archive.
        /// </summary>
        public IReadOnlyList<FileEntry> ListDirectories(string archivePath, string internalPath, string password = null)
        {
            var dirs = new List<FileEntry>();
            IArchive archive = GetOrCreateArchive(archivePath, password, out ArchiveOpenStatus _);
            if (archive == null) return dirs;

            try
            {
                internalPath = NormalizeInternalPath(internalPath);
                var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var entry in archive.Entries)
                {
                    if (!entry.IsDirectory) continue;

                    string entryPath = entry.Key;
                    if (string.IsNullOrEmpty(entryPath)) continue;

                    // Normalize
                    entryPath = entryPath.Replace('\\', '/').Trim('/');

                    // Get parent directory of this directory entry
                    string parentDir = string.Empty;
                    int lastSlash = entryPath.LastIndexOf('/');
                    if (lastSlash >= 0)
                    {
                        parentDir = entryPath.Substring(0, lastSlash);
                    }

                    // Check if this directory is at the requested level
                    bool matchesPath;
                    if (string.IsNullOrEmpty(internalPath))
                    {
                        matchesPath = string.IsNullOrEmpty(parentDir);
                    }
                    else
                    {
                        matchesPath = string.Equals(parentDir, internalPath, StringComparison.OrdinalIgnoreCase);
                    }

                    if (!matchesPath) continue;

                    // Get directory name
                    string dirName = string.IsNullOrEmpty(parentDir)
                        ? entryPath
                        : entryPath.Substring(lastSlash + 1);

                    // Skip if already seen
                    if (!seenDirs.Add(dirName)) continue;

                    dirs.Add(new FileEntry
                    {
                        Name = dirName,
                        SizeBytes = 0,
                        LastModified = entry.LastModifiedTime,
                        IsDirectory = true,
                        FullPath = $"{archivePath}|{entryPath}",
                        ArchiveRootPath = archivePath,
                        ArchiveInternalPath = entryPath
                    });
                }

                dirs = dirs.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();

                Log.Dbg("ArchiveBrowser.ListDirectories: archive={Archive} internal={Internal} count={Count}",
                    archivePath, internalPath ?? "", dirs.Count);
            }
            catch (Exception ex)
            {
                Log.Dbg("ArchiveBrowser.ListDirectories failed: {Error}", ex.Message);
            }

            return dirs;
        }

        /// <summary>
        /// Open a read stream for a specific entry inside the archive.
        /// Used by FilePreviewService for text/image preview.
        /// Returns null when the archive/entry cannot be opened.
        /// </summary>
        public Stream OpenEntryStream(string archivePath, string internalEntryPath)
        {
            return OpenEntryStreamCore(archivePath, internalEntryPath, null, out ArchiveOpenStatus _);
        }

        /// <summary>
        /// Open a read stream with password support. <paramref name="status"/>
        /// reports PasswordRequired when the entry is encrypted and the password
        /// is missing or wrong (the caller then prompts and retries).
        /// </summary>
        public Stream OpenEntryStream(
            string archivePath, string internalEntryPath, string password, out ArchiveOpenStatus status)
        {
            return OpenEntryStreamCore(archivePath, internalEntryPath, password, out status);
        }

        private Stream OpenEntryStreamCore(
            string archivePath, string internalEntryPath, string password, out ArchiveOpenStatus status)
        {
            status = ArchiveOpenStatus.Ok;

            IArchive archive = GetOrCreateArchive(archivePath, password, out status);
            if (archive == null) return null;

            try
            {
                // Normalize path
                internalEntryPath = internalEntryPath.Replace('\\', '/').Trim('/');

                // Find the entry via cached O(1) index
                ArchiveIndex idx = GetOrBuildIndex(archive);
                if (!idx.EntriesByPath.TryGetValue(internalEntryPath, out IArchiveEntry entry) || entry == null)
                {
                    Log.Warn("ArchiveBrowser.OpenEntryStream: entry not found: {Path}", internalEntryPath);
                    status = ArchiveOpenStatus.Failed;
                    return null;
                }

                Log.Info("ArchiveBrowser.OpenEntryStream: {Path} size={Size}", internalEntryPath, entry.Size);
                return entry.OpenEntryStream();
            }
            catch (CryptographicException ex)
            {
                // Encrypted entry: the archive opened (names are visible) but the
                // content needs the password.
                status = ArchiveOpenStatus.PasswordRequired;
                Log.Dbg("ArchiveBrowser.OpenEntryStream: password required for {Path}: {Error}", internalEntryPath, ex.Message);
                return null;
            }
            catch (Exception ex)
            {
                Log.Dbg("ArchiveBrowser.OpenEntryStream failed: {Error}", ex.Message);
                if (status == ArchiveOpenStatus.Ok) status = ArchiveOpenStatus.Failed;
                return null;
            }
        }

        /// <summary>
        /// Check if path points to an entry inside an archive.
        /// Format: "archivePath|internalPath"
        /// </summary>
        public static bool IsArchiveEntryPath(string fullPath)
        {
            return fullPath != null && fullPath.Contains("|");
        }

        /// <summary>
        /// Parse an archive entry path into archive path and internal path.
        /// </summary>
        public static void ParseArchiveEntryPath(string fullPath, out string archivePath, out string internalPath)
        {
            if (fullPath == null || !fullPath.Contains("|"))
            {
                archivePath = null;
                internalPath = null;
                return;
            }

            int pipeIndex = fullPath.IndexOf('|');
            archivePath = fullPath.Substring(0, pipeIndex);
            internalPath = fullPath.Substring(pipeIndex + 1);
        }

        /// <summary>
        /// Registers an already-open stream as the archive for a virtual key
        /// (e.g. a remote SMB file). The stream must be seekable; the archive is
        /// opened lazily and cached under <paramref name="archiveKey"/> so later
        /// ListEntries/OpenEntryStream calls resolve through the cache. On success
        /// the caller must NOT dispose the stream — SharpCompress owns it for the
        /// archive's lifetime. On failure (or cache hit) the stream is disposed here.
        /// </summary>
        public bool TryOpenArchiveFromStream(string archiveKey, Stream stream)
        {
            return TryOpenArchiveFromStream(archiveKey, stream, null, out ArchiveOpenStatus _);
        }

        /// <summary>
        /// Registers an already-open stream as the archive for a virtual key with
        /// password support. <paramref name="status"/> reports PasswordRequired
        /// when the archive has an encrypted header.
        /// </summary>
        public bool TryOpenArchiveFromStream(
            string archiveKey, Stream stream, string password, out ArchiveOpenStatus status)
        {
            status = ArchiveOpenStatus.Ok;
            if (stream == null) return false;
            lock (_lock)
            {
                string cacheKey = CacheKey(archiveKey, password);

                if (_archiveCache.ContainsKey(cacheKey))
                {
                    // Already open — discard the redundant stream.
                    try { stream.Dispose(); } catch { }
                    return true;
                }

                if (stream.Length == 0)
                {
                    Log.Warn("ArchiveBrowser: archive stream is empty, treating as no entries: {Key}", archiveKey);
                    try { stream.Dispose(); } catch { }
                    status = ArchiveOpenStatus.Failed;
                    return false;
                }

                try
                {
                    IArchive archive = OpenArchive(stream, password);
                    _archiveCache[cacheKey] = archive;
                    Log.Info("ArchiveBrowser: opened archive from stream {Key} ({Size} bytes){Pwd}",
                        archiveKey, stream.Length, string.IsNullOrEmpty(password) ? "" : " [with password]");
                    if (!string.IsNullOrEmpty(password)) _passwords[archiveKey] = password;
                    return true;
                }
                catch (CryptographicException ex)
                {
                    status = ArchiveOpenStatus.PasswordRequired;
                    Log.Dbg("ArchiveBrowser: password required for stream {Key}: {Error}", archiveKey, ex.Message);
                    try { stream.Dispose(); } catch { }
                    return false;
                }
                catch (Exception ex)
                {
                    status = ArchiveOpenStatus.Failed;
                    Log.Warn("ArchiveBrowser: failed to open archive from stream {Key}", archiveKey, ex);
                    try { stream.Dispose(); } catch { }
                    return false;
                }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                foreach (var archive in _archiveCache.Values)
                {
                    try { archive.Dispose(); } catch { }
                }
                _archiveCache.Clear();
                _indexCache.Clear();
                _passwords.Clear();
            }
        }

        private ArchiveIndex GetOrBuildIndex(IArchive archive)
        {
            lock (_lock)
            {
                if (_indexCache.TryGetValue(archive, out ArchiveIndex cached))
                    return cached;

                var idx = new ArchiveIndex();
                var explicitDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in archive.Entries)
                {
                    string entryPath = entry.Key;
                    if (string.IsNullOrEmpty(entryPath)) continue;

                    entryPath = entryPath.Replace('\\', '/').Trim('/');
                    if (string.IsNullOrEmpty(entryPath)) continue;

                    idx.AllEntryPaths.Add((entryPath, entry.IsDirectory, (long)entry.Size, entry.LastModifiedTime));

                    if (entry.IsDirectory)
                    {
                        explicitDirs.Add(entryPath.TrimEnd('/'));
                    }
                    else if (!idx.EntriesByPath.ContainsKey(entryPath))
                    {
                        idx.EntriesByPath[entryPath] = entry;
                    }
                }

                var inferredDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in idx.AllEntryPaths)
                {
                    if (item.isDir) continue;

                    string dir = item.path;
                    int lastSlash = dir.LastIndexOf('/');
                    while (lastSlash >= 0)
                    {
                        dir = dir.Substring(0, lastSlash);
                        if (string.IsNullOrEmpty(dir)) break;

                        string normalized = dir.TrimEnd('/');
                        if (!inferredDirs.Add(normalized) && explicitDirs.Contains(normalized))
                            break;
                        lastSlash = dir.LastIndexOf('/');
                    }
                }

                foreach (var d in explicitDirs) idx.AllDirs.Add(d);
                foreach (var d in inferredDirs) idx.AllDirs.Add(d);

                _indexCache[archive] = idx;
                return idx;
            }
        }

        /// <summary>
        /// Cache key for an archive handle. The password is folded in (hashed) so
        /// a no-password handle is never reused for an encrypted archive.
        /// </summary>
        private static string CacheKey(string archivePath, string password)
        {
            if (string.IsNullOrEmpty(password)) return archivePath;
            return archivePath + "|pwd:" + HashPassword(password);
        }

        /// <summary>
        /// FNV-1a hash — keeps the plaintext password out of the cache key and
        /// out of the logs.
        /// </summary>
        private static string HashPassword(string password)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in password)
                {
                    hash ^= c;
                    hash *= 16777619;
                }
                return hash.ToString("X8");
            }
        }

        private static IArchive OpenArchive(Stream stream, string password)
        {
            if (string.IsNullOrEmpty(password))
                return ArchiveFactory.Open(stream);

            var options = new ReaderOptions { Password = password };
            return ArchiveFactory.Open(stream, options);
        }

        private IArchive GetOrCreateArchive(string archivePath, string password, out ArchiveOpenStatus status)
        {
            status = ArchiveOpenStatus.Ok;

            string effectivePassword = password;
            lock (_lock)
            {
                if (string.IsNullOrEmpty(effectivePassword) &&
                    _passwords.TryGetValue(archivePath, out string cached))
                {
                    effectivePassword = cached;
                }

                string cacheKey = CacheKey(archivePath, effectivePassword);

                if (_archiveCache.TryGetValue(cacheKey, out IArchive cachedArchive))
                {
                    return cachedArchive;
                }

                Stream stream = null;
                try
                {
                    // Open via Win32 P/Invoke — System.IO.FileStream doesn't work in UWP sandbox
                    stream = Win32FileStream.OpenRead(archivePath);
                    if (stream == null)
                    {
                        Log.Warn("ArchiveBrowser: file not found or unreadable: {Path}", archivePath);
                        status = ArchiveOpenStatus.Failed;
                        return null;
                    }

                    // Empty/corrupt files make SharpCompress throw (NRE inside TarArchive.IsTarFile).
                    // Guard the size up-front so an invalid zip degrades to an empty listing.
                    if (stream.Length == 0)
                    {
                        stream.Dispose();
                        Log.Warn("ArchiveBrowser: archive is empty, treating as no entries: {Path}", archivePath);
                        status = ArchiveOpenStatus.Failed;
                        return null;
                    }

                    // SharpCompress takes ownership of the stream (reads on demand, not all-at-once)
                    IArchive archive = OpenArchive(stream, effectivePassword);

                    _archiveCache[cacheKey] = archive;
                    if (!string.IsNullOrEmpty(effectivePassword)) _passwords[archivePath] = effectivePassword;
                    Log.Info("ArchiveBrowser: opened archive {Path} ({Size} bytes){Pwd}",
                        archivePath, stream.Length, string.IsNullOrEmpty(effectivePassword) ? "" : " [with password]");
                    return archive;
                }
                catch (CryptographicException ex)
                {
                    // Wrong password or password required
                    status = ArchiveOpenStatus.PasswordRequired;
                    Log.Dbg("ArchiveBrowser: password required or incorrect for {Path}: {Error}", archivePath, ex.Message);
                    stream?.Dispose();
                    return null;
                }
                catch (Exception ex)
                {
                    status = ArchiveOpenStatus.Failed;
                    Log.Warn("ArchiveBrowser: failed to open {Path}", archivePath, ex);
                    stream?.Dispose();
                    return null;
                }
            }
        }

        private static string NormalizeInternalPath(string internalPath)
        {
            if (string.IsNullOrEmpty(internalPath)) return "";
            return internalPath.Replace('\\', '/').Trim('/');
        }
    }
}