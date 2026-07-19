using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace XFiles.FileSystem
{
    /// <summary>
    /// Browse compressed archives (zip/7z/rar) as virtual folders.
    /// Uses SharpCompress for format-agnostic reading.
    /// </summary>
    public class ArchiveBrowser : IDisposable
    {
        private readonly Dictionary<string, IArchive> _archiveCache = new Dictionary<string, IArchive>(StringComparer.OrdinalIgnoreCase);
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
                   string.Equals(ext, ".rar", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// List entries (files and folders) at internal path within archive.
        /// internalPath == "" lists archive root.
        /// Handles archives both with and without explicit directory entries
        /// by inferring directories from file paths.
        /// </summary>
        public IReadOnlyList<FileEntry> ListEntries(string archivePath, string internalPath)
        {
            var entries = new List<FileEntry>();
            IArchive archive = GetOrCreateArchive(archivePath);
            if (archive == null) return entries;

            try
            {
                internalPath = NormalizeInternalPath(internalPath);

                // Two-pass approach: first collect all paths, then build directory tree
                var allEntryPaths = new List<(string path, bool isDir, long size, DateTimeOffset? modified)>();
                var explicitDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var entry in archive.Entries)
                {
                    string entryPath = entry.Key;
                    if (string.IsNullOrEmpty(entryPath)) continue;

                    entryPath = entryPath.Replace('\\', '/').Trim('/');
                    if (string.IsNullOrEmpty(entryPath)) continue;

                    allEntryPaths.Add((entryPath, entry.IsDirectory, (long)entry.Size, entry.LastModifiedTime));

                    if (entry.IsDirectory)
                    {
                        explicitDirs.Add(entryPath.TrimEnd('/'));
                    }
                }

                // Infer directories from file paths (for archives without explicit dir entries)
                var inferredDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in allEntryPaths)
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
                            break; // already known
                        lastSlash = dir.LastIndexOf('/');
                    }
                }

                // Combine explicit + inferred directories
                var allDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var d in explicitDirs) allDirs.Add(d);
                foreach (var d in inferredDirs) allDirs.Add(d);

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
                    if (string.IsNullOrEmpty(internalPath))
                    {
                        matches = string.IsNullOrEmpty(entryDir);
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

                Log.Information("ArchiveBrowser.ListEntries: archive={Archive} internal={Internal} count={Count}",
                    archivePath, internalPath ?? "", entries.Count);
            }
            catch (Exception ex)
            {
                Log.Warning("ArchiveBrowser.ListEntries failed: {Error}", ex.Message);
            }

            return entries;
        }

        private static string GetParentPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            int lastSlash = path.LastIndexOf('/');
            return lastSlash >= 0 ? path.Substring(0, lastSlash) : "";
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
        public IReadOnlyList<FileEntry> ListDirectories(string archivePath, string internalPath)
        {
            var dirs = new List<FileEntry>();
            IArchive archive = GetOrCreateArchive(archivePath);
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

                Log.Information("ArchiveBrowser.ListDirectories: archive={Archive} internal={Internal} count={Count}",
                    archivePath, internalPath ?? "", dirs.Count);
            }
            catch (Exception ex)
            {
                Log.Warning("ArchiveBrowser.ListDirectories failed: {Error}", ex.Message);
            }

            return dirs;
        }

        /// <summary>
        /// Open a read stream for a specific entry inside the archive.
        /// Used by FilePreviewService for text/image preview.
        /// </summary>
        public Stream OpenEntryStream(string archivePath, string internalEntryPath)
        {
            IArchive archive = GetOrCreateArchive(archivePath);
            if (archive == null) return null;

            try
            {
                // Normalize path
                internalEntryPath = internalEntryPath.Replace('\\', '/').Trim('/');

                // Find the entry
                var entry = archive.Entries.FirstOrDefault(e =>
                    !e.IsDirectory &&
                    string.Equals(e.Key.Replace('\\', '/').Trim('/'), internalEntryPath, StringComparison.OrdinalIgnoreCase));

                if (entry == null)
                {
                    Log.Warning("ArchiveBrowser.OpenEntryStream: entry not found: {Path}", internalEntryPath);
                    return null;
                }

                Log.Information("ArchiveBrowser.OpenEntryStream: {Path} size={Size}", internalEntryPath, entry.Size);
                return entry.OpenEntryStream();
            }
            catch (Exception ex)
            {
                Log.Warning("ArchiveBrowser.OpenEntryStream failed: {Error}", ex.Message);
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

        public void Dispose()
        {
            lock (_lock)
            {
                foreach (var archive in _archiveCache.Values)
                {
                    try { archive.Dispose(); } catch { }
                }
                _archiveCache.Clear();
            }
        }

        private IArchive GetOrCreateArchive(string archivePath)
        {
            lock (_lock)
            {
                if (_archiveCache.TryGetValue(archivePath, out IArchive cached))
                {
                    return cached;
                }

                try
                {
                    // Open via Win32 P/Invoke — System.IO.FileStream doesn't work in UWP sandbox
                    var stream = Win32FileStream.OpenRead(archivePath);
                    if (stream == null)
                    {
                        Log.Warning("ArchiveBrowser: file not found or unreadable: {Path}", archivePath);
                        return null;
                    }

                    // SharpCompress takes ownership of the stream (reads on demand, not all-at-once)
                    var archive = ArchiveFactory.Open(stream);
                    _archiveCache[archivePath] = archive;
                    Log.Information("ArchiveBrowser: opened archive {Path} ({Size} bytes)", archivePath, stream.Length);
                    return archive;
                }
                catch (Exception ex)
                {
                    Log.Warning("ArchiveBrowser: failed to open {Path}: {Error}", archivePath, ex.Message);
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
