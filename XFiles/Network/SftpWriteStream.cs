using System;
using System.IO;

namespace XFiles.Network
{
    /// <summary>
    /// Write-only Stream over an SftpFileStream opened for overwrite.
    /// Every write/flush/close is serialized through the owning session's gate.
    /// </summary>
    public class SftpWriteStream : Stream
    {
        private readonly SftpSession _session;
        private readonly Stream _inner;
        private long _position;
        private bool _disposed;

        public SftpWriteStream(SftpSession session, Stream inner)
        {
            _session = session;
            _inner = inner;
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
            if (_disposed) throw new ObjectDisposedException(nameof(SftpWriteStream));
            if (count == 0) return;

            _session.WithGate(() => { _inner.Write(buffer, offset, count); return true; });
            _position += count;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void Flush()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SftpWriteStream));
            _session.WithGate(() => { _inner.Flush(); return true; });
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
                    _session.WithGate(() => { _inner.Flush(); return true; });
                }
                catch
                {
                    // Best-effort flush: the session gate may already be invalid.
                }
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
