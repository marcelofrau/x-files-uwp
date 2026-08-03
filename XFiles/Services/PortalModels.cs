using System;

namespace XFiles.Services
{
    /// <summary>
    /// An installed package on the console, as reported by the Device Portal
    /// packagemanager endpoint.
    /// </summary>
    public sealed class PortalPackage
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string FamilyName { get; set; }
        public string FullName { get; set; }
        public int Origin { get; set; }
    }

    /// <summary>
    /// A portal file/directory entry plus the portal context needed to address it
    /// (known folder, package, and parent portal path).
    /// </summary>
    public sealed class PortalFileEntry
    {
        public string Name { get; set; }
        public bool IsDirectory { get; set; }
        public long FileSize { get; set; }
        public long DateCreated { get; set; }
        public string KnownFolder { get; set; }
        public string PackageFullName { get; set; }
        public string PortalPath { get; set; }
    }

    /// <summary>
    /// Portal REST call failed with a non-success HTTP status.
    /// </summary>
    public sealed class PortalRequestException : Exception
    {
        public int StatusCode { get; }

        public PortalRequestException(int statusCode, string message) : base(message)
        {
            StatusCode = statusCode;
        }
    }
}
