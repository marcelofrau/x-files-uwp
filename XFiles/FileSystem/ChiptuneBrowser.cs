using System;
using System.Collections.Generic;
using System.IO;
using XFiles.Audio;

namespace XFiles.FileSystem
{
    /// <summary>
    /// Builds the virtual track list shown when a multi-track chiptune is drilled
    /// into (Miller-column style: parent | track list). Track entries reuse the
    /// FileEntry shape — each one carries the source address plus a track index so
    /// the preview/playback layer can render the right subsong.
    /// </summary>
    public static class ChiptuneBrowser
    {
        /// <summary>
        /// Probe the chiptune source and return one FileEntry per subsong.
        /// Returns an empty list if the source cannot be read or has no track info.
        /// Source may be a plain file path or an archive-entry address ("archive|internal").
        /// </summary>
        public static IReadOnlyList<FileEntry> BuildTrackEntries(string sourceKey, byte[] data, string extension)
        {
            var entries = new List<FileEntry>();

            ChiptuneTrackInfo info = RetroAudioPlayer.Probe(sourceKey, data, extension);
            if (info == null || info.TrackCount <= 0)
            {
                Log.Warn("ChiptuneBrowser: no track info for '{Key}'", sourceKey);
                return entries;
            }

            string sourceName = GetSourceDisplayName(sourceKey);

            for (int i = 0; i < info.TrackCount; i++)
            {
                string title = i < info.Titles.Length ? info.Titles[i] : null;
                string display = string.IsNullOrEmpty(title)
                    ? $"Track {i + 1}"
                    : $"Track {i + 1}: {title}";

                entries.Add(new FileEntry
                {
                    Name = display,
                    FullPath = sourceKey,
                    IsDirectory = false,
                    SizeBytes = 0,
                    ArchiveRootPath = null,
                    ArchiveInternalPath = null,
                    IsChiptune = true,
                    ChiptuneSourcePath = sourceKey,
                    ChiptuneTrackIndex = i
                });
            }

            Log.Dbg("ChiptuneBrowser: built {Count} track entries for '{Key}'", entries.Count, sourceName);
            return entries;
        }

        private static string GetSourceDisplayName(string sourceKey)
        {
            if (string.IsNullOrEmpty(sourceKey)) return sourceKey ?? "";
            string name = Path.GetFileName(sourceKey);
            return string.IsNullOrEmpty(name) ? sourceKey : name;
        }
    }
}
