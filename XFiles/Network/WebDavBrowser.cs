using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace XFiles.Network
{
    /// <summary>
    /// WebDAV implementation of INetworkFileSystemProvider. Stateless — no session
    /// pool; HttpClient handles connection pooling internally. Password comes from
    /// the credential vault. All failures surface as NetworkOperationException.
    /// </summary>
    public class WebDavBrowser : INetworkFileSystemProvider
    {
        public NetworkProtocol Protocol => NetworkProtocol.Webdav;

        // ─────────────────────── TestConnection ───────────────────────

        public async Task<string> TestConnectionAsync(NetworkServerConfig config, string password, CancellationToken ct)
        {
            string pwd = password ?? await NetworkServerManager.GetPasswordAsync(config);
            using (var session = new WebDavSession(config, pwd))
            {
                string remote = WebDavSession.EffectivePath(config.Share, "");
                Log.Info("WebDavBrowser.TestConnection: {Url}", NetworkUrl.Compose(config));
                string result = await session.TestConnectionAsync(remote, ct);
                Log.Dbg("WebDavBrowser.TestConnection: {Result}", result);
                return result;
            }
        }

        // ─────────────────────── ListShares ───────────────────────

        public Task<List<string>> ListSharesAsync(NetworkServerConfig config, CancellationToken ct)
        {
            return Task.FromResult(new List<string>());
        }

        // ─────────────────────── ListDirectory ───────────────────────

        public async Task<List<NetworkFileEntry>> ListDirectoryAsync(
            NetworkServerConfig config, string share, string path, CancellationToken ct)
        {
            string remote = WebDavSession.EffectivePath(share, path);
            string pwd = await NetworkServerManager.GetPasswordAsync(config);
            using (var session = new WebDavSession(config, pwd))
            {
                Log.Info("WebDavBrowser.ListDirectory: {Url}/{Remote}", NetworkUrl.Compose(config), remote);
                try
                {
                    var entries = await session.ListDirectoryAsync(remote, ct);
                    Log.Dbg("WebDavBrowser.ListDirectory: {Count} entries", entries.Count);
                    return entries;
                }
                catch (NetworkOperationException ex)
                {
                    Log.Dbg("WebDavBrowser.ListDirectory: {Reason} ({Message})", ex.Reason, ex.Message);
                    throw;
                }
            }
        }

        // ─────────────────────── OpenRead ───────────────────────

        public async Task<Stream> OpenReadAsync(
            NetworkServerConfig config, string share, string path, CancellationToken ct)
        {
            string remote = WebDavSession.EffectivePath(share, path);
            string pwd = await NetworkServerManager.GetPasswordAsync(config);
            var session = new WebDavSession(config, pwd);
            Log.Info("WebDavBrowser.OpenRead: {Url}/{Remote}", NetworkUrl.Compose(config), remote);
            try
            {
                var stream = await session.OpenReadAsync(remote, ct);
                // Stream owns the session — disposing the stream disposes the session.
                return stream;
            }
            catch (NetworkOperationException ex)
            {
                Log.Dbg("WebDavBrowser.OpenRead: {Reason} ({Message})", ex.Reason, ex.Message);
                session.Dispose();
                throw;
            }
        }

        // ─────────────────────── GetFileLength ───────────────────────

        public async Task<long> GetFileLengthAsync(
            NetworkServerConfig config, string share, string path, CancellationToken ct)
        {
            string remote = WebDavSession.EffectivePath(share, path);
            string pwd = await NetworkServerManager.GetPasswordAsync(config);
            using (var session = new WebDavSession(config, pwd))
            {
                try
                {
                    return await session.GetFileLengthAsync(remote, ct);
                }
                catch (NetworkOperationException ex)
                {
                    Log.Dbg("WebDavBrowser.GetFileLength: {Reason} ({Message})", ex.Reason, ex.Message);
                    throw;
                }
            }
        }

        // ─────────────────────── EntryExists ───────────────────────

        public async Task<bool> EntryExistsAsync(
            NetworkServerConfig config, string share, string path, bool isDirectory, CancellationToken ct)
        {
            string remote = WebDavSession.EffectivePath(share, path);
            string pwd = await NetworkServerManager.GetPasswordAsync(config);
            using (var session = new WebDavSession(config, pwd))
            {
                try
                {
                    return await session.EntryExistsAsync(remote, ct);
                }
                catch (NetworkOperationException ex)
                {
                    Log.Dbg("WebDavBrowser.EntryExists: {Reason} ({Message})", ex.Reason, ex.Message);
                    throw;
                }
            }
        }

        // ─────────────────────── WriteFile ───────────────────────

        public async Task WriteFileAsync(
            NetworkServerConfig config, string share, string path, string localPath, CancellationToken ct)
        {
            string remote = WebDavSession.EffectivePath(share, path);
            string pwd = await NetworkServerManager.GetPasswordAsync(config);
            using (var session = new WebDavSession(config, pwd))
            {
                Log.Info("WebDavBrowser.WriteFile: {LocalPath} → {Remote}", localPath, remote);
                try
                {
                    await session.WriteFileAsync(remote, localPath, ct);
                    Log.Info("WebDavBrowser.WriteFile: uploaded");
                }
                catch (NetworkOperationException ex)
                {
                    Log.Dbg("WebDavBrowser.WriteFile: {Reason} ({Message})", ex.Reason, ex.Message);
                    throw;
                }
            }
        }

        // ─────────────────────── OpenWriteStream ───────────────────────

        public async Task<Stream> OpenWriteStreamAsync(
            NetworkServerConfig config, string share, string path, CancellationToken ct)
        {
            string remote = WebDavSession.EffectivePath(share, path);
            string pwd = await NetworkServerManager.GetPasswordAsync(config);
            var session = new WebDavSession(config, pwd);
            Log.Info("WebDavBrowser.OpenWriteStream: {Remote}", remote);
            return session.OpenWriteStreamAsync(remote);
        }

        // ─────────────────────── DeleteFile ───────────────────────

        public async Task DeleteFileAsync(
            NetworkServerConfig config, string share, string path, CancellationToken ct)
        {
            string remote = WebDavSession.EffectivePath(share, path);
            string pwd = await NetworkServerManager.GetPasswordAsync(config);
            using (var session = new WebDavSession(config, pwd))
            {
                Log.Info("WebDavBrowser.DeleteFile: {Remote}", remote);
                try
                {
                    await session.DeleteFileAsync(remote, ct);
                }
                catch (NetworkOperationException ex)
                {
                    Log.Dbg("WebDavBrowser.DeleteFile: {Reason} ({Message})", ex.Reason, ex.Message);
                    throw;
                }
            }
        }

        // ─────────────────────── DeleteDirectory ───────────────────────

        public async Task DeleteDirectoryAsync(
            NetworkServerConfig config, string share, string path, CancellationToken ct)
        {
            string remote = WebDavSession.EffectivePath(share, path);
            string pwd = await NetworkServerManager.GetPasswordAsync(config);
            using (var session = new WebDavSession(config, pwd))
            {
                Log.Info("WebDavBrowser.DeleteDirectory: {Remote}", remote);
                try
                {
                    await session.DeleteDirectoryAsync(remote, ct);
                }
                catch (NetworkOperationException ex)
                {
                    Log.Dbg("WebDavBrowser.DeleteDirectory: {Reason} ({Message})", ex.Reason, ex.Message);
                    throw;
                }
            }
        }

        // ─────────────────────── RenameFile ───────────────────────

        public async Task RenameFileAsync(
            NetworkServerConfig config, string share, string path, string newName, bool isDirectory, CancellationToken ct)
        {
            string remote = WebDavSession.EffectivePath(share, path);
            string pwd = await NetworkServerManager.GetPasswordAsync(config);
            using (var session = new WebDavSession(config, pwd))
            {
                Log.Info("WebDavBrowser.RenameFile: {Remote} → {NewName}", remote, newName);
                try
                {
                    await session.RenameFileAsync(remote, newName, ct);
                }
                catch (NetworkOperationException ex)
                {
                    Log.Dbg("WebDavBrowser.RenameFile: {Reason} ({Message})", ex.Reason, ex.Message);
                    throw;
                }
            }
        }

        // ─────────────────────── CreateDirectory ───────────────────────

        public async Task CreateDirectoryAsync(
            NetworkServerConfig config, string share, string path, CancellationToken ct)
        {
            string remote = WebDavSession.EffectivePath(share, path);
            string pwd = await NetworkServerManager.GetPasswordAsync(config);
            using (var session = new WebDavSession(config, pwd))
            {
                Log.Info("WebDavBrowser.CreateDirectory: {Remote}", remote);
                try
                {
                    await session.CreateDirectoryAsync(remote, ct);
                }
                catch (NetworkOperationException ex)
                {
                    Log.Dbg("WebDavBrowser.CreateDirectory: {Reason} ({Message})", ex.Reason, ex.Message);
                    throw;
                }
            }
        }

        // ─────────────────────── Disconnect ───────────────────────

        public void Disconnect(NetworkServerConfig config)
        {
            // Stateless — no sessions to release.
            Log.Dbg("WebDavBrowser.Disconnect: {Key}", NetworkUrl.Compose(config));
        }
    }
}
