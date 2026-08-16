using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using SMBLibrary;
using SMBLibrary.Client;

namespace XFiles.Network
{
    /// <summary>
    /// Pool of SMB sessions keyed by canonical location URL. A session keeps its
    /// TCP connection + login open between operations (fast re-browse); a failed
    /// or timed-out session is invalidated and replaced on the next acquire.
    /// Deliberately free of Log/UWP dependencies so it links into the net8.0
    /// desktop tests for the real-share smoke test.
    /// </summary>
    public static class SmbSessionPool
    {
        private static readonly ConcurrentDictionary<string, SmbSession> _sessions =
            new ConcurrentDictionary<string, SmbSession>(StringComparer.Ordinal);

        /// <summary>
        /// Returns a connected session for the location, creating and logging in
        /// one if none exists. <paramref name="password"/> may be null for guest.
        /// </summary>
        public static async Task<SmbSession> AcquireAsync(NetworkServerConfig config, string password, CancellationToken ct)
        {
            string key = NetworkUrl.Compose(config);
            if (key == null)
                throw new NetworkOperationException(NetworkOperationReason.Unreachable, "No host configured");

            var session = _sessions.GetOrAdd(key, _ => new SmbSession(config));
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
    /// One SMB2 session (TCP + login + one tree connect at a time). All
    /// operations are synchronous under the hood and serialized by a gate;
    /// public methods offload to the thread pool and enforce the operation
    /// timeout via Task.WhenAny. Not safe to call the same session from two
    /// threads concurrently — the gate serializes (second caller waits).
    /// </summary>
    public class SmbSession : IDisposable
    {
        public const int OperationTimeoutMs = 10000;
        private const int BulkOperationTimeoutMs = 300000;
        private const int MaxWriteSize = 65536;

        private readonly NetworkServerConfig _config;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private SMB2Client _client;
        private ISMBFileStore _fileStore;
        private string _connectedShare;
        private bool _invalid;

        internal SmbSession(NetworkServerConfig config)
        {
            _config = config;
        }

        public bool IsConnected => _client != null && _client.IsConnected;

        /// <summary>Negotiated SMB limits for logging/diagnostics. Never throws.</summary>
        public string NegotiatedInfo()
        {
            try
            {
                if (_fileStore == null) return "not connected";
                return $"MaxReadSize={_fileStore.MaxReadSize} MaxWriteSize={_fileStore.MaxWriteSize}";
            }
            catch
            {
                return "unavailable";
            }
        }

        public async Task EnsureConnectedAsync(string password, CancellationToken ct)
        {
            if (IsConnected && !_invalid) return;
            await RunAsync(() =>
            {
                var client = new SMB2Client(OperationTimeoutMs);
                bool connected = client.Connect(_config.Host, SMBTransportType.DirectTCPTransport);
                if (!connected)
                    throw new NetworkOperationException(NetworkOperationReason.Unreachable,
                        $"SMB connect failed: {_config.Host}");
                NTStatus login = client.Login(string.Empty, (_config.Username ?? "").Trim(), password ?? "");
                if (login != NTStatus.STATUS_SUCCESS)
                    throw ExceptionFromStatus(login, "login");
                _client = client;
                return true;
            }, "connect", ct);
        }

        public async Task<List<string>> ListSharesAsync(CancellationToken ct)
        {
            return await RunAsync(() =>
            {
                List<string> shares = _client.ListShares(out NTStatus status);
                if (status != NTStatus.STATUS_SUCCESS)
                    throw ExceptionFromStatus(status, "list shares");
                return shares ?? new List<string>();
            }, "list shares", ct);
        }

        public async Task<List<NetworkFileEntry>> ListDirectoryAsync(string share, string path, CancellationToken ct)
        {
            return await RunAsync(() =>
            {
                EnsureTree(share);
                string dirPath = NormalizePath(path);
                object handle;
                FileStatus fileStatus;
                NTStatus status = _fileStore.CreateFile(
                    out handle, out fileStatus, dirPath,
                    AccessMask.GENERIC_READ, FileAttributes.Directory,
                    ShareAccess.Read | ShareAccess.Write,
                    CreateDisposition.FILE_OPEN, CreateOptions.FILE_DIRECTORY_FILE, null);
                if (status != NTStatus.STATUS_SUCCESS)
                    throw ExceptionFromStatus(status, "open directory");
                try
                {
                    status = _fileStore.QueryDirectory(
                        out List<QueryDirectoryFileInformation> items, handle,
                        "*", FileInformationClass.FileDirectoryInformation);
                    if (status != NTStatus.STATUS_SUCCESS && status != NTStatus.STATUS_NO_MORE_FILES)
                        throw ExceptionFromStatus(status, "query directory");

                    var result = new List<NetworkFileEntry>();
                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            var fi = item as FileDirectoryInformation;
                            if (fi == null) continue;
                            if (fi.FileName == "." || fi.FileName == "..") continue;
                            result.Add(new NetworkFileEntry
                            {
                                Name = fi.FileName,
                                IsDirectory = fi.FileAttributes.HasFlag(FileAttributes.Directory),
                                Size = fi.EndOfFile,
                                LastWriteTime = fi.LastWriteTime
                            });
                        }
                    }
                    return result;
                }
                finally
                {
                    try { _fileStore.CloseFile(handle); } catch { }
                }
            }, "list directory", ct);
        }

