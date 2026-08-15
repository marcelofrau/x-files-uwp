using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace XFiles.Network
{
    /// <summary>
    /// Protocol-agnostic network file access. SMB is the first implementation
    /// (SmbBrowser); FTP/WebDAV/SFTP plug in later behind the same contract.
    /// All operations take a NetworkServerConfig (credentials come from the
    /// credential vault, resolved inside the implementation) and a
    /// CancellationToken; implementations never block the UI thread and apply
    /// their own timeout. Failures surface as NetworkOperationException.
    /// </summary>
    public interface INetworkFileSystemProvider
    {
        NetworkProtocol Protocol { get; }

        /// <summary>Lists share names exported by the server.</summary>
        Task<List<string>> ListSharesAsync(NetworkServerConfig config, CancellationToken ct);

        /// <summary>
        /// Lists the contents of <paramref name="share"/> at <paramref name="path"/>
        /// (empty string or null = share root). Returns directories and files.
        /// </summary>
        Task<List<NetworkFileEntry>> ListDirectoryAsync(NetworkServerConfig config, string share, string path, CancellationToken ct);

        /// <summary>Opens a file for sequential/random read access.</summary>
        Task<Stream> OpenReadAsync(NetworkServerConfig config, string share, string path, CancellationToken ct);

        /// <summary>Returns the byte length of a file without downloading it.</summary>
        Task<long> GetFileLengthAsync(NetworkServerConfig config, string share, string path, CancellationToken ct);

        /// <summary>Best-effort release of all sessions held for the location.</summary>
        void Disconnect(NetworkServerConfig config);
    }
}
