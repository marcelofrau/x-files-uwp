using System;
using System.Collections.Generic;
using System.IO;

namespace XFiles.Network
{
    /// <summary>
    /// Pure remote path helpers (no logging, no UWP types) so the copy-service
    /// path composition can be unit-tested on desktop (linked source). SMB uses
    /// backslash separators and share-relative paths; FTP/SFTP use forward
    /// slashes and absolute-from-root paths — the protocol-aware overloads pick
    /// the separator.
    /// </summary>
    public static class NetworkPathUtil
    {
        /// <summary>Path separator for a protocol (SMB: '\', everything else: '/').</summary>
        public static char Separator(NetworkProtocol protocol)
        {
            return protocol == NetworkProtocol.Smb ? '\\' : '/';
        }

        /// <summary>Joins a share-relative directory with a child name using
        /// backslash separators; an empty directory yields the bare name.</summary>
        public static string Join(string dir, string name)
        {
            return Join(dir, name, '\\');
        }

        /// <summary>Joins using the given protocol's separator.</summary>
        public static string Join(string dir, string name, NetworkProtocol protocol)
        {
            return Join(dir, name, Separator(protocol));
        }

        /// <summary>Joins a directory with a child name using the given separator;
        /// an empty directory yields the bare name.</summary>
        public static string Join(string dir, string name, char sep)
        {
            if (string.IsNullOrEmpty(dir)) return name;
            return dir.TrimEnd(sep) + sep + name;
        }

        /// <summary>Returns the joined path, or the base path itself when the
        /// relative part is empty (used for the root of a copied subtree).</summary>
        public static string PathForItem(string basePath, string rel)
        {
            return string.IsNullOrEmpty(rel) ? basePath : Join(basePath, rel);
        }

        /// <summary>Returns the joined path using the given protocol's separator.</summary>
        public static string PathForItem(string basePath, string rel, NetworkProtocol protocol)
        {
            return string.IsNullOrEmpty(rel) ? basePath : Join(basePath, rel, protocol);
        }

        /// <summary>Parent directory of a share-relative path (empty for the
        /// share root). Used for same-directory paste detection.</summary>
        public static string Parent(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            int idx = path.LastIndexOf('\\');
            return idx < 0 ? string.Empty : path.Substring(0, idx);
        }

        /// <summary>Parent directory using the given protocol's separator.</summary>
        public static string Parent(string path, NetworkProtocol protocol)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            char sep = Separator(protocol);
            int idx = path.LastIndexOf(sep);
            return idx < 0 ? string.Empty : path.Substring(0, idx);
        }

        /// <summary>Yields "name (1)", "name (2)", ... candidates for a collision.
        /// The caller checks each against the filesystem until one is free.</summary>
        public static IEnumerable<string> NameCandidates(string dir, string name)
        {
            return NameCandidates(dir, name, '\\');
        }

        /// <summary>Yields collision candidates using the given protocol's separator.</summary>
        public static IEnumerable<string> NameCandidates(string dir, string name, NetworkProtocol protocol)
        {
            return NameCandidates(dir, name, Separator(protocol));
        }

        /// <summary>Yields collision candidates using the given separator.</summary>
        public static IEnumerable<string> NameCandidates(string dir, string name, char sep)
        {
            string ext = Path.GetExtension(name);
            string stem = name.Substring(0, name.Length - ext.Length);
            for (int i = 1; ; i++)
            {
                string candidate = $"{stem} ({i}){ext}";
                yield return string.IsNullOrEmpty(dir) ? candidate : dir.TrimEnd(sep) + sep + candidate;
            }
        }
    }
}
