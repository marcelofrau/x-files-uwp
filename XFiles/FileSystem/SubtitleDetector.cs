using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace XFiles.FileSystem
{
    /// <summary>
    /// Detects external subtitle files for a video using VLC-style same-name matching.
    /// Also provides helpers to detect embedded subtitle/audio track languages from track metadata.
    /// </summary>
    public static class SubtitleDetector
    {
        private static readonly HashSet<string> SubtitleExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".srt", ".ass", ".ssa", ".vtt", ".sub", ".idx", ".smi", ".lrc"
            };

        #region P/Invoke

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WIN32_FIND_DATA
        {
            public uint dwFileAttributes;
            public FILETIME ftCreationTime;
            public FILETIME ftLastAccessTime;
            public FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }

        private const uint FIND_FIRST_EX_LARGE_FETCH = 0x00000002;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
        private const int INVALID_HANDLE_VALUE = -1;

        public enum FINDEX_INFO_LEVELS { FindExInfoStandard = 0 }
        public enum FINDEX_SEARCH_OPS { FindExSearchNameMatch = 0 }

        [DllImport("api-ms-win-core-file-fromapp-l1-1-0.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindFirstFileExFromAppW(
            string lpFileName,
            FINDEX_INFO_LEVELS fInfoLevelId,
            out WIN32_FIND_DATA lpFindFileData,
            FINDEX_SEARCH_OPS fSearchOp,
            IntPtr lpSearchFilter,
            uint dwAdditionalFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool FindNextFileW(IntPtr hFindFile, out WIN32_FIND_DATA lpFindFileData);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FindClose(IntPtr hFindFile);

        #endregion

        /// <summary>
        /// Enumerate file names in a directory via P/Invoke (bypasses UWP sandbox).
        /// Returns empty list if the directory cannot be read.
        /// </summary>
        private static List<string> EnumerateFileNames(string dir)
        {
            var names = new List<string>();
            string searchPath = Path.Combine(dir, "*");
            IntPtr hFind = FindFirstFileExFromAppW(
                searchPath,
                FINDEX_INFO_LEVELS.FindExInfoStandard,
                out WIN32_FIND_DATA findData,
                FINDEX_SEARCH_OPS.FindExSearchNameMatch,
                IntPtr.Zero,
                FIND_FIRST_EX_LARGE_FETCH);

            if (hFind == new IntPtr(INVALID_HANDLE_VALUE))
            {
                int err = Marshal.GetLastWin32Error();
                Log.Warn("SubtitleDetector: FindFirstFileExFromAppW failed for '{Dir}' (error {Error})", dir, err);
                return names;
            }

            try
            {
                do
                {
                    string name = findData.cFileName;
                    if (name == "." || name == "..") continue;
                    if ((findData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0) continue;
                    names.Add(Path.Combine(dir, name));
                }
                while (FindNextFileW(hFind, out findData));
            }
            finally
            {
                FindClose(hFind);
            }

            return names;
        }

        /// <summary>
        /// Finds external subtitle files matching a video path.
        /// Matches VLC convention: "video_name.lang.srt", "video_name.srt", etc.
        /// Returns list of (language, filePath) tuples, sorted by language (unknown last).
        /// </summary>
        public static List<SubtitleTrack> FindExternalSubtitles(string videoPath)
        {
            var results = new List<SubtitleTrack>();
            if (string.IsNullOrEmpty(videoPath)) return results;

            string dir = Path.GetDirectoryName(videoPath);
            if (string.IsNullOrEmpty(dir) || FileOperations.CheckPathType(dir) != "directory") return results;

            string baseName = Path.GetFileNameWithoutExtension(videoPath);

            try
            {
                foreach (string file in EnumerateFileNames(dir))
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    string ext = Path.GetExtension(file);

                    if (!SubtitleExtensions.Contains(ext)) continue;

                    string lang = null;

                    // Pattern: "videoname.lang.ext" (e.g. "movie.en.srt", "movie.pt-BR.ass")
                    if (fileName.Length > baseName.Length && fileName.StartsWith(baseName, StringComparison.OrdinalIgnoreCase))
                    {
                        string suffix = fileName.Substring(baseName.Length);
                        if (suffix.Length > 1 && suffix[0] == '.')
                        {
                            lang = suffix.Substring(1);
                        }
                    }
                    // Pattern: "videoname.ext" (exact match, no language suffix)
                    else if (string.Equals(fileName, baseName, StringComparison.OrdinalIgnoreCase))
                    {
                        lang = "Default";
                    }
                    else
                    {
                        continue;
                    }

                    results.Add(new SubtitleTrack
                    {
                        Language = lang ?? "Unknown",
                        FilePath = file,
                        IsExternal = true
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Warn("SubtitleDetector.FindExternalSubtitles failed for '{Path}'", videoPath, ex);
            }

            // Sort: named languages first (alphabetical), then "Default", then "Unknown"
            results.Sort((a, b) =>
            {
                bool aDefault = a.Language == "Default";
                bool bDefault = b.Language == "Default";
                bool aUnknown = a.Language == "Unknown";
                bool bUnknown = b.Language == "Unknown";

                if (aDefault && !bDefault) return -1;
                if (!aDefault && bDefault) return 1;
                if (aUnknown && !bUnknown) return 1;
                if (!aUnknown && bUnknown) return -1;
                return string.Compare(a.Language, b.Language, StringComparison.OrdinalIgnoreCase);
            });

            return results;
        }

        /// <summary>
        /// Returns a friendly display name for a subtitle track.
        /// </summary>
        public static string GetDisplayName(this SubtitleTrack track)
        {
            if (track.IsExternal)
            {
                string ext = Path.GetExtension(track.FilePath)?.TrimStart('.').ToUpperInvariant() ?? "";
                string name = string.Equals(track.Language, "Default", StringComparison.OrdinalIgnoreCase)
                    ? $"External ({ext})"
                    : $"{track.Language} ({ext})";
                return name;
            }
            else
            {
                // Embedded track
                string lang = string.IsNullOrEmpty(track.Language) ? "Unknown" : track.Language;
                if (!string.IsNullOrEmpty(track.Title) && track.Title != lang)
                    return $"{lang} — {track.Title}";
                return lang;
            }
        }
    }

    public class SubtitleTrack
    {
        public string Language { get; set; } = "Unknown";
        public string Title { get; set; }
        public string FilePath { get; set; }
        public bool IsExternal { get; set; }
        public int EmbeddedIndex { get; set; } = -1;
    }

    public class AudioTrackInfo
    {
        public string Language { get; set; } = "Unknown";
        public string Title { get; set; }
        public int Index { get; set; }

        public string DisplayName
        {
            get
            {
                string lang = string.IsNullOrEmpty(Language) ? "Unknown" : Language;
                if (!string.IsNullOrEmpty(Title) && Title != lang)
                    return $"{lang} — {Title}";
                return lang;
            }
        }
    }
}
