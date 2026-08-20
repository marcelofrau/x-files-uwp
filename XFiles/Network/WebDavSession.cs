using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace XFiles.Network
{
    /// <summary>
    /// Stateless WebDAV session backed by HttpClient. No session pool needed —
    /// HTTP is request/response, and HttpClient handles connection pooling
    /// internally. All operations map to standard HTTP verbs plus the WebDAV
    /// extensions PROPFIND, MKCOL, and MOVE.
    /// </summary>
    public class WebDavSession : IDisposable
    {
        private static readonly HttpMethod PROPFIND_METHOD = new HttpMethod("PROPFIND");
        private static readonly HttpMethod MKCOL_METHOD = new HttpMethod("MKCOL");

        private static readonly XNamespace D = "DAV:";

        private const int TimeoutSeconds = 30;

        private readonly HttpClient _client;
        private readonly string _baseUrl;

        public WebDavSession(NetworkServerConfig config, string password)
        {
            var handler = new HttpClientHandler
            {
                Credentials = new NetworkCredential(config.Username ?? "", password ?? ""),
                ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => true
            };
            _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) };

            int port = config.EffectivePort;
            string scheme = (port == 443 || config.Protocol == NetworkProtocol.Webdav && config.Port == 0
                && port != 80) ? "https" : "http";
            _baseUrl = $"{scheme}://{config.Host}:{port}";
        }

        /// <summary>
        /// Sends an HTTP request using this session's client. Used by
        /// <see cref="WebDavWriteStream"/> for PUT operations that need
        /// the session's credentials and certificate handling.
        /// </summary>
        internal Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => _client.SendAsync(request, ct);

        /// <summary>
        /// Effective path for FTP/SFTP/WebDAV (no share layer — share is the start folder).
        /// </summary>
        public static string EffectivePath(string share, string path)
        {
            return string.IsNullOrEmpty(path) ? (share ?? "") : path;
        }

        /// <summary>
        /// Normalizes a remote path to have a leading slash and no trailing slash.
        /// </summary>
        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "/";
            if (!path.StartsWith("/")) path = "/" + path;
            return path.TrimEnd('/');
        }

        /// <summary>
        /// Full URL for a remote path.
        /// </summary>
        private string Url(string path) => _baseUrl + NormalizePath(path);

        // ─────────────────────── TestConnection ───────────────────────

        public async Task<string> TestConnectionAsync(string path, CancellationToken ct)
        {
            var entries = await ListDirectoryAsync(path, ct);
            return $"Connected — {entries.Count} item(s) in \"{NormalizePath(path)}\".";
        }

        // ─────────────────────── ListDirectory ───────────────────────

        public async Task<List<NetworkFileEntry>> ListDirectoryAsync(string path, CancellationToken ct)
        {
            string url = Url(path);
            var request = new HttpRequestMessage(PROPFIND_METHOD, url);
            request.Headers.Add("Depth", "1");

            HttpResponseMessage response;
            try
            {
                response = await _client.SendAsync(request, ct);
            }
            catch (TaskCanceledException ex)
            {
                throw new NetworkOperationException(NetworkOperationReason.TimedOut, "WebDAV request timed out", ex);
            }
            catch (HttpRequestException ex)
            {
                throw MapHttpError(0, url, ex);
            }

            if (!response.IsSuccessStatusCode)
            {
                request.Dispose();
                throw MapHttpError((int)response.StatusCode, url);
            }

            string xml = await response.Content.ReadAsStringAsync();
            request.Dispose();
            response.Dispose();

            return ParsePropfindListing(xml, path);
        }

        /// <summary>
        /// Parses a PROPFIND Depth:1 XML response into NetworkFileEntry list.
        /// Extractable as a pure helper for unit testing.
        /// </summary>
        public static List<NetworkFileEntry> ParsePropfindListing(string xml, string parentPath)
        {
            var result = new List<NetworkFileEntry>();
            var doc = XDocument.Parse(xml);
            string parentNorm = NormalizePath(parentPath);

            foreach (var resp in doc.Descendants(D + "response"))
            {
                string href = resp.Element(D + "href")?.Value;
                if (string.IsNullOrEmpty(href)) continue;

                // Decode URL-encoded href and normalize
                string decoded;
                try { decoded = Uri.UnescapeDataString(href); }
                catch { decoded = href; }
                if (!decoded.StartsWith("/")) decoded = "/" + decoded;
                decoded = decoded.TrimEnd('/');

                // Skip the parent directory itself
                if (decoded == parentNorm) continue;

                // Extract filename from the last path segment
                string name;
                int lastSlash = decoded.LastIndexOf('/');
                name = lastSlash >= 0 ? decoded.Substring(lastSlash + 1) : decoded;
                if (string.IsNullOrEmpty(name)) continue;

                var prop = resp.Element(D + "propstat")?.Element(D + "prop");
                if (prop == null) continue;

                bool isDir = prop.Element(D + "resourcetype")?.Element(D + "collection") != null;

                long size = 0;
                string lenStr = prop.Element(D + "getcontentlength")?.Value;
                if (!string.IsNullOrEmpty(lenStr) && long.TryParse(lenStr, out long parsed))
                    size = parsed;

                DateTime lastMod = DateTime.MinValue;
                string modStr = prop.Element(D + "getlastmodified")?.Value;
                if (!string.IsNullOrEmpty(modStr))
                {
                    if (DateTime.TryParse(modStr, System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out DateTime parsedMod))
                        lastMod = parsedMod;
                }

                result.Add(new NetworkFileEntry
                {
                    Name = name,
                    IsDirectory = isDir,
                    Size = size,
                    LastWriteTime = lastMod
                });
            }

            return result;
        }

        // ─────────────────────── OpenRead ───────────────────────

        public async Task<WebDavReadStream> OpenReadAsync(string path, CancellationToken ct)
        {
            // Get file size first via HEAD or PROPFIND
            long size = await GetFileLengthAsync(path, ct);

            string url = Url(path);
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Range", "bytes=0-");

            HttpResponseMessage response;
            try
            {
                response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (TaskCanceledException ex)
            {
                request.Dispose();
                throw new NetworkOperationException(NetworkOperationReason.TimedOut, "WebDAV GET timed out", ex);
            }
            catch (HttpRequestException ex)
            {
                request.Dispose();
                throw MapHttpError(0, url, ex);
            }

            if (!response.IsSuccessStatusCode)
            {
                request.Dispose();
                throw MapHttpError((int)response.StatusCode, url);
            }

            Stream stream = await response.Content.ReadAsStreamAsync();
            request.Dispose();

            // If server returned 200 (full) instead of 206 (partial), the stream is the whole file
            bool isPartial = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
            response.Dispose();

            return new WebDavReadStream(stream, _client, Url(path), size, isPartial, this);
        }

        // ─────────────────────── GetFileLength ───────────────────────

        public async Task<long> GetFileLengthAsync(string path, CancellationToken ct)
        {
            string url = Url(path);
            var request = new HttpRequestMessage(HttpMethod.Head, url);

            HttpResponseMessage response;
            try
            {
                response = await _client.SendAsync(request, ct);
            }
            catch (TaskCanceledException ex)
            {
                request.Dispose();
                throw new NetworkOperationException(NetworkOperationReason.TimedOut, "WebDAV HEAD timed out", ex);
            }
            catch (HttpRequestException ex)
            {
                request.Dispose();
                throw MapHttpError(0, url, ex);
            }

            long length = 0;
            if (response.Content.Headers.ContentLength.HasValue)
                length = response.Content.Headers.ContentLength.Value;

            request.Dispose();
            response.Dispose();
            return length;
        }

        // ─────────────────────── EntryExists ───────────────────────

        public async Task<bool> EntryExistsAsync(string path, CancellationToken ct)
        {
            string url = Url(path);
            var request = new HttpRequestMessage(HttpMethod.Head, url);

            HttpResponseMessage response;
            try
            {
                response = await _client.SendAsync(request, ct);
            }
            catch (TaskCanceledException)
            {
                request.Dispose();
                return false;
            }
            catch (HttpRequestException)
            {
                request.Dispose();
                return false;
            }

            bool exists = response.IsSuccessStatusCode;
            request.Dispose();
            response.Dispose();
            return exists;
        }

        // ─────────────────────── WriteFile ───────────────────────

        public async Task WriteFileAsync(string path, string localPath, CancellationToken ct)
        {
            string url = Url(path);
            using (var fileStream = File.OpenRead(localPath))
            {
                var content = new StreamContent(fileStream);
                var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = content };

                HttpResponseMessage response;
                try
                {
                    response = await _client.SendAsync(request, ct);
                }
                catch (TaskCanceledException ex)
                {
                    throw new NetworkOperationException(NetworkOperationReason.TimedOut, "WebDAV PUT timed out", ex);
                }
                catch (HttpRequestException ex)
                {
                    throw MapHttpError(0, url, ex);
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw MapHttpError((int)response.StatusCode, url);
                }
                response.Dispose();
            }
        }

        // ─────────────────────── OpenWriteStream ───────────────────────

        /// <summary>
        /// Opens a remote path for writing via PUT. Returns a <see cref="WebDavWriteStream"/>
        /// that buffers data in memory and uploads on dispose. Used by the remote→remote
        /// copy path where no local file path exists for <see cref="WriteFileAsync"/>.
        /// </summary>
        public Stream OpenWriteStreamAsync(string path)
        {
            return new WebDavWriteStream(this, Url(path));
        }

        /// <summary>
        /// Uploads a previously written MemoryStream to the remote path.
        /// Called after OpenWriteStreamAsync + writes are done.
        /// </summary>
        public async Task CommitWriteStreamAsync(string path, Stream stream, CancellationToken ct)
        {
            string url = Url(path);
            stream.Position = 0;
            var content = new StreamContent(stream);
            var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = content };

            HttpResponseMessage response;
            try
            {
                response = await _client.SendAsync(request, ct);
            }
            catch (TaskCanceledException ex)
            {
                throw new NetworkOperationException(NetworkOperationReason.TimedOut, "WebDAV PUT timed out", ex);
            }
            catch (HttpRequestException ex)
            {
                throw MapHttpError(0, url, ex);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw MapHttpError((int)response.StatusCode, url);
            }
            response.Dispose();
        }

        // ─────────────────────── DeleteFile ───────────────────────

        public async Task DeleteFileAsync(string path, CancellationToken ct)
        {
            string url = Url(path);
            var request = new HttpRequestMessage(HttpMethod.Delete, url);

            HttpResponseMessage response;
            try
            {
                response = await _client.SendAsync(request, ct);
            }
            catch (TaskCanceledException ex)
            {
                throw new NetworkOperationException(NetworkOperationReason.TimedOut, "WebDAV DELETE timed out", ex);
            }
            catch (HttpRequestException ex)
            {
                throw MapHttpError(0, url, ex);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw MapHttpError((int)response.StatusCode, url);
            }
            request.Dispose();
            response.Dispose();
        }

        // ─────────────────────── DeleteDirectory ───────────────────────

        public async Task DeleteDirectoryAsync(string path, CancellationToken ct)
        {
            string url = Url(path);
            var request = new HttpRequestMessage(HttpMethod.Delete, url);

            HttpResponseMessage response;
            try
            {
                response = await _client.SendAsync(request, ct);
            }
            catch (TaskCanceledException ex)
            {
                throw new NetworkOperationException(NetworkOperationReason.TimedOut, "WebDAV DELETE timed out", ex);
            }
            catch (HttpRequestException ex)
            {
                throw MapHttpError(0, url, ex);
            }

            // 409 Conflict = non-empty directory, need to recurse
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                request.Dispose();
                response.Dispose();
                await DeleteDirectoryRecursiveAsync(path, ct);
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw MapHttpError((int)response.StatusCode, url);
            }
            request.Dispose();
            response.Dispose();
        }

        private async Task DeleteDirectoryRecursiveAsync(string path, CancellationToken ct)
        {
            var entries = await ListDirectoryAsync(path, ct);
            foreach (var entry in entries)
            {
                string childPath = NormalizePath(path) + "/" + entry.Name;
                if (entry.IsDirectory)
                    await DeleteDirectoryAsync(childPath, ct);
                else
                    await DeleteFileAsync(childPath, ct);
            }

            // Now delete the empty directory
            string url = Url(path);
            var request = new HttpRequestMessage(HttpMethod.Delete, url);
            HttpResponseMessage response;
            try { response = await _client.SendAsync(request, ct); }
            catch (TaskCanceledException ex)
            {
                throw new NetworkOperationException(NetworkOperationReason.TimedOut, "WebDAV DELETE timed out", ex);
            }
            catch (HttpRequestException ex)
            {
                throw MapHttpError(0, url, ex);
            }
            if (!response.IsSuccessStatusCode)
                throw MapHttpError((int)response.StatusCode, url);
            request.Dispose();
            response.Dispose();
        }

        // ─────────────────────── RenameFile ───────────────────────

        public async Task RenameFileAsync(string path, string newName, CancellationToken ct)
        {
            string parentPath = NormalizePath(path);
            int lastSlash = parentPath.LastIndexOf('/');
            string destPath = lastSlash >= 0
                ? parentPath.Substring(0, lastSlash) + "/" + newName
                : "/" + newName;

            string url = Url(path);
            var request = new HttpRequestMessage(new HttpMethod("MOVE"), url);
            request.Headers.Add("Destination", Url(destPath));
            request.Headers.Add("Overwrite", "T");

            HttpResponseMessage response;
            try
            {
                response = await _client.SendAsync(request, ct);
            }
            catch (TaskCanceledException ex)
            {
                throw new NetworkOperationException(NetworkOperationReason.TimedOut, "WebDAV MOVE timed out", ex);
            }
            catch (HttpRequestException ex)
            {
                throw MapHttpError(0, url, ex);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw MapHttpError((int)response.StatusCode, url);
            }
            request.Dispose();
            response.Dispose();
        }

        // ─────────────────────── CreateDirectory ───────────────────────

        public async Task CreateDirectoryAsync(string path, CancellationToken ct)
        {
            string url = Url(path);
            var request = new HttpRequestMessage(MKCOL_METHOD, url);

            HttpResponseMessage response;
            try
            {
                response = await _client.SendAsync(request, ct);
            }
            catch (TaskCanceledException ex)
            {
                throw new NetworkOperationException(NetworkOperationReason.TimedOut, "WebDAV MKCOL timed out", ex);
            }
            catch (HttpRequestException ex)
            {
                throw MapHttpError(0, url, ex);
            }

            // 405 = Method Not Allowed = already exists
            if (response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
            {
                request.Dispose();
                response.Dispose();
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw MapHttpError((int)response.StatusCode, url);
            }
            request.Dispose();
            response.Dispose();
        }

        // ─────────────────────── Error mapping ───────────────────────

        private static NetworkOperationException MapHttpError(int statusCode, string url, Exception inner = null)
        {
            NetworkOperationReason reason;
            string message;

            switch (statusCode)
            {
                case 401:
                    reason = NetworkOperationReason.AuthFailed;
                    message = "Authentication failed";
                    break;
                case 403:
                    reason = NetworkOperationReason.AccessDenied;
                    message = "Access denied";
                    break;
                case 404:
                    reason = NetworkOperationReason.NotFound;
                    message = "Not found";
                    break;
                case 0:
                    reason = NetworkOperationReason.Unreachable;
                    message = "Connection failed";
                    break;
                default:
                    reason = NetworkOperationReason.Unreachable;
                    message = $"HTTP {statusCode}";
                    break;
            }

            if (inner != null)
                return new NetworkOperationException(reason, $"WebDAV {message} ({url})", inner);
            return new NetworkOperationException(reason, $"WebDAV {message} ({url})");
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}
