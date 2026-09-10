using System;
using System.Collections.Generic;

namespace XFiles.FileSystem
{
    /// <summary>
    /// Pure subtree statistics for archive entry lists (no SharpCompress dependency,
    /// linkable into the desktop unit-test project). Input is the flat list of
    /// (path, isDir, size) tuples as exposed by SharpCompress's IArchive.Entries.
    /// Paths are normalized to '/'-separated, no leading/trailing slashes.
    ///
    /// Mirrors the filesystem counters (file count, folder count, total bytes);
    /// directories not explicit in the archive are inferred from file path
    /// prefixes, exactly like ArchiveBrowser.ListEntries does. The subtree root
    /// itself is NOT counted as a folder (consistent with DirectoryStatsCalculator).
    /// </summary>
    public static class ArchiveSubtreeStats
    {
        /// <summary>Result of a subtree count over archive entries.</summary>
        public struct Stats
        {
            public long FileCount;
            public long FolderCount;
            public long TotalBytes;
        }

        public static Stats Compute(
            IEnumerable<(string path, bool isDir, long size)> entries,
            string internalPath)
        {
            var result = new Stats();

            string root = Normalize(internalPath ?? "");

            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (entries == null) return result;

            foreach (var item in entries)
            {
                string p = Normalize(item.path);
                if (string.IsNullOrEmpty(p)) continue;
                if (!IsInSubtree(p, root)) continue;

                if (item.isDir)
                {
                    if (!p.Equals(root, StringComparison.OrdinalIgnoreCase))
                        folders.Add(p);
                    continue;
                }

                if (item.size > 0) result.TotalBytes += item.size;
                result.FileCount++;

                // Infer ancestor directories below root (archives without explicit
                // directory entries).
                int idx = p.Length;
                while ((idx = p.LastIndexOf('/', idx - 1)) > 0)
                {
                    string prefix = p.Substring(0, idx);
                    if (prefix.Equals(root, StringComparison.OrdinalIgnoreCase)) break;
                    folders.Add(prefix);
                }
            }

            result.FolderCount = folders.Count;
            return result;
        }

        private static bool IsInSubtree(string path, string root)
        {
            if (string.IsNullOrEmpty(root)) return true; // archive root
            if (path.Equals(root, StringComparison.OrdinalIgnoreCase)) return true;
            return path.IndexOf(root + "/", StringComparison.OrdinalIgnoreCase) == 0;
        }

        private static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            return path.Replace('\\', '/').Trim('/');
        }
    }
}