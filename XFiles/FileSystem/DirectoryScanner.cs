using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;

namespace XFiles.FileSystem
{
    public static class DirectoryScanner
    {
        private static readonly HashSet<string> ArchiveExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".zip", ".7z", ".rar"
            };

        public static async Task<List<FileEntry>> ScanAsync(string path)
        {
            if (string.IsNullOrEmpty(path))
                return ScanRoot();

            return await ScanDirectoryAsync(path);
        }

        private static List<FileEntry> ScanRoot()
        {
            Log.Verbose("Scanning root — enumerating logical drives");
            var entries = new List<FileEntry>();

            foreach (var drive in DriveInfo.GetDrives())
            {
                entries.Add(new FileEntry
                {
                    Name = drive.Name,
                    FullPath = drive.Name,
                    IsDirectory = true
                });
                Log.Verbose("  Drive found: {Drive} ({Type})", drive.Name, drive.DriveType);
            }
            Log.Information("Root scan complete — {DriveCount} drives detected", entries.Count);

            try
            {
                string localPath = ApplicationData.Current.LocalFolder.Path;
                entries.Insert(0, new FileEntry
                {
                    Name = "[App Data]",
                    FullPath = localPath,
                    IsDirectory = true
                });
                Log.Verbose("  [App Data] entry added: {Path}", localPath);
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to get LocalFolder — skipping [App Data] entry", ex);
            }

            Log.Information("Root scan result: {Count} entries total", entries.Count);
            return entries;
        }

        private static async Task<List<FileEntry>> ScanDirectoryAsync(string path)
        {
            Log.Verbose("Scanning directory: {Path}", path);
            var entries = new List<FileEntry>();

            string parent = Directory.GetParent(path)?.FullName;
            entries.Add(new FileEntry { Name = "..", FullPath = parent, IsDirectory = true });

            try
            {
                StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(path);

                var dirs = await folder.GetFoldersAsync();
                foreach (var dir in dirs)
                {
                    entries.Add(new FileEntry
                    {
                        Name = dir.Name,
                        FullPath = dir.Path,
                        IsDirectory = true
                    });
                    Log.Verbose("  DIR: {Name}", dir.Name);
                }

                var files = await folder.GetFilesAsync();
                foreach (var file in files)
                {
                    var props = await file.GetBasicPropertiesAsync();
                    long size = (long)props.Size;
                    entries.Add(new FileEntry
                    {
                        Name = file.Name,
                        FullPath = file.Path,
                        IsDirectory = false,
                        SizeBytes = size,
                        IsArchive = ArchiveExtensions.Contains(Path.GetExtension(file.Name)),
                        LastModified = props.DateModified
                    });
                    Log.Verbose("  FILE: {Name} ({Size})", file.Name, FormatSize(size));
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.Warning("Access denied scanning '{Path}' — '..' entry only", path, ex);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to scan '{Path}'", ex, path);
            }

            Log.Information("Scan '{Path}' complete — {Total} entries", path, entries.Count);
            return entries;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }
}
