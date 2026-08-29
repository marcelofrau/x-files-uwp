using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.FileSystem;
using XFiles.Network;

namespace XFiles.Tests
{
    [TestClass]
    public class NetworkCopyServiceTests
    {
        #region Mock provider

        private class MockProvider : INetworkFileSystemProvider
        {
            public NetworkProtocol Protocol { get; set; } = NetworkProtocol.Ftp;
            public Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> Dirs { get; } = new(StringComparer.OrdinalIgnoreCase);

            public Func<string, CancellationToken, Task<Stream>> OpenReadOverride { get; set; }

            public Task<string> TestConnectionAsync(NetworkServerConfig c, string p, CancellationToken ct) => Task.FromResult("OK");
            public Task<List<string>> ListSharesAsync(NetworkServerConfig c, CancellationToken ct) => Task.FromResult(new List<string>());

            public Task<List<NetworkFileEntry>> ListDirectoryAsync(NetworkServerConfig c, string share, string path, CancellationToken ct)
            {
                var r = new List<NetworkFileEntry>();
                string px = string.IsNullOrEmpty(path) ? "" : path.TrimEnd('/', '\\') + "/";
                foreach (var kv in Files)
                {
                    string norm = kv.Key.Replace('\\', '/');
                    if (norm.StartsWith(px, StringComparison.OrdinalIgnoreCase) && norm.Length > px.Length)
                    {
                        string rel = norm.Substring(px.Length);
                        if (!rel.Contains('/') || rel.EndsWith('/'))
                            r.Add(new NetworkFileEntry { Name = rel.TrimEnd('/'), IsDirectory = rel.EndsWith("/"), Size = rel.EndsWith("/") ? 0 : kv.Value.Length });
                    }
                }
                foreach (var d in Dirs)
                {
                    string norm = d.Replace('\\', '/').TrimEnd('/');
                    if (norm.StartsWith(px, StringComparison.OrdinalIgnoreCase) && norm.Length > px.Length - 1 && !norm.Equals(px.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                    {
                        string rel = norm.Substring(Math.Min(px.Length, norm.Length));
                        if (!string.IsNullOrEmpty(rel) && !rel.Contains('/'))
                            r.Add(new NetworkFileEntry { Name = rel, IsDirectory = true });
                    }
                }
                return Task.FromResult(r);
            }

            public Task<Stream> OpenReadAsync(NetworkServerConfig c, string share, string path, CancellationToken ct)
            {
                if (OpenReadOverride != null) return OpenReadOverride(path, ct);
                if (Files.TryGetValue(path, out var data))
                    return Task.FromResult<Stream>(new MemoryStream(data, false));
                throw new NetworkOperationException(NetworkOperationReason.NotFound, $"Not found: {path}");
            }

            public Task<long> GetFileLengthAsync(NetworkServerConfig c, string share, string path, CancellationToken ct)
            {
                if (Files.TryGetValue(path, out var data)) return Task.FromResult((long)data.Length);
                throw new NetworkOperationException(NetworkOperationReason.NotFound, $"Not found: {path}");
            }

            public Task<bool> EntryExistsAsync(NetworkServerConfig c, string share, string path, bool isDir, CancellationToken ct)
            {
                return Task.FromResult(isDir ? Dirs.Contains(path) : Files.ContainsKey(path));
            }

            public Task WriteFileAsync(NetworkServerConfig c, string share, string path, string localPath, CancellationToken ct)
            {
                Files[path] = File.ReadAllBytes(localPath);
                return Task.CompletedTask;
            }

            public Task<Stream> OpenWriteStreamAsync(NetworkServerConfig c, string share, string path, CancellationToken ct)
            {
                var ms = new MemoryStream();
                return Task.FromResult<Stream>(new WriteCaptureStream(data => Files[path] = data));
            }

            public Task DeleteFileAsync(NetworkServerConfig c, string share, string path, CancellationToken ct) { Files.Remove(path); return Task.CompletedTask; }
            public Task DeleteDirectoryAsync(NetworkServerConfig c, string share, string path, CancellationToken ct) { Dirs.Remove(path); return Task.CompletedTask; }
            public Task RenameFileAsync(NetworkServerConfig c, string share, string path, string newName, bool isDir, CancellationToken ct) => Task.CompletedTask;
            public Task CreateDirectoryAsync(NetworkServerConfig c, string share, string path, CancellationToken ct) { Dirs.Add(path); return Task.CompletedTask; }
            public void Disconnect(NetworkServerConfig c) { }
        }

        private class WriteCaptureStream : MemoryStream
        {
            private readonly Action<byte[]> _onDispose;
            public WriteCaptureStream(Action<byte[]> onDispose) => _onDispose = onDispose;
            protected override void Dispose(bool disposing)
            {
                if (disposing) _onDispose(ToArray());
                base.Dispose(disposing);
            }
        }

        private static NetworkServerConfig Cfg() => Cfg(NetworkProtocol.Ftp);

        private static NetworkServerConfig Cfg(NetworkProtocol protocol) =>
            new NetworkServerConfig { Host = "host", Port = 21, Username = "u", Protocol = protocol };

        private static string TmpDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "xfiles_ncopy_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static byte[] Data(int len, int seed)
        {
            var buf = new byte[len];
            new Random(seed).NextBytes(buf);
            return buf;
        }

        private static void WaitUntil(Func<bool> done, int timeoutMs = 5000)
        {
            var sw = Stopwatch.StartNew();
            while (!done() && sw.ElapsedMilliseconds < timeoutMs) Thread.Sleep(25);
        }

        #endregion

        #region Provider contract

        [TestMethod]
        public async Task MockProvider_OpenRead_ReturnsSameBytes()
        {
            var mock = new MockProvider();
            byte[] data = Encoding.UTF8.GetBytes("hello world");
            mock.Files["a/b.txt"] = data;

            using var stream = await mock.OpenReadAsync(Cfg(), "", "a/b.txt", CancellationToken.None);
            var buf = new byte[data.Length];
            int read = await stream.ReadAsync(buf, 0, buf.Length);
            CollectionAssert.AreEqual(data, buf);
        }

        [TestMethod]
        public async Task MockProvider_OpenRead_ThrowsNotFound()
        {
            var mock = new MockProvider();
            await Assert.ThrowsExceptionAsync<NetworkOperationException>(async () =>
                await mock.OpenReadAsync(Cfg(), "", "missing.txt", CancellationToken.None));
        }

        [TestMethod]
        public async Task MockProvider_ListDirectory_ReturnsFilesAndDirs()
        {
            var mock = new MockProvider();
            mock.Files["root/a.txt"] = new byte[] { 1 };
            mock.Files["root/b.txt"] = new byte[] { 2, 3 };
            mock.Dirs.Add("root/sub");

            var entries = await mock.ListDirectoryAsync(Cfg(), "", "root", CancellationToken.None);
            Assert.AreEqual(3, entries.Count);
            Assert.IsTrue(entries.Exists(e => e.Name == "a.txt" && !e.IsDirectory && e.Size == 1));
            Assert.IsTrue(entries.Exists(e => e.Name == "b.txt" && !e.IsDirectory && e.Size == 2));
            Assert.IsTrue(entries.Exists(e => e.Name == "sub" && e.IsDirectory));
        }

        [TestMethod]
        public async Task MockProvider_OpenWrite_CapturesWrittenBytes()
        {
            var mock = new MockProvider();
            var ws = await mock.OpenWriteStreamAsync(Cfg(), "", "out.bin", CancellationToken.None);
            byte[] data = Encoding.UTF8.GetBytes("written");
            await ws.WriteAsync(data, 0, data.Length);
            ws.Dispose();
            Assert.IsTrue(mock.Files.ContainsKey("out.bin"), "File should be captured after dispose");
            CollectionAssert.AreEqual(data, mock.Files["out.bin"]);
        }

        #endregion

        #region CopyRemoteToLocal — single file

        [TestMethod]
        public async Task RemoteToLocal_SingleFile_CopiesAllBytes()
        {
            var src = new MockProvider();
            byte[] data = Data(128 * 1024, 1);
            src.Files["src/file.bin"] = data;
            string tmp = TmpDir();
            try
            {
                bool ok = await NetworkCopyService.CopyRemoteToLocalAsync(
                    src, Cfg(), "", "src/file.bin", tmp, false, null, CancellationToken.None);
                Assert.IsTrue(ok);
                CollectionAssert.AreEqual(data, File.ReadAllBytes(Path.Combine(tmp, "file.bin")));
            }
            finally { Directory.Delete(tmp, true); }
        }

        [TestMethod]
        public async Task RemoteToLocal_EmptyFile()
        {
            var src = new MockProvider();
            src.Files["src/empty.bin"] = Array.Empty<byte>();
            string tmp = TmpDir();
            try
            {
                bool ok = await NetworkCopyService.CopyRemoteToLocalAsync(
                    src, Cfg(), "", "src/empty.bin", tmp, false, null, CancellationToken.None);
                Assert.IsTrue(ok);
                string dest = Path.Combine(tmp, "empty.bin");
                Assert.IsTrue(File.Exists(dest));
                Assert.AreEqual(0, new FileInfo(dest).Length);
            }
            finally { Directory.Delete(tmp, true); }
        }

        [TestMethod]
        public async Task RemoteToLocal_LargeFile()
        {
            var src = new MockProvider();
            byte[] data = Data(2 * 1024 * 1024, 2);
            src.Files["src/big.bin"] = data;
            string tmp = TmpDir();
            try
            {
                bool ok = await NetworkCopyService.CopyRemoteToLocalAsync(
                    src, Cfg(), "", "src/big.bin", tmp, false, null, CancellationToken.None);
                Assert.IsTrue(ok);
                CollectionAssert.AreEqual(data, File.ReadAllBytes(Path.Combine(tmp, "big.bin")));
            }
            finally { Directory.Delete(tmp, true); }
        }

        #endregion

        #region CopyRemoteToLocal — directory tree

        [TestMethod]
        public async Task RemoteToLocal_DirectoryTree_CopiesAllFiles()
        {
            var src = new MockProvider();
            byte[] da = Data(1024, 11), db = Data(2048, 12), dc = Data(512, 13);
            src.Files["tree/a.txt"] = da;
            src.Files["tree/sub/b.txt"] = db;
            src.Files["tree/sub/deep/c.bin"] = dc;
            src.Dirs.Add("tree/sub");
            src.Dirs.Add("tree/sub/deep");
            string tmp = TmpDir();
            try
            {
                bool ok = await NetworkCopyService.CopyRemoteToLocalAsync(
                    src, Cfg(), "", "tree", tmp, true, null, CancellationToken.None);
                Assert.IsTrue(ok);
                Assert.IsTrue(Directory.Exists(Path.Combine(tmp, "tree", "sub", "deep")));
                CollectionAssert.AreEqual(da, File.ReadAllBytes(Path.Combine(tmp, "tree", "a.txt")));
                CollectionAssert.AreEqual(db, File.ReadAllBytes(Path.Combine(tmp, "tree", "sub", "b.txt")));
                CollectionAssert.AreEqual(dc, File.ReadAllBytes(Path.Combine(tmp, "tree", "sub", "deep", "c.bin")));
            }
            finally { Directory.Delete(tmp, true); }
        }

        #endregion

        #region CopyRemoteToLocal — retry

        [TestMethod]
        public async Task RemoteToLocal_Retry_SucceedsAfterTransientFailure()
        {
            var src = new MockProvider();
            byte[] data = Encoding.UTF8.GetBytes("flaky payload");
            int attempts = 0;
            src.Files["src/flaky.bin"] = data;
            src.OpenReadOverride = (path, ct) =>
            {
                int n = Interlocked.Increment(ref attempts);
                if (n <= 2) throw new IOException($"transient failure #{n}");
                return Task.FromResult<Stream>(new MemoryStream(data, false));
            };
            string tmp = TmpDir();
            try
            {
                bool ok = await NetworkCopyService.CopyRemoteToLocalAsync(
                    src, Cfg(), "", "src/flaky.bin", tmp, false, null, CancellationToken.None);
                Assert.IsTrue(ok);
                Assert.AreEqual(3, attempts);
                CollectionAssert.AreEqual(data, File.ReadAllBytes(Path.Combine(tmp, "flaky.bin")));
            }
            finally { Directory.Delete(tmp, true); }
        }

        [TestMethod]
        public async Task RemoteToLocal_Retry_Exhausted_ReturnsFalse()
        {
            var src = new MockProvider();
            int attempts = 0;
            src.Files["src/dead.bin"] = Data(1024, 91);
            src.OpenReadOverride = (path, ct) =>
            {
                Interlocked.Increment(ref attempts);
                throw new IOException("persistent failure");
            };
            string tmp = TmpDir();
            try
            {
                await Assert.ThrowsExceptionAsync<IOException>(async () =>
                    await NetworkCopyService.CopyRemoteToLocalAsync(
                        src, Cfg(), "", "src/dead.bin", tmp, false, null, CancellationToken.None));
                Assert.AreEqual(NetworkCopyService_MaxRetries, attempts);
            }
            finally { Directory.Delete(tmp, true); }
        }

        private const int NetworkCopyService_MaxRetries = 5;

        #endregion

        #region CopyRemoteToLocal — cancellation & errors

        [TestMethod]
        public async Task RemoteToLocal_Cancelled_ThrowsOperationCanceledException()
        {
            var src = new MockProvider();
            src.Files["src/cancel.bin"] = Data(4096, 21);
            src.OpenReadOverride = (path, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult<Stream>(new MemoryStream(new byte[4096], false));
            };
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            string tmp = TmpDir();
            try
            {
                await Assert.ThrowsExceptionAsync<OperationCanceledException>(async () =>
                    await NetworkCopyService.CopyRemoteToLocalAsync(
                        src, Cfg(), "", "src/cancel.bin", tmp, false, null, cts.Token));
            }
            finally { Directory.Delete(tmp, true); }
        }

        [TestMethod]
        public async Task RemoteToLocal_NotFound_ReturnsFalse()
        {
            var src = new MockProvider();
            string tmp = TmpDir();
            try
            {
                bool ok = await NetworkCopyService.CopyRemoteToLocalAsync(
                    src, Cfg(), "", "missing/nope.bin", tmp, false, null, CancellationToken.None);
                Assert.IsFalse(ok);
            }
            finally { Directory.Delete(tmp, true); }
        }

        #endregion

        #region CopyRemoteToLocal — progress

        [TestMethod]
        public async Task RemoteToLocal_ReportsProgressMonotonic()
        {
            var src = new MockProvider();
            byte[] data = Data(8 * 1024 * 1024, 31);
            src.Files["src/big.bin"] = data;

            var reports = new List<FileOperations.OperationProgress>();
            var progress = new Progress<FileOperations.OperationProgress>(p =>
                reports.Add(new FileOperations.OperationProgress
                {
                    FileName = p.FileName,
                    BytesCopied = p.BytesCopied,
                    TotalBytes = p.TotalBytes,
                    FileIndex = p.FileIndex,
                    FileTotal = p.FileTotal
                }));

            string tmp = TmpDir();
            try
            {
                bool ok = await NetworkCopyService.CopyRemoteToLocalAsync(
                    src, Cfg(), "", "src/big.bin", tmp, false, progress, CancellationToken.None);
                Assert.IsTrue(ok);

                Thread.Sleep(500);
                WaitUntil(() => reports.Count >= 2);
                Assert.IsTrue(reports.Count >= 2, $"Expected progress reports, got {reports.Count}");
                long prev = -1;
                foreach (var r in reports)
                {
                    Assert.IsTrue(r.BytesCopied >= prev, $"Non-monotonic: {prev} -> {r.BytesCopied}");
                    prev = r.BytesCopied;
                }
                var last = reports[reports.Count - 1];
                Assert.AreEqual("big.bin", last.FileName);
                Assert.AreEqual((long)data.Length, last.BytesCopied);
                Assert.AreEqual((long)data.Length, last.TotalBytes);
            }
            finally { Directory.Delete(tmp, true); }
        }

        #endregion

        #region CopyRemoteToLocal — conflict resolution

        [TestMethod]
        public async Task RemoteToLocal_ConflictReplaceAll_Overwrites()
        {
            var src = new MockProvider();
            byte[] newData = Data(16 * 1024, 81);
            src.Files["r/data.bin"] = newData;
            string tmp = TmpDir();
            try
            {
                string dest = Path.Combine(tmp, "data.bin");
                File.WriteAllBytes(dest, Encoding.UTF8.GetBytes("OLD"));

                bool ok = await NetworkCopyService.CopyRemoteToLocalAsync(
                    src, Cfg(), "", "r/data.bin", tmp, false, null, CancellationToken.None,
                    conflict: async (path) => ConflictDecision.ReplaceAll);

                Assert.IsTrue(ok);
                CollectionAssert.AreEqual(newData, File.ReadAllBytes(dest));
            }
            finally { Directory.Delete(tmp, true); }
        }

        [TestMethod]
        public async Task RemoteToLocal_ConflictRenameAll_Renames()
        {
            var src = new MockProvider();
            byte[] newData = Data(16 * 1024, 82);
            src.Files["r/data.bin"] = newData;
            string tmp = TmpDir();
            try
            {
                File.WriteAllBytes(Path.Combine(tmp, "data.bin"), Encoding.UTF8.GetBytes("KEEP"));

                bool ok = await NetworkCopyService.CopyRemoteToLocalAsync(
                    src, Cfg(), "", "r/data.bin", tmp, false, null, CancellationToken.None,
                    conflict: async (path) => ConflictDecision.RenameAll);

                Assert.IsTrue(ok);
                // Original kept; copy written under a uniquely-suffixed name
                Assert.AreEqual("KEEP", File.ReadAllText(Path.Combine(tmp, "data.bin")));
                string[] files = Directory.GetFiles(tmp);
                Assert.AreEqual(2, files.Length, "RenameAll must keep the original and write a renamed copy");
                bool found = false;
                foreach (var f in files)
                    if (f != Path.Combine(tmp, "data.bin") && new FileInfo(f).Length == newData.Length)
                        found = true;
                Assert.IsTrue(found, "A renamed copy must exist");
            }
            finally { Directory.Delete(tmp, true); }
        }

        [TestMethod]
        public async Task RemoteToLocal_ConflictCancel_Throws()
        {
            var src = new MockProvider();
            src.Files["r/data.bin"] = Data(16, 83);
            string tmp = TmpDir();
            try
            {
                File.WriteAllBytes(Path.Combine(tmp, "data.bin"), Encoding.UTF8.GetBytes("KEEP"));

                await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
                    NetworkCopyService.CopyRemoteToLocalAsync(
                        src, Cfg(), "", "r/data.bin", tmp, false, null, CancellationToken.None,
                        conflict: async (path) => ConflictDecision.Cancel));
            }
            finally { Directory.Delete(tmp, true); }
        }

