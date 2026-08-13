using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using XFiles.FileSystem;

namespace XFiles.Services
{
    /// <summary>
    /// Download files from URLs into a local folder. Phase-1 resolvers rewrite
    /// well-known provider links (Google Drive, OneDrive, Dropbox, gofile) to direct
    /// download URLs; anything else falls back to the raw URL. Every candidate is
    /// probed: HTML/JSON responses mean the file is behind a page, so the caller opens
    /// the WebView overlay (UrlDownloadOverlay) for a manual click-through. Binary
    /// responses are streamed straight to disk with progress + cancellation.
    /// Files are written via Win32FileWriteStream (CreateFileFromAppW) so downloads
    /// work on external drives on Xbox, mirroring FileOperations/FileShareService.
    /// </summary>
    internal static class DownloadService
    {
        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(2)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
            return client;
        }

        public enum DownloadOutcome
        {
            Downloaded,
            NeedsBrowser,
            Canceled,
            Failed
        }

        public sealed class DownloadResult
        {
            public DownloadOutcome Outcome;
            public string SavedPath;
            public string Error;
        }

        /// <summary>
        /// Resolve a provider URL to a direct-download candidate. Returns null when
        /// no confident rewrite exists — the caller then probes the original URL.
        /// </summary>
        public static async Task<string> ResolveAsync(string url, CancellationToken token)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url)) return null;

                if (IsMega(url))
                {
                    Log.Info("DownloadService: Mega link — WebView only");
                    return null;
                }

                if (TryResolveGoogleDrive(url, out var gdrive)) return gdrive;
                if (TryResolveOneDrive(url, out var onedrive)) return onedrive;
                if (TryResolveDropbox(url, out var dropbox)) return dropbox;
                if (TryGetGofileCode(url, out var gofileCode))
                {
                    var gofile = await ResolveGofileDirectAsync(gofileCode, token);
                    if (!string.IsNullOrEmpty(gofile)) return gofile;
                    Log.Warn("DownloadService: gofile API resolve failed — WebView fallback");
                    return null;
                }

                return null;
            }
            catch (Exception ex)
            {
                Log.Warn("DownloadService.ResolveAsync: {Error}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Stream-download a URL into destDir. Probes the response: HTML or JSON
        /// content means the real file is behind a page → NeedsBrowser. Otherwise
        /// streams bytes to disk, resolving the filename from Content-Disposition,
        /// then the URL path, then a fallback.
        /// </summary>
        public static async Task<DownloadResult> TryDownloadAsync(
            string url,
            string destDir,
            Action<long, long> progress,
            CancellationToken token)
        {
            string destPath = null;
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                var resp = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);

                if (!resp.IsSuccessStatusCode)
                {
                    Log.Warn("DownloadService: HTTP {Code} for {Url}", (int)resp.StatusCode, url);
                    return new DownloadResult
                    {
                        Outcome = DownloadOutcome.Failed,
                        Error = $"HTTP {(int)resp.StatusCode}"
                    };
                }

                string contentType = resp.Content.Headers.ContentType?.MediaType ?? "";
                if (contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) ||
                    contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Info("DownloadService: {Url} returned {Type} — browser needed", url, contentType);
                    return new DownloadResult { Outcome = DownloadOutcome.NeedsBrowser };
                }

                string fileName = ResolveFileName(url, resp);
                destPath = GetUniquePath(Path.Combine(destDir, fileName));

                long totalBytes = resp.Content.Headers.ContentLength ?? -1;

                using (var inStream = await resp.Content.ReadAsStreamAsync())
                using (var outStream = Win32FileWriteStream.Create(destPath))
                {
                    if (outStream == null)
                    {
                        Log.Warn("DownloadService: cannot create {Path}", destPath);
                        return new DownloadResult
                        {
                            Outcome = DownloadOutcome.Failed,
                            Error = "Cannot create destination file."
                        };
                    }

                    byte[] buffer = new byte[128 * 1024];
                    long copied = 0;
                    int read;
                    while ((read = await inStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                    {
                        outStream.Write(buffer, 0, read);
                        copied += read;
                        progress?.Invoke(copied, totalBytes);
                    }
                    Log.Info("DownloadService: downloaded {Url} → {Path} ({Bytes} bytes)", url, destPath, copied);
                }

                return new DownloadResult { Outcome = DownloadOutcome.Downloaded, SavedPath = destPath };
            }
            catch (OperationCanceledException)
            {
                Log.Info("DownloadService: cancelled {Url}", url);
                DeletePartial(destPath);
                return new DownloadResult { Outcome = DownloadOutcome.Canceled };
            }
            catch (Exception ex)
            {
                Log.Err("DownloadService: failed {Url}", ex, url);
                DeletePartial(destPath);
                return new DownloadResult { Outcome = DownloadOutcome.Failed, Error = ex.Message };
            }
        }

        // ── Provider resolvers ─────────────────────────────────

        private static bool IsMega(string url)
        {
            return url.IndexOf("mega.nz", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.IndexOf("mega.co.nz", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryResolveGoogleDrive(string url, out string direct)
        {
            direct = null;

            if (url.IndexOf("drive.google.com", StringComparison.OrdinalIgnoreCase) < 0 &&
                url.IndexOf("drive.usercontent.google.com", StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            // Already a direct usercontent download URL
            if (url.IndexOf("drive.usercontent.google.com", StringComparison.OrdinalIgnoreCase) >= 0 &&
                Regex.IsMatch(url, @"[?&]id=[\w\-]+", RegexOptions.IgnoreCase))
            {
                direct = url;
                return true;
            }

            string id = null;
            var m = Regex.Match(url, @"/file/d/([\w\-]+)", RegexOptions.IgnoreCase);
            if (m.Success)
                id = m.Groups[1].Value;
            else
            {
                m = Regex.Match(url, @"[?&]id=([\w\-]+)", RegexOptions.IgnoreCase);
                if (m.Success)
                    id = m.Groups[1].Value;
            }

            if (string.IsNullOrEmpty(id)) return false;

            direct = $"https://drive.usercontent.google.com/download?id={id}&export=download&confirm=t";
            Log.Dbg("DownloadService: Google Drive → {Direct}", direct);
            return true;
        }

        private static bool TryResolveOneDrive(string url, out string direct)
        {
            direct = null;

            if (url.IndexOf("1drv.ms", StringComparison.OrdinalIgnoreCase) < 0 &&
                url.IndexOf("onedrive.live.com", StringComparison.OrdinalIgnoreCase) < 0 &&
                url.IndexOf("onedrive.com", StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            // base64url of the full share URL (RFC 4648 §5) — the OneDrive API expects
            // u!{base64url} form for the shares endpoint.
            string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(url))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

            direct = $"https://api.onedrive.com/v1.0/shares/u!{b64}/root/content";
            Log.Dbg("DownloadService: OneDrive → {Direct}", direct);
            return true;
        }

        private static bool TryResolveDropbox(string url, out string direct)
        {
            direct = null;

            string lower = url.ToLowerInvariant();

            if (lower.StartsWith("https://dl.dropbox.com/") ||
                lower.StartsWith("http://dl.dropbox.com/"))
            {
                direct = EnsureDropboxDlParam(url);
                return true;
            }

            if (lower.StartsWith("https://dl.dropboxusercontent.com/") ||
                lower.StartsWith("http://dl.dropboxusercontent.com/"))
            {
                int idx = url.IndexOf("dropboxusercontent.com/", StringComparison.OrdinalIgnoreCase);
                string rest = url.Substring(idx + "dropboxusercontent.com/".Length);
                direct = EnsureDropboxDlParam("https://dl.dropbox.com/" + rest);
                return true;
            }

            var m = Regex.Match(url,
                @"dropbox\.com/(?:www\.)?((?:scl/\S+)|(?:s|sh)/[\w%]+/\S+)",
                RegexOptions.IgnoreCase);
            if (m.Success)
            {
                direct = EnsureDropboxDlParam("https://dl.dropbox.com/" + m.Groups[1].Value);
                Log.Dbg("DownloadService: Dropbox → {Direct}", direct);
                return true;
            }

            return false;
        }

        private static string EnsureDropboxDlParam(string url)
        {
            if (Regex.IsMatch(url, @"[?&]dl=\d", RegexOptions.IgnoreCase)) return url;
            return url + (url.Contains("?") ? "&" : "?") + "dl=1";
        }

        private static bool TryGetGofileCode(string url, out string code)
        {
            code = null;
            var m = Regex.Match(url, @"gofile\.io/d/([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            code = m.Groups[1].Value;
            return true;
        }

        private static async Task<string> ResolveGofileDirectAsync(string code, CancellationToken token)
        {
            var resp = await Client.GetAsync($"https://api.gofile.io/contents/{code}", token);
            if (!resp.IsSuccessStatusCode)
            {
                Log.Warn("DownloadService: gofile API status {Status}", resp.StatusCode);
                return null;
            }

            string json = await resp.Content.ReadAsStringAsync();
            var obj = JsonObject.Parse(json);
            if (obj.GetNamedString("status", "") != "ok") return null;

            var data = obj.GetNamedObject("data");
            if (!data.ContainsKey("contents")) return null;

            var contents = data.GetNamedObject("contents");
            foreach (string key in contents.Keys)
            {
                if (contents[key].ValueType != JsonValueType.Object) continue;
                var entry = contents[key].GetObject();

                if (entry.ContainsKey("directLink") &&
                    entry["directLink"].ValueType == JsonValueType.String)
                {
                    string dl = entry["directLink"].GetString();
                    if (!string.IsNullOrEmpty(dl))
                    {
                        Log.Dbg("DownloadService: gofile directLink = {Link}", dl);
                        return dl;
                    }
                }

                if (entry.ContainsKey("link") &&
                    entry["link"].ValueType == JsonValueType.String)
                {
                    string link = entry["link"].GetString();
                    if (!string.IsNullOrEmpty(link))
                    {
                        Log.Dbg("DownloadService: gofile link = {Link}", link);
                        return link;
                    }
                }
            }

            return null;
        }

        // ── Filename resolution ────────────────────────────────

        private static string ResolveFileName(string url, HttpResponseMessage resp)
        {
            string name = null;

            string cd = resp.Content.Headers.ContentDisposition?.ToString();
            if (!string.IsNullOrEmpty(cd))
                name = ParseContentDispositionFileName(cd);

            if (string.IsNullOrEmpty(name))
                name = FromUrlLastSegment(url);

            if (string.IsNullOrEmpty(name))
            {
                try { name = FromUrlLastSegment(new Uri(url).GetLeftPart(UriPartial.Path)); }
                catch { }
            }

            string sanitized = SanitizeFileName(name);
            if (string.IsNullOrEmpty(sanitized))
            {
                Log.Warn("DownloadService: no usable filename from {Url}", url);
                sanitized = "download";
            }

            return sanitized;
        }

        private static string ParseContentDispositionFileName(string header)
        {
            var m = Regex.Match(header, @"filename\*\s*=\s*([^;]+)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                string val = m.Groups[1].Value.Trim().Trim('"');
                int i = val.IndexOf("''", StringComparison.Ordinal);
                if (i >= 0) val = val.Substring(i + 2);
                try { val = Uri.UnescapeDataString(val); } catch { }
                return val;
            }

            m = Regex.Match(header, @"filename\s*=\s*""?([^"";]+)""?", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.Trim().Trim('"');

            return null;
        }

        private static string FromUrlLastSegment(string url)
        {
            try
            {
                var uri = new Uri(url);
                string segment = uri.Segments.Length > 0 ? uri.Segments[uri.Segments.Length - 1] : null;
                if (string.IsNullOrEmpty(segment) || segment.EndsWith("/")) return null;
                return Uri.UnescapeDataString(segment);
            }
            catch
            {
                return null;
            }
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            char[] invalid = Path.GetInvalidFileNameChars();
            string cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            if (string.IsNullOrWhiteSpace(cleaned)) return null;
            return cleaned;
        }

        private static string GetUniquePath(string path)
        {
            if (!File.Exists(path)) return path;

            string dir = Path.GetDirectoryName(path) ?? "";
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 1; i < 100; i++)
            {
                string candidate = Path.Combine(dir, $"{name} ({i}){ext}");
                if (!File.Exists(candidate)) return candidate;
            }
            return path;
        }

        private static void DeletePartial(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { File.Delete(path); }
            catch (Exception ex) { Log.Warn("DownloadService: partial delete failed {Path}", ex, path); }
        }
    }
}
