using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XFiles.FileSystem;

namespace XFiles.Network
{
    /// <summary>
    /// Stream-based copy/delete helpers bridging the network layer and the local
    /// filesystem. Mirrors the local copy dialog's progress shape
    /// (FileOperations.OperationProgress) while the remote side goes through
    /// the provider's read/write streams. Supports remote→local, local→remote
    /// and remote→remote (same or cross server) for both files and directory
    /// trees, across every INetworkFileSystemProvider protocol. Local writes
    /// use plain System.IO, matching the shipped portal copy path.
    /// </summary>
    public static class NetworkCopyService
    {
        private const int ChunkSize = 1024 * 1024;
        private const long ReportThrottle = 64 * 1024;

        /// <summary>Copies a remote file or directory tree into a local directory.</summary>
        public static async Task<bool> CopyRemoteToLocalAsync(
            INetworkFileSystemProvider browser, NetworkServerConfig config, string share, string path,
            string localDestDir, bool isDirectory,
            IProgress<FileOperations.OperationProgress> progress, CancellationToken ct,
            Func<string, Task<ConflictDecision>> conflict = null)
        {
            Log.Info("NetworkCopyService.CopyRemoteToLocal: {Share}/{Path} → {Dest}",
                share, path, localDestDir);
            try
            {
                if (!isDirectory)
                {
                    string name = Path.GetFileName(path.Replace('/', '\\'));
                    long size = await browser.GetFileLengthAsync(config, share, path, ct);
                    string dest = Path.Combine(localDestDir, name);
                    if (conflict != null && File.Exists(dest))
                        dest = await ResolveLocalConflictAsync(dest, false, ct, conflict);
                    using (var src = await browser.OpenReadAsync(config, share, path, ct))
                    using (var dst = File.Create(dest))
                    {
                        await CopyStreamAsync(src, dst, size, progress, name, 0, 1, 0, size, ct);
                    }
                    return true;
                }

                var items = await ListRemoteTreeAsync(browser, config, share, path, ct);
                long total = items.Where(i => !i.IsDir).Sum(i => i.Size);
                long fileTotal = items.Count(i => !i.IsDir);
                string rootFolder = Path.GetFileName(path.TrimEnd('\\').Replace('/', '\\'));
                string rootLocal = Path.Combine(localDestDir, rootFolder);
                if (conflict != null && Directory.Exists(rootLocal))
                    rootLocal = await ResolveLocalConflictAsync(rootLocal, true, ct, conflict);
                Directory.CreateDirectory(rootLocal);

                foreach (var d in items.Where(i => i.IsDir))
                    Directory.CreateDirectory(Path.Combine(rootLocal, d.RelPath.Replace('/', '\\')));

                long done = 0;
                int idx = 0;
                foreach (var f in items.Where(i => !i.IsDir))
                {
                    ct.ThrowIfCancellationRequested();
                    string destFile = Path.Combine(rootLocal, f.RelPath.Replace('/', '\\'));
                    if (conflict != null && File.Exists(destFile))
                        destFile = await ResolveLocalConflictAsync(destFile, false, ct, conflict);
                    using (var src = await browser.OpenReadAsync(config, share, PathForItem(path, f.RelPath, browser), ct))
                    using (var dst = File.Create(destFile))
                    {
                        await CopyStreamAsync(src, dst, f.Size, progress, Path.GetFileName(f.RelPath.Replace('/', '\\')),
                            (int)idx, (int)fileTotal, done, total, ct);
                    }
                    done += f.Size;
                    idx++;
                }
                return true;
            }
            catch (OperationCanceledException)
            {
                Log.Info("NetworkCopyService.CopyRemoteToLocal: cancelled by user");
                throw;
            }
            catch (NetworkOperationException ex)
            {
                Log.Warn("NetworkCopyService.CopyRemoteToLocal: {Reason} ({Detail})", ex.Reason, ex.Message);
                return false;
            }
        }

