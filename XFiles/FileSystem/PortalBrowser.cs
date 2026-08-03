using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using XFiles.Services;

namespace XFiles.FileSystem
{
    /// <summary>
    /// Virtual listing provider for the Device Portal AppData browser. Mirrors
    /// ArchiveBrowser: produces FileEntry trees (portal fields set) backed by the
    /// portal REST API. Owned by ColumnNavigator.
    /// Levels: root "User Folders" → known folders → (LocalAppData → packages) →
    /// portal file tree.
    /// </summary>
    public class PortalBrowser
    {
        public const string PortalRootName = "User Folders";

        /// <summary>
        /// Known folders exposed by the portal (DevelopmentFiles, LocalAppData).
        /// </summary>
        public async Task<List<FileEntry>> ListKnownFoldersAsync()
        {
            var folders = await DevicePortalService.GetKnownFoldersAsync();
            var entries = folders.Select(kf => new FileEntry
            {
                Name = kf,
                FullPath = null,
                IsDirectory = true,
                IsVirtual = true,
                IsPortal = true,
                PortalKnownFolder = kf
            }).ToList();

            Log.Info("PortalBrowser.KnownFolders: {Count} folders: {Names}", entries.Count, string.Join(", ", folders));
            return entries;
        }

        /// <summary>
        /// Installed (non-system) packages as virtual dirs under LocalAppData.
        /// </summary>
        public async Task<List<FileEntry>> ListPackagesAsync()
        {
            var packages = await DevicePortalService.GetInstalledPackagesAsync();
            var entries = packages.Select(p => new FileEntry
            {
                Name = string.IsNullOrEmpty(p.DisplayName) ? p.FullName : p.DisplayName,
                FullPath = null,
                IsDirectory = true,
                IsVirtual = true,
                IsPortal = true,
                PortalKnownFolder = "LocalAppData",
                PortalPackageFullName = p.FullName
            }).ToList();

            Log.Info("PortalBrowser.Packages: {Count} packages", entries.Count);
            return entries;
        }

        /// <summary>
        /// Lists a portal directory. portalPath is the backslash-quirk format
        /// (root = "\", one level = "\\Settings"). packageFullName may be "" for
        /// DevelopmentFiles.
        /// </summary>
        public async Task<List<FileEntry>> ListDirectoryAsync(string knownFolder, string packageFullName, string portalPath)
        {
            var items = await DevicePortalService.ListPortalFilesAsync(knownFolder, packageFullName ?? "", portalPath);
            var entries = items.Select(f => new FileEntry
            {
                Name = f.Name,
                FullPath = null,
                IsDirectory = f.IsDirectory,
                IsVirtual = f.IsDirectory,
                IsPortal = true,
                SizeBytes = f.FileSize,
                LastModified = f.DateCreated > 0
                    ? (DateTimeOffset?)DateTimeOffset.FromFileTime(f.DateCreated)
                    : null,
                IsArchive = !f.IsDirectory && ArchiveBrowser.IsArchiveFile(f.Name),
                PortalKnownFolder = knownFolder,
                PortalPackageFullName = packageFullName ?? "",
                PortalPath = f.PortalPath
            }).ToList();

            Log.Info("PortalBrowser.List: {Known}/{Pkg}{Path} => {Count} entries",
                knownFolder, packageFullName ?? "", portalPath, entries.Count);
            return entries;
        }

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
        /// Ensures a portal file is cached locally; returns the local path.
        /// </summary>
        public static Task<string> DownloadToCacheAsync(PortalFileEntry entry, IProgress<double> progress)
            => PortalCache.EnsureAsync(entry, progress);

        /// <summary>
        /// Downloads a portal file to an explicit local destination (copy-to-disk).
        /// </summary>
        public static async Task DownloadToDiskAsync(PortalFileEntry entry, string destinationPath, IProgress<double> progress)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
            using (var fs = File.Create(destinationPath))
                await DevicePortalService.DownloadPortalFileAsync(entry, fs, progress);
        }
    }
}