        [TestMethod]
        public async Task RemoteToLocal_DirConflictRenameAll_RenamesRoot()
        {
            var src = new MockProvider();
            byte[] data = Data(100, 84);
            src.Files["tree/a.txt"] = data;
            src.Dirs.Add("tree/sub");
            string tmp = TmpDir();
            try
            {
                // Pre-create a conflicting destination folder with the same name
                string conflictingRoot = Path.Combine(tmp, "tree");
                Directory.CreateDirectory(conflictingRoot);
                File.WriteAllText(Path.Combine(conflictingRoot, "existing.txt"), "OLD");

                bool ok = await NetworkCopyService.CopyRemoteToLocalAsync(
                    src, Cfg(), "", "tree", tmp, true, null, CancellationToken.None,
                    conflict: async (path) => ConflictDecision.RenameAll);

                Assert.IsTrue(ok);
                string[] dirs = Directory.GetDirectories(tmp);
                Assert.AreEqual(2, dirs.Length, "Original tree kept + renamed copy");
            }
            finally { Directory.Delete(tmp, true); }
        }

        #endregion

        #region CopyLocalToRemote

        [TestMethod]
        public async Task LocalToRemote_SingleFile_UploadsAllBytes()
        {
            var dst = new MockProvider();
            byte[] data = Data(96 * 1024, 41);
            string tmp = TmpDir();
            string localFile = Path.Combine(tmp, "upload.bin");
            File.WriteAllBytes(localFile, data);
            try
            {
                bool ok = await NetworkCopyService.CopyLocalToRemoteAsync(
                    dst, Cfg(), "", "dest", localFile, false, "upload.bin", null, CancellationToken.None);
                Assert.IsTrue(ok);
                Assert.IsTrue(dst.Files.ContainsKey("dest/upload.bin"));
                CollectionAssert.AreEqual(data, dst.Files["dest/upload.bin"]);
            }
            finally { Directory.Delete(tmp, true); }
        }

