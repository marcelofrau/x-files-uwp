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
    /// Real-server smoke test for the FTP/FTPS stack. Skipped (Inconclusive)
    /// unless the environment points at a reachable server — the docker smoke
    /// containers from tools/network-smoke are the intended target:
    ///   X_FILES_FTP_HOST   (required, e.g. 127.0.0.1)
    ///   X_FILES_FTP_PORT   (optional, default 21)
    ///   X_FILES_FTP_USER   (optional, default anonymous)
    ///   X_FILES_FTP_PASS   (optional)
    ///   X_FILES_FTP_START  (optional start folder, default root)
    ///   X_FILES_FTP_PROTO  (optional: ftp|ftps, default ftp)
    /// Exercises the M9 surface: connect, list, read first bytes, and (when a
    /// writable start folder is given) write-readback-delete.
    /// </summary>
    [TestClass]
    public class FtpSessionSmokeTests
    {
        private static NetworkServerConfig Config(string start)
        {
            string host = Environment.GetEnvironmentVariable("X_FILES_FTP_HOST");
            int port = int.TryParse(Environment.GetEnvironmentVariable("X_FILES_FTP_PORT"), out int p)
                ? p : 21;
            string user = Environment.GetEnvironmentVariable("X_FILES_FTP_USER");
            string proto = Environment.GetEnvironmentVariable("X_FILES_FTP_PROTO") ?? "ftp";
            return new NetworkServerConfig
            {
                Protocol = string.Equals(proto, "ftps", StringComparison.OrdinalIgnoreCase)
                    ? NetworkProtocol.Ftps
                    : NetworkProtocol.Ftp,
                Host = host,
                Port = port,
                Username = user,
                Share = start
            };
        }

        [TestMethod]
        public async Task RealFtp_ListAndRead()
        {
            string host = Environment.GetEnvironmentVariable("X_FILES_FTP_HOST");
            if (string.IsNullOrEmpty(host))
            {
                Assert.Inconclusive("X_FILES_FTP_HOST not set — FTP smoke test skipped.");
                return;
            }

            string pass = Environment.GetEnvironmentVariable("X_FILES_FTP_PASS") ?? string.Empty;
            var config = Config("");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var session = new FtpSession(config);
            await session.EnsureConnectedAsync(pass, cts.Token);

            var entries = await session.ListDirectoryAsync("", cts.Token);
            Assert.IsNotNull(entries);

            var file = entries.FirstOrDefault(e => !e.IsDirectory && e.Size > 0);
            if (file != null)
            {
                using var stream = await session.OpenReadAsync(file.Name, cts.Token);
                Assert.IsTrue(stream.Length > 0, $"File {file.Name} reports non-positive length.");
                int chunk = (int)Math.Min(stream.Length, 4096);
                var buffer = new byte[chunk];
                int read = stream.Read(buffer, 0, chunk);
                Assert.IsTrue(read > 0, $"Read returned 0 bytes for {file.Name}.");
            }
        }

        /// <summary>
        /// Seek smoke test (REST reopen path): opens the first seed file,
        /// seeks to the middle, reads, seeks back to 0, reads again — verifying
        /// the FtpReadStream reopens the data connection with a REST offset.
        /// Skipped unless X_FILES_FTP_HOST is set.
        /// </summary>
        [TestMethod]
        public async Task RealFtp_Seek_Restart()
        {
            string host = Environment.GetEnvironmentVariable("X_FILES_FTP_HOST");
            if (string.IsNullOrEmpty(host))
            {
                Assert.Inconclusive("X_FILES_FTP_HOST not set — FTP smoke test skipped.");
                return;
            }

            string pass = Environment.GetEnvironmentVariable("X_FILES_FTP_PASS") ?? string.Empty;
            string start = Environment.GetEnvironmentVariable("X_FILES_FTP_START") ?? "";
            var config = Config(start);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using (var session = new FtpSession(config))
            {
                await session.EnsureConnectedAsync(pass, cts.Token);
                Assert.IsTrue(session.SupportsRest, "Server should advertise REST for this test.");

                var entries = await session.ListDirectoryAsync(start, cts.Token);
                var file = entries.FirstOrDefault(e => !e.IsDirectory && e.Size > 0);
                Assert.IsNotNull(file, "No seed file to seek on.");

                string remotePath = string.IsNullOrEmpty(start) ? file.Name : start.TrimEnd('/') + "/" + file.Name;
                using var stream = await session.OpenReadAsync(remotePath, cts.Token);
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
        }

        /// <summary>
        /// Write-back smoke test (FTP STOR path): writes a temp file, reads it
        /// back, verifies the bytes, then deletes it. Skipped unless
        /// X_FILES_FTP_HOST is set.
        /// </summary>
        [TestMethod]
        public async Task RealFtp_Write_ReadBack()
        {
            string host = Environment.GetEnvironmentVariable("X_FILES_FTP_HOST");
            if (string.IsNullOrEmpty(host))
            {
                Assert.Inconclusive("X_FILES_FTP_HOST not set — FTP smoke test skipped.");
                return;
            }

            string pass = Environment.GetEnvironmentVariable("X_FILES_FTP_PASS") ?? string.Empty;
            string start = Environment.GetEnvironmentVariable("X_FILES_FTP_START") ?? "";
            var config = Config(start);
            string remoteName = "xfiles-smoke-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt";
            string remotePath = string.IsNullOrEmpty(start) ? remoteName : start.TrimEnd('/') + "/" + remoteName;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            string tmp = Path.Combine(Path.GetTempPath(), remoteName);
            try
            {
                File.WriteAllText(tmp, "X-Files FTP smoke write-readback\n" + Guid.NewGuid());

                using (var session = new FtpSession(config))
                {
                    await session.EnsureConnectedAsync(pass, cts.Token);
                    await session.WriteFileAsync(remotePath, tmp, cts.Token);
                }

                using (var session = new FtpSession(config))
                {
                    await session.EnsureConnectedAsync(pass, cts.Token);
                    Assert.IsTrue(await session.EntryExistsAsync(remotePath, false, cts.Token),
                        $"Remote file {remoteName} not visible after upload.");

                    using var stream = await session.OpenReadAsync(remotePath, cts.Token);
                    using var reader = new StreamReader(stream);
                    string back = reader.ReadToEnd();
                    Assert.AreEqual(File.ReadAllText(tmp), back, "Round-tripped content mismatch.");
                }

                using (var session = new FtpSession(config))
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
