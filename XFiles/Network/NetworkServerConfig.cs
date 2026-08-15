namespace XFiles.Network
{
    /// <summary>
    /// A saved network location (server/share pair). The password is NEVER
    /// stored here — it lives in Windows Credential Locker (PasswordVault),
    /// keyed by <see cref="NetworkUrl.Compose"/> (the canonical URL).
    /// Pure model, no UWP/SQLite dependencies (linkable into unit tests).
    /// </summary>
    public class NetworkServerConfig
    {
        /// <summary>Row id in the NetworkServerEntry table; 0 when not persisted.</summary>
        public int Id { get; set; }

        /// <summary>Protocol used to reach this server (SMB for now).</summary>
        public NetworkProtocol Protocol { get; set; } = NetworkProtocol.Smb;

        /// <summary>Optional friendly name shown in the Network column.
        /// When null/empty, the UI displays the composed canonical URL.</summary>
        public string DisplayName { get; set; }

        /// <summary>Host name or IP address of the server.</summary>
        public string Host { get; set; }

        /// <summary>Port override; 0 means the protocol default (SMB = 445).</summary>
        public int Port { get; set; }

        /// <summary>Account used to log in. Empty/guest for anonymous access.</summary>
        public string Username { get; set; }

        /// <summary>Optional share name. Empty = "list shares" when browsing.</summary>
        public string Share { get; set; }

        public int EffectivePort
        {
            get { return Port > 0 ? Port : NetworkUrl.DefaultPort(Protocol); }
        }
    }
}