        #endregion

        #region CopyRemoteToRemote — single file

        [TestMethod]
        public async Task RemoteToRemote_SingleFile_CopiesAllBytes()
        {
            var src = new MockProvider();
            var dst = new MockProvider();
            byte[] data = Data(64 * 1024, 51);
            src.Files["r/source.bin"] = data;
            bool ok = await NetworkCopyService.CopyRemoteToRemoteAsync(
                src, Cfg(), "", "r/source.bin",
                dst, Cfg(), "", "out", false, "source.bin", null, CancellationToken.None);
            Assert.IsTrue(ok);
            Assert.IsTrue(dst.Files.ContainsKey("out/source.bin"));
            CollectionAssert.AreEqual(data, dst.Files["out/source.bin"]);
        }

        [TestMethod]
        public async Task RemoteToRemote_CrossProtocol()
        {
            var src = new MockProvider { Protocol = NetworkProtocol.Ftp };
            var dst = new MockProvider { Protocol = NetworkProtocol.Smb };
            byte[] data = Data(32 * 1024, 52);
            src.Files["r/mixed.dat"] = data;
            bool ok = await NetworkCopyService.CopyRemoteToRemoteAsync(
                src, Cfg(NetworkProtocol.Ftp), "", "r/mixed.dat",
                dst, Cfg(NetworkProtocol.Smb), "", "out", false, "mixed.dat", null, CancellationToken.None);
            Assert.IsTrue(ok);
            Assert.IsTrue(dst.Files.ContainsKey(@"out\mixed.dat"), "Destination path must use SMB separator");
            CollectionAssert.AreEqual(data, dst.Files[@"out\mixed.dat"]);
        }

