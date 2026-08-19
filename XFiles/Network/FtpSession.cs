using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentFTP;
using FluentFTP.Exceptions;

namespace XFiles.Network
{
    /// <summary>
    /// One FTP/FTPS connection backed by FluentFTP. Created per operation (an
    /// FTP login is ~100ms — cheap enough that pooling, unlike SMB, is not
    /// worth the thread-safety cost: AsyncFtpClient is not thread-safe).
    /// Read/write streams returned to callers own the client and keep it
    /// alive until the stream is disposed.
    /// FTP has no share layer: paths are absolute from the server root (or
    /// relative to the login working directory when empty). share is ignored.
    /// Deliberately free of Log/UWP dependencies so it links into the net8.0
    /// desktop tests for the docker smoke test.
    /// </summary>
    public sealed class FtpSession : IDisposable
    {
        public const int OperationTimeoutMs = 15000;

        /// <summary>
        /// Optional sink for FluentFTP protocol traces (host, message, isError).
        /// Wired by the app to forward to the central log; stays null in the
        /// desktop tests. isError=false → diagnostic (Dbg); true → warning.
        /// </summary>
        public static Action<string, string, bool> TraceSink;

        private static string SanitizePassword(string msg)
        {
            if (msg == null) return msg;
            int idx = msg.IndexOf("pass='", StringComparison.Ordinal);
            if (idx < 0) return msg;
            int start = idx + 6;
            int end = msg.IndexOf('\'', start);
            if (end < 0) return msg;
            int len = end - start;
            return msg.Substring(0, start) + "***" + msg.Substring(end);
        }

        private readonly NetworkServerConfig _config;
        private readonly string _password;
        private AsyncFtpClient _client;
        private bool _supportsRest;

        public FtpSession(NetworkServerConfig config) : this(config, null) { }

        public FtpSession(NetworkServerConfig config, string password)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _password = password;
        }

        /// <summary>True when the server advertises the REST command (seekable reads).</summary>
        public bool SupportsRest => _supportsRest;

        /// <summary>Host/port the session targets (logging/diagnostics).</summary>
        public string Target => $"{_config.Host}:{_config.EffectivePort}";

        /// <summary>
        /// Connects and logs in. <paramref name="password"/> may be null; an
        /// empty username means anonymous. Throws NetworkOperationException.
        /// </summary>
        public async Task EnsureConnectedAsync(string password, CancellationToken ct)
        {
            if (_client != null && _client.IsConnected) return;

            string user = string.IsNullOrEmpty(_config.Username)
                ? "anonymous"
                : _config.Username;
            string pass = string.IsNullOrEmpty(_config.Username)
                ? "xfiles@local"
                : (password ?? "");

            bool isFtps = _config.Protocol == NetworkProtocol.Ftps;
            FtpEncryptionMode? mode = isFtps
                // Port 990 is the conventional implicit-FTPS port (RFC 4217):
                // TLS starts on the very first byte. Any other port is explicit
                // FTPS (AUTH TLS on the plaintext control connection). The user
                // picks the mode by entering the port in the location dialog.
                ? (_config.EffectivePort == 990 ? FtpEncryptionMode.Implicit : FtpEncryptionMode.Explicit)
                : (FtpEncryptionMode?)null;

            AsyncFtpClient client;
            try
            {
                client = await ConnectOnceAsync(user, pass, mode, ct).ConfigureAwait(false);
            }
            catch (NetworkOperationException ex) when (ShouldAutoUpgradeToFtps(ex))
            {
                // The server rejected a plaintext login and demands TLS
                // (FileZilla "503 Use AUTH first", vsftpd "530 ... must use
                // encryption"). Retry automatically with explicit FTPS instead
                // of failing the operation.
                TraceSink?.Invoke(_config.Host, "Plain FTP rejected — server demands TLS. Retrying with explicit FTPS.", true);
                client = await ConnectOnceAsync(user, pass, FtpEncryptionMode.Explicit, ct).ConfigureAwait(false);
            }

            _supportsRest = client.HasFeature(FtpCapability.REST);
            _client = client;
        }

