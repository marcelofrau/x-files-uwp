using System;
using System.IO;

namespace XFiles.Network
{
    /// <summary>
    /// Read-only, seekable Stream over an SftpFileStream. SftpFileStream is
    /// natively seekable, so media seeking needs no reopen trick (unlike FTP).
    /// Every read is serialized through the owning session's gate — SftpClient
    /// is not thread-safe and concurrent commands corrupt the connection.
    /// </summary>
    public class SftpReadStream : Stream
    {
        private readonly SftpSession _session;
        private readonly Stream _inner;
        private bool _disposed;

        public SftpReadStream(SftpSession session, Stream inner, long length)
        {
            _session = session;
            _inner = inner;
            Length = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override bool CanTimeout => false;
        public override long Length { get; }
        public override long Position
        {
            get => _session.WithGate(() => _inner.Position);
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                _session.WithGate(() =>
                {
                    _inner.Seek(value, SeekOrigin.Begin);
                    return true;
                });
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset + count > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (_disposed) throw new ObjectDisposedException(nameof(SftpReadStream));
            return _session.WithGate(() => _inner.Read(buffer, offset, count));
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _session.WithGate(() => _inner.Seek(offset, origin));
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
                    _session.WithGate(() => { _inner.Dispose(); return true; });
                }
                catch
                {
                    // Best-effort close: the session gate may already be invalid.
                }
            }
            base.Dispose(disposing);
        }
    }
}
