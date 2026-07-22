using System;
using System.IO;
using System.Text.RegularExpressions;

namespace XFiles.Metadata
{
    public static class FilenameParser
    {
        private static readonly Regex TrackPrefixRegex = new Regex(
            @"^(\d{1,3})[\s._\-]+(.+)$", RegexOptions.Compiled);

        private static readonly Regex ArtistAlbumRegex = new Regex(
            @"^(.+?)\s*[-–—]\s*(.+)$", RegexOptions.Compiled);

        public static TrackMetadata ExtractFromPath(string filePath)
        {
            var result = new TrackMetadata();
            if (string.IsNullOrEmpty(filePath)) return result;

            string fileName = Path.GetFileNameWithoutExtension(filePath);
            string parentDir = "";

            try
            {
                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir))
                    parentDir = Path.GetFileName(dir);
            }
            catch { }

            ExtractTrackAndTitle(fileName, result);

            if (string.IsNullOrEmpty(result.Artist) || string.IsNullOrEmpty(result.Album))
                ExtractArtistAlbumFromParent(parentDir, result);

            if (string.IsNullOrEmpty(result.Artist) || string.IsNullOrEmpty(result.Album))
                ExtractArtistAlbumFromFilename(fileName, result);

            return result;
        }

        private static void ExtractTrackAndTitle(string fileName, TrackMetadata meta)
        {
            var match = TrackPrefixRegex.Match(fileName);
            if (match.Success)
            {
                meta.TrackNumber = match.Groups[1].Value;
                meta.Title = CleanTitle(match.Groups[2].Value);
            }
            else
            {
                meta.Title = CleanTitle(fileName);
            }
        }

        private static void ExtractArtistAlbumFromParent(string parentDir, TrackMetadata meta)
        {
            if (string.IsNullOrWhiteSpace(parentDir)) return;

            var match = ArtistAlbumRegex.Match(parentDir);
            if (match.Success)
            {
                if (string.IsNullOrEmpty(meta.Artist))
                    meta.Artist = match.Groups[1].Value.Trim();
                if (string.IsNullOrEmpty(meta.Album))
                    meta.Album = match.Groups[2].Value.Trim();
            }
            else
            {
                if (string.IsNullOrEmpty(meta.Album))
                    meta.Album = parentDir.Trim();
            }
        }

        private static void ExtractArtistAlbumFromFilename(string fileName, TrackMetadata meta)
        {
            var match = ArtistAlbumRegex.Match(fileName);
            if (match.Success)
            {
                if (string.IsNullOrEmpty(meta.Artist))
                    meta.Artist = match.Groups[1].Value.Trim();
                if (string.IsNullOrEmpty(meta.Title))
                    meta.Title = CleanTitle(match.Groups[2].Value);
            }
        }

        private static string CleanTitle(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            string cleaned = raw.Trim();
            cleaned = cleaned.Replace("_", " ");
            cleaned = Regex.Replace(cleaned, @"\s*\(\d{4}\)\s*$", "");
            cleaned = Regex.Replace(cleaned, @"\s*\[.*?\]\s*$", "");
            return cleaned.Trim();
        }
    }
}