        #endregion

        #region CopyRemoteToRemote — directory tree

        [TestMethod]
        public async Task RemoteToRemote_DirectoryTree_CopiesAllFiles()
        {
            var src = new MockProvider();
            var dst = new MockProvider();
            byte[] da = Data(700, 61), db = Data(1400, 62), dc = Data(350, 63);
            src.Files["tree/a.txt"] = da;
            src.Files["tree/sub/b.txt"] = db;
            src.Files["tree/sub/deep/c.bin"] = dc;
            src.Dirs.Add("tree/sub");
            src.Dirs.Add("tree/sub/deep");

            bool ok = await NetworkCopyService.CopyRemoteToRemoteAsync(
                src, Cfg(), "", "tree",
                dst, Cfg(), "", "dstroot", true, "tree", null, CancellationToken.None);
            Assert.IsTrue(ok);
            Assert.IsTrue(dst.Files.ContainsKey("dstroot/tree/a.txt"));
            Assert.IsTrue(dst.Files.ContainsKey("dstroot/tree/sub/b.txt"));
            Assert.IsTrue(dst.Files.ContainsKey("dstroot/tree/sub/deep/c.bin"));
            CollectionAssert.AreEqual(da, dst.Files["dstroot/tree/a.txt"]);
            CollectionAssert.AreEqual(db, dst.Files["dstroot/tree/sub/b.txt"]);
            CollectionAssert.AreEqual(dc, dst.Files["dstroot/tree/sub/deep/c.bin"]);
        }

