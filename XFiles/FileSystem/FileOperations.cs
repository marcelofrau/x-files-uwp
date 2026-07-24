using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SharpCompress.Archives;

namespace XFiles.FileSystem
{
    /// <summary>
    /// File operations using Win32 *FromApp P/Invoke variants.
    /// System.IO.File.* works with broadFileSystemAccess in UWP.
    /// Inside archives, only Extract is supported (via SharpCompress).
    /// </summary>
    public static class FileOperations
    {
        #region P/Invoke

        [DllImport("api-ms-win-core-file-fromapp-l1-1-0.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CopyFileFromAppW(string lpExistingFileName, string lpNewFileName, bool bFailIfExists);

        [DllImport("api-ms-win-core-file-fromapp-l1-1-0.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileFromAppW(string lpExistingFileName, string lpNewFileName);

        [DllImport("api-ms-win-core-file-fromapp-l1-1-0.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool DeleteFileFromAppW(string lpFileName);

        [DllImport("api-ms-win-core-file-fromapp-l1-1-0.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool RemoveDirectoryFromAppW(string lpPathName);

        [DllImport("api-ms-win-core-file-fromapp-l1-1-0.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateDirectoryFromAppW(string lpPathName, IntPtr lpSecurityAttributes);

        #endregion

        public enum OperationResult
        {
            Success,
            Failed,
            Cancelled
        }

        public class OperationProgress
        {
            public string FileName { get; set; }
            public double PercentComplete { get; set; }
            public long BytesCopied { get; set; }
            public long TotalBytes { get; set; }
        }

        /// <summary>
        /// Copy file from source to destination directory.
        /// </summary>
        public static async Task<OperationResult> CopyAsync(string sourcePath, string destDir, IProgress<OperationProgress> progress = null)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string fileName = Path.GetFileName(sourcePath);
                    string destPath = Path.Combine(destDir, fileName);

                    // Handle name collision — append (1), (2), etc.
                    destPath = GetUniqueFilePath(destPath);

                    Log.Information("FileOperations.Copy: {Source} -> {Dest}", sourcePath, destPath);

                    bool ok = CopyFileFromAppW(sourcePath, destPath, false);
                    if (!ok)
                    {
                        int err = Marshal.GetLastWin32Error();
                        Log.Warning("FileOperations.Copy failed: error {Error}", err);
                        return OperationResult.Failed;
                    }

                    progress?.Report(new OperationProgress
                    {
                        FileName = fileName,
                        PercentComplete = 100,
                        BytesCopied = GetFileSize(destPath),
                        TotalBytes = GetFileSize(destPath)
                    });

                    return OperationResult.Success;
                }
                catch (Exception ex)
                {
                    Log.Warning("FileOperations.Copy exception: {Error}", ex.Message);
                    return OperationResult.Failed;
                }
            });
        }

        /// <summary>
        /// Copy directory recursively from source to destination.
        /// </summary>
        public static async Task<OperationResult> CopyDirectoryAsync(string sourceDir, string destDir, IProgress<OperationProgress> progress = null)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string dirName = Path.GetFileName(sourceDir.TrimEnd('\\', '/'));
                    string destPath = Path.Combine(destDir, dirName);
                    destPath = GetUniqueDirectoryPath(destPath);

                    Log.Information("FileOperations.CopyDirectory: {Source} -> {Dest}", sourceDir, destPath);
                    CreateDirectoryFromAppW(destPath, IntPtr.Zero);

                    return CopyDirectoryRecursive(sourceDir, destPath, progress);
                }
                catch (Exception ex)
                {
                    Log.Warning("FileOperations.CopyDirectory exception: {Error}", ex.Message);
                    return OperationResult.Failed;
                }
            });
        }

        private static OperationResult CopyDirectoryRecursive(string sourceDir, string destDir, IProgress<OperationProgress> progress)
        {
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                bool ok = CopyFileFromAppW(file, destFile, false);
                if (!ok)
                {
                    Log.Warning("FileOperations.CopyDirectory: failed to copy {File}", file);
                    return OperationResult.Failed;
                }

                progress?.Report(new OperationProgress
                {
                    FileName = Path.GetFileName(file),
                    PercentComplete = -1
                });
            }

            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
                CreateDirectoryFromAppW(destSubDir, IntPtr.Zero);
                var result = CopyDirectoryRecursive(dir, destSubDir, progress);
                if (result != OperationResult.Success) return result;
            }

            return OperationResult.Success;
        }

