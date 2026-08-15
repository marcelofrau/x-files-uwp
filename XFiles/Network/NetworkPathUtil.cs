using System;

namespace XFiles.Network
{
    /// <summary>
    /// Pure SMB path helpers (no logging, no UWP types) so the copy-service
    /// path composition can be unit-tested on desktop (linked source).
    /// </summary>
    public static class NetworkPathUtil
    {
        /// <summary>Joins a share-relative directory with a child name using
        /// backslash separators; an empty directory yields the bare name.</summary>
        public static string Join(string dir, string name)
        {
            if (string.IsNullOrEmpty(dir)) return name;
            return dir.TrimEnd('\\') + "\\" + name;
        }

        /// <summary>Returns the joined path, or the base path itself when the
        /// relative part is empty (used for the root of a copied subtree).</summary>
        public static string PathForItem(string basePath, string rel)
        {
            return string.IsNullOrEmpty(rel) ? basePath : Join(basePath, rel);
        }

        /// <summary>Parent directory of a share-relative path (empty for the
        /// share root). Used for same-directory paste detection.</summary>
        public static string Parent(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            int idx = path.LastIndexOf('\\');
            return idx < 0 ? string.Empty : path.Substring(0, idx);
        }
    }
}
