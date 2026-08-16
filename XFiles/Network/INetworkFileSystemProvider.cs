using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace XFiles.Network
{
    /// <summary>
    /// Protocol-agnostic network file access. SMB is the first implementation
    /// (SmbBrowser); FTP (M9) and SFTP (M10) plug in behind the same contract.
    /// All operations take a NetworkServerConfig (credentials come from the
    /// credential vault, resolved inside the implementation) and a
    /// CancellationToken; implementations never block the UI thread and apply
    /// their own timeout. Failures surface as NetworkOperationException.
    /// </summary>
    public interface INetworkFileSystemProvider
    {
        NetworkProtocol Protocol { get; }

        /// <summary>
        /// Connects with the given password (null when the provider should use
        /// the vault) and returns a human-readable result for the location
        /// dialog's Test button. Throws NetworkOperationException on failure.
        /// </summary>
        Task<string> TestConnectionAsync(NetworkServerConfig config, string password, CancellationToken ct);

        /// <summary>
        /// Lists share names exported by the server. FTP/SFTP have no share
        /// layer and return an empty list (their remote paths are absolute
        /// from the server root and <paramref name="share"/> is "").
        /// </summary>
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

        /// <summary>Checks whether an entry (file or directory) exists at the remote path.</summary>
        Task<bool> EntryExistsAsync(NetworkServerConfig config, string share, string path, bool isDirectory, CancellationToken ct);

        /// <summary>
        /// Uploads a local file's bytes to a remote path, overwriting it. Used by
        /// the text editor's save-back and the portal upload path.
        /// </summary>
        Task WriteFileAsync(NetworkServerConfig config, string share, string path, string localPath, CancellationToken ct);

        /// <summary>Opens a remote path for writing (overwrite disposition).</summary>
        Task<Stream> OpenWriteStreamAsync(NetworkServerConfig config, string share, string path, CancellationToken ct);

        /// <summary>Deletes a single remote file.</summary>
        Task DeleteFileAsync(NetworkServerConfig config, string share, string path, CancellationToken ct);

        /// <summary>Recursively deletes a remote directory tree.</summary>
        Task DeleteDirectoryAsync(NetworkServerConfig config, string share, string path, CancellationToken ct);

        /// <summary>Renames a remote file or directory within its parent.</summary>
        Task RenameFileAsync(NetworkServerConfig config, string share, string path, string newName, bool isDirectory, CancellationToken ct);

        /// <summary>Creates a remote directory.</summary>
        Task CreateDirectoryAsync(NetworkServerConfig config, string share, string path, CancellationToken ct);

        /// <summary>Best-effort release of all sessions held for the location.</summary>
        void Disconnect(NetworkServerConfig config);
    }
}
