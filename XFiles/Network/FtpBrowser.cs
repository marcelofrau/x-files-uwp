using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace XFiles.Network
{
    /// <summary>
    /// FTP/FTPS implementation of INetworkFileSystemProvider. FTP has no share
    /// layer: remote paths are absolute from the server root (or relative to
    /// the login working directory when empty), and <paramref name="share"/> is
    /// ignored. Operations are serialized via a SemaphoreSlim to prevent
    /// parallel EPSV data-connection races (FileZilla 1.12.6 fails with
    /// "TLS session of data connection not resumed" when two sessions open
    /// concurrent data ports). Resolves the password from the credential vault;
    /// all failures surface as NetworkOperationException.
    /// </summary>
    public class FtpBrowser : INetworkFileSystemProvider
    {
        /// <summary>
        /// Serializes all FTP operations so that only one FtpSession is active
        /// at a time. Prevents EPSV data-connection races on servers that cannot
        /// handle concurrent TLS sessions (e.g. FileZilla Server).
        /// </summary>
        private static readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);

        public NetworkProtocol Protocol => NetworkProtocol.Ftp;

        private static async Task<T> SerializeAsync<T>(Func<Task<T>> operation)
        {
            await _operationLock.WaitAsync();
            try { return await operation(); }
            finally { _operationLock.Release(); }
        }

        private static async Task SerializeAsync(Func<Task> operation)
        {
            await _operationLock.WaitAsync();
            try { await operation(); }
            finally { _operationLock.Release(); }
        }

        public async Task<string> TestConnectionAsync(NetworkServerConfig config, string password, CancellationToken ct)
        {
            using (var session = new FtpSession(config, password))
            {
                await session.EnsureConnectedAsync(password, ct);
                var entries = await session.ListDirectoryAsync(config.Share ?? "", ct);
                return $"Connected — {entries.Count} item(s) in \"{config.Share ?? "/"}\".";
            }
        }

        /// <summary>
        /// FTP/SFTP have no share layer; the location drill-in passes the start
        /// folder via both share and path. The path wins when set, otherwise the
        /// start folder is used.
        /// </summary>
        private static string EffectivePath(string share, string path)
        {
            return string.IsNullOrEmpty(path) ? (share ?? "") : path;
        }

        public Task<List<string>> ListSharesAsync(NetworkServerConfig config, CancellationToken ct)
        {
            return Task.FromResult(new List<string>());
        }

        public async Task<List<NetworkFileEntry>> ListDirectoryAsync(
            NetworkServerConfig config, string share, string path, CancellationToken ct)
        {
            return await SerializeAsync(async () =>
            {
                string remote = EffectivePath(share, path);
                string password = await NetworkServerManager.GetPasswordAsync(config);
                using (var session = new FtpSession(config, password))
                {
                    Log.Info($"FtpBrowser.ListDirectory: {NetworkUrl.Compose(config)}{remote}");
                    try
                    {
                        var entries = await session.ListDirectoryAsync(remote, ct);
                        Log.Dbg($"FtpBrowser.ListDirectory: {entries.Count} entries");
                        return entries;
                    }
                    catch (NetworkOperationException ex)
                    {
                        Log.Warn($"FtpBrowser.ListDirectory: {ex.Reason} ({ex.Message})");
                        throw;
                    }
                }
            });
        }

        public async Task<Stream> OpenReadAsync(
            NetworkServerConfig config, string share, string path, CancellationToken ct)
        {
            return await SerializeAsync(async () =>
            {
                string remote = EffectivePath(share, path);
                string password = await NetworkServerManager.GetPasswordAsync(config);
                var session = new FtpSession(config, password);
                Log.Info($"FtpBrowser.OpenRead: {NetworkUrl.Compose(config)}{remote}");
                try
                {
                    return await session.OpenReadAsync(remote, ct);
                }
                catch (NetworkOperationException ex)
                {
                    session.Dispose();
                    Log.Warn($"FtpBrowser.OpenRead: {ex.Reason} ({ex.Message})");
                    throw;
                }
            });
        }

        public async Task<long> GetFileLengthAsync(
            NetworkServerConfig config, string share, string path, CancellationToken ct)
        {
            return await SerializeAsync(async () =>
            {
                string remote = EffectivePath(share, path);
                string password = await NetworkServerManager.GetPasswordAsync(config);
                using (var session = new FtpSession(config, password))
                {
                    try
                    {
                        return await session.GetFileLengthAsync(remote, ct);
                    }
                    catch (NetworkOperationException ex)
                    {
                        Log.Warn($"FtpBrowser.GetFileLength: {ex.Reason} ({ex.Message})");
                        throw;
                    }
                }
            });
        }

        public async Task<bool> EntryExistsAsync(
            NetworkServerConfig config, string share, string path, bool isDirectory, CancellationToken ct)
        {
            return await SerializeAsync(async () =>
            {
                string remote = EffectivePath(share, path);
                string password = await NetworkServerManager.GetPasswordAsync(config);
                using (var session = new FtpSession(config, password))
                {
                    try
                    {
                        return await session.EntryExistsAsync(remote, isDirectory, ct);
                    }
                    catch (NetworkOperationException ex)
                    {
                        Log.Warn($"FtpBrowser.EntryExists: {ex.Reason} ({ex.Message})");
                        throw;
                    }
                }
            });
        }

        public async Task WriteFileAsync(
            NetworkServerConfig config, string share, string path, string localPath, CancellationToken ct)
        {
            await SerializeAsync(async () =>
            {
                string remote = EffectivePath(share, path);
                string password = await NetworkServerManager.GetPasswordAsync(config);
                using (var session = new FtpSession(config, password))
                {
                    Log.Info($"FtpBrowser.WriteFile: {localPath} → {remote}");
                    try
                    {
                        await session.WriteFileAsync(remote, localPath, ct);
                        Log.Info($"FtpBrowser.WriteFile: uploaded");
                    }
                    catch (NetworkOperationException ex)
                    {
                        Log.Warn($"FtpBrowser.WriteFile: {ex.Reason} ({ex.Message})");
                        throw;
                    }
                }
            });
        }

        public async Task<Stream> OpenWriteStreamAsync(
            NetworkServerConfig config, string share, string path, CancellationToken ct)
        {
            return await SerializeAsync(async () =>
            {
                string remote = EffectivePath(share, path);
                string password = await NetworkServerManager.GetPasswordAsync(config);
                var session = new FtpSession(config, password);
                Log.Info($"FtpBrowser.OpenWriteStream: {remote}");
                try
                {
                    return await session.OpenWriteStreamAsync(remote, ct);
                }
                catch (NetworkOperationException ex)
                {
                    session.Dispose();
                    Log.Warn($"FtpBrowser.OpenWriteStream: {ex.Reason} ({ex.Message})");
                    throw;
                }
            });
        }

        public async Task DeleteFileAsync(
            NetworkServerConfig config, string share, string path, CancellationToken ct)
        {
            await SerializeAsync(async () =>
            {
                string remote = EffectivePath(share, path);
                string password = await NetworkServerManager.GetPasswordAsync(config);
                using (var session = new FtpSession(config, password))
                {
                    Log.Info($"FtpBrowser.DeleteFile: {remote}");
                    try
                    {
                        await session.DeleteFileAsync(remote, ct);
                    }
                    catch (NetworkOperationException ex)
                    {
                        Log.Warn($"FtpBrowser.DeleteFile: {ex.Reason} ({ex.Message})");
                        throw;
                    }
                }
            });
        }

        public async Task DeleteDirectoryAsync(
            NetworkServerConfig config, string share, string path, CancellationToken ct)
        {
            await SerializeAsync(async () =>
            {
                string remote = EffectivePath(share, path);
                string password = await NetworkServerManager.GetPasswordAsync(config);
                using (var session = new FtpSession(config, password))
                {
                    Log.Info($"FtpBrowser.DeleteDirectory: {remote}");
                    try
                    {
                        await session.DeleteDirectoryAsync(remote, ct);
                    }
                    catch (NetworkOperationException ex)
                    {
                        Log.Warn($"FtpBrowser.DeleteDirectory: {ex.Reason} ({ex.Message})");
                        throw;
                    }
                }
            });
        }

        public async Task RenameFileAsync(
            NetworkServerConfig config, string share, string path, string newName, bool isDirectory, CancellationToken ct)
        {
            await SerializeAsync(async () =>
            {
                string remote = EffectivePath(share, path);
                string password = await NetworkServerManager.GetPasswordAsync(config);
                using (var session = new FtpSession(config, password))
                {
                    Log.Info($"FtpBrowser.RenameFile: {remote} → {newName}");
                    try
                    {
                        await session.RenameFileAsync(remote, newName, ct);
                    }
                    catch (NetworkOperationException ex)
                    {
                        Log.Warn($"FtpBrowser.RenameFile: {ex.Reason} ({ex.Message})");
                        throw;
                    }
                }
            });
        }

        public async Task CreateDirectoryAsync(
            NetworkServerConfig config, string share, string path, CancellationToken ct)
        {
            await SerializeAsync(async () =>
            {
                string remote = EffectivePath(share, path);
                string password = await NetworkServerManager.GetPasswordAsync(config);
                using (var session = new FtpSession(config, password))
                {
                    Log.Info($"FtpBrowser.CreateDirectory: {remote}");
                    try
                    {
                        await session.CreateDirectoryAsync(remote, ct);
                    }
                    catch (NetworkOperationException ex)
                    {
                        Log.Warn($"FtpBrowser.CreateDirectory: {ex.Reason} ({ex.Message})");
                        throw;
                    }
                }
            });
        }

        /// <summary>Sessions are per-operation; there is nothing to pool-release.</summary>
        public void Disconnect(NetworkServerConfig config)
        {
            Log.Dbg($"FtpBrowser.Disconnect: {NetworkUrl.Compose(config)} (no pooled sessions)");
        }
    }
}