        /// <summary>
        /// Move file from source to destination directory.
        /// </summary>
        public static async Task<OperationResult> MoveAsync(string sourcePath, string destDir, IProgress<OperationProgress> progress = null)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string fileName = Path.GetFileName(sourcePath);
                    string destPath = Path.Combine(destDir, fileName);
                    destPath = GetUniqueFilePath(destPath);

                    Log.Information("FileOperations.Move: {Source} -> {Dest}", sourcePath, destPath);

                    bool ok = MoveFileFromAppW(sourcePath, destPath);
                    if (!ok)
                    {
                        int err = Marshal.GetLastWin32Error();
                        Log.Warning("FileOperations.Move failed: error {Error}", err);

                        // Fallback: copy + delete (MoveFile fails across volumes)
                        Log.Information("FileOperations.Move: trying copy+delete fallback");
                        ok = CopyFileFromAppW(sourcePath, destPath, false);
                        if (!ok)
                        {
                            return OperationResult.Failed;
                        }
                        DeleteFileFromAppW(sourcePath);
                    }

                    progress?.Report(new OperationProgress
                    {
                        FileName = fileName,
                        PercentComplete = 100
                    });

                    return OperationResult.Success;
                }
                catch (Exception ex)
                {
                    Log.Warning("FileOperations.Move exception: {Error}", ex.Message);
                    return OperationResult.Failed;
                }
            });
        }

        /// <summary>
        /// Rename file or directory.
        /// </summary>
        public static async Task<OperationResult> RenameAsync(string path, string newName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string dir = Path.GetDirectoryName(path);
                    string newPath = Path.Combine(dir, newName);

                    Log.Information("FileOperations.Rename: {Old} -> {New}", path, newPath);

                    bool ok = MoveFileFromAppW(path, newPath);
                    if (!ok)
                    {
                        int err = Marshal.GetLastWin32Error();
                        Log.Warning("FileOperations.Rename failed: error {Error}", err);
                        return OperationResult.Failed;
                    }

                    return OperationResult.Success;
                }
                catch (Exception ex)
                {
                    Log.Warning("FileOperations.Rename exception: {Error}", ex.Message);
                    return OperationResult.Failed;
                }
            });
        }

        /// <summary>
        /// Delete file. Caller must confirm before calling.
        /// </summary>
        public static async Task<OperationResult> DeleteAsync(string path)
        {
            return await Task.Run(() =>
            {
                try
                {
                    Log.Information("FileOperations.Delete: {Path}", path);

                    if (File.Exists(path))
                    {
                        bool ok = DeleteFileFromAppW(path);
                        if (!ok)
                        {
                            int err = Marshal.GetLastWin32Error();
                            Log.Warning("FileOperations.Delete failed: error {Error}", err);
                            return OperationResult.Failed;
                        }
                    }
                    else if (Directory.Exists(path))
                    {
                        bool ok = RemoveDirectoryFromAppW(path);
                        if (!ok)
                        {
                            int err = Marshal.GetLastWin32Error();
                            Log.Warning("FileOperations.DeleteDirectory failed: error {Error}", err);
                            return OperationResult.Failed;
                        }
                    }

                    return OperationResult.Success;
                }
                catch (Exception ex)
                {
                    Log.Warning("FileOperations.Delete exception: {Error}", ex.Message);
                    return OperationResult.Failed;
                }
            });
        }

        /// <summary>
        /// Delete directory recursively. Caller must confirm before calling.
        /// </summary>
        public static async Task<OperationResult> DeleteDirectoryAsync(string path)
        {
            return await Task.Run(() =>
            {
                try
                {
                    Log.Information("FileOperations.DeleteDirectory: {Path}", path);

                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, true);
                    }

                    return OperationResult.Success;
                }
                catch (Exception ex)
                {
                    Log.Warning("FileOperations.DeleteDirectory exception: {Error}", ex.Message);
                    return OperationResult.Failed;
                }
            });
        }

        /// <summary>
        /// Check if archive has a single root folder containing all entries.
        /// Returns the root folder name if single-root, or null otherwise.
        /// E.g. "myarchive.zip" with "folder/file1.txt", "folder/file2.txt" → "folder".
        /// But "file1.txt", "folder/file2.txt" → null (mixed root).
        /// </summary>
        public static string GetSingleRootFolder(string archivePath)
        {
            try
            {
                using (var stream = Win32FileStream.OpenRead(archivePath))
                {
                    if (stream == null) return null;

                    using (var archive = SharpCompress.Archives.ArchiveFactory.Open(stream))
                    {
                        string rootFolder = null;
                        foreach (var entry in archive.Entries)
                        {
                            if (entry.IsDirectory) continue;
                            string key = entry.Key;
                            int sep = key.IndexOf('/');
                            if (sep < 0) sep = key.IndexOf('\\');
                            if (sep < 0) return null; // file at root level

                            string folder = key.Substring(0, sep);
                            if (rootFolder == null)
                                rootFolder = folder;
                            else if (!string.Equals(rootFolder, folder, StringComparison.OrdinalIgnoreCase))
                                return null; // multiple root folders
                        }
                        return rootFolder;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("GetSingleRootFolder error: {Error}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Extract archive to destination directory.
        /// conflictCallback receives the conflicting filename and returns:
        ///   0=Skip, 1=Overwrite, 2=OverwriteAll.
        /// If null, files are overwritten silently (existing behavior).
        /// </summary>
        public static async Task<OperationResult> ExtractAsync(
            string archivePath, string destDir,
            IProgress<OperationProgress> progress = null,
            Func<string, Task<int>> conflictCallback = null)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    Log.Information("FileOperations.Extract: {Archive} -> {Dest}", archivePath, destDir);

                    if (!Directory.Exists(destDir))
                    {
                        Log.Information("FileOperations.Extract: creating dest dir {Dir}", destDir);
                        Directory.CreateDirectory(destDir);
                    }

                    using (var stream = Win32FileStream.OpenRead(archivePath))
                    {
                        if (stream == null)
                        {
                            Log.Warning("FileOperations.Extract: cannot open archive {Path}", archivePath);
                            return OperationResult.Failed;
                        }

                        using (var archive = SharpCompress.Archives.ArchiveFactory.Open(stream))
                        {
                            var options = new SharpCompress.Common.ExtractionOptions
                            {
                                ExtractFullPath = true,
                                Overwrite = true
                            };

                            bool overwriteAll = conflictCallback == null;

                            foreach (var entry in archive.Entries)
                            {
                                if (entry.IsDirectory) continue;

                                progress?.Report(new OperationProgress
                                {
                                    FileName = entry.Key,
                                    PercentComplete = -1
                                });

                                if (!overwriteAll && conflictCallback != null)
                                {
                                    string destPath = System.IO.Path.Combine(destDir, entry.Key);
                                    if (File.Exists(destPath))
                                    {
                                        int decision = await conflictCallback(entry.Key);
                                        switch (decision)
                                        {
                                            case 0: // Skip
                                                Log.Information("Extract: skipping {File}", entry.Key);
                                                continue;
                                            case 1: // Overwrite this one
                                                Log.Information("Extract: overwriting {File}", entry.Key);
                                                break;
                                            case 2: // Overwrite all remaining
                                                Log.Information("Extract: overwrite all remaining");
                                                overwriteAll = true;
                                                break;
                                        }
                                    }
                                }

                                entry.WriteToDirectory(destDir, options);
                            }
                        }
                    }

                    progress?.Report(new OperationProgress
                    {
                        FileName = "",
                        PercentComplete = 100
                    });

                    return OperationResult.Success;
                }
                catch (Exception ex)
                {
                    Log.Warning("FileOperations.Extract exception: {Error}", ex.Message);
                    return OperationResult.Failed;
                }
            });
        }

        private static string GetUniqueFilePath(string path)
        {
            if (!File.Exists(path)) return path;

            string dir = Path.GetDirectoryName(path);
            string nameNoExt = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);

            for (int i = 1; ; i++)
            {
                string candidate = Path.Combine(dir, $"{nameNoExt} ({i}){ext}");
                if (!File.Exists(candidate)) return candidate;
            }
        }

        private static string GetUniqueDirectoryPath(string path)
        {
            if (!Directory.Exists(path)) return path;

            string parent = Path.GetDirectoryName(path);
            string name = Path.GetFileName(path);

            for (int i = 1; ; i++)
            {
                string candidate = Path.Combine(parent, $"{name} ({i})");
                if (!Directory.Exists(candidate)) return candidate;
            }
        }

        private static long GetFileSize(string path)
        {
            try
            {
                var info = new FileInfo(path);
                return info.Exists ? info.Length : 0;
            }
            catch
            {
                return 0;
            }
        }

        public static async Task<OperationResult> CreateFolderAsync(string folderPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    folderPath = GetUniqueDirectoryPath(folderPath);
                    Log.Information("FileOperations.CreateFolder: {Path}", folderPath);
                    bool ok = CreateDirectoryFromAppW(folderPath, IntPtr.Zero);
                    if (!ok)
                    {
                        int err = Marshal.GetLastWin32Error();
                        Log.Warning("FileOperations.CreateFolder: CreateDirectoryFromAppW failed, Win32 error={Error}", err);
                        return OperationResult.Failed;
                    }
                    return OperationResult.Success;
                }
                catch (Exception ex)
                {
                    Log.Warning("FileOperations.CreateFolder exception: {Error}", ex.Message);
                    return OperationResult.Failed;
                }
            });
        }

        public static async Task<OperationResult> CreateZipAsync(string sourcePath, string zipPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    zipPath = GetUniqueFilePath(zipPath);
                    Log.Information("FileOperations.CreateZip: {Source} -> {Zip}", sourcePath, zipPath);

                    using (var archive = SharpCompress.Archives.Zip.ZipArchive.Create())
                    {
                        MemoryStream singleFileStream = null;

                        if (File.Exists(sourcePath))
                        {
                            var fileName = System.IO.Path.GetFileName(sourcePath);
                            var data = File.ReadAllBytes(sourcePath);
                            singleFileStream = new MemoryStream(data);
                            archive.AddEntry(fileName, singleFileStream, data.Length);
                        }
                        else if (Directory.Exists(sourcePath))
                        {
                            archive.AddAllFromDirectory(sourcePath);
                        }
                        else
                        {
                            Log.Warning("FileOperations.CreateZip: source not found {Source}", sourcePath);
                            return OperationResult.Failed;
                        }

                        archive.SaveTo(zipPath, SharpCompress.Common.CompressionType.Deflate);
                        singleFileStream?.Dispose();
                    }

                    return OperationResult.Success;
                }
                catch (Exception ex)
                {
                    Log.Warning("FileOperations.CreateZip exception: {Error}", ex.Message);
                    return OperationResult.Failed;
                }
            });
        }

        /// <summary>
        /// Recursively list all files and folders under path.
        /// Returns (files, folderCount). For a single file, returns just that file.
        /// </summary>
        public static async Task<(List<string> entries, int folderCount)> ListRecursiveAsync(string path)
        {
            return await Task.Run(() =>
            {
                var entries = new List<string>();
                int folderCount = 0;

                try
                {
                    if (File.Exists(path))
                    {
                        entries.Add(path);
                    }
                    else if (Directory.Exists(path))
                    {
                        entries.Add(path + "\\");
                        folderCount++;
                        ListDirectoryRecursive(path, entries, ref folderCount);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning("FileOperations.ListRecursiveAsync error: {Error}", ex.Message);
                }

                return (entries, folderCount);
            });
        }

        private static void ListDirectoryRecursive(string dir, List<string> entries, ref int folderCount)
        {
            try
            {
                foreach (var file in Directory.GetFiles(dir))
                {
                    entries.Add(file);
                }
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    entries.Add(sub + "\\");
                    folderCount++;
                    ListDirectoryRecursive(sub, entries, ref folderCount);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("FileOperations.ListDirectoryRecursive: {Dir} error: {Error}", dir, ex.Message);
            }
        }
    }
}