        #endregion

        #region CopyRemoteToRemote — conflict resolution

        [TestMethod]
        public async Task RemoteToRemote_ConflictReplaceAll()
        {
            var src = new MockProvider();
            var dst = new MockProvider();
            byte[] oldData = Encoding.UTF8.GetBytes("OLD");
            byte[] newData = Data(16 * 1024, 71);
            src.Files["r/new.bin"] = newData;
            dst.Files["dir/dup.bin"] = oldData;

            bool ok = await NetworkCopyService.CopyRemoteToRemoteAsync(
                src, Cfg(), "", "r/new.bin",
                dst, Cfg(), "", "dir", false, "dup.bin", null, CancellationToken.None,
                conflict: async (path) => ConflictDecision.ReplaceAll);
            Assert.IsTrue(ok);
            Assert.IsFalse(dst.Files.ContainsKey("dir/dup (1).bin"), "ReplaceAll must not create renamed copies");
            CollectionAssert.AreEqual(newData, dst.Files["dir/dup.bin"]);
        }

        [TestMethod]
        public async Task RemoteToRemote_ConflictRenameAll()
        {
            var src = new MockProvider();
            var dst = new MockProvider();
            byte[] oldData = Encoding.UTF8.GetBytes("KEEP ME");
            byte[] newData = Data(16 * 1024, 72);
            src.Files["r/new.bin"] = newData;
            dst.Files["dir/dup.bin"] = oldData;

            bool ok = await NetworkCopyService.CopyRemoteToRemoteAsync(
                src, Cfg(), "", "r/new.bin",
                dst, Cfg(), "", "dir", false, "dup.bin", null, CancellationToken.None,
                conflict: async (path) => ConflictDecision.RenameAll);
            Assert.IsTrue(ok);
            Assert.IsTrue(dst.Files.ContainsKey("dir/dup.bin"), "Original destination must stay untouched");
            CollectionAssert.AreEqual(oldData, dst.Files["dir/dup.bin"]);
            Assert.IsTrue(dst.Files.ContainsKey("dir/dup (1).bin"), "Incoming file must be auto-renamed");
            CollectionAssert.AreEqual(newData, dst.Files["dir/dup (1).bin"]);
        }