        public async Task<SmbReadStream> OpenReadAsync(string share, string path, CancellationToken ct)
        {
            return await RunAsync(() =>
            {
                EnsureTree(share);
                object handle;
                FileStatus fileStatus;
                NTStatus status = _fileStore.CreateFile(
                    out handle, out fileStatus, NormalizePath(path),
                    AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE, FileAttributes.Normal,
                    ShareAccess.Read, CreateDisposition.FILE_OPEN,
                    CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT, null);
                if (status != NTStatus.STATUS_SUCCESS)
                    throw ExceptionFromStatus(status, "open file");

                long length = 0;
                try
                {
                    NTStatus lenStatus = _fileStore.GetFileInformation(
                        out FileInformation info, handle, FileInformationClass.FileStandardInformation);
                    if (lenStatus == NTStatus.STATUS_SUCCESS && info is FileStandardInformation std)
                        length = std.EndOfFile;
                }
                catch
                {
                    length = 0;
                }
                return new SmbReadStream(this, _fileStore, handle, length);
            }, "open file", ct);
        }

        public async Task<long> GetFileLengthAsync(string share, string path, CancellationToken ct)
        {
            return await RunAsync(() =>
            {
                EnsureTree(share);
                object handle;
                FileStatus fileStatus;
                NTStatus status = _fileStore.CreateFile(
                    out handle, out fileStatus, NormalizePath(path),
                    AccessMask.GENERIC_READ, FileAttributes.Normal,
                    ShareAccess.Read, CreateDisposition.FILE_OPEN,
                    CreateOptions.FILE_NON_DIRECTORY_FILE, null);
                if (status != NTStatus.STATUS_SUCCESS)
                    throw ExceptionFromStatus(status, "open file for length");
                try
                {
                    NTStatus lenStatus = _fileStore.GetFileInformation(
                        out FileInformation info, handle, FileInformationClass.FileStandardInformation);
                    if (lenStatus != NTStatus.STATUS_SUCCESS)
                        throw ExceptionFromStatus(lenStatus, "get file length");
                    if (!(info is FileStandardInformation std))
                        throw new NetworkOperationException(NetworkOperationReason.Unreachable,
                            "Unexpected file information class");
                    return std.EndOfFile;
                }
                finally
                {
                    try { _fileStore.CloseFile(handle); } catch { }
                }
            }, "get file length", ct);
        }

