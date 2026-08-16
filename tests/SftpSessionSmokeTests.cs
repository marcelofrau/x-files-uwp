using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.Network;

namespace XFiles.Tests
{
    /// <summary>
    /// Real-server smoke test for the SFTP stack. Skipped (Inconclusive)
    /// unless the environment points at a reachable server — the docker smoke
    /// container from tools/network-smoke is the intended target:
    ///   X_FILES_SFTP_HOST   (required, e.g. 127.0.0.1)
    ///   X_FILES_SFTP_PORT   (optional, default 22)
    ///   X_FILES_SFTP_USER   (optional, default smoke)
    ///   X_FILES_SFTP_PASS   (optional, default smoke123)
    ///   X_FILES_SFTP_START  (optional absolute start folder, default /share)
    ///   X_FILES_SFTP_WRITE  (optional absolute writable folder for write-back)
    /// Paths are ABSOLUTE (SFTP chroots to the user home; the atmoz/sftp smoke
    /// container starts at /share). Exercises the M10 surface: connect,
    /// host-key verify, list, read first bytes, seek, and write-readback-delete.
    /// </summary>
    [TestClass]
    public class SftpSessionSmokeTests
    {
        private static NetworkServerConfig Config()
        {
            string host = Environment.GetEnvironmentVariable("X_FILES_SFTP_HOST");
            int port = int.TryParse(Environment.GetEnvironmentVariable("X_FILES_SFTP_PORT"), out int p)
                ? p : 22;
            string user = Environment.GetEnvironmentVariable("X_FILES_SFTP_USER") ?? "smoke";
            return new NetworkServerConfig
            {
                Protocol = NetworkProtocol.Sftp,
                Host = host,
                Port = port,
                Username = user
            };
        }

        private static string Pass() => Environment.GetEnvironmentVariable("X_FILES_SFTP_PASS") ?? "smoke123";

        private static SftpSession NewSession()
        {
            var session = new SftpSession(Config());
            // Smoke server key is ephemeral per container — trust any key here.
            session.HostKeyResolver = (hostPort, fp) => true;
            return session;
        }

        private static string SkipUnless()
        {
            string host = Environment.GetEnvironmentVariable("X_FILES_SFTP_HOST");
            if (string.IsNullOrEmpty(host))
            {
                Assert.Inconclusive("X_FILES_SFTP_HOST not set — SFTP smoke test skipped.");
                return null;
            }
            return host;
        }

        [TestMethod]
        public async Task RealSftp_ListAndRead()
        {
            if (SkipUnless() == null) return;
            string start = Environment.GetEnvironmentVariable("X_FILES_SFTP_START") ?? "/share";

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var session = NewSession();
            await session.EnsureConnectedAsync(Pass(), cts.Token);

            var entries = await session.ListDirectoryAsync(start, cts.Token);
            Assert.IsNotNull(entries);
            Assert.IsTrue(entries.Any(e => !e.IsDirectory && e.Size > 0), "Expected seed files in start folder.");

            var file = entries.FirstOrDefault(e => !e.IsDirectory && e.Size > 0);
            var stream = await session.OpenReadAsync(start + "/" + file.Name, cts.Token);
            using (stream)
            {
                Assert.IsTrue(stream.Length > 0, $"File {file.Name} reports non-positive length.");
                int chunk = (int)Math.Min(stream.Length, 4096);
                var buffer = new byte[chunk];
                int read = stream.Read(buffer, 0, chunk);
                Assert.IsTrue(read > 0, $"Read returned 0 bytes for {file.Name}.");
            }
        }

        /// <summary>
        /// Seek smoke test: opens the first seed file, seeks to the middle,
        /// reads, seeks back to 0, reads again — verifying the natively
        /// seekable SftpFileStream path (no reopen needed, unlike FTP).
        /// </summary>
        [TestMethod]
        public async Task RealSftp_Seek_Reopen()
        {
            if (SkipUnless() == null) return;
            string start = Environment.GetEnvironmentVariable("X_FILES_SFTP_START") ?? "/share";

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var session = NewSession();
            await session.EnsureConnectedAsync(Pass(), cts.Token);

            var entries = await session.ListDirectoryAsync(start, cts.Token);
            var file = entries.FirstOrDefault(e => !e.IsDirectory && e.Size > 0);
            Assert.IsNotNull(file, "No seed file to seek on.");

            using var stream = await session.OpenReadAsync(start + "/" + file.Name, cts.Token);
            Assert.IsTrue(stream.CanSeek, "SftpFileStream should be natively seekable.");
            long mid = stream.Length / 2;
            stream.Seek(mid, SeekOrigin.Begin);
            Assert.AreEqual(mid, stream.Position);

            var buf = new byte[16];
            int read = stream.Read(buf, 0, buf.Length);
            Assert.IsTrue(read > 0, "Read at middle returned 0 bytes.");

            stream.Seek(0, SeekOrigin.Begin);
            read = stream.Read(buf, 0, buf.Length);
            Assert.IsTrue(read > 0, "Read after seek-back returned 0 bytes.");
        }

        /// <summary>
        /// Write-back smoke test: writes a temp file into the writable folder,
        /// reads it back, verifies the bytes, then deletes it. The docker seed
        /// mount is read-only, so X_FILES_SFTP_WRITE must point at a writable
        /// absolute path (e.g. /uploads — see the compose file).
        /// </summary>
        [TestMethod]
        public async Task RealSftp_Write_ReadBack()
        {
            string writeFolder = Environment.GetEnvironmentVariable("X_FILES_SFTP_WRITE");
            if (SkipUnless() == null) return;
            if (string.IsNullOrEmpty(writeFolder))
            {
                Assert.Inconclusive("X_FILES_SFTP_WRITE not set (writable absolute folder) — write-back smoke test skipped.");
                return;
            }

            string pass = Pass();
            string remoteName = "xfiles-smoke-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt";
            string remotePath = writeFolder.TrimEnd('/') + "/" + remoteName;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            string tmp = Path.Combine(Path.GetTempPath(), remoteName);
            try
            {
                File.WriteAllText(tmp, "X-Files SFTP smoke write-readback\n" + Guid.NewGuid());

                using (var session = NewSession())
                {
                    await session.EnsureConnectedAsync(pass, cts.Token);
                    await session.WriteFileAsync(remotePath, tmp, cts.Token);
                }

                using (var session = NewSession())
                {
                    await session.EnsureConnectedAsync(pass, cts.Token);
                    Assert.IsTrue(await session.EntryExistsAsync(remotePath, false, cts.Token),
                        $"Remote file {remoteName} not visible after upload.");

                    using var stream = await session.OpenReadAsync(remotePath, cts.Token);
                    using var reader = new StreamReader(stream);
                    string back = reader.ReadToEnd();
                    Assert.AreEqual(File.ReadAllText(tmp), back, "Round-tripped content mismatch.");
                }

                using (var session = NewSession())
                {
                    await session.EnsureConnectedAsync(pass, cts.Token);
                    await session.DeleteFileAsync(remotePath, cts.Token);
                    Assert.IsFalse(await session.EntryExistsAsync(remotePath, false, cts.Token),
                        $"Remote file {remoteName} still present after delete.");
                }
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
        }
    }
}
