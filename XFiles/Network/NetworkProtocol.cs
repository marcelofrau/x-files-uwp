namespace XFiles.Network
{
    /// <summary>
    /// Supported remote file-access protocols. Only SMB is wired in the first
    /// delivery; FTP/FTPS/SFTP plug into <see cref="NetworkServerManager"/>
    /// later without schema changes (stored as int). WebDAV stays on the
    /// roadmap but is out of the current delivery.
    /// </summary>
    public enum NetworkProtocol
    {
        Smb = 0,
        Ftp = 1,
        Sftp = 2,
        Webdav = 3,
        Ftps = 4
    }
}
