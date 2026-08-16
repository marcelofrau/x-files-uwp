using System;
using System.Collections.Generic;
using System.IO;

namespace XFiles.Network
{
    /// <summary>
    /// Persists accepted SSH host key fingerprints per host:port so SFTP
    /// connects can be verified without re-prompting. Pure model with no
    /// UWP/SQLite dependencies (linkable into unit tests) — persistence is a
    /// caller-supplied JSON file path. On the Xbox the file lives under
    /// LocalState\Network\host-keys.json.
    /// </summary>
    public class HostKeyTrustStore
    {
        /// <summary>Stored fingerprint per host:port key ("host:port").</summary>
        private readonly Dictionary<string, string> _trusted =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>JSON file used by <see cref="Load"/> / <see cref="Save"/>; null when memory-only.</summary>
        private readonly string _filePath;

        public HostKeyTrustStore(string filePath = null)
        {
            _filePath = filePath;
            if (filePath != null && File.Exists(filePath))
                Load(filePath);
        }

        /// <summary>Returns the accepted SHA256 fingerprint for a host:port, or null.</summary>
        public string GetFingerprint(string hostPort)
        {
            if (hostPort == null) return null;
            return _trusted.TryGetValue(hostPort, out var fp) ? fp : null;
        }

        /// <summary>True when host:port is trusted AND the offered fingerprint matches.</summary>
        public bool IsTrusted(string hostPort, string sha256Fingerprint)
        {
            string expected = GetFingerprint(hostPort);
            if (expected == null) return false;
            return string.Equals(expected, sha256Fingerprint, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Records the fingerprint for a host:port and persists.</summary>
        public void Accept(string hostPort, string sha256Fingerprint)
        {
            _trusted[hostPort] = sha256Fingerprint;
            Save();
        }

        /// <summary>Removes a host:port entry (used when the key changes and the user declines).</summary>
        public void Forget(string hostPort)
        {
            _trusted.Remove(hostPort);
            Save();
        }

        private void Load(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                var map = JsonSimple.ParseObject(json);
                foreach (var kv in map)
                    _trusted[kv.Key] = kv.Value;
            }
            catch
            {
                // Corrupt/missing file — start with an empty trust set.
            }
        }

        private void Save()
        {
            if (_filePath == null) return;
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var sb = new System.Text.StringBuilder();
                sb.Append('{');
                bool first = true;
                foreach (var kv in _trusted)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('"').Append(JsonSimple.Escape(kv.Key)).Append("\":\"")
                      .Append(JsonSimple.Escape(kv.Value)).Append('"');
                }
                sb.Append('}');
                File.WriteAllText(_filePath, sb.ToString());
            }
            catch
            {
                // Best-effort persistence — in-memory trust still applies.
            }
        }
    }

    /// <summary>Minimal JSON object/string helper — avoids a JSON dependency in the UWP app.</summary>
    internal static class JsonSimple
    {
        public static Dictionary<string, string> ParseObject(string json)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(json)) return map;
            string body = json.Trim();
            if (body.StartsWith("{") && body.EndsWith("}"))
                body = body.Substring(1, body.Length - 2).Trim();
            if (body.Length == 0) return map;
            foreach (var rawPair in SplitTopLevel(body))
            {
                int colon = IndexOfTopLevel(rawPair, ':');
                if (colon < 0) continue;
                string key = Unquote(rawPair.Substring(0, colon).Trim());
                string value = Unquote(rawPair.Substring(colon + 1).Trim());
                if (key.Length > 0) map[key] = value;
            }
            return map;
        }

        public static string Escape(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        private static List<string> SplitTopLevel(string s)
        {
            var parts = new List<string>();
            var cur = new System.Text.StringBuilder();
            bool inString = false;
            bool escaped = false;
            int depth = 0;
            foreach (char c in s)
            {
                if (inString)
                {
                    cur.Append(c);
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }
                switch (c)
                {
                    case '"': inString = true; cur.Append(c); break;
                    case '{': case '[': depth++; cur.Append(c); break;
                    case '}': case ']': depth--; cur.Append(c); break;
                    case ',' when depth == 0:
                        parts.Add(cur.ToString());
                        cur.Clear();
                        break;
                    default: cur.Append(c); break;
                }
            }
            if (cur.Length > 0) parts.Add(cur.ToString());
            return parts;
        }

        private static int IndexOfTopLevel(string s, char needle)
        {
            bool inString = false;
            bool escaped = false;
            int depth = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }
                switch (c)
                {
                    case '"': inString = true; break;
                    case '{': case '[': depth++; break;
                    case '}': case ']': depth--; break;
                    default:
                        if (c == needle && depth == 0) return i;
                        break;
                }
            }
            return -1;
        }

        private static string Unquote(string s)
        {
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
            {
                s = s.Substring(1, s.Length - 2);
                s = s.Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\n", "\n")
                     .Replace("\\r", "\r").Replace("\\t", "\t");
            }
            return s;
        }
    }
}
