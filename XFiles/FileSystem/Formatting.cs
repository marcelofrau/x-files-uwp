using System;
using System.Collections.Generic;
using System.Linq;

namespace XFiles.FileSystem
{
    public static class Formatting
    {
        public static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        public static string FormatBytes(long bytes) => FormatSize(bytes);

        public static string FormatFsTime(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            return $"{ts.Minutes}:{ts.Seconds:D2}";
        }

        public static string FormatCount(List<FileEntry> entries)
        {
            if (entries == null) return "0 items";
            int folders = entries.Count(e => e.Name != ".." && e.IsDirectory);
            int files = entries.Count(e => e.Name != ".." && !e.IsDirectory);
            var parts = new List<string>();
            if (folders > 0) parts.Add($"{folders} folder{(folders == 1 ? "" : "s")}");
            if (files > 0) parts.Add($"{files} file{(files == 1 ? "" : "s")}");
            return parts.Count > 0 ? string.Join(", ", parts) : "0 items";
        }
    }
}
