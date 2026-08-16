using System;
using XFiles.Network;

namespace XFiles.FileSystem
{
    /// <summary>
    /// Kind of a fake action row rendered inside a column (e.g. "＋ Add location").
    /// Confirm (A) on such a row opens the matching flow instead of navigating.
    /// </summary>
    public enum ActionKind
    {
        None = 0,
        AddLocation,
        DownloadUrl
    }

    public class FileEntry
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public bool IsDirectory { get; set; }
        public bool IsDrive { get; set; }
        public bool IsArchive { get; set; }
        public long SizeBytes { get; set; }
        public DateTimeOffset? LastModified { get; set; }

        // Network entries (saved locations, remote shares/directories/files).
        // FullPath stays null for these — the remote tree is addressed via
        // NetworkLocationId + NetworkShareName + NetworkPath.
        public bool IsNetwork { get; set; }
        public ActionKind ActionKind { get; set; } = ActionKind.None;

        /// <summary>Id of the saved location (NetworkServerEntry) this entry belongs to. 0 = unbound.</summary>
        public long NetworkLocationId { get; set; }

        /// <summary>Remote share name this entry lives in (null = location/shares level).</summary>
        public string NetworkShareName { get; set; }

        /// <summary>Remote path within the share ("" = share root, null = no share bound).</summary>
        public string NetworkPath { get; set; }

        /// <summary>
        /// Protocol of the saved location row this entry represents (only set on
        /// network location rows). Used by the column list to pick a per-protocol
        /// icon (SMB = server, FTP/FTPS/SFTP = globe).
        /// </summary>
        public NetworkProtocol NetworkProtocol { get; set; } = NetworkProtocol.Smb;

        // Only set when entry lives INSIDE an archive:
        public string ArchiveRootPath { get; set; }
        public string ArchiveInternalPath { get; set; }

        // Virtual entry (e.g. Favorites pseudo-folder in root)
        public bool IsVirtual { get; set; }

        // Chiptune subsong entry produced by ChiptuneBrowser when a multi-track
        // chiptune is drilled into. ChiptuneTrackIndex is the subsong index to play;
        // ChiptuneSourcePath is the source file (or "archive|internal" address).
        public bool IsChiptune { get; set; }
        public int ChiptuneTrackIndex { get; set; } = -1;
        public string ChiptuneSourcePath { get; set; }

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
