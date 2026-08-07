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

        // Virtual entry (e.g. Favorites pseudo-folder in root)
        public bool IsVirtual { get; set; }

        // Visual-only divider row in a column list (not navigable/selectable)
        public bool IsSeparator { get; set; }

        // Portal entry (Device Portal AppData browser) — set for every portal node
        public bool IsPortal { get; set; }
        public string PortalKnownFolder { get; set; }
        public string PortalPackageFullName { get; set; }

        // Portal-relative parent directory of the entry (e.g. "\" or "\Settings").
        // For known-folder and package entries this is null (they are addressed via
        // knownfolderid/packagefullname query params, not a portal path).
        public string PortalPath { get; set; }

        /// <summary>
        /// True for root-level container entries that only offer Refresh / Disk Space
        /// and are excluded from batch selection: logical drives, the portal browser
        /// roots (User Folders, known folders, packages — portal entries without a
        /// PortalPath), and the root AppData shortcut.
        /// </summary>
        public bool IsRootContainer
        {
            get
            {
                if (IsDrive) return true;
                if (IsPortal && string.IsNullOrEmpty(PortalPath)) return true;
                return Name != null && Name.Equals("AppData", StringComparison.OrdinalIgnoreCase)
                    && IsDirectory && !IsPortal;
            }
        }
    }
}
