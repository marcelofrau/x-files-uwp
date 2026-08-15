using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.Network;

namespace XFiles.Tests
{
    /// <summary>
    /// Real-share smoke test for the SMB stack. Skipped (Inconclusive) unless
    /// the environment points at a reachable server:
    ///   X_FILES_SMB_HOST  (required)
    ///   X_FILES_SMB_USER  (optional, empty = guest)
    ///   X_FILES_SMB_PASS  (optional)
    ///   X_FILES_SMB_SHARE (optional — when set, the test drills into it)
    /// Exercises the full M2 surface: connect, login, list shares, list a
    /// directory and read the first bytes of a file.
    /// </summary>
    [TestClass]
    public class SmbSessionSmokeTests
    {
        [TestMethod]
        public async Task RealShare_ListShares_ListAndRead()
        {
            string host = Environment.GetEnvironmentVariable("X_FILES_SMB_HOST");
            if (string.IsNullOrEmpty(host))
            {
                Assert.Inconclusive("X_FILES_SMB_HOST not set — SMB smoke test skipped.");
                return;
            }

            string user = Environment.GetEnvironmentVariable("X_FILES_SMB_USER") ?? string.Empty;
            string pass = Environment.GetEnvironmentVariable("X_FILES_SMB_PASS") ?? string.Empty;
            string share = Environment.GetEnvironmentVariable("X_FILES_SMB_SHARE");

            var config = new NetworkServerConfig
            {
                Protocol = NetworkProtocol.Smb,
                Host = host,
                Username = user,
                Share = share
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = cts.Token;

            var session = await SmbSessionPool.AcquireAsync(config, pass, ct);
            try
            {
                var shares = await session.ListSharesAsync(ct);
                Assert.IsTrue(shares.Count > 0, "Server exported no shares.");

                if (!string.IsNullOrEmpty(share))
                {
                    Assert.IsTrue(
                        shares.Contains(share, StringComparer.OrdinalIgnoreCase),
                        $"Share '{share}' not exported by {host} (got: {string.Join(", ", shares)}).");

                    var entries = await session.ListDirectoryAsync(share, string.Empty, ct);
                    Assert.IsNotNull(entries);

                    var file = entries.FirstOrDefault(e => !e.IsDirectory && e.Size > 0);
                    if (file != null)
                    {
                        using var stream = await session.OpenReadAsync(share, file.Name, ct);
                        Assert.IsTrue(stream.Length > 0, $"File {file.Name} reports non-positive length.");
                        int chunk = (int)Math.Min(stream.Length, 4096);
                        var buffer = new byte[chunk];
                        int read = stream.Read(buffer, 0, chunk);
                        Assert.IsTrue(read > 0, $"Read returned 0 bytes for {file.Name}.");
                    }
                }
            }
            finally
            {
                session.Disconnect();
                SmbSessionPool.DisconnectAll();
            }
        }

        /// <summary>
        /// Write-back smoke test (text-editor save-back path): writes a small temp
        /// file to the configured share, reads it back, verifies the bytes, then
        /// deletes it. Skipped unless X_FILES_SMB_HOST + X_FILES_SMB_SHARE are set.
        /// </summary>
        [TestMethod]
        public async Task RealShare_Write_ReadBack()
        {
            string host = Environment.GetEnvironmentVariable("X_FILES_SMB_HOST");
            string share = Environment.GetEnvironmentVariable("X_FILES_SMB_SHARE");
            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(share))
            {
                Assert.Inconclusive("X_FILES_SMB_HOST/SHARE not set — SMB write smoke test skipped.");
                return;
            }

            string user = Environment.GetEnvironmentVariable("X_FILES_SMB_USER") ?? string.Empty;
            string pass = Environment.GetEnvironmentVariable("X_FILES_SMB_PASS") ?? string.Empty;

            var config = new NetworkServerConfig
            {
                Protocol = NetworkProtocol.Smb,
                Host = host,
                Username = user,
                Share = share
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = cts.Token;

            string remoteName = $"XFilesSmoke_{Guid.NewGuid():N}.txt";
            byte[] payload = System.Text.Encoding.UTF8.GetBytes(
                "x-files SMB write-back smoke test\n" + Guid.NewGuid().ToString("N") + "\n");

            var session = await SmbSessionPool.AcquireAsync(config, pass, ct);
            try
            {
                await session.WriteFileAsync(share, remoteName, payload, ct);

                using var stream = await session.OpenReadAsync(share, remoteName, ct);
                var buffer = new byte[payload.Length];
                int total = 0;
                while (total < buffer.Length)
                {
                    int read = stream.Read(buffer, total, buffer.Length - total);
                    if (read <= 0) break;
                    total += read;
                }
                Assert.AreEqual(payload.Length, total, "Read-back length differs.");
                Assert.IsTrue(payload.SequenceEqual(buffer.Take(total)),
                    "Read-back payload does not match what was written.");

                await session.DeleteFileAsync(share, remoteName, ct);
            }
            finally
            {
                session.Disconnect();
                SmbSessionPool.DisconnectAll();
            }
        }
        /// <summary>
        /// File-operations smoke (M5.5): exercises every SMB op that backs
        /// copy/paste/rename/delete — CreateDirectoryAsync, OpenWriteStreamAsync
        /// (SmbWriteStream, chunked), RenameFileAsync, read-back via
        /// OpenReadAsync, DeleteFileAsync and recursive DeleteDirectoryAsync.
        /// Works inside a per-run temp folder and deletes it at the end, so a
        /// scratch share is safe. Skipped unless X_FILES_SMB_HOST + SHARE set.
        /// </summary>
        [TestMethod]
        public async Task RealShare_FileOps_Rename_Delete()
        {
            string host = Environment.GetEnvironmentVariable("X_FILES_SMB_HOST");
            string share = Environment.GetEnvironmentVariable("X_FILES_SMB_SHARE");
            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(share))
            {
                Assert.Inconclusive("X_FILES_SMB_HOST/SHARE not set — SMB file-ops smoke skipped.");
                return;
            }

            string user = Environment.GetEnvironmentVariable("X_FILES_SMB_USER") ?? string.Empty;
            string pass = Environment.GetEnvironmentVariable("X_FILES_SMB_PASS") ?? string.Empty;

            var config = new NetworkServerConfig
            {
                Protocol = NetworkProtocol.Smb,
                Host = host,
                Username = user,
                Share = share
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            CancellationToken ct = cts.Token;

            string root = $"XFilesSmoke_{Guid.NewGuid():N}";
            byte[] payload = System.Text.Encoding.UTF8.GetBytes(
                "x-files SMB file-ops smoke\n" + new string('x', 150_000) + "\n");

            var session = await SmbSessionPool.AcquireAsync(config, pass, ct);
            try
            {
                // mkdir
                await session.CreateDirectoryAsync(share, root, ct);

                // write a chunked file (> MaxWriteSize) through SmbWriteStream
                using (var dst = await session.OpenWriteStreamAsync(share, root + "\\a.bin", ct))
                {
                    dst.Write(payload, 0, payload.Length);
                }

                // rename
                await session.RenameFileAsync(share, root + "\\a.bin", "b.bin", isDirectory: false, ct);

                // read back the renamed file
                using (var stream = await session.OpenReadAsync(share, root + "\\b.bin", ct))
                {
                    Assert.AreEqual(payload.Length, stream.Length, "Renamed file length differs.");
                    var buffer = new byte[payload.Length];
                    int total = 0;
                    while (total < buffer.Length)
                    {
                        int read = stream.Read(buffer, total, buffer.Length - total);
                        if (read <= 0) break;
                        total += read;
                    }
                    Assert.AreEqual(payload.Length, total, "Read-back length differs.");
                    Assert.IsTrue(payload.SequenceEqual(buffer.Take(total)),
                        "Read-back payload does not match what was written.");
                }

                // delete file, then the (now empty) tree — recursive delete path
                await session.DeleteFileAsync(share, root + "\\b.bin", ct);
                await session.DeleteDirectoryAsync(share, root, ct);

                // confirm gone
                var entries = await session.ListDirectoryAsync(share, string.Empty, ct);
                Assert.IsFalse(entries.Any(e => string.Equals(e.Name, root, StringComparison.OrdinalIgnoreCase)),
                    "Temp folder still present after delete.");
            }
            finally
            {
                session.Disconnect();
                SmbSessionPool.DisconnectAll();
            }
        }
    }
}