        /// <summary>Copies a local file or directory tree into a remote share/directory.</summary>
        public static async Task<bool> CopyLocalToRemoteAsync(
            INetworkFileSystemProvider browser, NetworkServerConfig config, string share, string destDir,
            string localPath, bool isDirectory, string displayName,
            IProgress<FileOperations.OperationProgress> progress, CancellationToken ct,
            string destName = null, Func<string, Task<ConflictDecision>> conflict = null)
        {
            Log.Info("NetworkCopyService.CopyLocalToRemote: {Path} → {Share}/{Dest}",
                localPath, share, destDir);
            try
            {
                char sep = NetworkPathUtil.Separator(browser.Protocol);
                if (!isDirectory)
                {
                    long size = new FileInfo(localPath).Length;
                    string name = destName ?? displayName;
                    string remotePath = Join(destDir, name, sep);
                    if (conflict != null && await browser.EntryExistsAsync(config, share, remotePath, false, ct))
                        remotePath = await ResolveRemoteConflictAsync(browser, config, share, remotePath, false, ct, conflict);
                    using (var src = new FileStream(localPath, FileMode.Open, FileAccess.Read,
                        FileShare.Read | FileShare.Delete))
                    using (var dst = await browser.OpenWriteStreamAsync(config, share, remotePath, ct))
                    {
                        await CopyStreamAsync(src, dst, size, progress, name, 0, 1, 0, size, ct);
                    }
                    return true;
                }

                var (entries, folderCount) = await FileOperations.ListRecursiveAsync(localPath);
                var files = entries.Where(e => !e.EndsWith("\\")).ToList();
                string rootName = destName ?? Path.GetFileName(localPath.TrimEnd('\\'));
                string rootRemote = Join(destDir, rootName, sep);
                if (conflict != null && await browser.EntryExistsAsync(config, share, rootRemote, true, ct))
                    rootRemote = await ResolveRemoteConflictAsync(browser, config, share, rootRemote, true, ct, conflict);

                // Create remote dirs first (root + children).
                await browser.CreateDirectoryAsync(config, share, rootRemote, ct);
                foreach (var d in entries.Where(e => e.EndsWith("\\")))
                {
                    string rel = d.Substring(localPath.Length).TrimStart('\\').TrimEnd('\\');
                    if (string.IsNullOrEmpty(rel)) continue;
                    await browser.CreateDirectoryAsync(config, share, Join(rootRemote, rel.Replace('\\', sep), sep), ct);
                }

                long total = 0;
                var sizes = new List<long>();
                foreach (var f in files)
                {
                    long s = new FileInfo(f).Length;
                    sizes.Add(s);
                    total += s;
                }

                long done = 0;
                int idx = 0;
                int fileTotal = files.Count;
                for (int i = 0; i < files.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    string rel = files[i].Substring(localPath.Length).TrimStart('\\');
                    string remotePath = Join(rootRemote, rel.Replace('\\', sep), sep);
                    if (conflict != null && await browser.EntryExistsAsync(config, share, remotePath, false, ct))
                        remotePath = await ResolveRemoteConflictAsync(browser, config, share, remotePath, false, ct, conflict);
                    using (var src = new FileStream(files[i], FileMode.Open, FileAccess.Read,
                        FileShare.Read | FileShare.Delete))
                    using (var dst = await browser.OpenWriteStreamAsync(config, share, remotePath, ct))
                    {
                        await CopyStreamAsync(src, dst, sizes[i], progress, Path.GetFileName(files[i]),
                            idx, fileTotal, done, total, ct);
                    }
                    done += sizes[i];
                    idx++;
                }
                return true;
            }
            catch (OperationCanceledException)
            {
                Log.Info("NetworkCopyService.CopyLocalToRemote: cancelled by user");
                throw;
            }
            catch (NetworkOperationException ex)
            {
                Log.Warn("NetworkCopyService.CopyLocalToRemote: {Reason} ({Detail})", ex.Reason, ex.Message);
                return false;
            }
        }

