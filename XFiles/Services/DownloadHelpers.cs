using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace XFiles.Services
{
    /// <summary>
    /// Pure helper methods extracted from DownloadService for testability.
    /// Provider URL resolvers (Google Drive, OneDrive, Dropbox) and filename
    /// resolution — all string transforms with no HTTP or UWP dependencies.
    /// </summary>
    internal static class DownloadHelpers
    {
        public static bool IsMega(string url)
        {
            return url.IndexOf("mega.nz", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.IndexOf("mega.co.nz", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool TryResolveGoogleDrive(string url, out string direct)
        {
            direct = null;
            if (url.IndexOf("drive.google.com", StringComparison.OrdinalIgnoreCase) < 0 &&
                url.IndexOf("drive.usercontent.google.com", StringComparison.OrdinalIgnoreCase) < 0)
                return false;

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
                if (m.Success) id = m.Groups[1].Value;
            }

            if (string.IsNullOrEmpty(id)) return false;
            direct = $"https://drive.usercontent.google.com/download?id={id}&export=download&confirm=t";
            return true;
        }

        public static bool TryResolveOneDrive(string url, out string direct)
        {
            direct = null;
            if (url.IndexOf("1drv.ms", StringComparison.OrdinalIgnoreCase) < 0 &&
                url.IndexOf("onedrive.live.com", StringComparison.OrdinalIgnoreCase) < 0 &&
                url.IndexOf("onedrive.com", StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            string b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(url))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            direct = $"https://api.onedrive.com/v1.0/shares/u!{b64}/root/content";
            return true;
        }

        public static bool TryResolveDropbox(string url, out string direct)
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
                return true;
            }

            return false;
        }

        public static string EnsureDropboxDlParam(string url)
        {
            if (Regex.IsMatch(url, @"[?&]dl=\d", RegexOptions.IgnoreCase)) return url;
            return url + (url.Contains("?") ? "&" : "?") + "dl=1";
        }

        public static bool TryGetGofileCode(string url, out string code)
        {
            code = null;
            var m = Regex.Match(url, @"gofile\.io/d/([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            code = m.Groups[1].Value;
            return true;
        }

        public static string ResolveFileName(string url, string contentDisposition)
        {
            return ResolveFileName(url, contentDisposition, null);
        }

        public static string ResolveFileName(string url, string contentDisposition, string suggestedName)
        {
            string name = null;

            if (!string.IsNullOrEmpty(contentDisposition))
                name = ParseContentDispositionFileName(contentDisposition);

            if (string.IsNullOrEmpty(name) && !string.IsNullOrWhiteSpace(suggestedName))
                name = suggestedName;

            if (string.IsNullOrEmpty(name))
                name = FromUrlLastSegment(url);

            if (string.IsNullOrEmpty(name))
            {
                try { name = FromUrlLastSegment(new Uri(url).GetLeftPart(UriPartial.Path)); }
                catch { }
            }

            string sanitized = SanitizeFileName(name);
            if (string.IsNullOrEmpty(sanitized))
                sanitized = "download";

            return sanitized;
        }

        public static string ParseContentDispositionFileName(string header)
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

        public static string FromUrlLastSegment(string url)
        {
            try
            {
                var uri = new Uri(url);
                string segment = uri.Segments.Length > 0 ? uri.Segments[uri.Segments.Length - 1] : null;
                if (string.IsNullOrEmpty(segment) || segment.EndsWith("/")) return null;
                return Uri.UnescapeDataString(segment);
            }
            catch { return null; }
        }

        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            char[] invalid = Path.GetInvalidFileNameChars();
            string cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            if (string.IsNullOrWhiteSpace(cleaned)) return null;
            return cleaned;
        }

        public static string GetUniquePath(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path) ?? "";
            string nameNoExt = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 1; i < 100; i++)
            {
                string candidate = Path.Combine(dir, $"{nameNoExt} ({i}){ext}");
                if (!File.Exists(candidate)) return candidate;
            }
            return path;
        }
    }
}