        private async Task<AsyncFtpClient> ConnectOnceAsync(
            string user, string pass, FtpEncryptionMode? mode, CancellationToken ct)
        {
            var client = new AsyncFtpClient(_config.Host, user, pass, _config.EffectivePort, null, new FtpVerboseLogger(_config.Host));
            client.Config.ConnectTimeout = OperationTimeoutMs;
            client.Config.ReadTimeout = OperationTimeoutMs;
            client.Config.DataConnectionConnectTimeout = OperationTimeoutMs;
            client.Config.DataConnectionReadTimeout = OperationTimeoutMs;
            // Force IPv4 — Xbox UWP may resolve AAAA records but fail to
            // connect via IPv6, causing a silent hang until timeout.
            client.Config.InternetProtocolVersions = FtpIpVersion.IPv4;
            if (mode.HasValue)
            {
                client.Config.EncryptionMode = mode.Value;
                client.Config.ValidateAnyCertificate = true;
            }

            // Pre-connect DNS resolution logging
            string[] dnsIps = new string[0];
            try
            {
                var addresses = await System.Net.Dns.GetHostAddressesAsync(_config.Host);
                dnsIps = Array.ConvertAll(addresses, a => a.ToString());
#if FTP_CONNECT_DEBUG
                TraceSink?.Invoke(_config.Host, string.Format(
                    "DNS resolved {0} → [{1}] (count={2}, af={3})",
                    _config.Host, string.Join(", ", dnsIps), dnsIps.Length,
                    addresses.Length > 0 ? addresses[0].AddressFamily.ToString() : "none"), false);
#endif
            }
            catch (Exception dnsEx)
            {
                TraceSink?.Invoke(_config.Host, string.Format(
                    "DNS FAILED for {0}: {1}", _config.Host, dnsEx.Message), true);
            }

            // Connect attempt summary (always visible)
            TraceSink?.Invoke(_config.Host, string.Format(
                "CONNECT ATTEMPT host={0} port={1} user={2} passLen={3} mode={4}",
                _config.Host, _config.EffectivePort, user, pass.Length,
                mode?.ToString() ?? "null"), false);
#if FTP_CONNECT_DEBUG
            TraceSink?.Invoke(_config.Host, string.Format(
                "CONNECT CONFIG timeout={0}ms encrypt={1} sslProtocols={2} ipVersions={3} dns=[{4}]",
                client.Config.ConnectTimeout,
                client.Config.EncryptionMode,
                client.Config.SslProtocols,
                client.Config.InternetProtocolVersions,
                string.Join(", ", dnsIps)), false);
#endif

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // FluentFTP (.NET Standard 2.0) doesn't use ConfigureAwait(false)
                // internally. On UWP, its SslStream.AuthenticateAsClientAsync
                // captures the DispatcherSynchronizationContext and deadlocks
                // (UI thread blocked → TLS handshake can't pump → hang). Task.Run
                // forces the entire FluentFTP call onto a thread pool thread.
                await Task.Run(() => client.Connect(ct), ct).ConfigureAwait(false);
                sw.Stop();
                TraceSink?.Invoke(_config.Host, string.Format(
                    "CONNECTED OK elapsed={0}ms isAuthenticated={1} isEncrypted={2}",
                    sw.ElapsedMilliseconds, client.IsAuthenticated, client.IsEncrypted), false);
#if FTP_CONNECT_DEBUG
                TraceSink?.Invoke(_config.Host, string.Format(
                    "CONNECT CAPABILITIES capabilities={0}",
                    client.HasFeature(FtpCapability.REST) ? "REST" : "no-REST"), false);
#endif
                return client;
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                TraceSink?.Invoke(_config.Host, string.Format("CONNECT CANCELLED elapsed={0}ms", sw.ElapsedMilliseconds), true);
                client.Dispose();
                throw new NetworkOperationException(
                    NetworkOperationReason.Cancelled, "FTP connect cancelled");
            }
            catch (FtpAuthenticationException ex)
            {
                sw.Stop();
                TraceSink?.Invoke(_config.Host, string.Format("CONNECT AUTH FAILED elapsed={0}ms: {1}", sw.ElapsedMilliseconds, ex.Message), true);
                client.Dispose();
                throw new NetworkOperationException(
                    NetworkOperationReason.AuthFailed, "FTP login failed — check user and password", ex);
            }
            catch (FtpCommandException ex)
            {
                sw.Stop();
                TraceSink?.Invoke(_config.Host, string.Format("CONNECT CMD FAILED elapsed={0}ms code={1}: {2}", sw.ElapsedMilliseconds, ex.CompletionCode, ex.Message), true);
                client.Dispose();
                string message = ex.Message ?? "";
                if (message.IndexOf("AUTH first", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    throw new NetworkOperationException(
                        NetworkOperationReason.AccessDenied,
                        "This FTP server requires a secure connection. Use FTPS as the protocol (port 21 explicit or 990 implicit) and try again.",
                        ex);
                }
                throw new NetworkOperationException(
                    MapStatusCode(ex.CompletionCode), ex.Message, ex);
            }
            catch (SocketException ex)
            {
                sw.Stop();
                TraceSink?.Invoke(_config.Host, string.Format("CONNECT SOCKET FAILED elapsed={0}ms: SocketError={1} nativeError={2}: {3}",
                    sw.ElapsedMilliseconds, ex.SocketErrorCode, ex.NativeErrorCode, ex.Message), true);
                client.Dispose();
                throw new NetworkOperationException(
                    NetworkOperationReason.Unreachable, ex.Message, ex);
            }
            catch (TimeoutException ex)
            {
                sw.Stop();
                TraceSink?.Invoke(_config.Host, string.Format("CONNECT TIMEOUT elapsed={0}ms: {1}", sw.ElapsedMilliseconds, ex.Message), true);
                client.Dispose();
                throw new NetworkOperationException(
                    NetworkOperationReason.TimedOut, ex.Message, ex);
            }
            catch (ObjectDisposedException ex)
            {
                sw.Stop();
                TraceSink?.Invoke(_config.Host, string.Format("CONNECT DISPOSED elapsed={0}ms: {1}\n  inner={2}\n  stackTrace={3}",
                    sw.ElapsedMilliseconds, ex.Message,
                    ex.InnerException?.Message ?? "(none)",
                    ex.StackTrace ?? "(none)"), true);
                client.Dispose();
                throw new NetworkOperationException(
                    NetworkOperationReason.Unreachable,
                    mode == FtpEncryptionMode.Implicit
                        ? "FTPS connection failed — the server did not complete the secure handshake. Check that the server supports FTPS and the port (990 = implicit, otherwise explicit)."
                        : mode == FtpEncryptionMode.Explicit
                            ? "FTPS connection failed — the server did not complete the secure handshake. Check that the server supports FTPS and the port (990 = implicit, otherwise explicit)."
                            : "The FTP server rejected the connection (it may require a secure FTPS connection). Try FTPS as the protocol.",
                    ex);
            }
            catch (Exception ex)
            {
                sw.Stop();
                TraceSink?.Invoke(_config.Host, string.Format("CONNECT UNEXPECTED elapsed={0}ms type={1}: {2}",
                    sw.ElapsedMilliseconds, ex.GetType().Name, ex.Message), true);
                client.Dispose();
                throw new NetworkOperationException(
                    NetworkOperationReason.Unreachable, ex.Message, ex);
            }
        }

