using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace XFiles.Network
{
    /// <summary>
    /// Write stream that buffers data in memory and uploads via HTTP PUT
    /// on dispose. Used for WebDAV remote→remote copy where no local file
    /// path exists for WriteFileAsync.
    /// </summary>
    internal class WebDavWriteStream : MemoryStream
    {
        private readonly WebDavSession _session;
        private readonly string _url;
        private bool _uploaded;

        public WebDavWriteStream(WebDavSession session, string url)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _url = url ?? throw new ArgumentNullException(nameof(url));
        }

        public async Task UploadAsync(CancellationToken ct = default)
        {
            if (_uploaded) return;
            _uploaded = true;

            Position = 0;
            var content = new StreamContent(this);
            var request = new HttpRequestMessage(HttpMethod.Put, _url) { Content = content };
            HttpResponseMessage response;
            try
            {
                response = await _session.SendAsync(request, ct);
            }
            catch (HttpRequestException ex)
            {
                throw new NetworkOperationException(
                    NetworkOperationReason.Unreachable,
                    $"WebDAV PUT failed: {ex.Message}", ex);
            }

            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync();
                Log.Warn("WebDavWriteStream.Upload: PUT {Url} → {Status} {Body}",
                    _url, (int)response.StatusCode, body);
                throw new NetworkOperationException(
                    NetworkOperationReason.AccessDenied,
                    $"WebDAV PUT returned {(int)response.StatusCode}: {body}");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_uploaded && Length > 0)
            {
                UploadAsync().GetAwaiter().GetResult();
            }
            if (disposing)
            {
                _session?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
