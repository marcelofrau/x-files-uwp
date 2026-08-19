using System;

namespace XFiles.Network
{
    public enum NetworkOperationReason
    {
        /// <summary>Operation exceeded the configured timeout (10s default).</summary>
        TimedOut,

        /// <summary>The server refused the operation (share/file permissions).</summary>
        AccessDenied,

        /// <summary>Server unreachable, DNS failure, connection refused, or a
        /// generic protocol failure that does not map to a more specific reason.</summary>
        Unreachable,

        /// <summary>Login failed: bad credentials, account disabled/locked, expired.</summary>
        AuthFailed,

        /// <summary>Operation was cancelled by the caller.</summary>
        Cancelled,

        /// <summary>The requested share/path/file does not exist.</summary>
        NotFound
    }

    public class NetworkOperationException : Exception
    {
        public NetworkOperationReason Reason { get; }

        public NetworkOperationException(NetworkOperationReason reason, string message, Exception inner = null)
            : base(message, inner)
        {
            Reason = reason;
        }

        /// <summary>
        /// User-facing message for a reason, with the raw detail appended when the
        /// reason is generic. Pure helper — used by the UI error overlay.
        /// </summary>
        public static string FriendlyMessage(NetworkOperationReason reason, string detail = null)
        {
            switch (reason)
            {
                case NetworkOperationReason.TimedOut:
                    return "Network timed out — the server did not respond in time.";
                case NetworkOperationReason.AccessDenied:
                    return "Access denied — check the location's permissions.";
                case NetworkOperationReason.AuthFailed:
                    return "Authentication failed — check user and password.";
                case NetworkOperationReason.NotFound:
                    return "Share or path not found.";
                case NetworkOperationReason.Cancelled:
                    return "Operation cancelled.";
                default:
                    return "Could not reach the server" + Hint(detail) + ".";
            }
        }

        /// <summary>
        /// Turns a raw exception detail into a short user-facing hint. Internal
        /// .NET noise (type names, object names, stack text) is dropped; only
        /// the first meaningful line is kept.
        /// </summary>
        private static string Hint(string detail)
        {
            if (string.IsNullOrWhiteSpace(detail)) return "";
            string line = detail;
            int newline = line.IndexOfAny(new[] { '\r', '\n' });
            if (newline >= 0) line = line.Substring(0, newline);
            line = line.Trim();

            if (line.Length == 0 || line.Length > 140) return "";
            if (line.Contains("Cannot access a disposed object")) return "";
            if (line.Contains("Object name:")) return "";
            if (line.StartsWith("at ", StringComparison.Ordinal)) return "";
            if (line.Contains(" ---> ")) return "";
            if (line.Contains("XFiles.") || line.Contains("System.")) return "";
            return " — " + line;
        }
    }
}