        /// <summary>
        /// True if a file (or directory when isDirectory) exists at the path.
        /// Used for copy/move collision detection before opening a write stream.
        /// </summary>
        public async Task<bool> EntryExistsAsync(string share, string path, bool isDirectory, CancellationToken ct)
        {
            return await RunAsync(() =>
            {
                EnsureTree(share);
                object handle;
                FileStatus fileStatus;
                var createOptions = isDirectory
                    ? CreateOptions.FILE_DIRECTORY_FILE
                    : CreateOptions.FILE_NON_DIRECTORY_FILE;
                NTStatus status = _fileStore.CreateFile(
                    out handle, out fileStatus, NormalizePath(path),
                    AccessMask.GENERIC_READ, FileAttributes.Normal,
                    ShareAccess.Read | ShareAccess.Write | ShareAccess.Delete,
                    CreateDisposition.FILE_OPEN, createOptions, null);
                if (status == NTStatus.STATUS_SUCCESS)
                {
                    try { _fileStore.CloseFile(handle); } catch { }
                    return true;
                }
                if (status == NTStatus.STATUS_OBJECT_NAME_NOT_FOUND ||
                    status == NTStatus.STATUS_OBJECT_PATH_NOT_FOUND ||
                    status == NTStatus.STATUS_NOT_A_DIRECTORY ||
                    status == NTStatus.STATUS_FILE_IS_A_DIRECTORY)
                    return false;
                throw ExceptionFromStatus(status, "check entry exists");
            }, "check entry exists", ct);
        }

        /// <summary>
        /// Writes all <paramref name="data"/> to the remote file, overwriting it
        /// (CREATE_ALWAYS semantics). Used for text-editor save-back and future
        /// write operations. Files are written in MaxWriteSize chunks.
        /// </summary>
        public async Task WriteFileAsync(string share, string path, byte[] data, CancellationToken ct)
        {
            await RunAsync(() =>
            {
                EnsureTree(share);
                object handle;
                FileStatus fileStatus;
                NTStatus status = _fileStore.CreateFile(
                    out handle, out fileStatus, NormalizePath(path),
                    AccessMask.GENERIC_WRITE | AccessMask.SYNCHRONIZE, FileAttributes.Normal,
                    ShareAccess.Read | ShareAccess.Write, CreateDisposition.FILE_OVERWRITE_IF,
                    CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT, null);
                if (status != NTStatus.STATUS_SUCCESS)
                    throw ExceptionFromStatus(status, "open file for write");
                try
                {
                    long offset = 0;
                    int maxChunk = (int)Math.Min((uint)MaxWriteSize, _fileStore.MaxWriteSize);
                    while (offset < data.Length)
                    {
                        int chunk = (int)Math.Min(maxChunk, data.Length - offset);
                        byte[] slice = new byte[chunk];
                        Array.Copy(data, (int)offset, slice, 0, chunk);
                        NTStatus wstatus = _fileStore.WriteFile(out int written, handle, offset, slice);
                        if (wstatus != NTStatus.STATUS_SUCCESS)
                            throw ExceptionFromStatus(wstatus, "write file");
                        offset += written;
                        if (written == 0)
                            throw new NetworkOperationException(NetworkOperationReason.Unreachable,
                                "zero-byte write over SMB");
                    }
                    try { _fileStore.FlushFileBuffers(handle); } catch { }
                }
                finally
                {
                    try { _fileStore.CloseFile(handle); } catch { }
                }
                return true;
            }, "write file", ct);
        }

        /// <summary>
        /// Opens a write-only stream that overwrites the remote file
        /// (CREATE_ALWAYS semantics). Used for copies; chunked at MaxWriteSize.
        /// </summary>
        public async Task<SmbWriteStream> OpenWriteStreamAsync(string share, string path, CancellationToken ct)
        {
            return await RunAsync(() =>
            {
                EnsureTree(share);
                object handle;
                FileStatus fileStatus;
                NTStatus status = _fileStore.CreateFile(
                    out handle, out fileStatus, NormalizePath(path),
                    AccessMask.GENERIC_WRITE | AccessMask.SYNCHRONIZE, FileAttributes.Normal,
                    ShareAccess.Read | ShareAccess.Write, CreateDisposition.FILE_OVERWRITE_IF,
                    CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT, null);
                if (status != NTStatus.STATUS_SUCCESS)
                    throw ExceptionFromStatus(status, "open file for write stream");
                return new SmbWriteStream(this, _fileStore, handle);
            }, "open write stream", ct);
        }

        /// <summary>Deletes a single remote file (DELETE access + disposition delete).</summary>
        public async Task DeleteFileAsync(string share, string path, CancellationToken ct)
        {
            await RunAsync(() =>
            {
                EnsureTree(share);
                DeleteFileCore(NormalizePath(path));
                return true;
            }, "delete file", ct);
        }

