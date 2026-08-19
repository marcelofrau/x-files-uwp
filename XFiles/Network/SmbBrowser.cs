using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace XFiles.Network
{
    /// <summary>
    /// SMB implementation of INetworkFileSystemProvider. Resolves the location's
    /// password from the credential vault, acquires a pooled SmbSession and
    /// forwards the operation. All failures surface as NetworkOperationException
    /// (with a Reason the UI can translate to a message) and are logged here —
    /// SmbSession itself stays Log-free so it links into the desktop tests.
    /// </summary>
    public class SmbBrowser : INetworkFileSystemProvider
    {
        private SmbSession _lastNegotiatedSession;

        public NetworkProtocol Protocol => NetworkProtocol.Smb;

        public async Task<string> TestConnectionAsync(NetworkServerConfig config, string password, CancellationToken ct)
        {
            using (var session = new SmbSession(config))
            {
                await session.EnsureConnectedAsync(password, ct);
                var shares = await session.ListSharesAsync(ct);
                if (!string.IsNullOrEmpty(config.Share))
                {
                    var entries = await session.ListDirectoryAsync(config.Share, "", ct);
                    return $"Connected — share \"{config.Share}\" OK ({entries.Count} items).";
                }
                return $"Connected — {shares.Count} share(s) found.";
            }
        }

        private void LogNegotiatedOnce(SmbSession session)
        {
            if (ReferenceEquals(session, _lastNegotiatedSession)) return;
            _lastNegotiatedSession = session;
            Log.Dbg("SmbBrowser: session negotiated — {Info}", session.NegotiatedInfo());
        }

        public async Task<List<string>> ListSharesAsync(NetworkServerConfig config, CancellationToken ct)
        {
            string password = await NetworkServerManager.GetPasswordAsync(config);
            var session = await SmbSessionPool.AcquireAsync(config, password, ct);
            Log.Info("SmbBrowser.ListShares: {Url}", NetworkUrl.Compose(config));
            try
            {
                var shares = await session.ListSharesAsync(ct);
                Log.Dbg("SmbBrowser.ListShares: {Count} shares", shares.Count);
                return shares;
            }
            catch (NetworkOperationException ex)
            {
                Log.Dbg("SmbBrowser.ListShares: {Reason} ({Message})", ex.Reason, ex.Message);
                throw;
            }
        }

        public async Task<List<NetworkFileEntry>> ListDirectoryAsync(
            NetworkServerConfig config, string share, string path, CancellationToken ct)
        {
            string password = await NetworkServerManager.GetPasswordAsync(config);
            var session = await SmbSessionPool.AcquireAsync(config, password, ct);
            LogNegotiatedOnce(session);
            Log.Info("SmbBrowser.ListDirectory: {Url}/{Share}/{Path}", NetworkUrl.Compose(config), share, path);
            try
            {
                var entries = await session.ListDirectoryAsync(share, path, ct);
                Log.Dbg("SmbBrowser.ListDirectory: {Count} entries", entries.Count);
                return entries;
            }
            catch (NetworkOperationException ex)
            {
                Log.Dbg("SmbBrowser.ListDirectory: {Reason} ({Message})", ex.Reason, ex.Message);
                throw;
            }
        }

        public async Task<Stream> OpenReadAsync(
            NetworkServerConfig config, string share, string path, CancellationToken ct)
        {
            string password = await NetworkServerManager.GetPasswordAsync(config);
            var session = await SmbSessionPool.AcquireAsync(config, password, ct);
            Log.Info("SmbBrowser.OpenRead: {Url}/{Share}/{Path}", NetworkUrl.Compose(config), share, path);
            try
            {
                return await session.OpenReadAsync(share, path, ct);
            }
            catch (NetworkOperationException ex)
            {
                Log.Dbg("SmbBrowser.OpenRead: {Reason} ({Message})", ex.Reason, ex.Message);
                throw;
            }
        }

        public async Task<long> GetFileLengthAsync(
            NetworkServerConfig config, string share, string path, CancellationToken ct)
        {
            string password = await NetworkServerManager.GetPasswordAsync(config);
            var session = await SmbSessionPool.AcquireAsync(config, password, ct);
            try
            {
                return await session.GetFileLengthAsync(share, path, ct);
            }
            catch (NetworkOperationException ex)
            {
                Log.Dbg("SmbBrowser.GetFileLength: {Reason} ({Message})", ex.Reason, ex.Message);
                throw;
            }
        }

        public async Task<bool> EntryExistsAsync(
            NetworkServerConfig config, string share, string path, bool isDirectory, CancellationToken ct)
        {
            string password = await NetworkServerManager.GetPasswordAsync(config);
            var session = await SmbSessionPool.AcquireAsync(config, password, ct);
            try
            {
                return await session.EntryExistsAsync(share, path, isDirectory, ct);
            }
            catch (NetworkOperationException ex)
            {
                Log.Dbg("SmbBrowser.EntryExists: {Reason} ({Message})", ex.Reason, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Uploads a local file's bytes to a remote path, overwriting it. Used by the
        /// text editor's save-back. The local file is read in full (text files are
        /// capped at the editor's size tiers, so this stays small).
        /// </summary>
        public async Task WriteFileAsync(
            NetworkServerConfig config, string share, string path, string localPath, CancellationToken ct)
        {
            byte[] data = File.ReadAllBytes(localPath);
            string password = await NetworkServerManager.GetPasswordAsync(config);
            var session = await SmbSessionPool.AcquireAsync(config, password, ct);
            Log.Info("SmbBrowser.WriteFile: {Length} bytes → {Share}/{Path}", data.Length, share, path);
            try
            {
                await session.WriteFileAsync(share, path, data, ct);
                Log.Info("SmbBrowser.WriteFile: uploaded {Length} bytes", data.Length);
            }
            catch (NetworkOperationException ex)
            {
                Log.Dbg("SmbBrowser.WriteFile: {Reason} ({Message})", ex.Reason, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Opens a remote path for writing (overwrite disposition). Used by copy
        /// and the text editor's save-back. Returns a write-only SMB stream.
        /// </summary>
        public async Task<Stream> OpenWriteStreamAsync(
            NetworkServerConfig config, string share, string path, CancellationToken ct)
        {
            string password = await NetworkServerManager.GetPasswordAsync(config);
            var session = await SmbSessionPool.AcquireAsync(config, password, ct);
            Log.Info("SmbBrowser.OpenWriteStream: {Share}/{Path}", share, path);
            try
            {
                return await session.OpenWriteStreamAsync(share, path, ct);
            }
            catch (NetworkOperationException ex)
            {
                Log.Dbg("SmbBrowser.OpenWriteStream: {Reason} ({Message})", ex.Reason, ex.Message);
                throw;
            }
        }

        /// <summary>Deletes a single remote file.</summary>
        public async Task DeleteFileAsync(
            NetworkServerConfig config, string share, string path, CancellationToken ct)
        {
            string password = await NetworkServerManager.GetPasswordAsync(config);
            var session = await SmbSessionPool.AcquireAsync(config, password, ct);
            Log.Info("SmbBrowser.DeleteFile: {Share}/{Path}", share, path);
            try
            {
                await session.DeleteFileAsync(share, path, ct);
            }
            catch (NetworkOperationException ex)
            {
                Log.Dbg("SmbBrowser.DeleteFile: {Reason} ({Message})", ex.Reason, ex.Message);
                throw;
            }
        }

        /// <summary>Recursively deletes a remote directory tree.</summary>
        public async Task DeleteDirectoryAsync(
            NetworkServerConfig config, string share, string path, CancellationToken ct)
        {
            string password = await NetworkServerManager.GetPasswordAsync(config);
            var session = await SmbSessionPool.AcquireAsync(config, password, ct);
            Log.Info("SmbBrowser.DeleteDirectory: {Share}/{Path}", share, path);
            try
            {
                await session.DeleteDirectoryAsync(share, path, ct);
            }
            catch (NetworkOperationException ex)
            {
                Log.Dbg("SmbBrowser.DeleteDirectory: {Reason} ({Message})", ex.Reason, ex.Message);
                throw;
            }
        }

        /// <summary>Renames a remote file or directory within its parent.</summary>
        public async Task RenameFileAsync(
            NetworkServerConfig config, string share, string path, string newName, bool isDirectory, CancellationToken ct)
        {
            string password = await NetworkServerManager.GetPasswordAsync(config);
            var session = await SmbSessionPool.AcquireAsync(config, password, ct);
            Log.Info("SmbBrowser.RenameFile: {Share}/{Path} → {NewName}", share, path, newName);
            try
            {
                await session.RenameFileAsync(share, path, newName, isDirectory, ct);
            }
            catch (NetworkOperationException ex)
            {
                Log.Dbg("SmbBrowser.RenameFile: {Reason} ({Message})", ex.Reason, ex.Message);
                throw;
            }
        }

        /// <summary>Creates a remote directory.</summary>
        public async Task CreateDirectoryAsync(
            NetworkServerConfig config, string share, string path, CancellationToken ct)
        {
            string password = await NetworkServerManager.GetPasswordAsync(config);
            var session = await SmbSessionPool.AcquireAsync(config, password, ct);
            Log.Info("SmbBrowser.CreateDirectory: {Share}/{Path}", share, path);
            try
            {
                await session.CreateDirectoryAsync(share, path, ct);
            }
            catch (NetworkOperationException ex)
            {
                Log.Dbg("SmbBrowser.CreateDirectory: {Reason} ({Message})", ex.Reason, ex.Message);
                throw;
            }
        }

        public void Disconnect(NetworkServerConfig config)
        {
            string key = NetworkUrl.Compose(config);
            if (key == null) return;
            SmbSessionPool.Remove(key);
            Log.Dbg("SmbBrowser.Disconnect: {Key}", key);
        }
    }
}
