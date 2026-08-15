namespace XFiles.Network
{
    /// <summary>
    /// Supported remote file-access protocols. Only SMB is wired in the first
    /// delivery; FTP/WebDAV/SFTP plug into <see cref="NetworkServerManager"/>
    /// later without schema changes (stored as int).
    /// </summary>
    public enum NetworkProtocol
    {
        Smb = 0,
        Ftp = 1,
        Sftp = 2,
        Webdav = 3
    }
}
