using System.Collections.Generic;

namespace XFiles.FileSystem
{
    /// <summary>
    /// In-app clipboard for copy/paste workflow.
    /// Holds entries that were copied, ready to paste in a different directory.
    /// </summary>
    public static class ClipboardState
    {
        private static readonly List<FileEntry> _entries = new List<FileEntry>();

        public static IReadOnlyList<FileEntry> Entries => _entries;
        public static int Count => _entries.Count;
        public static bool HasItems => _entries.Count > 0;

        public static void Copy(IReadOnlyList<FileEntry> entries)
        {
            Clear();
            _entries.AddRange(entries);
        }

        public static void Clear()
        {
            _entries.Clear();
        }
    }
}