        /// <summary>Copies a remote file or directory tree to another remote destination.</summary>
        public static async Task<bool> CopyRemoteToRemoteAsync(
            INetworkFileSystemProvider srcBrowser, NetworkServerConfig srcConfig, string srcShare, string srcPath,
            INetworkFileSystemProvider dstBrowser, NetworkServerConfig dstConfig, string dstShare, string dstDir,
            bool isDirectory, string displayName,
            IProgress<FileOperations.OperationProgress> progress, CancellationToken ct,
            string destName = null, Func<string, Task<ConflictDecision>> conflict = null)
        {
            Log.Info("NetworkCopyService.CopyRemoteToRemote: {SShare}/{SPath} → {DShare}/{DDir}",
                srcShare, srcPath, dstShare, dstDir);
            try
            {
                char sep = NetworkPathUtil.Separator(dstBrowser.Protocol);
                string name = destName ?? displayName;
                if (!isDirectory)
                {
                    long size = await srcBrowser.GetFileLengthAsync(srcConfig, srcShare, srcPath, ct);
                    string remotePath = Join(dstDir, name, sep);
                    if (conflict != null && await dstBrowser.EntryExistsAsync(dstConfig, dstShare, remotePath, false, ct))
                        remotePath = await ResolveRemoteConflictAsync(dstBrowser, dstConfig, dstShare, remotePath, false, ct, conflict);
                    using (var src = await srcBrowser.OpenReadAsync(srcConfig, srcShare, srcPath, ct))
                    using (var dst = await dstBrowser.OpenWriteStreamAsync(dstConfig, dstShare, remotePath, ct))
                    {
                        await CopyStreamAsync(src, dst, size, progress, name, 0, 1, 0, size, ct);
                    }
                    return true;
                }

                var items = await ListRemoteTreeAsync(srcBrowser, srcConfig, srcShare, srcPath, ct);
                long total = items.Where(i => !i.IsDir).Sum(i => i.Size);
                long fileTotal = items.Count(i => !i.IsDir);
                string rootName = destName ?? Path.GetFileName(srcPath.TrimEnd('\\'));
                string rootRemote = Join(dstDir, rootName, sep);
                if (conflict != null && await dstBrowser.EntryExistsAsync(dstConfig, dstShare, rootRemote, true, ct))
                    rootRemote = await ResolveRemoteConflictAsync(dstBrowser, dstConfig, dstShare, rootRemote, true, ct, conflict);

                await dstBrowser.CreateDirectoryAsync(dstConfig, dstShare, rootRemote, ct);
                foreach (var d in items.Where(i => i.IsDir))
                    await dstBrowser.CreateDirectoryAsync(dstConfig, dstShare,
                        Join(rootRemote, d.RelPath.Replace('\\', sep), sep), ct);

                long done = 0;
                int idx = 0;
                foreach (var f in items.Where(i => !i.IsDir))
                {
                    ct.ThrowIfCancellationRequested();
                    string remotePath = Join(rootRemote, f.RelPath.Replace('\\', sep), sep);
                    if (conflict != null && await dstBrowser.EntryExistsAsync(dstConfig, dstShare, remotePath, false, ct))
                        remotePath = await ResolveRemoteConflictAsync(dstBrowser, dstConfig, dstShare, remotePath, false, ct, conflict);
                    using (var src = await srcBrowser.OpenReadAsync(srcConfig, srcShare, PathForItem(srcPath, f.RelPath, srcBrowser), ct))
                    using (var dst = await dstBrowser.OpenWriteStreamAsync(dstConfig, dstShare, remotePath, ct))
                    {
                        await CopyStreamAsync(src, dst, f.Size, progress, Path.GetFileName(f.RelPath.Replace('/', '\\')),
                            (int)idx, (int)fileTotal, done, total, ct);
                    }
                    done += f.Size;
                    idx++;
                }
                return true;
            }
            catch (OperationCanceledException)
            {
                Log.Info("NetworkCopyService.CopyRemoteToRemote: cancelled by user");
                throw;
            }
            catch (NetworkOperationException ex)
            {
                Log.Warn("NetworkCopyService.CopyRemoteToRemote: {Reason} ({Detail})", ex.Reason, ex.Message);
                return false;
            }
        }

        /// <summary>Deletes a remote file or directory (recursive for directories).</summary>
        public static async Task DeleteRemoteAsync(
            INetworkFileSystemProvider browser, NetworkServerConfig config, string share, string path,
            bool isDirectory, CancellationToken ct)
        {
            if (isDirectory)
                await browser.DeleteDirectoryAsync(config, share, path, ct);
            else
                await browser.DeleteFileAsync(config, share, path, ct);
        }

        /// <summary>Counts files + total bytes of a remote entry without copying.</summary>
        public static async Task<(int FileCount, long TotalBytes)> ScanRemoteEntriesAsync(
            INetworkFileSystemProvider browser, NetworkServerConfig config, string share, string path,
            bool isDirectory, CancellationToken ct)
        {
            if (!isDirectory)
            {
                long size = await browser.GetFileLengthAsync(config, share, path, ct);
                return (1, size);
            }
            var items = await ListRemoteTreeAsync(browser, config, share, path, ct);
            return (items.Count(i => !i.IsDir), items.Where(i => !i.IsDir).Sum(i => i.Size));
        }

        private sealed class RemoteItem
        {
            public string RelPath;
            public bool IsDir;
            public long Size;
        }

        private static async Task<List<RemoteItem>> ListRemoteTreeAsync(
            INetworkFileSystemProvider browser, NetworkServerConfig config, string share, string path, CancellationToken ct)
        {
            var result = new List<RemoteItem>();
            await WalkRemoteAsync(browser, config, share, path, "", result, ct);
            return result;
        }

