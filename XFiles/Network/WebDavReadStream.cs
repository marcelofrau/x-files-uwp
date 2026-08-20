using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace XFiles.Network
{
    /// <summary>
    /// A seekable read stream over HTTP with Range header support. Each Seek
    /// issues a new GET with a Range header from the target offset, enabling
    /// MediaSource.CreateFromStream probing (read + clone + seek) without
    /// deadlocks. Unlike FTP data connections, HTTP Range requests reuse the
    /// same TCP connection via HttpClient's connection pool.
    /// </summary>
    public sealed class WebDavReadStream : Stream
    {
        private readonly HttpClient _client;
        private readonly WebDavSession _session;
        private readonly string _url;
        private readonly long _totalSize;
        private Stream _responseStream;
        private HttpResponseMessage _currentResponse;
        private long _position;
        private bool _disposed;

        /// <summary>
        /// Creates a WebDAV read stream backed by HTTP Range requests.
        /// </summary>
        /// <param name="initialStream">The initial response stream from the first GET.</param>
        /// <param name="client">HttpClient for subsequent Range requests (seek/reopen).</param>
        /// <param name="url">Full URL of the resource.</param>
        /// <param name="totalSize">File size from PROPFIND/HEAD (used for Length).</param>
        /// <param name="isPartialResponse">True if initialStream is from a 206 Partial response.</param>
        public WebDavReadStream(Stream initialStream, HttpClient client, string url,
            long totalSize, bool isPartialResponse, WebDavSession session)
        {
            _responseStream = initialStream ?? throw new ArgumentNullException(nameof(initialStream));
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _url = url ?? throw new ArgumentNullException(nameof(url));
            _totalSize = totalSize;
            _position = 0;
            _session = session;
        }

        public override bool CanRead => !_disposed;
        public override bool CanSeek => !_disposed;
        public override bool CanWrite => false;
        public override long Length => _totalSize;
        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ThrowIfDisposed();
            EnsureResponseOpen();
            if (_responseStream == null || !_responseStream.CanRead)
                return 0;

            int bytesRead = _responseStream.Read(buffer, offset, count);
            _position += bytesRead;
            return bytesRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            ThrowIfDisposed();

            long target;
            switch (origin)
            {
                case SeekOrigin.Begin:
                    target = offset;
                    break;
                case SeekOrigin.Current:
                    target = _position + offset;
                    break;
                case SeekOrigin.End:
                    target = _totalSize + offset;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(origin));
            }

            if (target < 0) target = 0;
            if (target == _position) return _position;

            // Close the current response and open a new one at the target position
            CloseCurrentResponse();
            _position = target;
            return _position;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException("WebDavReadStream is read-only.");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("WebDavReadStream is read-only.");
        }

        /// <summary>
        /// Ensures a response stream is open at the current position.
        /// Called lazily on the next Read after a Seek or at stream start.
        /// </summary>
        private void EnsureResponseOpen()
        {
            if (_responseStream != null && _responseStream.CanRead)
                return;

            var request = new HttpRequestMessage(HttpMethod.Get, _url);
            request.Headers.Add("Range", $"bytes={_position}-");

            HttpResponseMessage response;
            try
            {
                response = _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                request.Dispose();
                throw new IOException("WebDAV Range request failed", ex);
            }

            if (!response.IsSuccessStatusCode)
            {
                request.Dispose();
                throw new IOException($"WebDAV Range request returned {(int)response.StatusCode}");
            }

            _currentResponse = response;
            _responseStream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
            request.Dispose();
        }

        private void CloseCurrentResponse()
        {
            try { _responseStream?.Dispose(); } catch { }
            try { _currentResponse?.Dispose(); } catch { }
            _responseStream = null;
            _currentResponse = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;
            if (disposing)
            {
                CloseCurrentResponse();
                _session?.Dispose();
            }
            base.Dispose(disposing);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WebDavReadStream));
        }
    }
}
