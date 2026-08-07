using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using XFiles.FileSystem;

namespace XFiles.Services
{
    /// <summary>
    /// Pure Device Portal helpers shared by DevicePortalService, PortalBrowser and
    /// PortalCache. No HTTP, no UWP — unit-testable on desktop. All logic here is
    /// derived from the WDP REST contract (see docs/portal-appdata/PLAN.md).
    /// </summary>
    public static class PortalCore
    {
        // --- Portal paths (backslash-quirk format: root = "\", one = "\\Settings") ---

        /// <summary>
        /// Builds the child portal path from a parent portal path + child name.
        /// Root ("\") + "Settings" → "\\Settings"; "\\Settings" + "Sub" → "\\Settings\\Sub".
        /// </summary>
        public static string CombinePortalPath(string parent, string childName)
        {
            if (string.IsNullOrEmpty(parent) || parent == "\\")
                return "\\" + childName;
            return parent + "\\" + childName;
        }

        /// <summary>
        /// Physical drive root hosting a WDP known folder, used for free-space checks
        /// (the portal REST API does not expose free space; the app queries the console's
        /// own volume via P/Invoke). LocalAppData lives on the Q: volume; DevelopmentFiles
        /// on the dev-scratch D: volume. Unknown folders return null — the caller skips
        /// the check.
        /// </summary>
        public static string DestinationDriveRoot(string knownFolder)
        {
            if (string.IsNullOrEmpty(knownFolder)) return null;
            if (string.Equals(knownFolder, "LocalAppData", StringComparison.OrdinalIgnoreCase))
                return "Q:\\";
            if (string.Equals(knownFolder, "DevelopmentFiles", StringComparison.OrdinalIgnoreCase))
                return "D:\\";
            return null;
        }

        /// <summary>
        /// WDP entry Type is a bitmask; bit 0x10 marks directories.
        /// </summary>
        public static bool IsDirectoryType(int type) => (type & 0x10) != 0;

        /// <summary>
        /// Directory-first, then case-insensitive alphabetical — the listing convention
        /// used by both the local scanner and the portal browser.
        /// </summary>
        public static int CompareDirectoryEntries(bool aDir, string aName, bool bDir, string bName)
        {
            if (aDir != bDir) return aDir ? -1 : 1;
            return string.Compare(aName, bName, StringComparison.OrdinalIgnoreCase);
        }

        // --- WDP query strings ---

        public static string BuildListFilesQuery(string knownFolder, string packageFullName, string portalPath)
        {
            return "/api/filesystem/apps/files?knownfolderid=" + Uri.EscapeDataString(knownFolder) +
                   "&packagefullname=" + Uri.EscapeDataString(packageFullName ?? "") +
                   "&path=" + Uri.EscapeDataString(portalPath);
        }

        /// <summary>
        /// Download query — the filename is a SEPARATE parameter; path is the parent
        /// folder only (WDP quirk).
        /// </summary>
        public static string BuildDownloadFileQuery(string knownFolder, string packageFullName, string portalPath, string fileName)
        {
            return "/api/filesystem/apps/file?knownfolderid=" + Uri.EscapeDataString(knownFolder) +
                   "&filename=" + Uri.EscapeDataString(fileName) +
                   "&packagefullname=" + Uri.EscapeDataString(packageFullName ?? "") +
                   "&path=" + Uri.EscapeDataString(portalPath);
        }

        // --- Installed-package display names ---

        /// <summary>
        /// Strips the publisher suffix ("_8wekyb3d8bbwe") and takes the last dot segment
        /// of a package family name, for the disambiguation suffix on name collisions.
        /// </summary>
        public static string ShortFamilyName(string familyName)
        {
            if (string.IsNullOrEmpty(familyName)) return null;
            string core = familyName;
            int lastUnderscore = familyName.LastIndexOf('_');
            if (lastUnderscore > 0)
                core = familyName.Substring(0, lastUnderscore);
            int lastDot = core.LastIndexOf('.');
            return lastDot >= 0 && lastDot < core.Length - 1
                ? core.Substring(lastDot + 1)
                : core;
        }

        /// <summary>
        /// Deduplicates package display names. First use keeps the base name; collisions
        /// append a short family name (or a counter when none is derivable). The caller's
        /// <paramref name="usedNames"/> dictionary is updated as a side effect.
        /// </summary>
        public static string BuildPackageDisplayName(string baseName, Dictionary<string, int> usedNames, string familyName)
        {
            if (usedNames.TryGetValue(baseName, out int n))
            {
                usedNames[baseName] = n + 1;
                string shortName = ShortFamilyName(familyName);
                return string.IsNullOrEmpty(shortName)
                    ? $"{baseName} ({n + 1})"
                    : $"{baseName} ({shortName})";
            }
            usedNames[baseName] = 1;
            return baseName;
        }

        // --- Portal cache key / hash ---

        public static string GetCacheKey(PortalFileEntry entry)
            => GetCacheKey(entry.KnownFolder, entry.PackageFullName, entry.PortalPath, entry.Name, entry.FileSize, entry.DateCreated);

        public static string GetCacheKey(string knownFolder, string packageFullName, string portalPath, string name, long size, long dateCreated)
        {
            var sb = new StringBuilder();
            sb.Append(knownFolder).Append('|')
              .Append(packageFullName).Append('|')
              .Append(portalPath).Append('|')
              .Append(name).Append('|')
              .Append(size).Append('|')
              .Append(dateCreated);
            return sb.ToString();
        }

        /// <summary>
        /// 40-char lowercase hex SHA-1 of the cache key — the cached file name.
        /// </summary>
        public static string ComputeCacheHash(string key)
        {
            using (var sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
                var sb = new StringBuilder(40);
                foreach (byte b in hash)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>
        /// Lowercased file extension (with dot) used to keep preview/playback type
        /// detection working on the hashed cache file name. Empty for missing or
        /// over-long extensions.
        /// </summary>
        public static string SanitizeCacheExtension(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            string ext = Path.GetExtension(name);
            if (string.IsNullOrEmpty(ext) || ext.Length > 16) return "";
            return ext.ToLowerInvariant();
        }

        // --- Entry mapping ---

        /// <summary>
        /// Converts a portal-aware FileEntry into the service-level portal entry used
        /// by cache/download/write operations.
        /// </summary>
        public static PortalFileEntry ToPortalEntry(FileEntry e)
        {
            return new PortalFileEntry
            {
                Name = e.Name,
                IsDirectory = e.IsDirectory,
                FileSize = e.SizeBytes,
                DateCreated = e.LastModified.HasValue ? e.LastModified.Value.ToFileTime() : 0,
                KnownFolder = e.PortalKnownFolder,
                PackageFullName = e.PortalPackageFullName ?? "",
                PortalPath = e.PortalPath
            };
        }
    }
}