        /// <summary>
        /// Recursively deletes a remote directory. Runs under the gate for the
        /// whole walk with a generous timeout — large trees exceed the 10s
        /// operation timeout (which would invalidate the session mid-delete).
        /// </summary>
        public async Task DeleteDirectoryAsync(string share, string path, CancellationToken ct)
        {
            await RunAsync(() =>
            {
                EnsureTree(share);
                DeleteDirectoryCore(NormalizePath(path));
                return true;
            }, "delete directory", ct, BulkOperationTimeoutMs);
        }

        /// <summary>Renames a remote file or directory in place (same parent).</summary>
        public async Task RenameFileAsync(string share, string path, string newName, bool isDirectory, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(newName))
                throw new ArgumentException("New name is empty.", nameof(newName));
            await RunAsync(() =>
            {
                EnsureTree(share);
                object handle;
                FileStatus fileStatus;
                var createOptions = (isDirectory ? CreateOptions.FILE_DIRECTORY_FILE : CreateOptions.FILE_NON_DIRECTORY_FILE)
                    | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT;
                NTStatus status = _fileStore.CreateFile(
                    out handle, out fileStatus, NormalizePath(path),
                    AccessMask.DELETE | AccessMask.SYNCHRONIZE, isDirectory ? FileAttributes.Directory : FileAttributes.Normal,
                    ShareAccess.Read | ShareAccess.Write | ShareAccess.Delete,
                    CreateDisposition.FILE_OPEN, createOptions, null);
                if (status != NTStatus.STATUS_SUCCESS)
                    throw ExceptionFromStatus(status, "open for rename");
                try
                {
                    var info = new FileRenameInformationType2
                    {
                        ReplaceIfExists = false,
                        RootDirectory = 0,
                        FileName = NormalizePath(newName)
                    };
                    status = _fileStore.SetFileInformation(handle, info);
                    if (status != NTStatus.STATUS_SUCCESS)
                        throw ExceptionFromStatus(status, "rename file");
                }
                finally
                {
                    try { _fileStore.CloseFile(handle); } catch { }
                }
                return true;
            }, "rename file", ct);
        }

        /// <summary>Creates a remote directory (idempotent — succeeds if it exists).</summary>
        public async Task CreateDirectoryAsync(string share, string path, CancellationToken ct)
        {
            await RunAsync(() =>
            {
                EnsureTree(share);
                object handle;
                FileStatus fileStatus;
                NTStatus status = _fileStore.CreateFile(
                    out handle, out fileStatus, NormalizePath(path),
                    AccessMask.GENERIC_WRITE, FileAttributes.Directory,
                    ShareAccess.Read | ShareAccess.Write,
                    CreateDisposition.FILE_OPEN_IF, CreateOptions.FILE_DIRECTORY_FILE, null);
                if (status != NTStatus.STATUS_SUCCESS)
                    throw ExceptionFromStatus(status, "create directory");
                try { _fileStore.CloseFile(handle); } catch { }
                return true;
            }, "create directory", ct);
        }

        /// <summary>Closes the current tree connection (if any) and the TCP session.</summary>
        public void Disconnect()
        {
            try { _fileStore?.Disconnect(); } catch { }
            _fileStore = null;
            _connectedShare = null;
            try { _client?.Disconnect(); } catch { }
            _client = null;
        }

        public void Dispose() => Disconnect();

        /// <summary>
        /// Marks the session dead and removes it from the pool so the next
        /// acquire builds a fresh one. Called on operation timeout.
        /// </summary>
        internal void Invalidate()
        {
            _invalid = true;
            string key = NetworkUrl.Compose(_config);
            if (key != null)
                SmbSessionPool.Remove(key);
            Disconnect();
        }

        /// <summary>
        /// Runs a synchronous store operation (e.g. a stream read) under the
        /// session gate. Stream reads MUST be serialized with every other
        /// operation: SMB2FileStore is not thread-safe and concurrent in-flight
        /// commands corrupt the connection ("The client is no longer connected").
        /// </summary>
        internal T WithStoreLock<T>(Func<T> op)
        {
            _gate.Wait();
            try
            {
                if (_invalid || _fileStore == null)
                    throw new NetworkOperationException(NetworkOperationReason.Unreachable,
                        "Session is no longer connected");
                return op();
            }
            finally
            {
                _gate.Release();
            }
        }

