using System;
using System.Collections.Generic;

namespace XFiles.FileSystem
{
    /// <summary>
    /// Pure ordering helper for directory scan results.
    /// Appends sorted folders then sorted files to an existing entry list,
    /// preserving entries already present (e.g. the ".." parent entry at index 0).
    /// </summary>
    public static class DirectoryEntryOrder
    {
        public static void AppendSorted(List<FileEntry> entries, List<FileEntry> dirs, List<FileEntry> files)
        {
            dirs.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            files.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            entries.AddRange(dirs);
            entries.AddRange(files);
        }
    }
}
