using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace XFiles.Network
{
    /// <summary>
    /// Pool of live SFTP sessions keyed by canonical URL. A second connect to
    /// the same server reuses the logged-in SftpClient; callers that hold a
    /// stream keep the session alive for the stream's lifetime.
    /// </summary>
    public static class SftpSessionPool
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SftpSession> _sessions =
            new System.Collections.Concurrent.ConcurrentDictionary<string, SftpSession>(
                System.StringComparer.Ordinal);

        /// <summary>
        /// Returns a connected session for the location, creating and logging in
        /// one if none exists. <paramref name="configure"/> runs on a newly
        /// created session BEFORE it connects, so the host-key resolver can be
        /// attached before HostKeyReceived fires.
        /// </summary>
        public static async Task<SftpSession> AcquireAsync(
            NetworkServerConfig config, string password, Action<SftpSession> configure, CancellationToken ct)
        {
            string key = NetworkUrl.Compose(config);
            if (key == null)
                throw new NetworkOperationException(NetworkOperationReason.Unreachable, "No host configured");

            var session = _sessions.GetOrAdd(key, _ =>
            {
                var created = new SftpSession(config);
                configure?.Invoke(created);
                return created;
            });
            try
            {
                await session.EnsureConnectedAsync(password, ct);
                return session;
            }
            catch
            {
                _sessions.TryRemove(key, out _);
                throw;
            }
        }

        public static void Remove(string key)
        {
            if (_sessions.TryRemove(key, out var session))
                session?.Dispose();
        }

        public static void DisconnectAll()
        {
            foreach (string key in _sessions.Keys.ToArray())
                Remove(key);
        }

        public static int ActiveSessionCount => _sessions.Count;
    }

    /// <summary>
    /// Pooled SFTP session backed by Renci.SshNet. SftpClient is NOT thread-safe,
    /// so every store call is serialized through a per-session gate (mirrors the
    /// SmbSession/SMB2FileStore constraint — concurrent in-flight commands throw
    /// or corrupt the connection). SftpFileStream is natively seekable, so remote
    /// media seeking works out of the box. Host keys are verified through an
    /// injected resolver (HostKeyTrustStore + optional confirmation dialog).
    /// </summary>
    public class SftpSession : IDisposable
    {
        private const int OperationTimeoutMs = 30000;
        private const int BulkOperationTimeoutMs = 120000;

        private readonly NetworkServerConfig _config;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private SftpClient _client;
        private bool _connected;
        private bool _invalid;

        /// <summary>
        /// Decides whether an offered SSH host key is acceptable. Receives the
        /// host:port key and the SHA256 fingerprint; returns true to trust.
        /// When null, every key is rejected (fail-safe).
        /// </summary>
        public Func<string, string, bool> HostKeyResolver { get; set; }

        public SftpSession(NetworkServerConfig config)
        {
            _config = config;
        }

        public NetworkServerConfig Config => _config;

        /// <summary>Connects and authenticates. Idempotent — reuses the live connection.</summary>
        public async Task EnsureConnectedAsync(string password, CancellationToken ct)
        {
            if (_connected && _client != null && _client.IsConnected)
                return;

            _client = new SftpClient(
                _config.Host,
                _config.EffectivePort,
                _config.Username ?? "",
                password ?? "");

            var hostKeyKey = $"{_config.Host}:{_config.EffectivePort}";
            _client.HostKeyReceived += (s, e) =>
            {
                e.CanTrust = HostKeyResolver?.Invoke(hostKeyKey, e.FingerPrintSHA256) ?? false;
            };

            await Task.Run(() => _client.Connect(), ct);
            _connected = true;
        }

        public async Task<List<NetworkFileEntry>> ListDirectoryAsync(string remotePath, CancellationToken ct)
        {
            return await RunAsync(() =>
            {
                string path = NormalizePath(remotePath);
                var result = new List<NetworkFileEntry>();
                foreach (var item in _client.ListDirectory(path))
                {
                    if (item.Name == "." || item.Name == "..") continue;
                    result.Add(new NetworkFileEntry
                    {
                        Name = item.Name,
                        IsDirectory = item.IsDirectory,
                        Size = item.Length,
                        LastWriteTime = item.LastWriteTime
                    });
                }
                return result;
            }, "list directory", ct);
        }

        /// <summary>
        /// Opens a seekable read stream (SftpFileStream). The caller owns the
        /// session for the stream's lifetime; the stream routes every read
        /// through the session gate so other operations stay safe.
        /// </summary>
        public async Task<SftpReadStream> OpenReadAsync(string remotePath, CancellationToken ct)
        {
            return await RunAsync(() =>
            {
                string path = NormalizePath(remotePath);
                var fs = _client.OpenRead(path);
                long length = fs.Length;
                return new SftpReadStream(this, fs, length);
            }, "open read stream", ct);
        }

        public async Task<long> GetFileLengthAsync(string remotePath, CancellationToken ct)
        {
            return await RunAsync(() =>
            {
                string path = NormalizePath(remotePath);
                var attrs = _client.GetAttributes(path);
                return attrs.IsRegularFile ? attrs.Size : -1;
            }, "get file length", ct);
        }

        public async Task<bool> EntryExistsAsync(string remotePath, bool isDirectory, CancellationToken ct)
        {
            return await RunAsync(() =>
            {
                string path = NormalizePath(remotePath);
                try
                {
                    var attrs = _client.GetAttributes(path);
                    if (attrs == null) return false;
                    return isDirectory ? attrs.IsDirectory : attrs.IsRegularFile;
                }
                catch (SftpPathNotFoundException)
                {
                    return false;
                }
            }, "entry exists", ct);
        }

        /// <summary>Uploads a local file, overwriting the remote path.</summary>
        public async Task WriteFileAsync(string remotePath, string localPath, CancellationToken ct)
        {
            await RunAsync(() =>
            {
                string path = NormalizePath(remotePath);
                using (var fs = _client.Create(path))
                {
                    using (var src = File.OpenRead(localPath))
                    {
                        src.CopyTo(fs);
                    }
                }
                return true;
            }, "write file", ct, BulkOperationTimeoutMs);
        }

        /// <summary>
        /// Opens a write stream that overwrites the remote file
        /// (CREATE_ALWAYS semantics). Used for copies; Dispose flushes + closes.
        /// </summary>
        public async Task<SftpWriteStream> OpenWriteStreamAsync(string remotePath, CancellationToken ct)
        {
            return await RunAsync(() =>
            {
                string path = NormalizePath(remotePath);
                var fs = _client.Create(path);
                return new SftpWriteStream(this, fs);
            }, "open write stream", ct);
        }

        /// <summary>Deletes a single remote file.</summary>
        public async Task DeleteFileAsync(string remotePath, CancellationToken ct)
        {
            await RunAsync(() =>
            {
                _client.DeleteFile(NormalizePath(remotePath));
                return true;
            }, "delete file", ct);
        }

        /// <summary>
        /// Recursively deletes a remote directory (SSH.NET DeleteDirectory only
        /// removes empty dirs). Runs under the gate for the whole walk with a
        /// generous timeout.
        /// </summary>
        public async Task DeleteDirectoryAsync(string remotePath, CancellationToken ct)
        {
            await RunAsync(() =>
            {
                DeleteDirectoryCore(NormalizePath(remotePath));
                return true;
            }, "delete directory", ct, BulkOperationTimeoutMs);
        }

        /// <summary>Renames a remote file or directory in place (same parent).</summary>
        public async Task RenameFileAsync(string remotePath, string newName, bool isDirectory, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(newName))
                throw new ArgumentException("New name is empty.", nameof(newName));
            await RunAsync(() =>
            {
                string path = NormalizePath(remotePath);
                string target = JoinPath(Path.GetDirectoryName(path), newName);
                _client.RenameFile(path, target);
                return true;
            }, "rename file", ct);
        }

        /// <summary>Creates a remote directory (idempotent — succeeds if it exists).</summary>
        public async Task CreateDirectoryAsync(string remotePath, CancellationToken ct)
        {
            await RunAsync(() =>
            {
                string path = NormalizePath(remotePath);
                if (!DirectoryExists(path))
                    _client.CreateDirectory(path);
                return true;
            }, "create directory", ct);
        }

        /// <summary>True when the remote path is an existing directory.</summary>
        private bool DirectoryExists(string path)
        {
            try
            {
                var attrs = _client.GetAttributes(path);
                return attrs != null && attrs.IsDirectory;
            }
            catch (SftpPathNotFoundException)
            {
                return false;
            }
        }

        private void DeleteDirectoryCore(string path)
        {
            foreach (var item in _client.ListDirectory(path))
            {
                if (item.Name == "." || item.Name == "..") continue;
                if (item.IsDirectory)
                    DeleteDirectoryCore(item.FullName);
                else
                    _client.DeleteFile(item.FullName);
            }
            _client.DeleteDirectory(path);
        }

        private string NormalizePath(string remotePath)
        {
            if (string.IsNullOrEmpty(remotePath)) return ".";
            return remotePath;
        }

        private static string JoinPath(string parent, string name)
        {
            parent = parent ?? ".";
            parent = parent.TrimEnd('/');
            if (parent.Length == 0) parent = ".";
            return parent + "/" + name;
        }

        /// <summary>
        /// Runs a synchronous client operation on a worker task with a timeout.
        /// On timeout the session is invalidated and removed from the pool.
        /// </summary>
        private async Task<T> RunAsync<T>(
            Func<T> op, string opName, CancellationToken ct, int timeoutMs = OperationTimeoutMs)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_invalid || _client == null || !_client.IsConnected)
                    throw new NetworkOperationException(NetworkOperationReason.Unreachable,
                        "SFTP session is no longer connected");

                var task = Task.Run(op, ct);
                var done = await Task.WhenAny(task, Task.Delay(timeoutMs, ct)).ConfigureAwait(false);
                if (done != task)
                {
                    Invalidate();
                    throw new NetworkOperationException(NetworkOperationReason.TimedOut,
                        $"SFTP operation timed out: {opName}");
                }
                return await task.ConfigureAwait(false);
            }
            catch (NetworkOperationException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw new NetworkOperationException(NetworkOperationReason.TimedOut,
                    $"SFTP operation cancelled: {opName}");
            }
            catch (Exception ex)
            {
                throw ExceptionFrom(ex, opName);
            }
            finally
            {
                _gate.Release();
            }
        }

        private NetworkOperationException ExceptionFrom(Exception ex, string opName)
        {
            var reason = NetworkOperationReason.Unreachable;
            if (ex is SshConnectionException) reason = NetworkOperationReason.Unreachable;
            else if (ex is SshAuthenticationException) reason = NetworkOperationReason.AuthFailed;
            else if (ex is SftpPathNotFoundException || ex is FileNotFoundException)
                reason = NetworkOperationReason.NotFound;
            else if (ex is SftpPermissionDeniedException) reason = NetworkOperationReason.AccessDenied;
            return new NetworkOperationException(reason, $"SFTP {opName}: {ex.Message}", ex);
        }

        /// <summary>Serializes a stream read/write under the gate (see WithStoreLock).</summary>
        internal T WithGate<T>(Func<T> op)
        {
            _gate.Wait();
            try
            {
                if (_invalid || _client == null || !_client.IsConnected)
                    throw new NetworkOperationException(NetworkOperationReason.Unreachable,
                        "SFTP session is no longer connected");
                return op();
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Marks the session dead and removes it from the pool.</summary>
        internal void Invalidate()
        {
            _invalid = true;
            string key = NetworkUrl.Compose(_config);
            if (key != null)
                SftpSessionPool.Remove(key);
            Disconnect();
        }

        /// <summary>Closes the client connection and releases resources.</summary>
        public void Disconnect()
        {
            try { _client?.Disconnect(); } catch { }
            _client = null;
            _connected = false;
        }

        public void Dispose() => Disconnect();
    }
}