        #endregion

        #region DeleteRemote

        [TestMethod]
        public async Task DeleteRemote_File()
        {
            var mock = new MockProvider();
            mock.Files["junk/gone.txt"] = new byte[] { 1, 2, 3 };
            await NetworkCopyService.DeleteRemoteAsync(mock, Cfg(), "", "junk/gone.txt", false, CancellationToken.None);
            Assert.IsFalse(mock.Files.ContainsKey("junk/gone.txt"));
        }

        [TestMethod]
        public async Task DeleteRemote_Directory()
        {
            var mock = new MockProvider();
            mock.Dirs.Add("junk/folder");
            await NetworkCopyService.DeleteRemoteAsync(mock, Cfg(), "", "junk/folder", true, CancellationToken.None);
            Assert.IsFalse(mock.Dirs.Contains("junk/folder"));
        }

        #endregion

        #region ScanRemoteEntries

        [TestMethod]
        public async Task ScanRemote_SingleFile()
        {
            var mock = new MockProvider();
            mock.Files["solo/file.dat"] = Data(12345, 81);
            var (count, bytes) = await NetworkCopyService.ScanRemoteEntriesAsync(
                mock, Cfg(), "", "solo/file.dat", false, CancellationToken.None);
            Assert.AreEqual(1, count);
            Assert.AreEqual(12345L, bytes);
        }

