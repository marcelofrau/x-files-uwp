using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
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

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetDiskFreeSpaceExW(
            string lpDirectoryName,
            out ulong lpFreeBytesAvailableToCaller,
            out ulong lpTotalNumberOfBytes,
            out ulong lpTotalNumberOfFreeBytes);

        [DllImport("api-ms-win-core-file-l1-1-0.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
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
        internal static string CheckPathType(string path)
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

        public class ScanResult
        {
            public int FileCount;
            public long TotalBytes;
        }

        private const int CopyChunkSize = 1024 * 1024; // 1MB

        /// <summary>
        /// Stream wrapper that counts bytes written and invokes a throttled callback.
        /// Used to report real bytes-written progress while a zip archive is saved.
        /// </summary>
        private sealed class CountingWriteStream : System.IO.Stream
        {
            private const long ReportThrottle = 256 * 1024; // report every 256KB
            private readonly System.IO.Stream _inner;
            private readonly Action<long> _onWrite;
            private readonly CancellationToken _token;
            private long _lastReport;

            public long BytesWritten { get; private set; }

            public CountingWriteStream(System.IO.Stream inner, Action<long> onWrite, CancellationToken token)
            {
                _inner = inner;
                _onWrite = onWrite;
                _token = token;
                _lastReport = -ReportThrottle;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (_token.IsCancellationRequested)
                    throw new OperationCanceledException(_token);
                _inner.Write(buffer, offset, count);
                BytesWritten += count;
                if (_onWrite != null && BytesWritten - _lastReport >= ReportThrottle)
                {
                    _lastReport = BytesWritten;
                    _onWrite(BytesWritten);
                }
            }

            public override void Flush() => _inner.Flush();
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => _inner.Length;
            public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, System.IO.SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }

        /// <summary>
        /// Saves a zip archive while reporting bytes written as progress. Returns false
        /// when the destination file cannot be created.
        /// </summary>
        private static bool SaveZipWithProgress(
            SharpCompress.Archives.Zip.ZipArchive archive, string zipPath,
            long totalBytes, IProgress<OperationProgress> progress,
            CancellationToken token = default)
        {
            using (var writeStream = Win32FileWriteStream.Create(zipPath))
            {
                if (writeStream == null)
                {
                    Log.Warn("FileOperations.SaveZipWithProgress: cannot create zip file {Path}", zipPath);
                    return false;
                }

                // SharpCompress's ZipWriter emits Deflate output in ~8KB chunks; buffer
                // them so Win32 WriteFile calls are 1MB instead of thousands of small ones.
                using (var buffered = new BufferedStream(writeStream, 1024 * 1024))
                {
                    var counting = new CountingWriteStream(buffered, written =>
                    {
                        progress?.Report(new OperationProgress
                        {
                            FileName = "Writing ZIP...",
                            BytesCopied = written,
                            TotalBytes = totalBytes,
                            PercentComplete = totalBytes > 0
                                ? Math.Min(100.0, (double)written / totalBytes * 100.0)
                                : -1
                        });
                    }, token);

                    archive.SaveTo(counting,
                        new SharpCompress.Writers.WriterOptions(SharpCompress.Common.CompressionType.Deflate));
                    counting.Flush();
                    buffered.Flush();

                    progress?.Report(new OperationProgress
                    {
                        FileName = "Finalizing...",
                        BytesCopied = counting.BytesWritten,
                        TotalBytes = totalBytes,
                        PercentComplete = totalBytes > 0
                            ? Math.Min(100.0, (double)counting.BytesWritten / totalBytes * 100.0)
                            : -1
                    });
                    return true;
                }
            }
        }

        private static void CleanupFailedZip(string zipPath)
        {
            try
            {
                if (DeleteFileFromAppW(zipPath))
                    Log.Dbg("FileOperations.CleanupFailedZip: removed partial zip {Path}", zipPath);
            }
            catch (Exception ex)
            {
                Log.Warn("FileOperations.CleanupFailedZip: could not remove partial zip {Path}", ex, zipPath);
            }
        }

        /// <summary>
        /// Streaming file copy using Win32 P/Invoke streams.
        /// Supports per-chunk cancel and byte-level progress reporting.
        /// </summary>
        private static OperationResult CopyFileStreaming(
            string sourcePath, string destPath,
            IProgress<OperationProgress> progress, string fileName,
            long fileBytesCopied, long fileTotalBytes,
            CancellationToken token)
        {
            try
            {
                using (var readStream = Win32FileStream.OpenRead(sourcePath))
                {
                    if (readStream == null)
                    {
                        Log.Warn("FileOperations.CopyFileStreaming: cannot open source {Path}", sourcePath);
                        return OperationResult.Failed;
                    }

                    using (var writeStream = Win32FileWriteStream.Create(destPath))
                    {
                        if (writeStream == null)
                        {
                            Log.Warn("FileOperations.CopyFileStreaming: cannot create dest {Path}", destPath);
                            return OperationResult.Failed;
                        }

                        var buffer = ArrayPool<byte>.Shared.Rent(CopyChunkSize);
                        long totalCopied = 0;
                        int bytesRead;
                        var lastReport = Stopwatch.GetTimestamp();

                        try
                        {
                            do
                            {
                                if (token.IsCancellationRequested) return OperationResult.Cancelled;

                                bytesRead = readStream.Read(buffer, 0, CopyChunkSize);
                                if (bytesRead > 0)
                                {
                                    writeStream.Write(buffer, 0, bytesRead);
                                    totalCopied += bytesRead;

                                    long now = Stopwatch.GetTimestamp();
                                    double elapsedMs = (now - lastReport) * 1000.0 / Stopwatch.Frequency;
                                    if (fileTotalBytes > 0 && (elapsedMs >= 100 || totalCopied >= fileTotalBytes))
                                    {
                                        lastReport = now;
                                        progress?.Report(new OperationProgress
                                        {
                                            FileName = fileName,
                                            PercentComplete = (double)totalCopied / fileTotalBytes * 100.0,
                                            BytesCopied = fileBytesCopied + totalCopied,
                                            TotalBytes = fileBytesCopied + fileTotalBytes
                                        });
                                    }
                                }
                            }
                            while (bytesRead > 0);
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(buffer);
                        }
                    }
                }
                return OperationResult.Success;
            }
            catch (Exception ex)
            {
                Log.Warn("FileOperations.CopyFileStreaming exception: {File}", ex, sourcePath);
                try { DeleteFileFromAppW(destPath); } catch { }
                return OperationResult.Failed;
            }
        }

        /// <summary>
        /// Pre-scan source paths to count total files and total bytes.
        /// Used to show accurate progress before starting the actual operation.
        /// </summary>
        public static async Task<ScanResult> ScanPathsAsync(List<string> paths)
        {
            return await Task.Run(() =>
            {
                var result = new ScanResult();
                foreach (var path in paths)
                {
                    var pathType = CheckPathType(path);
                    if (pathType == "file")
                    {
                        result.FileCount++;
                        result.TotalBytes += GetFileSizePInvoke(path);
                    }
                    else if (pathType == "directory")
                    {
                        ScanDirectoryRecursive(path, result);
                    }
                }
                return result;
            });
        }

        private static void ScanDirectoryRecursive(string dir, ScanResult result)
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
                        ScanDirectoryRecursive(dir + "\\" + findData.cFileName, result);
                    }
                    else
                    {
                        result.FileCount++;
                        long fileSize = ((long)findData.nFileSizeHigh << 32) | findData.nFileSizeLow;
                        result.TotalBytes += fileSize;
                    }
                }
                while (FindNextFileW(hFind, out findData));

                FindClose(hFind);
            }
            catch (Exception ex)
            {
                Log.Warn("ScanDirectoryRecursive: {Dir} error", ex, dir);
            }
        }

        private static long GetFileSizePInvoke(string path)
        {
            if (GetFileAttributesExFromAppW(path, 0, out var attr))
            {
                return ((long)attr.nFileSizeHigh << 32) | attr.nFileSizeLow;
            }
            return 0;
        }

        /// <summary>
        /// Queries free + total bytes for the volume containing path (e.g. "E:\").
        /// Returns null when the query fails. Uses the plain kernel32 export — the
        /// file API-set DLL has no *FromApp variant for this function, and volume
        /// queries are not restricted in app containers.
        ///
        /// In the app container, volume ROOTS the app has no ACL over (e.g. "Q:\",
        /// "C:\" on Xbox) return ERROR_ACCESS_DENIED even though GetDiskFreeSpaceExW
        /// is permitted. Accessible SUB-paths still resolve to their volume, so this
        /// tries the given path first, then the path root, then — for the Q:\ portal
        /// volume — the app's own LocalFolder as an anchor (the app always has access
        /// to its own sandbox under Q:\).
        /// </summary>
        public static (ulong FreeBytes, ulong TotalBytes)? GetDriveFreeSpace(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return null;
                string root = Path.GetPathRoot(path) ?? path;
                Log.Verb("FileOperations.GetDriveFreeSpace: path={Path} root={Root} (thread {Thread})",
                    path, root, Environment.CurrentManagedThreadId);

                var candidates = new List<string> { path };
                if (!string.Equals(root, path, StringComparison.OrdinalIgnoreCase))
                    candidates.Add(root);
                if (string.Equals(root, "Q:\\", StringComparison.OrdinalIgnoreCase))
                {
                    string anchor = Windows.Storage.ApplicationData.Current?.LocalFolder?.Path;
                    if (!string.IsNullOrEmpty(anchor))
                        candidates.Add(anchor);
                }

                foreach (var c in candidates)
                {
                    if (QueryDriveFreeSpace(c, out ulong free, out ulong total))
                    {
                        Log.Dbg("FileOperations.GetDriveFreeSpace: {Path} (via {Anchor}) free={Free} total={Total}",
                            path, c, free, total);
                        return (free, total);
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                Log.Warn("FileOperations.GetDriveFreeSpace exception", ex);
                return null;
            }
        }

        private static bool QueryDriveFreeSpace(string path, out ulong free, out ulong total)
        {
            if (GetDiskFreeSpaceExW(path, out free, out total, out ulong totalFree))
                return true;
            Log.Warn("FileOperations.GetDriveFreeSpace: failed for {Path} (err {Err})",
                path, Marshal.GetLastWin32Error());
            free = 0;
            total = 0;
            return false;
        }

        /// <summary>
        /// First-pass sum of the uncompressed sizes of all non-directory entries in an
        /// archive. Used to pre-check free space before extraction.
        /// </summary>
        public static async Task<long> GetArchiveUncompressedSizeAsync(string archivePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var stream = Win32FileStream.OpenRead(archivePath))
                    {
                        if (stream == null) return 0L;
                        using (var archive = SharpCompress.Archives.ArchiveFactory.Open(stream))
                        {
                            long total = 0;
                            foreach (var entry in archive.Entries)
                            {
                                if (!entry.IsDirectory)
                                    total += (long)entry.Size;
                            }
                            return total;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("FileOperations.GetArchiveUncompressedSize exception", ex);
                    return 0;
                }
            });
        }

        /// <summary>
        /// Copy file from source to destination directory.
        /// If sameDir is true, uses "Copy N" naming to avoid overwriting in same directory.
        /// Uses streaming copy with cancel support and per-file byte progress.
        /// If fileBytesOffset is provided, progress reports are offset (for multi-file overall tracking).
        /// </summary>
        public static async Task<OperationResult> CopyAsync(
            string sourcePath, string destDir,
            IProgress<OperationProgress> progress = null,
            bool sameDir = false, CancellationToken token = default,
            long fileBytesOffset = 0, long overallTotalBytes = 0)
        {
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

                    long fileSize = GetFileSizePInvoke(sourcePath);
                    Log.Info("FileOperations.Copy: {Source} -> {Dest} ({Size} bytes)", sourcePath, destPath, fileSize);

                    var result = CopyFileStreaming(sourcePath, destPath, progress, fileName,
                        fileBytesOffset, overallTotalBytes > 0 ? overallTotalBytes : fileSize, token);

                    if (result == OperationResult.Success)
                    {
                        progress?.Report(new OperationProgress
                        {
                            FileName = fileName,
                            PercentComplete = 100,
                            BytesCopied = overallTotalBytes > 0 ? fileBytesOffset + fileSize : fileSize,
                            TotalBytes = overallTotalBytes > 0 ? overallTotalBytes : fileSize
                        });
                    }

                    return result;
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
        /// Pre-scans for accurate file count + total bytes before starting.
        /// </summary>
        public static async Task<OperationResult> CopyDirectoryAsync(string sourceDir, string destDir, IProgress<OperationProgress> progress = null, bool sameDir = false, CancellationToken token = default)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    string dirName = Path.GetFileName(sourceDir.TrimEnd('\\', '/'));
                    string destPath = Path.Combine(destDir, dirName);
                    destPath = sameDir ? GetCopyName(destPath) : GetUniqueDirectoryPath(destPath);

                    Log.Info("FileOperations.CopyDirectory: {Source} -> {Dest}", sourceDir, destPath);
                    CreateDirectoryFromAppW(destPath, IntPtr.Zero);

                    // Pre-scan for accurate progress
                    var scan = new ScanResult();
                    ScanDirectoryRecursive(sourceDir, scan);

                    int completedFiles = 0;
                    long completedBytes = 0;

                    progress?.Report(new OperationProgress
                    {
                        FileName = dirName,
                        PercentComplete = 0,
                        FileIndex = 0,
                        FileTotal = scan.FileCount,
                        BytesCopied = 0,
                        TotalBytes = scan.TotalBytes
                    });

                    return CopyDirectoryRecursive(sourceDir, destPath, progress, token,
                        ref completedFiles, scan.FileCount, ref completedBytes, scan.TotalBytes);
                }
                catch (Exception ex)
                {
                    Log.Warn("FileOperations.CopyDirectory exception", ex);
                    return OperationResult.Failed;
                }
            });
        }

        private static OperationResult CopyDirectoryRecursive(
            string sourceDir, string destDir,
            IProgress<OperationProgress> progress, CancellationToken token,
            ref int completedFiles, long totalFiles, ref long completedBytes, long totalBytes)
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
                        var result = CopyDirectoryRecursive(fullPath, destSubDir, progress, token,
                            ref completedFiles, totalFiles, ref completedBytes, totalBytes);
                        if (result != OperationResult.Success) { FindClose(hFind); return result; }
                    }
                    else
                    {
                        string destFile = destDir + "\\" + findData.cFileName;
                        long fileSize = ((long)findData.nFileSizeHigh << 32) | findData.nFileSizeLow;

                        var result = CopyFileStreaming(fullPath, destFile, progress, findData.cFileName,
                            completedBytes, totalBytes, token);

                        if (result != OperationResult.Cancelled)
                        {
                            completedFiles++;
                            completedBytes += fileSize;

                            progress?.Report(new OperationProgress
                            {
                                FileName = findData.cFileName,
                                PercentComplete = totalFiles > 0 ? (double)completedFiles / totalFiles * 100.0 : -1,
                                FileIndex = completedFiles,
                                FileTotal = (int)totalFiles,
                                BytesCopied = completedBytes,
                                TotalBytes = totalBytes
                            });
                        }

                        if (result == OperationResult.Cancelled) { FindClose(hFind); return OperationResult.Cancelled; }
                        if (result == OperationResult.Failed) { FindClose(hFind); return OperationResult.Failed; }
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

                        // Fallback: streaming copy + delete (MoveFile fails across volumes)
                        Log.Dbg("FileOperations.Move: trying streaming copy+delete fallback");
                        long fileSize = GetFileSizePInvoke(sourcePath);
                        var copyResult = CopyFileStreaming(sourcePath, destPath, progress, fileName, 0, fileSize, token);
                        if (copyResult != OperationResult.Success)
                        {
                            return copyResult;
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
                            // Fallback: streaming copy + delete
                            long fileSize = ((long)findData.nFileSizeHigh << 32) | findData.nFileSizeLow;
                            var copyResult = CopyFileStreaming(fullPath, destFile, progress, findData.cFileName, 0, fileSize, token);
                            if (copyResult != OperationResult.Success)
                            {
                                Log.Warn("FileOperations.MoveDirectoryRecursive: failed to move {File}", fullPath);
                                FindClose(hFind);
                                return copyResult;
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
        /// Pre-scans archive entries for accurate progress.
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
                            // Pre-scan: count files and total uncompressed size
                            int totalFiles = 0;
                            long totalBytes = 0;
                            foreach (var entry in archive.Entries)
                            {
                                if (!entry.IsDirectory)
                                {
                                    totalFiles++;
                                    totalBytes += (long)entry.Size;
                                }
                            }

                            int completedFiles = 0;
                            long completedBytes = 0;
                            bool overwriteAll = conflictCallback == null;

                            // Reset stream for extraction pass
                            stream.Seek(0, SeekOrigin.Begin);
                            using (var archive2 = SharpCompress.Archives.ArchiveFactory.Open(stream))
                            {
                                foreach (var entry in archive2.Entries)
                                {
                                    if (token.IsCancellationRequested) return OperationResult.Cancelled;

                                    if (entry.IsDirectory) continue;

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
                                                    completedFiles++;
                                                    completedBytes += (long)entry.Size;
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

                                    progress?.Report(new OperationProgress
                                    {
                                        FileName = entry.Key,
                                        PercentComplete = totalFiles > 0 ? (double)completedFiles / totalFiles * 100.0 : -1,
                                        FileIndex = completedFiles,
                                        FileTotal = totalFiles,
                                        BytesCopied = completedBytes,
                                        TotalBytes = totalBytes
                                    });

                                    // Ensure parent directory exists
                                    string entryDestPath = System.IO.Path.Combine(destDir, entry.Key);
                                    string entryParentDir = System.IO.Path.GetDirectoryName(entryDestPath);
                                    if (entryParentDir != null && CheckPathType(entryParentDir) == null)
                                    {
                                        CreateDirectoryFromAppW(entryParentDir, IntPtr.Zero);
                                    }

                                    // Extract with cancel support via chunked copy
                                    using (var entryStream = entry.OpenEntryStream())
                                    {
                                        if (entryStream == null)
                                        {
                                            Log.Warn("Extract: cannot open entry stream {File}", entry.Key);
                                            completedFiles++;
                                            completedBytes += (long)entry.Size;
                                            continue;
                                        }

                                        using (var writeStream = Win32FileWriteStream.Create(entryDestPath))
                                        {
                                            if (writeStream == null)
                                            {
                                                Log.Warn("Extract: cannot create dest file {Path}", entryDestPath);
                                                completedFiles++;
                                                completedBytes += (long)entry.Size;
                                                continue;
                                            }

                                            var buffer = new byte[CopyChunkSize];
                                            long entryCopied = 0;
                                            long entrySize = (long)entry.Size;
                                            int bytesRead;
                                            var lastReport = Stopwatch.GetTimestamp();

                                            do
                                            {
                                                if (token.IsCancellationRequested) return OperationResult.Cancelled;

                                                bytesRead = entryStream.Read(buffer, 0, CopyChunkSize);
                                                if (bytesRead > 0)
                                                {
                                                    writeStream.Write(buffer, 0, bytesRead);
                                                    entryCopied += bytesRead;

                                                    long now = Stopwatch.GetTimestamp();
                                                    double elapsedMs = (now - lastReport) * 1000.0 / Stopwatch.Frequency;
                                                    if (elapsedMs >= 100 || entryCopied >= entrySize)
                                                    {
                                                        lastReport = now;
                                                        progress?.Report(new OperationProgress
                                                        {
                                                            FileName = entry.Key,
                                                            PercentComplete = totalFiles > 0 ? (double)(completedFiles) / totalFiles * 100.0 : -1,
                                                            FileIndex = completedFiles,
                                                            FileTotal = totalFiles,
                                                            BytesCopied = completedBytes + entryCopied,
                                                            TotalBytes = totalBytes
                                                        });
                                                    }
                                                }
                                            }
                                            while (bytesRead > 0);
                                        }
                                    }

                                    completedFiles++;
                                    completedBytes += (long)entry.Size;
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

        /// <summary>
        /// Uncompressed size of a single non-directory entry inside an archive.
        /// </summary>
        public static async Task<long> GetArchiveEntryUncompressedSizeAsync(string archivePath, string internalPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var stream = Win32FileStream.OpenRead(archivePath))
                    {
                        if (stream == null) return 0L;
                        using (var archive = SharpCompress.Archives.ArchiveFactory.Open(stream))
                        {
                            foreach (var entry in archive.Entries)
                            {
                                if (entry.IsDirectory) continue;
                                if (string.Equals(entry.Key, internalPath, StringComparison.OrdinalIgnoreCase))
                                    return (long)entry.Size;
                            }
                            return 0L;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("FileOperations.GetArchiveEntryUncompressedSize exception", ex);
                    return 0;
                }
            });
        }

        public static async Task<OperationResult> ExtractFileAsync(
            string archivePath, string internalPath, string destDir,
            Func<string, Task<int>> conflictCallback = null,
            CancellationToken token = default,
            IProgress<OperationProgress> progress = null)
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

                                    var buffer = new byte[CopyChunkSize];
                                    long entryCopied = 0;
                                    long entrySize = (long)entry.Size;
                                    int bytesRead;
                                    var lastReport = Stopwatch.GetTimestamp();

                                    do
                                    {
                                        if (token.IsCancellationRequested) return OperationResult.Cancelled;

                                        bytesRead = entryStream.Read(buffer, 0, CopyChunkSize);
                                        if (bytesRead > 0)
                                        {
                                            writeStream.Write(buffer, 0, bytesRead);
                                            entryCopied += bytesRead;

                                            long now = Stopwatch.GetTimestamp();
                                            double elapsedMs = (now - lastReport) * 1000.0 / Stopwatch.Frequency;
                                            if (elapsedMs >= 100 || entryCopied >= entrySize)
                                            {
                                                lastReport = now;
                                                progress?.Report(new OperationProgress
                                                {
                                                    FileName = fileName,
                                                    PercentComplete = entrySize > 0 ? (double)entryCopied / entrySize * 100.0 : -1,
                                                    BytesCopied = entryCopied,
                                                    TotalBytes = entrySize
                                                });
                                            }
                                        }
                                    }
                                    while (bytesRead > 0);
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

            int maxAttempts = 10000;
            for (int i = startIdx; i < startIdx + maxAttempts; i++)
            {
                string candidate = Path.Combine(dir, $"{baseName} ({i}){ext}");
                if (CheckPathType(candidate) == null) return candidate;
            }
            return path;
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

            int maxAttempts = 10000;
            for (int i = startIdx; i < startIdx + maxAttempts; i++)
            {
                string candidate = Path.Combine(parent, $"{baseName} ({i})");
                if (CheckPathType(candidate) == null) return candidate;
            }
            return path;
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

            int maxAttempts = 10000;
            for (int i = startIdx; i < startIdx + maxAttempts; i++)
            {
                string candidate = Path.Combine(dir, $"{baseName} (Copy {i}){ext}");
                if (CheckPathType(candidate) == null) return candidate;
            }
            return path;
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
                    var started = DateTime.UtcNow;

                    using (var archive = SharpCompress.Archives.Zip.ZipArchive.Create())
                    {
                        var pathType = CheckPathType(sourcePath);
                        Log.Info("FileOperations.CreateZip: pathType={Type}", pathType ?? "null");

                        // SharpCompress reads entry streams lazily during SaveTo, so the
                        // streams must stay open until after SaveZipWithProgress completes.
                        var openStreams = new List<Stream>();
                        try
                        {
                            if (pathType == "file")
                            {
                                var fileName = System.IO.Path.GetFileName(sourcePath);
                                var stream = Win32FileStream.OpenRead(sourcePath);
                                if (stream == null)
                                {
                                    Log.Warn("FileOperations.CreateZip: cannot read {Path}", sourcePath);
                                    return OperationResult.Failed;
                                }
                                openStreams.Add(stream);
                                archive.AddEntry(fileName, stream);
                                Log.Verb("FileOperations.CreateZip: added {Entry} ({Size} bytes)", fileName, stream.Length);

                                progress?.Report(new OperationProgress
                                {
                                    FileName = fileName,
                                    PercentComplete = -1
                                });
                            }
                            else if (pathType == "directory")
                            {
                                var files = EnumerateFilesRecursive(sourcePath);
                                Log.Info("FileOperations.CreateZip: enumerated {Count} files from {Source}", files.Count, sourcePath);
                                int fileIndex = 0;
                                foreach (var file in files)
                                {
                                    if (token.IsCancellationRequested) return OperationResult.Cancelled;

                                    var entryName = file.Substring(sourcePath.Length + 1);
                                    var stream = Win32FileStream.OpenRead(file);
                                    if (stream == null)
                                    {
                                        Log.Warn("FileOperations.CreateZip: cannot read {Path}", file);
                                        continue;
                                    }
                                    openStreams.Add(stream);
                                    archive.AddEntry(entryName, stream);
                                    Log.Verb("FileOperations.CreateZip: added {Entry} ({Size} bytes)", entryName, stream.Length);

                                    fileIndex++;
                                    progress?.Report(new OperationProgress
                                    {
                                        FileName = entryName,
                                        PercentComplete = -1,
                                        FileIndex = fileIndex,
                                        FileTotal = files.Count
                                    });
                                }
                                Log.Info("FileOperations.CreateZip: added {Count} entries total", fileIndex);
                            }
                            else
                            {
                                Log.Warn("FileOperations.CreateZip: source not found {Source}", sourcePath);
                                return OperationResult.Failed;
                            }

                            long zipTotal = 0;
                            if (pathType == "file")
                            {
                                zipTotal = GetFileSizePInvoke(sourcePath);
                            }
                            else if (pathType == "directory")
                            {
                                var filesForTotal = EnumerateFilesRecursive(sourcePath);
                                foreach (var f in filesForTotal) zipTotal += GetFileSizePInvoke(f);
                            }

                            if (!SaveZipWithProgress(archive, zipPath, zipTotal, progress, token))
                            {
                                Log.Warn("FileOperations.CreateZip: cannot create zip file {Path}", zipPath);
                                return OperationResult.Failed;
                            }
                        }
                        finally
                        {
                            foreach (var s in openStreams) s.Dispose();
                        }
                    }

                    Log.Info("FileOperations.CreateZip: done — {Zip} ({Size} bytes) in {Ms}ms", zipPath, GetFileSizePInvoke(zipPath), (DateTime.UtcNow - started).TotalMilliseconds);
                    return OperationResult.Success;
                }
                catch (OperationCanceledException)
                {
                    Log.Info("FileOperations.CreateZip: cancelled");
                    CleanupFailedZip(zipPath);
                    return OperationResult.Cancelled;
                }
                catch (Exception ex)
                {
                    Log.Warn("FileOperations.CreateZip exception", ex);
                    CleanupFailedZip(zipPath);
                    return OperationResult.Failed;
                }
            });
        }

        /// <summary>
        /// Create a ZIP archive from multiple source paths (files and/or directories).
        /// </summary>
        public static async Task<OperationResult> CreateZipAsync(List<string> sourcePaths, string zipPath, IProgress<OperationProgress> progress = null, CancellationToken token = default)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (token.IsCancellationRequested) return OperationResult.Cancelled;

                    zipPath = GetUniqueFilePath(zipPath);
                    Log.Info("FileOperations.CreateZip(multi): {Count} sources -> {Zip}", sourcePaths.Count, zipPath);
                    var started = DateTime.UtcNow;

                    using (var archive = SharpCompress.Archives.Zip.ZipArchive.Create())
                    {
                        int fileIndex = 0;
                        var allFiles = new List<string>();

                        foreach (var sourcePath in sourcePaths)
                        {
                            var pathType = CheckPathType(sourcePath);
                            Log.Verb("FileOperations.CreateZip(multi): source {Source} type={Type}", sourcePath, pathType ?? "null");
                            if (pathType == "file")
                            {
                                allFiles.Add(sourcePath);
                            }
                            else if (pathType == "directory")
                            {
                                allFiles.AddRange(EnumerateFilesRecursive(sourcePath));
                            }
                            else
                            {
                                Log.Warn("FileOperations.CreateZip(multi): source not found {Source}", sourcePath);
                            }
                        }
                        Log.Info("FileOperations.CreateZip(multi): {Count} files to compress", allFiles.Count);

                        // SharpCompress reads entry streams lazily during SaveTo, so the
                        // streams must stay open until after SaveZipWithProgress completes.
                        var openStreams = new List<Stream>();
                        try
                        {
                            foreach (var file in allFiles)
                            {
                                if (token.IsCancellationRequested) return OperationResult.Cancelled;

                                string entryName = System.IO.Path.GetFileName(file);

                                var stream = Win32FileStream.OpenRead(file);
                                if (stream == null)
                                {
                                    Log.Warn("FileOperations.CreateZip(multi): cannot read {Path}", file);
                                    continue;
                                }
                                openStreams.Add(stream);
                                archive.AddEntry(entryName, stream);
                                Log.Verb("FileOperations.CreateZip(multi): added {Entry} ({Size} bytes)", entryName, stream.Length);

                                fileIndex++;
                                progress?.Report(new OperationProgress
                                {
                                    FileName = entryName,
                                    PercentComplete = -1,
                                    FileIndex = fileIndex,
                                    FileTotal = allFiles.Count
                                });
                            }
                            Log.Info("FileOperations.CreateZip(multi): added {Count} entries total", fileIndex);

                            long zipTotal = 0;
                            foreach (var f in allFiles) zipTotal += GetFileSizePInvoke(f);

                            if (!SaveZipWithProgress(archive, zipPath, zipTotal, progress, token))
                            {
                                Log.Warn("FileOperations.CreateZip(multi): cannot create zip file {Path}", zipPath);
                                return OperationResult.Failed;
                            }
                        }
                        finally
                        {
                            foreach (var s in openStreams) s.Dispose();
                        }
                    }

                    Log.Info("FileOperations.CreateZip(multi): done — {Zip} ({Size} bytes) in {Ms}ms", zipPath, GetFileSizePInvoke(zipPath), (DateTime.UtcNow - started).TotalMilliseconds);
                    return OperationResult.Success;
                }
                catch (OperationCanceledException)
                {
                    Log.Info("FileOperations.CreateZip(multi): cancelled");
                    CleanupFailedZip(zipPath);
                    return OperationResult.Cancelled;
                }
                catch (Exception ex)
                {
                    Log.Warn("FileOperations.CreateZip(multi) exception", ex);
                    CleanupFailedZip(zipPath);
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
