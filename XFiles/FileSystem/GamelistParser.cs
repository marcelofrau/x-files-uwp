using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Xml;

namespace XFiles.FileSystem
{
    /// <summary>
    /// EmulationStation / ES-DE / Batocera gamelist.xml parser.
    /// Uses XmlReader (streaming) to avoid DOM overhead for large gamelists.
    /// </summary>
    public class GamelistEntry
    {
        public string RawPath;
        public string Name;
        public string Description;
        public string ImagePath;
        public string CoverPath;
        public string ThumbnailPath;
        public string VideoPath;
        public string Developer;
        public string Publisher;
        public string Genre;
        public int Players;
        public float Rating;
        public DateTime? ReleaseDate;
    }

    public static class GamelistParser
    {
        /// <summary>
        /// Parse gamelist.xml using XmlReader streaming via P/Invoke file handle.
        /// Returns dictionary keyed by normalized path variants for fast lookup.
        /// Keys are lowercase, without ./ prefix, include both full name and name-without-extension.
        /// </summary>
        public static async Task<Dictionary<string, GamelistEntry>> ParseAsync(string gamelistPath)
        {
            var result = new Dictionary<string, GamelistEntry>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using (var stream = DirectoryScanner.OpenFileRead(gamelistPath))
                {
                    if (stream == null)
                    {
                        Log.Warn("GamelistParser: could not open {Path} via P/Invoke", gamelistPath);
                        return result;
                    }

                    // Read entire file into MemoryStream to decouple from P/Invoke handle.
                    // XmlReader has issues with FileStream wrapping SafeFileHandle.
                    var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    ms.Position = 0;
                    Log.Verb("GamelistParser: loaded {Size} bytes from {Path}", ms.Length, gamelistPath);

                    using (var reader = XmlReader.Create(ms, new XmlReaderSettings { Async = false }))
                    {
                        GamelistEntry current = null;
                        string gamelistDir = Path.GetDirectoryName(gamelistPath);

                        while (reader.Read())
                        {
                            // Check EndElement BEFORE skipping non-Element nodes.
                            // ReadElementContentAsString() leaves reader at child EndElement;
                            // the parent </game> EndElement comes on next Read() call.
                            if (reader.NodeType == XmlNodeType.EndElement &&
                                (reader.Name == "game" || reader.Name == "folder") &&
                                current != null)
                            {
                                IndexEntry(result, current);
                                current = null;
                                continue;
                            }

                            if (reader.NodeType != XmlNodeType.Element) continue;

                            string tagName = reader.Name;

                            if (tagName == "game" || tagName == "folder")
                            {
                                current = new GamelistEntry();
                            }
                            else if (tagName == "path" && current != null)
                            {
                                current.RawPath = reader.ReadElementContentAsString();
                            }
                            else if (tagName == "name" && current != null)
                            {
                                current.Name = reader.ReadElementContentAsString();
                            }
                            else if (tagName == "desc" && current != null)
                            {
                                current.Description = reader.ReadElementContentAsString();
                            }
                            else if (tagName == "image" && current != null)
                            {
                                current.ImagePath = ResolveImagePath(reader.ReadElementContentAsString(), gamelistDir);
                            }
                            else if (tagName == "cover" && current != null)
                            {
                                current.CoverPath = ResolveImagePath(reader.ReadElementContentAsString(), gamelistDir);
                            }
                            else if (tagName == "thumbnail" && current != null)
                            {
                                current.ThumbnailPath = ResolveImagePath(reader.ReadElementContentAsString(), gamelistDir);
                            }
                            else if (tagName == "video" && current != null)
                            {
                                current.VideoPath = ResolveImagePath(reader.ReadElementContentAsString(), gamelistDir);
                            }
                            else if (tagName == "developer" && current != null)
                            {
                                current.Developer = reader.ReadElementContentAsString();
                            }
                            else if (tagName == "publisher" && current != null)
                            {
                                current.Publisher = reader.ReadElementContentAsString();
                            }
                            else if (tagName == "genre" && current != null)
                            {
                                current.Genre = reader.ReadElementContentAsString();
                            }
                            else if (tagName == "players" && current != null)
                            {
                                int.TryParse(reader.ReadElementContentAsString(), out int p);
                                current.Players = p;
                            }
                            else if (tagName == "rating" && current != null)
                            {
                                float.TryParse(reader.ReadElementContentAsString(),
                                    System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out float r);
                                current.Rating = r;
                            }
                            else if (tagName == "releasedate" && current != null)
                            {
                                string dateStr = reader.ReadElementContentAsString();
                                if (DateTime.TryParse(dateStr, out DateTime dt))
                                    current.ReleaseDate = dt;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("GamelistParser: failed to parse {Path}: {Error}", gamelistPath, ex.Message);
            }

            Log.Info("GamelistParser: parsed {Path} -> {Count} entries", gamelistPath, result.Count);
            return result;
        }

        /// <summary>
        /// Index a gamelist entry under multiple keys for flexible lookup.
        /// Keys: lowercase, no ./ prefix, full filename and name-without-extension.
        /// </summary>
        private static void IndexEntry(Dictionary<string, GamelistEntry> dict, GamelistEntry entry)
        {
            if (string.IsNullOrEmpty(entry.RawPath)) return;

            // Normalize: strip ./ or .\ prefix, lowercase
            string path = entry.RawPath.TrimStart('.', '/', '\\');
            if (string.IsNullOrEmpty(path)) return;

            // Key 1: full path (e.g. "Super Mario Bros (USA).nes")
            string key = NormalizeKey(path);
            if (!dict.ContainsKey(key))
                dict[key] = entry;

            // Key 2: name without extension (e.g. "Super Mario Bros (USA)")
            string nameNoExt = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrEmpty(nameNoExt))
            {
                key = NormalizeKey(nameNoExt);
                if (!dict.ContainsKey(key))
                    dict[key] = entry;
            }
        }

        private static string NormalizeKey(string s)
        {
            return s.ToLowerInvariant().Replace('\\', '/').Trim('/');
        }

        /// <summary>
        /// Resolve gamelist image paths:
        /// - "./image.png" -> relative to gamelist.xml directory
        /// - "~/image.png" -> relative to home directory (use as-is for UWP)
        /// - "/abs/path" -> absolute path (use as-is)
        /// </summary>
        private static string ResolveImagePath(string rawPath, string gamelistDir)
        {
            if (string.IsNullOrEmpty(rawPath)) return null;

            rawPath = rawPath.Trim();

            if (rawPath.StartsWith("./") || rawPath.StartsWith(".\\"))
            {
                return Path.Combine(gamelistDir, rawPath.Substring(2));
            }

            if (rawPath.StartsWith("~/"))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.Combine(home, rawPath.Substring(2));
            }

            if (Path.IsPathRooted(rawPath))
                return rawPath;

            // Fallback: treat as relative to gamelist dir
            return Path.Combine(gamelistDir, rawPath);
        }

        /// <summary>
        /// Look up a gamelist entry for a given filename.
        /// Tries: exact match, name without ext.
        /// </summary>
        public static GamelistEntry FindEntry(
            Dictionary<string, GamelistEntry> gamelist,
            string filename)
        {
            if (gamelist == null || string.IsNullOrEmpty(filename))
                return null;

            // 1. Exact match: "game.zip" or "game.nes"
            string key = NormalizeKey(filename);
            if (gamelist.TryGetValue(key, out var entry))
                return entry;

            // 2. Name without extension: "game"
            string nameNoExt = Path.GetFileNameWithoutExtension(filename);
            if (!string.IsNullOrEmpty(nameNoExt))
            {
                key = NormalizeKey(nameNoExt);
                if (gamelist.TryGetValue(key, out entry))
                    return entry;
            }

            return null;
        }

        /// <summary>
        /// Get the best available cover image path from a gamelist entry.
        /// Priority: cover > image > thumbnail.
        /// </summary>
        public static string GetCoverPath(GamelistEntry entry)
        {
            if (entry == null) return null;

            if (!string.IsNullOrEmpty(entry.CoverPath) && DirectoryScanner.FileExists(entry.CoverPath))
                return entry.CoverPath;
            if (!string.IsNullOrEmpty(entry.ImagePath) && DirectoryScanner.FileExists(entry.ImagePath))
                return entry.ImagePath;
            if (!string.IsNullOrEmpty(entry.ThumbnailPath) && DirectoryScanner.FileExists(entry.ThumbnailPath))
                return entry.ThumbnailPath;

            // Return path even if file doesn't exist (caller can decide)
            return entry.CoverPath ?? entry.ImagePath ?? entry.ThumbnailPath;
        }
    }
}