        [TestMethod]
        public async Task ScanRemote_Directory()
        {
            var mock = new MockProvider();
            mock.Files["tree/a.txt"] = Data(1000, 82);
            mock.Files["tree/sub/b.txt"] = Data(2000, 83);
            mock.Files["tree/sub/deep/c.bin"] = Data(500, 84);
            mock.Dirs.Add("tree/sub");
            mock.Dirs.Add("tree/sub/deep");
            var (count, bytes) = await NetworkCopyService.ScanRemoteEntriesAsync(
                mock, Cfg(), "", "tree", true, CancellationToken.None);
            Assert.AreEqual(3, count);
            Assert.AreEqual(3500L, bytes);
        }

        #endregion

        #region WebDavWriteStream temp-file behavior

        [TestMethod]
        public void WebDavWriteStream_TempFileExists_DuringWrite()
        {
            string tempPath = null;
            var stream = new XFiles.Network.WebDavWriteStream_Facade(out tempPath);
            stream.Write(new byte[] { 1, 2, 3 }, 0, 3);
            Assert.IsTrue(File.Exists(tempPath), "Temp file should exist during write");
            stream.Dispose();
            Assert.IsFalse(File.Exists(tempPath), "Temp file should be cleaned up after dispose");
        }

        [TestMethod]
        public void WebDavWriteStream_DeleteOnClose_CleansUp()
        {
            string tempPath = null;
            var stream = new XFiles.Network.WebDavWriteStream_Facade(out tempPath);
            stream.Write(new byte[] { 10, 20, 30, 40 }, 0, 4);
            Assert.AreEqual(4, stream.Length);
            stream.Dispose();
            Assert.IsFalse(File.Exists(tempPath));
        }

        #endregion

        #region SafeFileExists / SafeCreateFile patterns

        [TestMethod]
        public void SafeFileExists_NormalFile_ReturnsTrue()
        {
            string tmp = Path.GetTempFileName();
            try
            {
                bool exists = SafeFileExists(tmp);
                Assert.IsTrue(exists);
            }
            finally { File.Delete(tmp); }
        }

        [TestMethod]
        public void SafeFileExists_MissingFile_ReturnsFalse()
        {
            Assert.IsFalse(SafeFileExists(Path.Combine(Path.GetTempPath(), "nonexistent_" + Guid.NewGuid() + ".txt")));
        }

        [TestMethod]
        public void SafeCreateFile_NormalFile_Works()
        {
            string tmp = Path.GetTempPath() + "xfiles_safecreate_" + Guid.NewGuid() + ".txt";
            try
            {
                using (var fs = SafeCreateFile(tmp))
                    fs.Write(new byte[] { 1, 2, 3 }, 0, 3);
                Assert.IsTrue(File.Exists(tmp));
                Assert.AreEqual(3, new FileInfo(tmp).Length);
            }
            finally { File.Delete(tmp); }
        }

        private static bool SafeFileExists(string path)
        {
            try { return File.Exists(path); }
            catch { return false; }
        }

        private static FileStream SafeCreateFile(string path)
        {
            try { return File.Create(path); }
            catch (IOException) { File.Delete(path); return File.Create(path); }
        }

