using System;
using System.IO;
using SMBLibrary;
using SMBLibrary.Client;

namespace XFiles.Network
{
    /// <summary>
    /// Write-only Stream backed by an SMB file handle opened for overwrite.
    /// Writes are offset-based SMB WriteFile calls chunked at the negotiated
    /// MaxWriteSize. Dispose flushes buffers then closes the handle. Every store
    /// call is serialized through the owning session's gate — SMB2FileStore is
    /// not thread-safe and concurrent in-flight commands corrupt the connection.
    /// </summary>
    public class SmbWriteStream : Stream
    {
        private readonly SmbSession _session;
        private readonly ISMBFileStore _store;
        private readonly object _handle;
        private long _position;
        private bool _disposed;

        public SmbWriteStream(SmbSession session, ISMBFileStore store, object handle)
        {
            _session = session;
            _store = store;
            _handle = handle;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override bool CanTimeout => false;
        public override long Length => _position;
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset + count > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (_disposed) throw new ObjectDisposedException(nameof(SmbWriteStream));
            if (count == 0) return;

            int max = (int)Math.Min(count, _store.MaxWriteSize);
            if (max <= 0) max = count;

            // SMBLibrary's WriteFile writes the whole byte[] at the given file
            // offset, so each chunk is copied into a dedicated array.
            int written = 0;
            while (written < count)
            {
                int chunk = Math.Min(max, count - written);
                var chunkData = new byte[chunk];
                Array.Copy(buffer, offset + written, chunkData, 0, chunk);
                int n = 0;
                NTStatus status = _session.WithStoreLock(() =>
                    _store.WriteFile(out n, _handle, _position, chunkData));
                if (status != NTStatus.STATUS_SUCCESS)
                    throw SmbSession.ExceptionFromStatus(status, "write file");
                if (n <= 0)
                    throw new IOException("SMB write made no progress.");
                written += n;
                _position += n;
            }
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void Flush()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SmbWriteStream));
            NTStatus status = _session.WithStoreLock(() => _store.FlushFileBuffers(_handle));
            if (status != NTStatus.STATUS_SUCCESS)
                throw SmbSession.ExceptionFromStatus(status, "flush file");
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;
            if (disposing)
            {
                try
                {
                    _session.WithStoreLock(() => _store.FlushFileBuffers(_handle));
                }
                catch (Exception ex)
                {
                    Log.Dbg($"SmbWriteStream.Dispose: flush failed ({ex.Message})");
                }
                try
                {
                    _session.WithStoreLock(() =>
                    {
                        _store.CloseFile(_handle);
                        return true;
                    });
                }
                catch (Exception ex)
                {
                    Log.Dbg($"SmbWriteStream.Dispose: CloseFile failed ({ex.Message})");
                }
            }
            base.Dispose(disposing);
        }
    }
}