        private void EnsureTree(string share)
        {
            if (_fileStore != null && _connectedShare == share) return;
            if (_fileStore != null)
            {
                try { _fileStore.Disconnect(); } catch { }
                _fileStore = null;
                _connectedShare = null;
            }
            ISMBFileStore store = _client.TreeConnect(share, out NTStatus status);
            if (status != NTStatus.STATUS_SUCCESS)
                throw ExceptionFromStatus(status, $"connect share {share}");
            _fileStore = store;
            _connectedShare = share;
        }

        /// <summary>Opens a file for delete and sets the delete-on-close disposition.</summary>
        private void DeleteFileCore(string path)
        {
            object handle;
            FileStatus fileStatus;
            NTStatus status = _fileStore.CreateFile(
                out handle, out fileStatus, path,
                AccessMask.DELETE | AccessMask.SYNCHRONIZE, FileAttributes.Normal,
                ShareAccess.Read | ShareAccess.Write | ShareAccess.Delete,
                CreateDisposition.FILE_OPEN,
                CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT, null);
            if (status != NTStatus.STATUS_SUCCESS)
                throw ExceptionFromStatus(status, "open file for delete");
            try
            {
                status = _fileStore.SetFileInformation(handle, new FileDispositionInformation { DeletePending = true });
                if (status != NTStatus.STATUS_SUCCESS)
                    throw ExceptionFromStatus(status, "delete file");
            }
            finally
            {
                try { _fileStore.CloseFile(handle); } catch { }
            }
        }

