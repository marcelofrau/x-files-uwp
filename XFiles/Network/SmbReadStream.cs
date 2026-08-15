using System;
using System.IO;
using SMBLibrary;
using SMBLibrary.Client;

namespace XFiles.Network
{
    /// <summary>
    /// Read-only, seekable Stream backed by an SMB file handle. Reads are
    /// offset-based SMB ReadFile calls chunked at the negotiated MaxReadSize.
    /// CloseFile runs on Dispose. Every store call is serialized through the
    /// owning session's gate — SMB2FileStore is not thread-safe and concurrent
    /// in-flight commands corrupt the connection.
    /// </summary>
    public class SmbReadStream : Stream
    {
        private readonly SmbSession _session;
        private readonly ISMBFileStore _store;
        private readonly object _handle;
        private long _position;
        private bool _disposed;

        public SmbReadStream(SmbSession session, ISMBFileStore store, object handle, long length)
        {
            _session = session;
            _store = store;
            _handle = handle;
            Length = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override bool CanTimeout => false;
        public override long Length { get; }
        public override long Position
        {
            get => _position;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                _position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset + count > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (_disposed) throw new ObjectDisposedException(nameof(SmbReadStream));
            if (_position >= Length) return 0;

            int max = (int)Math.Min(count, (int)_store.MaxReadSize);
            if (max <= 0) return 0;

            byte[] data = null;
            NTStatus status = _session.WithStoreLock(() =>
                _store.ReadFile(out data, _handle, _position, max));
            if (status == NTStatus.STATUS_END_OF_FILE) return 0;
            if (status != NTStatus.STATUS_SUCCESS)
                throw SmbSession.ExceptionFromStatus(status, "read file");
            if (data == null || data.Length == 0) return 0;

            Array.Copy(data, 0, buffer, offset, data.Length);
            _position += data.Length;
            return data.Length;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long target;
            switch (origin)
            {
                case SeekOrigin.Begin: target = offset; break;
                case SeekOrigin.Current: target = _position + offset; break;
                case SeekOrigin.End: target = Length + offset; break;
                default: throw new ArgumentOutOfRangeException(nameof(origin));
            }
            if (target < 0) throw new IOException("Seek before start of stream.");
            _position = target;
            return _position;
        }

        public override void Flush() { }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;
            if (disposing)
            {
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
                    Log.Dbg($"SmbReadStream.Dispose: CloseFile failed ({ex.Message})");
                }
            }
            base.Dispose(disposing);
        }
    }
}
