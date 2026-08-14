using System;
using System.Collections.Generic;

namespace XFiles.FileSystem
{
    /// <summary>
    /// Pure, testable classification of music file extensions. No UWP/WinRT
    /// types — linked into the unit-test project via Compile Include.
    /// </summary>
    /// <remarks>
    /// The chiptune set mirrors the native-backed formats in RetroAudioPlayer
    /// (GME + libopenmpt + aosdk/lazyusf). Project rule #9: when a chiptune
    /// format is added, keep this list, RetroAudioPlayer.cs,
    /// FilePreviewService.cs and ColumnListView.xaml.cs in sync.
    /// </remarks>
    public static class MusicFormatClassifier
    {
        // Standard audio files AudioGraph plays natively.
        private static readonly HashSet<string> StandardAudioExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".wma", ".aac"
            };

        // RetroAudio.dll formats (game-music-emu).
        private static readonly string[] GmeExtensions =
        {
            ".spc", ".gbs", ".nsf", ".nsfe", ".vgm", ".vgz", ".gym",
            ".sid", ".hes", ".kss", ".ay", ".sap"
        };

        // RetroAudio.dll formats (libopenmpt trackers).
        private static readonly string[] OpenmptExtensions =
        {
            ".mod", ".xm", ".s3m", ".it", ".mtm", ".stm", ".669", ".med",
            ".far", ".mdl", ".ult", ".ptm", ".dbm", ".dsm", ".amf", ".okt",
            ".dmf", ".ams", ".mt2", ".pol", ".ppm", ".cba", ".psm", ".j2b",
            ".mpm", ".umx", ".mo3"
        };

        // RetroAudio.dll formats (aosdk engine_psf + lazyusf).
        private static readonly string[] PsfUsfExtensions =
        {
            ".psf", ".minipsf", ".usf", ".miniusf"
        };

        private static readonly HashSet<string> ChiptuneSet =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>BGM volume levels (percent), in cycle order.</summary>
        public static readonly int[] VolumeLevels = { 10, 25, 50, 75, 100 };

        /// <summary>All playable music extensions (standard audio ∪ chiptune),
        /// dot-prefixed. Ready to pass as a picker filter.</summary>
        public static readonly string[] MusicExtensions;

        static MusicFormatClassifier()
        {
            foreach (string ext in GmeExtensions) ChiptuneSet.Add(ext);
            foreach (string ext in OpenmptExtensions) ChiptuneSet.Add(ext);
            foreach (string ext in PsfUsfExtensions) ChiptuneSet.Add(ext);

            var all = new List<string>(StandardAudioExtensions);
            all.AddRange(GmeExtensions);
            all.AddRange(OpenmptExtensions);
            all.AddRange(PsfUsfExtensions);
            MusicExtensions = all.ToArray();
        }

        public static bool IsStandardAudio(string extension)
        {
            return !string.IsNullOrEmpty(extension)
                && StandardAudioExtensions.Contains(extension);
        }

        public static bool IsChiptune(string extension)
        {
            return !string.IsNullOrEmpty(extension)
                && ChiptuneSet.Contains(extension);
        }

        public static bool IsMusicFile(string extension)
        {
            return IsStandardAudio(extension) || IsChiptune(extension);
        }

        public static IEnumerable<string> StandardAudioList => StandardAudioExtensions;
        public static IEnumerable<string> ChiptuneList => ChiptuneSet;

        /// <summary>Map a percentage (10..100) to a 0-1 gain.</summary>
        public static float PercentToGain(int percent)
        {
            return Math.Max(0, Math.Min(100, percent)) / 100f;
        }

        /// <summary>Next level in the cycle (wraps around).</summary>
        public static int NextVolumeLevel(int currentPercent)
        {
            int index = Array.IndexOf(VolumeLevels, currentPercent);
            if (index < 0) index = -1; // unknown value → next is the first
            return VolumeLevels[(index + 1) % VolumeLevels.Length];
        }
    }
}
