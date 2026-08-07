using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
            folders.Sort(StringComparer.OrdinalIgnoreCase);
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
        /// Deduplicates per-family entries (the portal reports one entry per package
        /// architecture/resource variant) and sorts by display name. Display-name
        /// collisions across families get a short family suffix.
        /// </summary>
        public async Task<List<FileEntry>> ListPackagesAsync()
        {
            var packages = await DevicePortalService.GetInstalledPackagesAsync();

            var byFamily = packages
                .GroupBy(p => p.FamilyName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            var usedNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var entries = new List<FileEntry>(byFamily.Count);
            foreach (var p in byFamily)
            {
                string baseName = string.IsNullOrEmpty(p.DisplayName) ? p.FullName : p.DisplayName;
                string name = PortalCore.BuildPackageDisplayName(baseName, usedNames, p.FamilyName);

                entries.Add(new FileEntry
                {
                    Name = name,
                    FullPath = null,
                    IsDirectory = true,
                    IsVirtual = true,
                    IsPortal = true,
                    PortalKnownFolder = "LocalAppData",
                    PortalPackageFullName = p.FullName
                });
            }
            entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            Log.Info("PortalBrowser.Packages: {Count} packages (deduped from {Raw})", entries.Count, packages.Count);
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
            return SortDirectory(entries);
        }

        /// <summary>
        /// Folders first, then files, each alphabetical — same convention as the local
        /// directory scanner.
        /// </summary>
        private static List<FileEntry> SortDirectory(List<FileEntry> entries)
        {
            var dirs = new List<FileEntry>();
            var files = new List<FileEntry>();
            foreach (var e in entries)
            {
                if (e.IsDirectory) dirs.Add(e);
                else files.Add(e);
            }
            dirs.Sort((a, b) => PortalCore.CompareDirectoryEntries(a.IsDirectory, a.Name, b.IsDirectory, b.Name));
            files.Sort((a, b) => PortalCore.CompareDirectoryEntries(a.IsDirectory, a.Name, b.IsDirectory, b.Name));
            var sorted = new List<FileEntry>(entries.Count);
            sorted.AddRange(dirs);
            sorted.AddRange(files);
            return sorted;
        }

        /// <summary>
        /// Converts a portal-aware FileEntry into the service-level portal entry used
        /// by cache/download/write operations.
        /// </summary>
        public static PortalFileEntry ToPortalEntry(FileEntry e)
            => PortalCore.ToPortalEntry(e);

        /// <summary>
        /// Builds the child portal path from a parent portal path + child name.
        /// Root ("\") + "Settings" → "\\Settings"; "\\Settings" + "Sub" → "\\Settings\\Sub".
        /// </summary>
        public static string CombinePortalPath(string parent, string childName)
            => PortalCore.CombinePortalPath(parent, childName);

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

        /// <summary>
        /// One node in a recursively enumerated portal subtree. RelativePath is the
        /// backslash-relative location under the enumeration root ("" = root itself).
        /// </summary>
        public class PortalTreeItem
        {
            public PortalFileEntry Entry { get; set; }
            public string RelativePath { get; set; }
        }

        /// <summary>
        /// Enumerates a portal directory tree (files + subdirs) with relative paths.
        /// </summary>
        public static async Task<List<PortalTreeItem>> EnumeratePortalTreeAsync(
            string knownFolder, string packageFullName, string portalPath, CancellationToken ct)
        {
            var result = new List<PortalTreeItem>();
            await EnumeratePortalDirAsync(knownFolder, packageFullName ?? "", portalPath, "", result, ct);
            return result;
        }

        private static async Task EnumeratePortalDirAsync(
            string knownFolder, string packageFullName, string portalPath,
            string relPrefix, List<PortalTreeItem> acc, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var items = await DevicePortalService.ListPortalFilesAsync(knownFolder, packageFullName, portalPath);
            foreach (var it in items)
            {
                ct.ThrowIfCancellationRequested();
                string rel = string.IsNullOrEmpty(relPrefix) ? it.Name : relPrefix + "\\" + it.Name;
                acc.Add(new PortalTreeItem { Entry = it, RelativePath = rel });
                if (it.IsDirectory)
                {
                    await EnumeratePortalDirAsync(knownFolder, packageFullName,
                        CombinePortalPath(portalPath, it.Name), rel, acc, ct);
                }
            }
        }

        /// <summary>
        /// Copies a portal entry (file or directory tree) into a local destination
        /// directory. Progress mirrors the local copy dialog.
        /// </summary>
        public static async Task CopyPortalToLocalAsync(PortalFileEntry root, string destDir,
            IProgress<FileOperations.OperationProgress> progress, CancellationToken ct)
        {
            if (!root.IsDirectory)
            {
                string dest = Path.Combine(destDir, root.Name);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                await DownloadPortalFileWithProgressAsync(root, dest, root.FileSize, 0, 1, 0, root.FileSize, progress, ct);
                return;
            }

            string rootPath = CombinePortalPath(root.PortalPath, root.Name);
            var tree = await EnumeratePortalTreeAsync(root.KnownFolder, root.PackageFullName, rootPath, ct);
            var files = tree.Where(t => !t.Entry.IsDirectory).ToList();
            var dirs = tree.Where(t => t.Entry.IsDirectory).ToList();

            long totalBytes = files.Sum(t => t.Entry.FileSize);
            int fileCount = files.Count;

            // Create all local folders first.
            foreach (var d in dirs)
                Directory.CreateDirectory(Path.Combine(destDir, d.RelativePath));

            long done = 0;
            int idx = 0;
            foreach (var t in files)
            {
                ct.ThrowIfCancellationRequested();
                string dest = Path.Combine(destDir, t.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                var entry = t.Entry;
                await DownloadPortalFileWithProgressAsync(entry, dest, entry.FileSize,
                    idx, fileCount, done, totalBytes, progress, ct);
                done += entry.FileSize;
                idx++;
            }
        }

        private static async Task DownloadPortalFileWithProgressAsync(PortalFileEntry entry, string destPath,
            long fileSize, int fileIndex, int fileTotal, long bytesDone, long totalBytes,
            IProgress<FileOperations.OperationProgress> progress, CancellationToken ct)
        {
            using (var fs = File.Create(destPath))
            {
                await DevicePortalService.DownloadPortalFileAsync(entry, fs, new Progress<double>(p =>
                {
                    progress?.Report(new FileOperations.OperationProgress
                    {
                        FileName = entry.Name,
                        FileIndex = fileIndex,
                        FileTotal = fileTotal,
                        PercentComplete = fileTotal > 0 ? (double)fileIndex / fileTotal * 100.0 : -1,
                        BytesCopied = bytesDone + (long)(p * fileSize),
                        TotalBytes = totalBytes
                    });
                }));
            }
        }

        /// <summary>
        /// Copies a portal entry (file or directory tree) into another portal location
        /// by round-tripping through a local staging directory: download, then upload.
        /// Progress covers both phases.
        /// </summary>
        public static async Task CopyPortalToPortalAsync(PortalFileEntry source,
            string destKnownFolder, string destPackageFullName, string destPortalPath,
            string stagingDir,
            IProgress<FileOperations.OperationProgress> progress, CancellationToken ct)
        {
            string uploadPath;
            if (source.IsDirectory)
            {
                // Staging wrapper folder preserves the root name for the upload.
                string wrapper = Path.Combine(stagingDir, source.Name);
                Directory.CreateDirectory(wrapper);
                await CopyPortalToLocalAsync(source, wrapper, progress, ct);
                uploadPath = wrapper;
            }
            else
            {
                await CopyPortalToLocalAsync(source, stagingDir, progress, ct);
                uploadPath = Path.Combine(stagingDir, source.Name);
            }

            await UploadLocalToPortalAsync(uploadPath, destKnownFolder, destPackageFullName, destPortalPath, progress, ct);
        }

        /// <summary>
        /// Uploads a local file or directory tree into a portal directory. Recursively
        /// creates portal folders to mirror the local layout. Progress mirrors the local
        /// copy dialog.
        /// </summary>
        public static async Task UploadLocalToPortalAsync(string localPath, string knownFolder,
            string packageFullName, string portalPath,
            IProgress<FileOperations.OperationProgress> progress, CancellationToken ct)
        {
            if (File.Exists(localPath))
            {
                string name = Path.GetFileName(localPath);
                long size = new FileInfo(localPath).Length;
                byte[] bytes = await Task.Run(() => File.ReadAllBytes(localPath), ct);
                await DevicePortalService.UploadPortalFileAsync(knownFolder, packageFullName, portalPath,
                    name, bytes, new Progress<double>(p =>
                        progress?.Report(new FileOperations.OperationProgress
                        {
                            FileName = name,
                            PercentComplete = (int)(p * 100),
                            BytesCopied = (long)(p * size),
                            TotalBytes = size
                        })));
                return;
            }

            if (!Directory.Exists(localPath))
                throw new DirectoryNotFoundException("Source path not found: " + localPath);

            string rootName = Path.GetFileName(localPath.TrimEnd('\\', '/'));
            var files = Directory.EnumerateFiles(localPath, "*", SearchOption.AllDirectories).ToList();
            long totalBytes = files.Sum(f => new FileInfo(f).Length);
            int fileCount = files.Count;

            long done = 0;
            int idx = 0;
            foreach (var f in files)
            {
                ct.ThrowIfCancellationRequested();
                string rel = f.Substring(localPath.Length).TrimStart('\\');
                string relDir = Path.GetDirectoryName(rel) ?? "";
                string name = Path.GetFileName(f);

                // Mirror the folder layout on the portal (root folder name included),
                // creating dirs as needed.
                var segments = new List<string> { rootName };
                if (!string.IsNullOrEmpty(relDir))
                    segments.AddRange(relDir.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries));

                string cur = portalPath;
                foreach (var seg in segments)
                {
                    try
                    {
                        await DevicePortalService.CreatePortalFolderAsync(knownFolder, packageFullName, cur, seg);
                    }
                    catch (Exception ex)
                    {
                        // Folder likely already exists — the upload below will surface real errors.
                        Log.Dbg("PortalBrowser.Upload: create folder '{Seg}' in {Path}: {Message}", seg, cur, ex.Message);
                    }
                    cur = CombinePortalPath(cur, seg);
                }

                long size = new FileInfo(f).Length;
                byte[] bytes = await Task.Run(() => File.ReadAllBytes(f), ct);
                await DevicePortalService.UploadPortalFileAsync(knownFolder, packageFullName, cur, name, bytes, null);
                done += size;
                idx++;
                progress?.Report(new FileOperations.OperationProgress
                {
                    FileName = name,
                    FileIndex = idx,
                    FileTotal = fileCount,
                    PercentComplete = fileCount > 0 ? (double)idx / fileCount * 100.0 : -1,
                    BytesCopied = done,
                    TotalBytes = totalBytes
                });
            }
        }
    }
}
