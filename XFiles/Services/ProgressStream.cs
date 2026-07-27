using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace XFiles.Services
{
    /// <summary>
    /// Stream wrapper that reports bytes read via a callback.
    /// Used to track upload progress when streaming from disk to HttpClient.
    /// </summary>
    internal class ProgressStream : Stream
    {
        private readonly Stream _inner;
        private readonly Action<long> _onBytesRead;
        private long _totalBytesRead;

        public long TotalBytesRead => Interlocked.Read(ref _totalBytesRead);

        public ProgressStream(Stream inner, Action<long> onBytesRead)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _onBytesRead = onBytesRead;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = _inner.Read(buffer, offset, count);
            if (read > 0)
            {
                Interlocked.Add(ref _totalBytesRead, read);
                _onBytesRead?.Invoke(read);
            }
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int read = await _inner.ReadAsync(buffer, offset, count, cancellationToken);
            if (read > 0)
            {
                Interlocked.Add(ref _totalBytesRead, read);
                _onBytesRead?.Invoke(read);
            }
            return read;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
