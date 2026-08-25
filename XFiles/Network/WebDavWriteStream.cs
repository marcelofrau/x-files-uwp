using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace XFiles.Network
{
    /// <summary>
    /// Temp-file-backed write stream for WebDAV remote→remote copies. Data is
    /// streamed to a temp file on disk (no memory blowup), then uploaded via
    /// HTTP PUT on dispose. This avoids buffering the entire file in a
    /// MemoryStream while still providing a Stream API for CopyStreamAsync.
    /// </summary>
    internal class WebDavWriteStream : Stream
    {
        private readonly WebDavSession _session;
        private readonly string _url;
        private readonly FileStream _tempFile;
        private readonly string _tempPath;
        private bool _uploaded;

        public WebDavWriteStream(WebDavSession session, string url)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _url = url ?? throw new ArgumentNullException(nameof(url));
            _tempPath = Path.Combine(Path.GetTempPath(), "xfiles_webdav_" + Guid.NewGuid().ToString("N") + ".tmp");
            _tempFile = new FileStream(_tempPath, FileMode.Create, FileAccess.ReadWrite,
                FileShare.None, 64 * 1024, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _tempFile.Length;

        public override long Position
        {
            get => _tempFile.Position;
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _tempFile.Write(buffer, offset, count);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _tempFile.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override void Flush() { _tempFile.Flush(); }
        public override Task FlushAsync(CancellationToken cancellationToken) => _tempFile.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    _tempFile.Flush();
                    long length = _tempFile.Length;
                    _tempFile.Dispose(); // close before upload

                    if (length > 0 && !_uploaded)
                    {
                        UploadSync();
                    }
                }
                catch (Exception ex)
                {
                    Log.Err("WebDavWriteStream.Dispose: failed for {Url}", ex, _url);
                }
                finally
                {
                    TryDeleteTemp();
                    _session?.Dispose();
                }
            }
        }

        private void UploadSync()
        {
            _uploaded = true;
            using (var fs = new FileStream(_tempPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var content = new StreamContent(fs);
                var request = new HttpRequestMessage(HttpMethod.Put, _url) { Content = content };
                HttpResponseMessage response;
                try
                {
                    response = _session.SendAsync(request, CancellationToken.None)
                        .GetAwaiter().GetResult();
                }
                catch (HttpRequestException ex)
                {
                    throw new NetworkOperationException(
                        NetworkOperationReason.Unreachable,
                        $"WebDAV PUT failed: {ex.Message}", ex);
                }

                if (!response.IsSuccessStatusCode)
                {
                    string body = response.Content.ReadAsStringAsync()
                        .GetAwaiter().GetResult();
                    Log.Warn("WebDavWriteStream.Upload: PUT {Url} → {Status} {Body}",
                        _url, (int)response.StatusCode, body);
                    throw new NetworkOperationException(
                        NetworkOperationReason.AccessDenied,
                        $"WebDAV PUT returned {(int)response.StatusCode}: {body}");
                }
                response.Dispose();
            }
        }

        private void TryDeleteTemp()
        {
            try { File.Delete(_tempPath); }
            catch { /* FileOptions.DeleteOnClose handles cleanup best-effort */ }
        }
    }
}
