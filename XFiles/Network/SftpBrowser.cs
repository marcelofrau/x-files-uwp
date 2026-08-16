using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace XFiles.Network
{
    /// <summary>
    /// SFTP implementation of INetworkFileSystemProvider backed by SSH.NET.
    /// SFTP has no share layer: remote paths are absolute from the server root
    /// (or relative to the login home when empty). Sessions are pooled per
    /// canonical URL (an SSH handshake is expensive); streams hold their
    /// session alive. Host keys are verified through HostKeyTrustStore with an
    /// optional confirmation callback for first connects. All failures surface
    /// as NetworkOperationException.
    /// </summary>
    public class SftpBrowser : INetworkFileSystemProvider
    {
        private readonly HostKeyTrustStore _trustStore;

        /// <summary>
        /// Invoked when a host key is not yet trusted. Receives the host:port
        /// key and the SHA256 fingerprint; returns true to accept and persist.
        /// When null, unknown keys are rejected (fail-safe).
        /// </summary>
        public Func<string, string, bool> HostKeyConfirmation { get; set; }

        public SftpBrowser()
        {
            try
            {
                var dir = ApplicationData.Current.LocalFolder.Path;
                _trustStore = new HostKeyTrustStore(Path.Combine(dir, "Network", "host-keys.json"));
            }
            catch
            {
                _trustStore = new HostKeyTrustStore();
            }
        }

        public NetworkProtocol Protocol => NetworkProtocol.Sftp;

        private void ConfigureHostKey(SftpSession session)
        {
            session.HostKeyResolver = (hostPort, fingerprint) =>
            {
                if (_trustStore.IsTrusted(hostPort, fingerprint))
                    return true;
                if (HostKeyConfirmation != null && HostKeyConfirmation(hostPort, fingerprint))
                {
                    _trustStore.Accept(hostPort, fingerprint);
                    return true;
                }
                Log.Warn($"SftpBrowser: host key for {hostPort} rejected");
                return false;
            };
        }

        public async Task<string> TestConnectionAsync(NetworkServerConfig config, string password, CancellationToken ct)
        {
            var session = await SftpSessionPool.AcquireAsync(config, password, ConfigureHostKey, ct);
            try
            {
                ConfigureHostKey(session);
                await session.EnsureConnectedAsync(password, ct);
                var entries = await session.ListDirectoryAsync(config.Share ?? "", ct);
                return $"Connected — {entries.Count} item(s) in \"{config.Share ?? "/"}\".";
            }
            catch (NetworkOperationException ex)
            {
                Log.Warn($"SftpBrowser.TestConnection: {ex.Reason} ({ex.Message})");
                throw;
            }
        }

        /// <summary>FTP/SFTP have no share layer; the start folder comes via path (or share as fallback).</summary>
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
            string remote = EffectivePath(share, path);
            string password = await NetworkServerManager.GetPasswordAsync(config);
            var session = await SftpSessionPool.AcquireAsync(config, password, ConfigureHostKey, ct);
            ConfigureHostKey(session);
            Log.Info($"SftpBrowser.ListDirectory: {NetworkUrl.Compose(config)}{remote}");
            try
            {
                var entries = await session.ListDirectoryAsync(remote, ct);
                Log.Dbg($"SftpBrowser.ListDirectory: {entries.Count} entries");
                return entries;
            }
            catch (NetworkOperationException ex)
            {
                Log.Warn($"SftpBrowser.ListDirectory: {ex.Reason} ({ex.Message})");
                throw;
            }
        }

        public async Task<Stream> OpenReadAsync(
            NetworkServerConfig config, string share, string path, CancellationToken ct)
        {
            string remote = EffectivePath(share, path);
            string password = await NetworkServerManager.GetPasswordAsync(config);
            var session = await SftpSessionPool.AcquireAsync(config, password, ConfigureHostKey, ct);
            ConfigureHostKey(session);
            Log.Info($"SftpBrowser.OpenRead: {NetworkUrl.Compose(config)}{remote}");
            try
            {
                return await session.OpenReadAsync(remote, ct);
            }
            catch (NetworkOperationException ex)
            {
                Log.Warn($"SftpBrowser.OpenRead: {ex.Reason} ({ex.Message})");
                throw;
            }
        }

        public async Task<long> GetFileLengthAsync(
            NetworkServerConfig config, string share, string path, CancellationToken ct)
        {
            string remote = EffectivePath(share, path);
            string password = await NetworkServerManager.GetPasswordAsync(config);
            var session = await SftpSessionPool.AcquireAsync(config, password, ConfigureHostKey, ct);
            ConfigureHostKey(session);
            try
            {
                return await session.GetFileLengthAsync(remote, ct);
            }
            catch (NetworkOperationException ex)
            {
                Log.Warn($"SftpBrowser.GetFileLength: {ex.Reason} ({ex.Message})");
                throw;
            }
        }

        public async Task<bool> EntryExistsAsync(
            NetworkServerConfig config, string share, string path, bool isDirectory, CancellationToken ct)
        {
            string remote = EffectivePath(share, path);
            string password = await NetworkServerManager.GetPasswordAsync(config);
            var session = await SftpSessionPool.AcquireAsync(config, password, ConfigureHostKey, ct);
            ConfigureHostKey(session);
            try
            {
                return await session.EntryExistsAsync(remote, isDirectory, ct);
            }
            catch (NetworkOperationException ex)
            {
                Log.Warn($"SftpBrowser.EntryExists: {ex.Reason} ({ex.Message})");
                throw;
            }
        }

        public async Task WriteFileAsync(
            NetworkServerConfig config, string share, string path, string localPath, CancellationToken ct)
        {
            string remote = EffectivePath(share, path);
            string password = await NetworkServerManager.GetPasswordAsync(config);
            var session = await SftpSessionPool.AcquireAsync(config, password, ConfigureHostKey, ct);
            ConfigureHostKey(session);
            Log.Info($"SftpBrowser.WriteFile: {localPath} → {remote}");
            try
            {
                await session.WriteFileAsync(remote, localPath, ct);
                Log.Info($"SftpBrowser.WriteFile: uploaded");
            }
            catch (NetworkOperationException ex)
            {
                Log.Warn($"SftpBrowser.WriteFile: {ex.Reason} ({ex.Message})");
                throw;
            }
        }

        public async Task<Stream> OpenWriteStreamAsync(
            NetworkServerConfig config, string share, string path, CancellationToken ct)
        {
            string remote = EffectivePath(share, path);
            string password = await NetworkServerManager.GetPasswordAsync(config);
            var session = await SftpSessionPool.AcquireAsync(config, password, ConfigureHostKey, ct);
            ConfigureHostKey(session);
            Log.Info($"SftpBrowser.OpenWriteStream: {remote}");
            try
            {
                return await session.OpenWriteStreamAsync(remote, ct);
            }
            catch (NetworkOperationException ex)
            {
                Log.Warn($"SftpBrowser.OpenWriteStream: {ex.Reason} ({ex.Message})");
                throw;
            }
        }

        public async Task DeleteFileAsync(
            NetworkServerConfig config, string share, string path, CancellationToken ct)
        {
            string remote = EffectivePath(share, path);
            string password = await NetworkServerManager.GetPasswordAsync(config);
            var session = await SftpSessionPool.AcquireAsync(config, password, ConfigureHostKey, ct);
            ConfigureHostKey(session);
            Log.Info($"SftpBrowser.DeleteFile: {remote}");
            try
            {
                await session.DeleteFileAsync(remote, ct);
            }
            catch (NetworkOperationException ex)
            {
                Log.Warn($"SftpBrowser.DeleteFile: {ex.Reason} ({ex.Message})");
                throw;
            }
        }

        public async Task DeleteDirectoryAsync(
            NetworkServerConfig config, string share, string path, CancellationToken ct)
        {
            string remote = EffectivePath(share, path);
            string password = await NetworkServerManager.GetPasswordAsync(config);
            var session = await SftpSessionPool.AcquireAsync(config, password, ConfigureHostKey, ct);
            ConfigureHostKey(session);
            Log.Info($"SftpBrowser.DeleteDirectory: {remote}");
            try
            {
                await session.DeleteDirectoryAsync(remote, ct);
            }
            catch (NetworkOperationException ex)
            {
                Log.Warn($"SftpBrowser.DeleteDirectory: {ex.Reason} ({ex.Message})");
                throw;
            }
        }

        public async Task RenameFileAsync(
            NetworkServerConfig config, string share, string path, string newName, bool isDirectory, CancellationToken ct)
        {
            string remote = EffectivePath(share, path);
            string password = await NetworkServerManager.GetPasswordAsync(config);
            var session = await SftpSessionPool.AcquireAsync(config, password, ConfigureHostKey, ct);
            ConfigureHostKey(session);
            Log.Info($"SftpBrowser.RenameFile: {remote} → {newName}");
            try
            {
                await session.RenameFileAsync(remote, newName, isDirectory, ct);
            }
            catch (NetworkOperationException ex)
            {
                Log.Warn($"SftpBrowser.RenameFile: {ex.Reason} ({ex.Message})");
                throw;
            }
        }

        public async Task CreateDirectoryAsync(
            NetworkServerConfig config, string share, string path, CancellationToken ct)
        {
            string remote = EffectivePath(share, path);
            string password = await NetworkServerManager.GetPasswordAsync(config);
            var session = await SftpSessionPool.AcquireAsync(config, password, ConfigureHostKey, ct);
            ConfigureHostKey(session);
            Log.Info($"SftpBrowser.CreateDirectory: {remote}");
            try
            {
                await session.CreateDirectoryAsync(remote, ct);
            }
            catch (NetworkOperationException ex)
            {
                Log.Warn($"SftpBrowser.CreateDirectory: {ex.Reason} ({ex.Message})");
                throw;
            }
        }

        /// <summary>Releases the pooled session(s) for the location.</summary>
        public void Disconnect(NetworkServerConfig config)
        {
            string key = NetworkUrl.Compose(config);
            if (key != null)
            {
                SftpSessionPool.Remove(key);
                Log.Dbg($"SftpBrowser.Disconnect: {key}");
            }
        }
    }
}