        /// <summary>
        /// True when a plain-FTP connect failed because the server demands TLS
        /// ("503 Use AUTH first", "530 ... must use encryption") or FluentFTP
        /// collapsed that rejection into an ObjectDisposedException (FileZilla).
        /// The raw detail is preserved as the inner exception for detection.
        /// </summary>
        private bool ShouldAutoUpgradeToFtps(NetworkOperationException ex)
        {
            if (_config.Protocol == NetworkProtocol.Ftps) return false;
            if (ex.InnerException is ObjectDisposedException) return true;
            string detail = ex.InnerException?.Message ?? ex.Message ?? "";
            return detail.IndexOf("encryption", StringComparison.OrdinalIgnoreCase) >= 0
                || detail.IndexOf("AUTH first", StringComparison.OrdinalIgnoreCase) >= 0
                || detail.IndexOf("must use", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static NetworkOperationReason MapStatusCode(string completionCode)
        {
            string code = (completionCode ?? "").Trim();
            if (code.Length >= 3)
                code = code.Substring(0, 3);
            switch (code)
            {
                case "430":
                case "530":
                case "534":
                    return NetworkOperationReason.AuthFailed;
                case "550":
                case "553":
                    return NetworkOperationReason.NotFound;
                case "450":
                case "451":
                case "452":
                case "532":
                case "552":
                    return NetworkOperationReason.AccessDenied;
                case "425":
                case "426":
                case "421":
                    return NetworkOperationReason.Unreachable;
                default:
                    return NetworkOperationReason.Unreachable;
            }
        }

        /// <summary>Lists a directory. Empty path = login working directory.</summary>
        public async Task<List<NetworkFileEntry>> ListDirectoryAsync(string path, CancellationToken ct)
        {
            var client = await EnsureAsync(ct);
            try
            {
                var items = await client.GetListing(path, FtpListOption.AllFiles, ct).ConfigureAwait(false);
                var entries = new List<NetworkFileEntry>(items.Length);
                foreach (var item in items)
                {
                    if (item == null) continue;
                    entries.Add(new NetworkFileEntry
                    {
                        Name = item.Name,
                        IsDirectory = item.Type == FtpObjectType.Directory,
                        Size = item.Type == FtpObjectType.File ? Math.Max(0, item.Size) : 0,
                        LastWriteTime = item.Modified
                    });
                }
                return entries;
            }
            catch (Exception ex)
            {
                throw new NetworkOperationException(
                    MapListException(path, ex), $"FTP list failed: {ex.Message}", ex);
            }
        }

        private NetworkOperationReason MapListException(string path, Exception ex)
        {
            if (ex is OperationCanceledException)
                return NetworkOperationReason.Cancelled;
            if (ex is FtpCommandException fe)
            {
                // "550 Permission denied" is an access failure, not a missing
                // entry — the numeric code alone can't tell them apart.
                if (fe.Message.IndexOf("Permission", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    fe.Message.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0)
                    return NetworkOperationReason.AccessDenied;
                return MapStatusCode(fe.CompletionCode);
            }
            if (ex is SocketException)
                return NetworkOperationReason.Unreachable;
            if (ex is TimeoutException)
                return NetworkOperationReason.TimedOut;
            return NetworkOperationReason.Unreachable;
        }

        /// <summary>Returns the file length without downloading it.</summary>
        public async Task<long> GetFileLengthAsync(string path, CancellationToken ct)
        {
            var client = await EnsureAsync(ct);
            try
            {
                long size = await client.GetFileSize(path, 0, ct).ConfigureAwait(false);
                if (size < 0)
                    throw new NetworkOperationException(NetworkOperationReason.NotFound,
                        $"FTP file not found: {path}");
                return size;
            }
            catch (NetworkOperationException) { throw; }
            catch (Exception ex)
            {
                throw new NetworkOperationException(
                    MapListException(path, ex), $"FTP stat failed: {ex.Message}", ex);
            }
        }

        /// <summary>Checks whether a file or directory exists at the remote path.</summary>
        public async Task<bool> EntryExistsAsync(string path, bool isDirectory, CancellationToken ct)
        {
            var client = await EnsureAsync(ct);
            try
            {
                if (isDirectory)
                    return await client.DirectoryExists(path, ct).ConfigureAwait(false);
                return await client.FileExists(path, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new NetworkOperationException(
                    MapListException(path, ex), $"FTP exists failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Opens a seekable read stream. Seekability depends on the server
        /// advertising REST; when it does not, Seek(non-zero) throws
        /// NotSupportedException (the media pipeline degrades to sequential
        /// playback without seeking). The returned stream owns the client.
        /// </summary>
        public async Task<Stream> OpenReadAsync(string path, CancellationToken ct)
        {
            var client = await EnsureAsync(ct);
            long length = await GetFileLengthAsync(path, ct).ConfigureAwait(false);
            try
            {
                var data = await client.OpenRead(path, FtpDataType.Binary, 0, length, ct).ConfigureAwait(false);
                return new FtpReadStream(client, this, path, length, _supportsRest, ct, data);
            }
            catch (Exception ex)
            {
                client.Dispose();
                throw new NetworkOperationException(
                    MapListException(path, ex), $"FTP open failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Opens a write stream (STOR, overwrite disposition). The returned
        /// stream owns the client; disposal completes the transfer.
        /// </summary>
        public async Task<Stream> OpenWriteStreamAsync(string path, CancellationToken ct)
        {
            var client = await EnsureAsync(ct);
            try
            {
                var data = await client.OpenWrite(path, FtpDataType.Binary, true, ct).ConfigureAwait(false);
                return new FtpWriteStream(client, this, data, path);
            }
            catch (Exception ex)
            {
                client.Dispose();
                throw new NetworkOperationException(
                    MapListException(path, ex), $"FTP open write failed: {ex.Message}", ex);
            }
        }

        /// <summary>Uploads a local file, overwriting the remote path.</summary>
        public async Task WriteFileAsync(string path, string localPath, CancellationToken ct)
        {
            var client = await EnsureAsync(ct);
            try
            {
                var result = await client.UploadFile(localPath, path, FtpRemoteExists.Overwrite, false,
                    FtpVerify.None, null, ct).ConfigureAwait(false);
                if (result != FtpStatus.Success)
                    throw new NetworkOperationException(NetworkOperationReason.AccessDenied,
                        $"FTP upload failed with status {result}");
            }
            catch (NetworkOperationException) { throw; }
            catch (Exception ex)
            {
                throw new NetworkOperationException(
                    MapListException(path, ex), $"FTP upload failed: {ex.Message}", ex);
            }
        }

        /// <summary>Deletes a single remote file.</summary>
        public async Task DeleteFileAsync(string path, CancellationToken ct)
        {
            var client = await EnsureAsync(ct);
            try
            {
                await client.DeleteFile(path, ct).ConfigureAwait(false);
            }
            catch (NetworkOperationException) { throw; }
            catch (Exception ex)
            {
                throw new NetworkOperationException(
                    MapListException(path, ex), $"FTP delete failed: {ex.Message}", ex);
            }
        }

        /// <summary>Recursively deletes a remote directory tree.</summary>
        public async Task DeleteDirectoryAsync(string path, CancellationToken ct)
        {
            var client = await EnsureAsync(ct);
            try
            {
                await client.DeleteDirectory(path, FtpListOption.Recursive, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new NetworkOperationException(
                    MapListException(path, ex), $"FTP delete directory failed: {ex.Message}", ex);
            }
        }

        /// <summary>Renames a remote file or directory within its parent.</summary>
        public async Task RenameFileAsync(string path, string newName, CancellationToken ct)
        {
            var client = await EnsureAsync(ct);
            string dest = JoinRemote(path, newName);
            try
            {
                await client.Rename(path, dest, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new NetworkOperationException(
                    MapListException(path, ex), $"FTP rename failed: {ex.Message}", ex);
            }
        }

        /// <summary>Creates a remote directory (including missing parents).</summary>
        public async Task CreateDirectoryAsync(string path, CancellationToken ct)
        {
            var client = await EnsureAsync(ct);
            try
            {
                await client.CreateDirectory(path, true, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new NetworkOperationException(
                    MapListException(path, ex), $"FTP create directory failed: {ex.Message}", ex);
            }
        }

        private async Task<AsyncFtpClient> EnsureAsync(CancellationToken ct)
        {
            await EnsureConnectedAsync(_password, ct).ConfigureAwait(false);
            return _client;
        }

        /// <summary>Joins a remote path's parent with a new name (FTP paths use '/').</summary>
        private static string JoinRemote(string path, string newName)
        {
            if (string.IsNullOrEmpty(path)) return newName;
            int slash = path.LastIndexOf('/');
            string parent = slash > 0 ? path.Substring(0, slash) : "";
            if (parent.Length == 0) return newName;
            return parent + "/" + newName;
        }

        public void Dispose()
        {
            try
            {
                _client?.Dispose();
            }
            catch
            {
                // Best-effort connection close; the client is per-operation.
            }
            _client = null;
        }
    }

    /// <summary>
    /// Seekable read stream over an FTP data connection. Seek reopens the data
    /// connection with a REST offset (only when the server supports REST);
    /// Position is tracked locally since the raw data stream is not seekable.
    /// Owns the AsyncFtpClient and disposes it when the stream is closed.
    /// </summary>
    public sealed class FtpReadStream : Stream
    {
        private readonly AsyncFtpClient _client;
        private readonly FtpSession _session;
        private readonly string _path;
        private readonly long _length;
        private readonly bool _supportsRest;
        private readonly CancellationToken _ct;
        private Stream _data;
        private long _position;

        internal FtpReadStream(AsyncFtpClient client, FtpSession session, string path,
            long length, bool supportsRest, CancellationToken ct, Stream data)
        {
            _client = client;
            _session = session;
            _path = path;
            _length = length;
            _supportsRest = supportsRest;
            _ct = ct;
            _data = data;
        }

        public override bool CanRead => true;
        public override bool CanSeek => _supportsRest;
        public override bool CanWrite => false;
        public override long Length => _length;

        public override long Position
        {
            get { return _position; }
            set { Seek(value, SeekOrigin.Begin); }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            EnsureData();
            int read = _data.Read(buffer, offset, count);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long target;
            switch (origin)
            {
                case SeekOrigin.Begin: target = offset; break;
                case SeekOrigin.Current: target = _position + offset; break;
                case SeekOrigin.End: target = _length + offset; break;
                default: throw new ArgumentOutOfRangeException(nameof(origin));
            }
            if (target < 0)
                throw new ArgumentOutOfRangeException(nameof(offset), "Negative seek target");
            if (target == _position && _data != null)
                return _position;

            if (!_supportsRest)
            {
                if (target == 0)
                {
                    // Plain RETR from the start is always possible.
                    ReleaseData();
                    _position = 0;
                    EnsureData();
                    return _position;
                }
                throw new NotSupportedException(
                    "FTP server does not advertise REST — seek not supported (sequential playback).");
            }

            ReleaseData();
            _position = target;
            EnsureData();
            return _position;
        }

        private void EnsureData()
        {
            if (_data != null) return;
            _data = _client.OpenRead(_path, FtpDataType.Binary, _position, _length, _ct)
                .GetAwaiter().GetResult();
        }

        private void ReleaseData()
        {
            try
            {
                _data?.Dispose();
            }
            catch
            {
                // Best-effort close; a fresh data connection is opened on next read.
            }
            _data = null;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ReleaseData();
                _session.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Sequential write stream over an FTP data connection (STOR). Position is
    /// tracked locally; the raw data stream is not seekable. Owns the
    /// AsyncFtpClient; disposal completes the transfer and closes the client.
    /// </summary>
    public sealed class FtpWriteStream : Stream
    {
        private readonly AsyncFtpClient _client;
        private readonly FtpSession _session;
        private readonly Stream _data;
        private readonly string _path;
        private long _position;

        internal FtpWriteStream(AsyncFtpClient client, FtpSession session, Stream data, string path)
        {
            _client = client;
            _session = session;
            _data = data;
            _path = path;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _position;
        public override long Position { get { return _position; } set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _data.Write(buffer, offset, count);
            _position += count;
        }

        public override void Flush()
        {
            try
            {
                _data.Flush();
            }
            catch (NotImplementedException) { }
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    _data.Dispose();
                }
                catch
                {
                    // Best-effort; the client close below also tears the transfer.
                }
                _session.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Forwards FluentFTP protocol traces to the central log.
    /// Uses an injected sink so this file stays free of Log/UWP dependencies
    /// (it links into the net8.0 desktop tests).
    /// </summary>
    internal sealed class FtpVerboseLogger : IFtpLogger
    {
        private readonly string _host;

        /// <summary>
        /// Maps a FluentFTP trace level to a boolean — return true to forward,
        /// false to suppress. The app wires this to the current app log level
        /// so FluentFTP verbosity tracks the app setting.
        /// Null means forward everything (safe default).
        /// </summary>
        public static Func<FtpTraceLevel, bool> TraceFilter { get; set; }

        public FtpVerboseLogger(string host)
        {
            _host = host;
        }

        public void Log(FtpLogEntry entry)
        {
            if (TraceFilter != null && !TraceFilter(entry.Severity))
                return;

            string level;
            switch (entry.Severity)
            {
                case FtpTraceLevel.Error:   level = "ERR"; break;
                case FtpTraceLevel.Warn:    level = "WRN"; break;
                case FtpTraceLevel.Info:    level = "INF"; break;
                case FtpTraceLevel.Verbose: level = "VRB"; break;
                default:                    level = "???"; break;
            }

            string msg = entry.Message;
            if (!string.IsNullOrEmpty(msg) && msg.IndexOf("pass=", StringComparison.OrdinalIgnoreCase) >= 0)
                msg = System.Text.RegularExpressions.Regex.Replace(msg, @"pass=\S+", "pass=***", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            bool isError = entry.Severity == FtpTraceLevel.Error || entry.Severity == FtpTraceLevel.Warn;
            FtpSession.TraceSink?.Invoke(_host, string.Format("[FTP {0}] {1}", level, msg), isError);
        }
    }
}