        #endregion

        #region NetworkOperationException contract

        [TestMethod]
        public void NetworkOperationException_Properties()
        {
            var ex = new NetworkOperationException(NetworkOperationReason.AccessDenied, "no access");
            Assert.AreEqual(NetworkOperationReason.AccessDenied, ex.Reason);
            Assert.AreEqual("no access", ex.Message);
        }

        [TestMethod]
        public void NetworkOperationException_WithInner()
        {
            var inner = new IOException("disk error");
            var ex = new NetworkOperationException(NetworkOperationReason.TimedOut, "copy failed", inner);
            Assert.AreSame(inner, ex.InnerException);
        }

        [TestMethod]
        public async Task LocalToRemote_DirectoryTree_CreatesDirsAndUploadsAll()
        {
            var browser = new MockProvider();
            string dir = TmpDir();
            try
            {
                string sub = Path.Combine(dir, "sub");
                Directory.CreateDirectory(sub);
                byte[] a = Data(1000, 11), b = Data(2000, 22);
                File.WriteAllBytes(Path.Combine(dir, "a.txt"), a);
                File.WriteAllBytes(Path.Combine(sub, "b.bin"), b);

                bool ok = await NetworkCopyService.CopyLocalToRemoteAsync(
                    browser, Cfg(), "share", "dst", dir, true, Path.GetFileName(dir),
                    new Progress<FileOperations.OperationProgress>(), CancellationToken.None);

                Assert.IsTrue(ok);
                string root = Path.GetFileName(dir.TrimEnd('\\', '/'));
                CollectionAssert.AreEqual(a, browser.Files[$"dst/{root}/a.txt"]);
                CollectionAssert.AreEqual(b, browser.Files[$"dst/{root}/sub/b.bin"]);
                Assert.IsTrue(browser.Dirs.Contains($"dst/{root}/sub"));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public async Task LocalToRemote_DirectoryNoFiles_StillCreatesRoot()
        {
            var browser = new MockProvider();
            string dir = TmpDir();
            try
            {
                bool ok = await NetworkCopyService.CopyLocalToRemoteAsync(
                    browser, Cfg(), "share", "dst", dir, true, Path.GetFileName(dir),
                    new Progress<FileOperations.OperationProgress>(), CancellationToken.None);

                Assert.IsTrue(ok);
                string root = Path.GetFileName(dir.TrimEnd('\\', '/'));
                Assert.IsTrue(browser.Dirs.Contains($"dst/{root}"));
                Assert.AreEqual(0, browser.Files.Count);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public async Task LocalToRemote_SingleFileCannotOpen_Throws()
        {
            var browser = new MockProvider();
            string lp = Path.Combine(Path.GetTempPath(), "xfiles_nonexistent_" + Guid.NewGuid().ToString("N") + ".bin");

            // A missing local source isn't an OperationCanceled/NetworkOperation error, so it propagates.
            await Assert.ThrowsExceptionAsync<System.IO.FileNotFoundException>(() =>
                NetworkCopyService.CopyLocalToRemoteAsync(
                    browser, Cfg(), "share", "dst", lp, false, "missing.bin",
                    new Progress<FileOperations.OperationProgress>(), CancellationToken.None));
        }

        #endregion
    }
}

namespace XFiles.Network
{
    /// <summary>
    /// Facade that writes to a temp file and cleans up on dispose, mirroring
    /// WebDavWriteStream's temp-file behavior without requiring a WebDavSession.
    /// </summary>
    internal class WebDavWriteStream_Facade : Stream
    {
        private readonly FileStream _tempFile;
        private readonly string _tempPath;

        public WebDavWriteStream_Facade(out string tempPath)
        {
            _tempPath = Path.Combine(Path.GetTempPath(), "xfiles_test_" + Guid.NewGuid().ToString("N") + ".tmp");
            _tempFile = new FileStream(_tempPath, FileMode.Create, FileAccess.ReadWrite,
                FileShare.None, 64 * 1024, FileOptions.DeleteOnClose);
            tempPath = _tempPath;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _tempFile.Length;
        public override long Position { get => _tempFile.Position; set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => _tempFile.Write(buffer, offset, count);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => _tempFile.WriteAsync(buffer, offset, count, ct);
        public override void Flush() => _tempFile.Flush();
        public override Task FlushAsync(CancellationToken ct) => _tempFile.FlushAsync(ct);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _tempFile.Dispose(); }
    }
}
