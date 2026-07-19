using System;

namespace XFiles.FileSystem
{
    public class FileEntry
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public bool IsDirectory { get; set; }
        public bool IsDrive { get; set; }
        public bool IsArchive { get; set; }
        public long SizeBytes { get; set; }
        public DateTimeOffset? LastModified { get; set; }

        // Only set when entry lives INSIDE an archive:
        public string ArchiveRootPath { get; set; }
        public string ArchiveInternalPath { get; set; }
    }
}
