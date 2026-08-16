using System.Collections.Generic;

namespace XFiles.Network
{
    /// <summary>
    /// Resolves the <see cref="INetworkFileSystemProvider"/> for a saved
    /// location's protocol. Browsers are stateless facades over their
    /// connection pools, so one instance per protocol is cached and reused.
    /// </summary>
    public static class NetworkProviderFactory
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<NetworkProtocol, INetworkFileSystemProvider> _providers =
            new Dictionary<NetworkProtocol, INetworkFileSystemProvider>();

        public static INetworkFileSystemProvider Create(NetworkProtocol protocol)
        {
            lock (Gate)
            {
                if (_providers.TryGetValue(protocol, out var existing))
                    return existing;

                INetworkFileSystemProvider provider;
                switch (protocol)
                {
                    case NetworkProtocol.Smb:
                        provider = new SmbBrowser();
                        break;
                    case NetworkProtocol.Ftp:
                    case NetworkProtocol.Ftps:
                        provider = new FtpBrowser();
                        break;
                    case NetworkProtocol.Sftp:
                        provider = new SftpBrowser();
                        break;
                    // WebDAV stays on the roadmap (M13+).
                    default:
                        throw new System.NotSupportedException(
                            $"Network protocol '{protocol}' is not implemented yet.");
                }

                _providers[protocol] = provider;
                return provider;
            }
        }

        public static INetworkFileSystemProvider Create(NetworkServerConfig config)
        {
            return Create(config?.Protocol ?? NetworkProtocol.Smb);
        }

        /// <summary>Clears all cached providers (used on disconnect-all).</summary>
        public static void Clear()
        {
            lock (Gate)
            {
                foreach (var p in _providers.Values)
                    p.Disconnect(new NetworkServerConfig());
                _providers.Clear();
            }
        }
    }
}