        /// <summary>Recursively deletes a remote directory (children first, then the dir).</summary>
        private void DeleteDirectoryCore(string path)
        {
            object dirHandle;
            FileStatus fileStatus;
            NTStatus status = _fileStore.CreateFile(
                out dirHandle, out fileStatus, path,
                AccessMask.GENERIC_READ, FileAttributes.Directory,
                ShareAccess.Read | ShareAccess.Write | ShareAccess.Delete,
                CreateDisposition.FILE_OPEN, CreateOptions.FILE_DIRECTORY_FILE, null);
            if (status != NTStatus.STATUS_SUCCESS)
                throw ExceptionFromStatus(status, "open directory for delete");
            try
            {
                status = _fileStore.QueryDirectory(
                    out List<QueryDirectoryFileInformation> items, dirHandle,
                    "*", FileInformationClass.FileDirectoryInformation);
                if (status != NTStatus.STATUS_SUCCESS && status != NTStatus.STATUS_NO_MORE_FILES)
                    throw ExceptionFromStatus(status, "query directory for delete");
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        var fi = item as FileDirectoryInformation;
                        if (fi == null) continue;
                        if (fi.FileName == "." || fi.FileName == "..") continue;
                        string child = JoinPath(path, fi.FileName);
                        if (fi.FileAttributes.HasFlag(FileAttributes.Directory))
                            DeleteDirectoryCore(child);
                        else
                            DeleteFileCore(child);
                    }
                }
            }
            finally
            {
                try { _fileStore.CloseFile(dirHandle); } catch { }
            }

            status = _fileStore.CreateFile(
                out dirHandle, out fileStatus, path,
                AccessMask.DELETE | AccessMask.SYNCHRONIZE, FileAttributes.Directory,
                ShareAccess.Read | ShareAccess.Write | ShareAccess.Delete,
                CreateDisposition.FILE_OPEN, CreateOptions.FILE_DIRECTORY_FILE, null);
            if (status != NTStatus.STATUS_SUCCESS)
                throw ExceptionFromStatus(status, "open directory handle for delete");
            try
            {
                status = _fileStore.SetFileInformation(dirHandle, new FileDispositionInformation { DeletePending = true });
                if (status != NTStatus.STATUS_SUCCESS)
                    throw ExceptionFromStatus(status, "delete directory");
            }
            finally
            {
                try { _fileStore.CloseFile(dirHandle); } catch { }
            }
        }

        private static string JoinPath(string dir, string name)
        {
            if (string.IsNullOrEmpty(dir)) return name;
            return dir.TrimEnd('\\') + "\\" + name;
        }

        private async Task<T> RunAsync<T>(Func<T> op, string what, CancellationToken ct, int timeoutMs = OperationTimeoutMs)
        {
            try
            {
                await _gate.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                throw new NetworkOperationException(NetworkOperationReason.Cancelled, $"Cancelled: {what}");
            }

            try
            {
                if (_invalid)
                    throw new NetworkOperationException(NetworkOperationReason.Unreachable, "Session was invalidated");

                // Run the op inside the task and capture any failure, so the task
                // itself never faults. An exception thrown inside Task.Run is
                // reported by the debugger as "not handled in user code" (the async
                // state machine continuation is external code) and pauses the app
                // even when the caller catches it further up the chain. Re-throwing
                // the captured failure below the task keeps every error handled.
                T result = default;
                Exception failure = null;
                var task = Task.Run(() =>
                {
                    try
                    {
                        result = op();
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }
                });

                var timeout = Task.Delay(timeoutMs);
                var completed = await Task.WhenAny(task, timeout);
                if (completed == timeout)
                {
                    Invalidate();
                    throw new NetworkOperationException(NetworkOperationReason.TimedOut, $"SMB operation timed out: {what}");
                }

                await task;
                if (failure != null)
                    ExceptionDispatchInfo.Capture(failure).Throw();
                return result;
            }
            catch (NetworkOperationException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw new NetworkOperationException(NetworkOperationReason.Cancelled, $"Cancelled: {what}");
            }
            catch (Exception ex)
            {
                throw new NetworkOperationException(NetworkOperationReason.Unreachable, $"SMB {what} failed", ex);
            }
            finally
            {
                _gate.Release();
            }
        }

        internal static NetworkOperationException ExceptionFromStatus(NTStatus status, string context)
        {
            var reason = MapStatus(status);
            return new NetworkOperationException(reason, $"SMB {context} failed ({status})");
        }

        internal static NetworkOperationReason MapStatus(NTStatus status)
        {
            if (status == NTStatus.STATUS_ACCESS_DENIED ||
                status == NTStatus.STATUS_SHARING_VIOLATION ||
                status == NTStatus.STATUS_PRIVILEGE_NOT_HELD ||
                status == NTStatus.STATUS_CANNOT_DELETE)
                return NetworkOperationReason.AccessDenied;

            if (status == NTStatus.STATUS_LOGON_FAILURE ||
                status == NTStatus.STATUS_WRONG_PASSWORD ||
                status == NTStatus.STATUS_ACCOUNT_RESTRICTION ||
                status == NTStatus.STATUS_INVALID_LOGON_HOURS ||
                status == NTStatus.STATUS_INVALID_WORKSTATION ||
                status == NTStatus.STATUS_PASSWORD_EXPIRED ||
                status == NTStatus.STATUS_ACCOUNT_DISABLED ||
                status == NTStatus.STATUS_ACCOUNT_LOCKED_OUT ||
                status == NTStatus.STATUS_LOGON_TYPE_NOT_GRANTED ||
                status == NTStatus.STATUS_ACCOUNT_EXPIRED ||
                status == NTStatus.STATUS_PASSWORD_MUST_CHANGE)
                return NetworkOperationReason.AuthFailed;

            if (status == NTStatus.STATUS_IO_TIMEOUT ||
                status == NTStatus.STATUS_REQUEST_NOT_ACCEPTED)
                return NetworkOperationReason.TimedOut;

            if (status == NTStatus.STATUS_OBJECT_NAME_NOT_FOUND ||
                status == NTStatus.STATUS_OBJECT_NAME_INVALID ||
                status == NTStatus.STATUS_OBJECT_PATH_NOT_FOUND ||
                status == NTStatus.STATUS_OBJECT_PATH_INVALID ||
                status == NTStatus.STATUS_OBJECT_PATH_SYNTAX_BAD ||
                status == NTStatus.STATUS_BAD_NETWORK_NAME ||
                status == NTStatus.STATUS_NO_SUCH_FILE ||
                status == NTStatus.STATUS_NOT_A_DIRECTORY ||
                status == NTStatus.STATUS_FILE_IS_A_DIRECTORY)
                return NetworkOperationReason.NotFound;

            return NetworkOperationReason.Unreachable;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            return path.Replace('/', '\\').Trim('\\');
        }
    }
}
