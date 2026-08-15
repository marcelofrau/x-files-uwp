using System;

namespace XFiles.Network
{
    /// <summary>
    /// Pure helpers for composing/parsing the canonical network URL
    /// ({protocol}://[user@]host[/share]). The canonical URL is the storage
    /// identity (unique index in SQLite) and the PasswordVault resource key.
    /// Host is normalized to lowercase for case-insensitive identity;
    /// username and share are kept as typed (trimmed). Pure — no UWP/SQLite.
    /// </summary>
    public static class NetworkUrl
    {
        public static int DefaultPort(NetworkProtocol protocol)
        {
            switch (protocol)
            {
                case NetworkProtocol.Smb:
                    return 445;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Composes the canonical URL for a config.
        /// Returns null when the config has no host (not saveable).
        /// </summary>
        public static string Compose(NetworkServerConfig config)
        {
            if (config == null) return null;

            string host = (config.Host ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(host)) return null;

            string scheme = config.Protocol.ToString().ToLowerInvariant();
            string user = (config.Username ?? "").Trim();
            string share = (config.Share ?? "").Trim().TrimStart('/');

            var sb = new System.Text.StringBuilder();
            sb.Append(scheme).Append("://");
            if (user.Length > 0)
            {
                sb.Append(user).Append('@');
            }
            sb.Append(host);
            if (share.Length > 0)
            {
                sb.Append('/').Append(share);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Parses a canonical URL back into a config (password left null).
        /// Returns null when the URL is null/empty, has an unknown scheme, or
        /// lacks a host. Port is always 0 (not part of the canonical form).
        /// </summary>
        public static NetworkServerConfig Parse(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            int schemeSep = url.IndexOf("://", StringComparison.Ordinal);
            if (schemeSep < 0) return null;

            string scheme = url.Substring(0, schemeSep).Trim().ToLowerInvariant();
            NetworkProtocol protocol;
            if (scheme == "smb")
            {
                protocol = NetworkProtocol.Smb;
            }
            else
            {
                return null;
            }

            string rest = url.Substring(schemeSep + 3);
            if (rest.Length == 0) return null;

            string userPart = null;
            string hostPart = rest;
            int shareSep = rest.IndexOf('/');
            string share = null;
            if (shareSep >= 0)
            {
                hostPart = rest.Substring(0, shareSep);
                share = rest.Substring(shareSep + 1);
            }

            int at = hostPart.IndexOf('@');
            if (at >= 0)
            {
                userPart = hostPart.Substring(0, at);
                hostPart = hostPart.Substring(at + 1);
            }

            hostPart = hostPart.Trim();
            if (hostPart.Length == 0) return null;

            string username = string.IsNullOrWhiteSpace(userPart) ? null : userPart.Trim();

            return new NetworkServerConfig
            {
                Protocol = protocol,
                Host = hostPart,
                Username = username,
                Share = string.IsNullOrWhiteSpace(share) ? null : share
            };
        }

        /// <summary>
        /// PasswordVault resource key for a config's stored credential.
        /// Equals the canonical URL (unique per saved location).
        /// </summary>
        public static string VaultResource(NetworkServerConfig config)
        {
            return Compose(config);
        }

        /// <summary>Display name: friendly name when set, else the canonical URL.</summary>
        public static string DisplayName(NetworkServerConfig config)
        {
            if (config == null) return null;
            string friendly = (config.DisplayName ?? "").Trim();
            if (friendly.Length > 0) return friendly;
            return Compose(config);
        }

        /// <summary>
        /// Case-insensitive sort key for the saved-locations list.
        /// </summary>
        public static string SortKey(NetworkServerConfig config)
        {
            return (DisplayName(config) ?? "").ToLowerInvariant();
        }
    }
}
