using System;

namespace XFiles.Network
{
    /// <summary>
    /// Single entry returned by a network directory listing. Mirrors the subset
    /// of FileEntry the file browser needs to render a column (name, kind, size,
    /// modified). Mapped to FileEntry by the navigation layer (M3).
    /// </summary>
    public class NetworkFileEntry
    {
        public string Name { get; set; }

        public bool IsDirectory { get; set; }

        public long Size { get; set; }

        public DateTime LastWriteTime { get; set; }
    }
}