        private static async Task WalkRemoteAsync(
            INetworkFileSystemProvider browser, NetworkServerConfig config, string share, string path,
            string relPrefix, List<RemoteItem> result, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            char sep = NetworkPathUtil.Separator(browser.Protocol);
            var entries = await browser.ListDirectoryAsync(config, share, path, ct);
            foreach (var e in entries)
            {
                string rel = string.IsNullOrEmpty(relPrefix) ? e.Name : relPrefix + sep + e.Name;
                if (e.IsDirectory)
                {
                    result.Add(new RemoteItem { RelPath = rel, IsDir = true, Size = 0 });
                    await WalkRemoteAsync(browser, config, share, Join(path, e.Name, sep), rel, result, ct);
                }
                else
                {
                    result.Add(new RemoteItem { RelPath = rel, IsDir = false, Size = e.Size });
                }
            }
        }

        private static async Task<string> ResolveLocalConflictAsync(string dest, bool isDirectory,
            CancellationToken ct, Func<string, Task<ConflictDecision>> conflict)
        {
            ConflictDecision decision;
            try { decision = await conflict(dest); }
            catch { decision = ConflictDecision.Cancel; }
            switch (decision)
            {
                case ConflictDecision.ReplaceAll:
                    return dest;
                case ConflictDecision.RenameAll:
                    return isDirectory
                        ? FileOperations.GetUniqueDirectoryPath(dest)
                        : FileOperations.GetUniqueFilePath(dest);
                default:
                    Log.Info("NetworkCopyService.ResolveLocalConflict: cancelled on {Path}", dest);
                    ct.ThrowIfCancellationRequested();
                    throw new OperationCanceledException(ct);
            }
        }

        private static async Task<string> ResolveRemoteConflictAsync(
            INetworkFileSystemProvider browser, NetworkServerConfig config, string share, string path,
            bool isDirectory, CancellationToken ct, Func<string, Task<ConflictDecision>> conflict)
        {
            ConflictDecision decision;
            try { decision = await conflict(path); }
            catch { decision = ConflictDecision.Cancel; }
            switch (decision)
            {
                case ConflictDecision.ReplaceAll:
                    return path;
                case ConflictDecision.RenameAll:
                    return await GetUniqueRemotePathAsync(browser, config, share, path, isDirectory, ct);
                default:
                    Log.Info("NetworkCopyService.ResolveRemoteConflict: cancelled on {Path}", path);
                    ct.ThrowIfCancellationRequested();
                    throw new OperationCanceledException(ct);
            }
        }

        private static async Task<string> GetUniqueRemotePathAsync(
            INetworkFileSystemProvider browser, NetworkServerConfig config, string share, string path,
            bool isDirectory, CancellationToken ct)
        {
            string dir = NetworkPathUtil.Parent(path, browser.Protocol);
            string name = Path.GetFileName(path.Replace('/', '\\'));
            foreach (var candidate in NetworkPathUtil.NameCandidates(dir, name, browser.Protocol))
            {
                if (!await browser.EntryExistsAsync(config, share, candidate, isDirectory, ct))
                    return candidate;
            }
            return path;
        }

        private static async Task CopyStreamAsync(Stream src, Stream dst, long fileSize,
            IProgress<FileOperations.OperationProgress> progress, string name,
            int fileIndex, int fileTotal, long bytesDone, long totalBytes, CancellationToken ct)
        {
            // Size the buffer to the file when small: a 1MB chunk for a 3KB file is a
            // pointless LOH allocation on a big directory copy. The effective chunk is
            // Min(buffer, MaxReadSize) anyway, so a smaller buffer never costs an extra
            // round-trip (a small file is one round-trip regardless). 64KB floor sits
            // below the 85KB LOH threshold — gen0 allocation, cheap.
            int chunk = (int)Math.Min(ChunkSize, Math.Max(64 * 1024, fileSize));
            var buffer = new byte[chunk];
            long fileDone = 0;
            long lastReport = -ReportThrottle;
            while (true)
            {
                int read = await src.ReadAsync(buffer, 0, buffer.Length, ct);
                if (read <= 0) break;
                await dst.WriteAsync(buffer, 0, read, ct);
                fileDone += read;
                if (progress != null && (fileDone - lastReport >= ReportThrottle || read < buffer.Length))
                {
                    lastReport = fileDone;
                    progress.Report(new FileOperations.OperationProgress
                    {
                        FileName = name,
                        FileIndex = fileIndex,
                        FileTotal = fileTotal,
                        BytesCopied = bytesDone + fileDone,
                        TotalBytes = totalBytes
                    });
                }
            }
        }

        private static string Join(string dir, string name) => NetworkPathUtil.Join(dir, name);

        private static string Join(string dir, string name, char sep) => NetworkPathUtil.Join(dir, name, sep);

        private static string PathForItem(string basePath, string rel) => NetworkPathUtil.PathForItem(basePath, rel);

        private static string PathForItem(string basePath, string rel, INetworkFileSystemProvider browser)
            => NetworkPathUtil.PathForItem(basePath, rel, browser.Protocol);
    }
}
