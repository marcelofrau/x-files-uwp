using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Common;

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

        [DllImport("api-ms-win-core-file-l1-1-0.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetFileAttributesExFromAppW(string lpFileName, int fInfoLevelId, out WIN32_FILE_ATTRIBUTE_DATA lpFileInformation);

        [StructLayout(LayoutKind.Sequential)]
        private struct WIN32_FILE_ATTRIBUTE_DATA
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
        }

        private const uint INVALID_FILE_ATTRIBUTES = 0xFFFFFFFF;

        [DllImport("api-ms-win-core-file-fromapp-l1-1-0.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindFirstFileExFromAppW(
            string lpFileName,
            int fInfoLevelId,
            out WIN32_FIND_DATA lpFindFileData,
            int fSearchOp,
            IntPtr lpSearchFilter,
            uint dwAdditionalFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool FindNextFileW(IntPtr hFindFile, out WIN32_FIND_DATA lpFindFileData);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FindClose(IntPtr hFindFile);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WIN32_FIND_DATA
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }

        private static List<string> EnumerateFilesRecursive(string dir)
        {
            var files = new List<string>();
            try
            {
                var findData = new WIN32_FIND_DATA();
                IntPtr hFind = FindFirstFileExFromAppW(
                    dir + "\\*", 0, out findData, 0, IntPtr.Zero, 0);
                if (hFind == new IntPtr(-1)) return files;

                do
                {
                    if (findData.cFileName == "." || findData.cFileName == "..") continue;
                    string fullPath = dir + "\\" + findData.cFileName;
                    if ((findData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
                    {
                        files.AddRange(EnumerateFilesRecursive(fullPath));
                    }
                    else
                    {
                        files.Add(fullPath);
                    }
                }
                while (FindNextFileW(hFind, out findData));

                FindClose(hFind);
            }
            catch (Exception ex)
            {
                Log.Warn("EnumerateFilesRecursive: {Dir} error", ex, dir);
            }
            return files;
        }
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

        /// <summary>
        /// P/Invoke-based path existence check. Works in UWP with broadFileSystemAccess
        /// where System.IO.File.Exists / Directory.Exists may fail.
        /// Returns: "file", "directory", or null.
        /// </summary>
        private static string CheckPathType(string path)
        {
            if (GetFileAttributesExFromAppW(path, 0, out var attr))
            {
                if ((attr.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
                    return "directory";
                return "file";
            }
            return null;
        }

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
            public int FileIndex { get; set; }
            public int FileTotal { get; set; }
        }

        /// <summary>
        /// Copy file from source to destination directory.
        /// If sameDir is true, uses "Copy N" naming to avoid overwriting in same directory.
        /// </summary>
        public static async Task<OperationResult> CopyAsync(string sourcePath, string destDir, IProgress<OperationProgress> progress = null, bool sameDir = false, CancellationToken token = default)
        {
            // Directory: delegate to CopyDirectoryAsync
            var pathType = CheckPathType(sourcePath);
            if (pathType == "directory")
            {
                return await CopyDirectoryAsync(sourcePath, destDir, progress, sameDir, token);
            }

            return await Task.Run(() =>
            {
                try
                {
                    if (token.IsCancellationRequested) return OperationResult.Cancelled;

                    string fileName = Path.GetFileName(sourcePath);
                    string destPath = Path.Combine(destDir, fileName);

                    destPath = sameDir ? GetCopyName(destPath) : GetUniqueFilePath(destPath);

                    Log.Info("FileOperations.Copy: {Source} -> {Dest}", sourcePath, destPath);

                    bool ok = CopyFileFromAppW(sourcePath, destPath, false);
                    if (!ok)
                    {
                        int err = Marshal.GetLastWin32Error();
                        Log.Warn("FileOperations.Copy failed: error {Error}", err);
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
                    Log.Warn("FileOperations.Copy exception", ex);
                    return OperationResult.Failed;
                }
            });
        }

        /// <summary>
        /// Copy directory recursively from source to destination.
        /// If sameDir is true, uses "Copy N" naming to avoid overwriting in same directory.
        /// </summary>
        public static async Task<OperationResult> CopyDirectoryAsync(string sourceDir, string destDir, IProgress<OperationProgress> progress = null, bool sameDir = false, CancellationToken token = default)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string dirName = Path.GetFileName(sourceDir.TrimEnd('\\', '/'));
                    string destPath = Path.Combine(destDir, dirName);
                    destPath = sameDir ? GetCopyName(destPath) : GetUniqueDirectoryPath(destPath);

                    Log.Info("FileOperations.CopyDirectory: {Source} -> {Dest}", sourceDir, destPath);
                    CreateDirectoryFromAppW(destPath, IntPtr.Zero);

                    return CopyDirectoryRecursive(sourceDir, destPath, progress, token);
                }
                catch (Exception ex)
                {
                    Log.Warn("FileOperations.CopyDirectory exception", ex);
                    return OperationResult.Failed;
                }
            });
        }

        private static OperationResult CopyDirectoryRecursive(string sourceDir, string destDir, IProgress<OperationProgress> progress, CancellationToken token = default)
        {
            try
            {
                var findData = new WIN32_FIND_DATA();
                IntPtr hFind = FindFirstFileExFromAppW(
                    sourceDir + "\\*", 0, out findData, 0, IntPtr.Zero, 0);
                if (hFind == new IntPtr(-1)) return OperationResult.Success;

                do
                {
                    if (token.IsCancellationRequested) { FindClose(hFind); return OperationResult.Cancelled; }

                    if (findData.cFileName == "." || findData.cFileName == "..") continue;
                    string fullPath = sourceDir + "\\" + findData.cFileName;

                    if ((findData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
                    {
                        string destSubDir = destDir + "\\" + findData.cFileName;
                        CreateDirectoryFromAppW(destSubDir, IntPtr.Zero);
                        var result = CopyDirectoryRecursive(fullPath, destSubDir, progress, token);
                        if (result != OperationResult.Success) { FindClose(hFind); return result; }
                    }
                    else
                    {
                        string destFile = destDir + "\\" + findData.cFileName;
                        bool ok = CopyFileFromAppW(fullPath, destFile, false);
                        if (!ok)
                        {
                            Log.Warn("FileOperations.CopyDirectory: failed to copy {File}", fullPath);
                            FindClose(hFind);
                            return OperationResult.Failed;
                        }

                        progress?.Report(new OperationProgress
                        {
                            FileName = findData.cFileName,
                            PercentComplete = -1
                        });
                    }
                }
                while (FindNextFileW(hFind, out findData));

                FindClose(hFind);
            }
            catch (Exception ex)
            {
                Log.Warn("FileOperations.CopyDirectoryRecursive: {Dir} error", ex, sourceDir);
                return OperationResult.Failed;
            }

            return OperationResult.Success;
        }

        /// <summary>
        /// Move file or directory from source to destination directory.
        /// </summary>
        public static async Task<OperationResult> MoveAsync(string sourcePath, string destDir, IProgress<OperationProgress> progress = null, CancellationToken token = default)
        {
            var pathType = CheckPathType(sourcePath);
            if (pathType == "directory")
            {
                return await MoveDirectoryAsync(sourcePath, destDir, progress, token);
            }

            return await Task.Run(() =>
            {
                try
                {
                    if (token.IsCancellationRequested) return OperationResult.Cancelled;

                    string fileName = Path.GetFileName(sourcePath);
                    string destPath = Path.Combine(destDir, fileName);
                    destPath = GetUniqueFilePath(destPath);

                    Log.Info("FileOperations.Move: {Source} -> {Dest}", sourcePath, destPath);

                    progress?.Report(new OperationProgress
                    {
                        FileName = fileName,
                        PercentComplete = 0
                    });

                    bool ok = MoveFileFromAppW(sourcePath, destPath);
                    if (!ok)
                    {
                        int err = Marshal.GetLastWin32Error();
                        Log.Warn("FileOperations.Move failed: error {Error}", err);

                        // Fallback: copy + delete (MoveFile fails across volumes)
                        Log.Dbg("FileOperations.Move: trying copy+delete fallback");
                        ok = CopyFileFromAppW(sourcePath, destPath, false);
                        if (!ok)
                        {
                            return OperationResult.Failed;
                        }
                        bool deleted = DeleteFileFromAppW(sourcePath);
                        if (!deleted)
                        {
                            int delErr = Marshal.GetLastWin32Error();
                            Log.Warn("FileOperations.Move: copy succeeded but delete failed (source still exists): error {Error}", delErr);
                            return OperationResult.Failed;
                        }
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
                    Log.Warn("FileOperations.Move exception", ex);
                    return OperationResult.Failed;
                }
            });
        }

        /// <summary>
        /// Move directory recursively from source to destination.
        /// </summary>
        public static async Task<OperationResult> MoveDirectoryAsync(string sourceDir, string destDir, IProgress<OperationProgress> progress = null, CancellationToken token = default)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string dirName = Path.GetFileName(sourceDir.TrimEnd('\\', '/'));
                    string destPath = Path.Combine(destDir, dirName);
                    destPath = GetUniqueDirectoryPath(destPath);

                    Log.Info("FileOperations.MoveDirectory: {Source} -> {Dest}", sourceDir, destPath);

                    progress?.Report(new OperationProgress
                    {
                        FileName = dirName,
                        PercentComplete = 0
                    });

                    // Try native move first (same volume)
                    bool ok = MoveFileFromAppW(sourceDir, destPath);
                    if (ok)
                    {
                        progress?.Report(new OperationProgress
                        {
                            FileName = dirName,
                            PercentComplete = 100
                        });
                        return OperationResult.Success;
                    }

                    // Fallback: move each file individually with progress
                    Log.Dbg("FileOperations.MoveDirectory: native move failed, using per-file fallback");
                    CreateDirectoryFromAppW(destPath, IntPtr.Zero);

                    var result = MoveDirectoryRecursive(sourceDir, destPath, progress, token);
                    if (result == OperationResult.Success)
                    {
                        // Source should be empty after moving all files — remove it
                        bool removed = RemoveDirectoryFromAppW(sourceDir);
                        if (!removed)
                        {
                            int rmErr = Marshal.GetLastWin32Error();
                            Log.Warn("FileOperations.MoveDirectory: files moved but source directory cleanup failed: {Dir} error {Error}", sourceDir, rmErr);
                        }
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    Log.Warn("FileOperations.MoveDirectory exception", ex);
                    return OperationResult.Failed;
                }
            });
        }

        private static OperationResult MoveDirectoryRecursive(string sourceDir, string destDir, IProgress<OperationProgress> progress, CancellationToken token = default)
        {
            try
            {
                var findData = new WIN32_FIND_DATA();
                IntPtr hFind = FindFirstFileExFromAppW(
                    sourceDir + "\\*", 0, out findData, 0, IntPtr.Zero, 0);
                if (hFind == new IntPtr(-1)) return OperationResult.Success;

                do
                {
                    if (token.IsCancellationRequested) { FindClose(hFind); return OperationResult.Cancelled; }

                    if (findData.cFileName == "." || findData.cFileName == "..") continue;
                    string fullPath = sourceDir + "\\" + findData.cFileName;

                    if ((findData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
                    {
                        string destSubDir = destDir + "\\" + findData.cFileName;
                        CreateDirectoryFromAppW(destSubDir, IntPtr.Zero);
                        var result = MoveDirectoryRecursive(fullPath, destSubDir, progress, token);
                        if (result != OperationResult.Success) { FindClose(hFind); return result; }
                    }
                    else
                    {
                        string destFile = destDir + "\\" + findData.cFileName;
                        bool ok = MoveFileFromAppW(fullPath, destFile);
                        if (!ok)
                        {
                            // Fallback: copy + delete
                            ok = CopyFileFromAppW(fullPath, destFile, false);
                            if (!ok)
                            {
                                Log.Warn("FileOperations.MoveDirectoryRecursive: failed to move {File}", fullPath);
                                FindClose(hFind);
                                return OperationResult.Failed;
                            }
                            bool deleted = DeleteFileFromAppW(fullPath);
                            if (!deleted)
                            {
                                int delErr = Marshal.GetLastWin32Error();
                                Log.Warn("FileOperations.MoveDirectoryRecursive: copy succeeded but delete failed for {File}: error {Error}", fullPath, delErr);
                                FindClose(hFind);
                                return OperationResult.Failed;
                            }
                        }

                        progress?.Report(new OperationProgress
                        {
                            FileName = findData.cFileName,
                            PercentComplete = -1
                        });
                    }
                }
                while (FindNextFileW(hFind, out findData));

                FindClose(hFind);
            }
            catch (Exception ex)
            {
                Log.Warn("FileOperations.MoveDirectoryRecursive: {Dir} error", ex, sourceDir);
                return OperationResult.Failed;
            }

            return OperationResult.Success;
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

                    Log.Info("FileOperations.Rename: {Old} -> {New}", path, newPath);

                    bool ok = MoveFileFromAppW(path, newPath);
                    if (!ok)
                    {
                        int err = Marshal.GetLastWin32Error();
                        Log.Warn("FileOperations.Rename failed: error {Error}", err);
                        return OperationResult.Failed;
                    }

                    return OperationResult.Success;
                }
                catch (Exception ex)
                {
                    Log.Warn("FileOperations.Rename exception", ex);
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
                    Log.Info("FileOperations.Delete: {Path}", path);

                    var pathType = CheckPathType(path);
                    if (pathType == "file")
                    {
                        bool ok = DeleteFileFromAppW(path);
                        if (!ok)
                        {
                            int err = Marshal.GetLastWin32Error();
                            Log.Warn("FileOperations.Delete failed: error {Error}", err);
                            return OperationResult.Failed;
                        }
                    }
                    else if (pathType == "directory")
                    {
                        bool ok = RemoveDirectoryFromAppW(path);
                        if (!ok)
                        {
                            int err = Marshal.GetLastWin32Error();
                            Log.Warn("FileOperations.DeleteDirectory failed: error {Error}", err);
                            return OperationResult.Failed;
                        }
                    }

                    return OperationResult.Success;
                }
                catch (Exception ex)
                {
                    Log.Warn("FileOperations.Delete exception", ex);
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
                    Log.Info("FileOperations.DeleteDirectory: {Path}", path);

                    // Recursively delete contents via P/Invoke, then remove the directory itself
                    var files = EnumerateFilesRecursive(path);
                    foreach (var file in files)
                    {
                        bool ok = DeleteFileFromAppW(file);
                        if (!ok)
                        {
                            int err = Marshal.GetLastWin32Error();
                            Log.Warn("FileOperations.DeleteDirectory: failed to delete file {File}, error {Error}", file, err);
                            return OperationResult.Failed;
                        }
                    }

                    // Remove subdirectories bottom-up
                    RemoveDirectoriesRecursive(path);

                    // Remove the root directory
                    if (CheckPathType(path) == "directory")
                    {
                        bool ok = RemoveDirectoryFromAppW(path);
                        if (!ok)
                        {
                            int err = Marshal.GetLastWin32Error();
                            Log.Warn("FileOperations.DeleteDirectory: failed to remove root {Path}, error {Error}", path, err);
                            return OperationResult.Failed;
                        }
                    }

                    return OperationResult.Success;
                }
                catch (Exception ex)
                {
                    Log.Warn("FileOperations.DeleteDirectory exception", ex);
                    return OperationResult.Failed;
                }
            });
        }

        private static void RemoveDirectoriesRecursive(string dir)
        {
            try
            {
                var findData = new WIN32_FIND_DATA();
                IntPtr hFind = FindFirstFileExFromAppW(
                    dir + "\\*", 0, out findData, 0, IntPtr.Zero, 0);
                if (hFind == new IntPtr(-1)) return;

                do
                {
                    if (findData.cFileName == "." || findData.cFileName == "..") continue;
                    if ((findData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
                    {
                        string subDir = dir + "\\" + findData.cFileName;
                        RemoveDirectoriesRecursive(subDir);
                        RemoveDirectoryFromAppW(subDir);
                    }
                }
                while (FindNextFileW(hFind, out findData));

                FindClose(hFind);
            }
            catch (Exception ex)
            {
                Log.Warn("RemoveDirectoriesRecursive: {Dir} error", ex, dir);
            }
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
                Log.Warn("GetSingleRootFolder error", ex);
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
            Func<string, Task<int>> conflictCallback = null,
            CancellationToken token = default)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    Log.Info("FileOperations.Extract: {Archive} -> {Dest}", archivePath, destDir);

                    if (CheckPathType(destDir) == null)
                    {
                        Log.Info("FileOperations.Extract: creating dest dir {Dir}", destDir);
                        CreateDirectoryFromAppW(destDir, IntPtr.Zero);
                    }

                    using (var stream = Win32FileStream.OpenRead(archivePath))
                    {
                        if (stream == null)
                        {
                            Log.Warn("FileOperations.Extract: cannot open archive {Path}", archivePath);
                            return OperationResult.Failed;
                        }

                        using (var archive = SharpCompress.Archives.ArchiveFactory.Open(stream))
                        {
                            bool overwriteAll = conflictCallback == null;

                            foreach (var entry in archive.Entries)
                            {
                                if (token.IsCancellationRequested) return OperationResult.Cancelled;

                                if (entry.IsDirectory) continue;

                                progress?.Report(new OperationProgress
                                {
                                    FileName = entry.Key,
                                    PercentComplete = -1
                                });

                                if (!overwriteAll && conflictCallback != null)
                                {
                                    string destPath = System.IO.Path.Combine(destDir, entry.Key);
                                    if (CheckPathType(destPath) == "file")
                                    {
                                        int decision = await conflictCallback(entry.Key);
                                        switch (decision)
                                        {
                                            case 0: // Skip
                                                Log.Dbg("Extract: skipping {File}", entry.Key);
                                                continue;
                                            case 1: // Overwrite this one
                                                Log.Dbg("Extract: overwriting {File}", entry.Key);
                                                break;
                                            case 2: // Overwrite all remaining
                                                Log.Dbg("Extract: overwrite all remaining");
                                                overwriteAll = true;
                                                break;
                                        }
                                    }
                                }

                                // Ensure parent directory exists (SharpCompress entry.Key may include subdirs)
                                string entryDestPath = System.IO.Path.Combine(destDir, entry.Key);
                                string entryParentDir = System.IO.Path.GetDirectoryName(entryDestPath);
                                if (entryParentDir != null && CheckPathType(entryParentDir) == null)
                                {
                                    CreateDirectoryFromAppW(entryParentDir, IntPtr.Zero);
                                }

                                // Extract via OpenEntryStream + Win32FileWriteStream (no WriteToDirectory)
                                using (var entryStream = entry.OpenEntryStream())
                                {
                                    if (entryStream == null)
                                    {
                                        Log.Warn("Extract: cannot open entry stream {File}", entry.Key);
                                        continue;
                                    }

                                    using (var writeStream = Win32FileWriteStream.Create(entryDestPath))
                                    {
                                        if (writeStream == null)
                                        {
                                            Log.Warn("Extract: cannot create dest file {Path}", entryDestPath);
                                            continue;
                                        }

                                        entryStream.CopyTo(writeStream);
                                    }
                                }
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
                    Log.Warn("FileOperations.Extract exception", ex);
                    return OperationResult.Failed;
                }
            });
        }

        public static async Task<OperationResult> ExtractFileAsync(
            string archivePath, string internalPath, string destDir,
            Func<string, Task<int>> conflictCallback = null,
            CancellationToken token = default)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    if (token.IsCancellationRequested) return OperationResult.Cancelled;

                    Log.Info("FileOperations.ExtractFile: {Archive}|{Internal} -> {Dest}",
                        archivePath, internalPath, destDir);

                    if (CheckPathType(destDir) == null)
                        CreateDirectoryFromAppW(destDir, IntPtr.Zero);

                    using (var stream = Win32FileStream.OpenRead(archivePath))
                    {
                        if (stream == null)
                        {
                            Log.Warn("ExtractFile: cannot open archive {Path}", archivePath);
                            return OperationResult.Failed;
                        }

                        using (var archive = SharpCompress.Archives.ArchiveFactory.Open(stream))
                        {
                            string normalizedInternal = internalPath.Replace('\\', '/').Trim('/');
                            var entry = archive.Entries.FirstOrDefault(e =>
                                e.Key.Replace('\\', '/').Trim('/') == normalizedInternal);

                            if (entry == null || entry.IsDirectory)
                            {
                                Log.Warn("ExtractFile: entry not found or is directory: {Internal}", internalPath);
                                return OperationResult.Failed;
                            }

                            string fileName = Path.GetFileName(entry.Key);
                            string destPath = Path.Combine(destDir, fileName);

                            if (CheckPathType(destPath) == "file" && conflictCallback != null)
                            {
                                int decision = await conflictCallback(fileName);
                                if (decision == 0)
                                {
                                    Log.Dbg("ExtractFile: skipping {File}", fileName);
                                    return OperationResult.Success;
                                }
                            }

                            // Extract via OpenEntryStream + Win32FileWriteStream (no WriteToDirectory)
                            using (var entryStream = entry.OpenEntryStream())
                            {
                                if (entryStream == null)
                                {
                                    Log.Warn("ExtractFile: cannot open entry stream {File}", internalPath);
                                    return OperationResult.Failed;
                                }

                                using (var writeStream = Win32FileWriteStream.Create(destPath))
                                {
                                    if (writeStream == null)
                                    {
                                        Log.Warn("ExtractFile: cannot create dest file {Path}", destPath);
                                        return OperationResult.Failed;
                                    }

                                    entryStream.CopyTo(writeStream);
                                }
                            }

                            Log.Info("ExtractFile: extracted {File} -> {Dest}", fileName, destPath);
                        }
                    }

                    return OperationResult.Success;
                }
                catch (Exception ex)
                {
                    Log.Warn("FileOperations.ExtractFile exception", ex);
                    return OperationResult.Failed;
                }
            });
        }

        private static string GetUniqueFilePath(string path)
        {
            if (CheckPathType(path) != "file") return path;

            string dir = Path.GetDirectoryName(path);
            string nameNoExt = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);

            // Detect "file (N)" pattern — increment N instead of nesting
            var match = System.Text.RegularExpressions.Regex.Match(nameNoExt, @"^(.+?)\s*\((\d+)\)$");
            string baseName;
            int startIdx;
            if (match.Success)
            {
                baseName = match.Groups[1].Value;
                startIdx = int.Parse(match.Groups[2].Value) + 1;
            }
            else
            {
                baseName = nameNoExt;
                startIdx = 1;
            }

            for (int i = startIdx; ; i++)
            {
                string candidate = Path.Combine(dir, $"{baseName} ({i}){ext}");
                if (CheckPathType(candidate) == null) return candidate;
            }
        }

        private static string GetUniqueDirectoryPath(string path)
        {
            if (CheckPathType(path) != "directory") return path;

            string parent = Path.GetDirectoryName(path);
            string name = Path.GetFileName(path);

            // Detect "folder (N)" pattern — increment N instead of nesting
            var match = System.Text.RegularExpressions.Regex.Match(name, @"^(.+?)\s*\((\d+)\)$");
            string baseName;
            int startIdx;
            if (match.Success)
            {
                baseName = match.Groups[1].Value;
                startIdx = int.Parse(match.Groups[2].Value) + 1;
            }
            else
            {
                baseName = name;
                startIdx = 1;
            }

            for (int i = startIdx; ; i++)
            {
                string candidate = Path.Combine(parent, $"{baseName} ({i})");
                if (CheckPathType(candidate) == null) return candidate;
            }
        }

        /// <summary>
        /// Generate a copy name like "file (Copy 1).ext" or "folder (Copy 1)".
        /// Used when pasting in the same directory to avoid overwriting.
        /// </summary>
        public static string GetCopyName(string path)
        {
            if (CheckPathType(path) == null) return path;

            string dir = Path.GetDirectoryName(path);
            string nameNoExt = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);

            // Detect "file (Copy N)" pattern — increment N instead of nesting
            var match = System.Text.RegularExpressions.Regex.Match(nameNoExt, @"^(.+?)\s*\(Copy\s+(\d+)\)$");
            string baseName;
            int startIdx;
            if (match.Success)
            {
                baseName = match.Groups[1].Value;
                startIdx = int.Parse(match.Groups[2].Value) + 1;
            }
            else
            {
                baseName = nameNoExt;
                startIdx = 1;
            }

            for (int i = startIdx; ; i++)
            {
                string candidate = Path.Combine(dir, $"{baseName} (Copy {i}){ext}");
                if (CheckPathType(candidate) == null) return candidate;
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
                    Log.Info("FileOperations.CreateFolder: {Path}", folderPath);
                    bool ok = CreateDirectoryFromAppW(folderPath, IntPtr.Zero);
                    if (!ok)
                    {
                        int err = Marshal.GetLastWin32Error();
                        Log.Warn("FileOperations.CreateFolder: CreateDirectoryFromAppW failed, Win32 error={Error}", err);
                        return OperationResult.Failed;
                    }
                    return OperationResult.Success;
                }
                catch (Exception ex)
                {
                    Log.Warn("FileOperations.CreateFolder exception", ex);
                    return OperationResult.Failed;
                }
            });
        }

        public static async Task<OperationResult> CreateZipAsync(string sourcePath, string zipPath, IProgress<OperationProgress> progress = null, CancellationToken token = default)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (token.IsCancellationRequested) return OperationResult.Cancelled;

                    zipPath = GetUniqueFilePath(zipPath);
                    Log.Info("FileOperations.CreateZip: {Source} -> {Zip}", sourcePath, zipPath);

                    using (var archive = SharpCompress.Archives.Zip.ZipArchive.Create())
                    {
                        var pathType = CheckPathType(sourcePath);
                        if (pathType == "file")
                        {
                            var fileName = System.IO.Path.GetFileName(sourcePath);
                            var data = ReadAllBytesWin32(sourcePath);
                            if (data == null)
                            {
                                Log.Warn("FileOperations.CreateZip: cannot read {Path}", sourcePath);
                                return OperationResult.Failed;
                            }
                            archive.AddEntry(fileName, new MemoryStream(data), data.Length);

                            progress?.Report(new OperationProgress
                            {
                                FileName = fileName,
                                PercentComplete = -1
                            });
                        }
                        else if (pathType == "directory")
                        {
                            var files = EnumerateFilesRecursive(sourcePath);
                            int fileIndex = 0;
                            foreach (var file in files)
                            {
                                if (token.IsCancellationRequested) return OperationResult.Cancelled;

                                var entryName = file.Substring(sourcePath.Length + 1);
                                var entryData = ReadAllBytesWin32(file);
                                if (entryData == null)
                                {
                                    Log.Warn("FileOperations.CreateZip: cannot read {Path}", file);
                                    continue;
                                }
                                archive.AddEntry(entryName, new MemoryStream(entryData), entryData.Length);

                                fileIndex++;
                                progress?.Report(new OperationProgress
                                {
                                    FileName = entryName,
                                    PercentComplete = -1,
                                    FileIndex = fileIndex,
                                    FileTotal = files.Count
                                });
                            }
                        }
                        else
                        {
                            Log.Warn("FileOperations.CreateZip: source not found {Source}", sourcePath);
                            return OperationResult.Failed;
                        }

                        // Save to MemoryStream first (avoids SharpCompress SaveTo using System.IO)
                        using (var zipStream = new MemoryStream())
                        {
                            archive.SaveTo(zipStream, new SharpCompress.Writers.WriterOptions(SharpCompress.Common.CompressionType.Deflate));
                            zipStream.Position = 0;

                            // Write to disk via P/Invoke
                            using (var writeStream = Win32FileWriteStream.Create(zipPath))
                            {
                                if (writeStream == null)
                                {
                                    Log.Warn("FileOperations.CreateZip: cannot create zip file {Path}", zipPath);
                                    return OperationResult.Failed;
                                }
                                zipStream.CopyTo(writeStream);
                            }
                        }
                    }

                    return OperationResult.Success;
                }
                catch (Exception ex)
                {
                    Log.Warn("FileOperations.CreateZip exception", ex);
                    return OperationResult.Failed;
                }
            });
        }

        /// <summary>
        /// Read all bytes from a file using Win32 P/Invoke (Xbox-safe).
        /// </summary>
        private static byte[] ReadAllBytesWin32(string filePath)
        {
            using (var stream = Win32FileStream.OpenRead(filePath))
            {
                if (stream == null) return null;
                byte[] data = new byte[stream.Length];
                int offset = 0;
                int remaining = data.Length;
                while (remaining > 0)
                {
                    int read = stream.Read(data, offset, remaining);
                    if (read == 0) break;
                    offset += read;
                    remaining -= read;
                }
                return data;
            }
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
                    var pathType = CheckPathType(path);
                    if (pathType == "file")
                    {
                        entries.Add(path);
                    }
                    else if (pathType == "directory")
                    {
                        entries.Add(path + "\\");
                        folderCount++;
                        ListDirectoryRecursive(path, entries, ref folderCount);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("FileOperations.ListRecursiveAsync error", ex);
                }

                return (entries, folderCount);
            });
        }

        private static void ListDirectoryRecursive(string dir, List<string> entries, ref int folderCount)
        {
            try
            {
                var findData = new WIN32_FIND_DATA();
                IntPtr hFind = FindFirstFileExFromAppW(
                    dir + "\\*", 0, out findData, 0, IntPtr.Zero, 0);
                if (hFind == new IntPtr(-1)) return;

                do
                {
                    if (findData.cFileName == "." || findData.cFileName == "..") continue;
                    string fullPath = dir + "\\" + findData.cFileName;
                    if ((findData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
                    {
                        entries.Add(fullPath + "\\");
                        folderCount++;
                        ListDirectoryRecursive(fullPath, entries, ref folderCount);
                    }
                    else
                    {
                        entries.Add(fullPath);
                    }
                }
                while (FindNextFileW(hFind, out findData));

                FindClose(hFind);
            }
            catch (Exception ex)
            {
                Log.Warn("FileOperations.ListDirectoryRecursive: {Dir} error", ex, dir);
            }
        }
    }
}
