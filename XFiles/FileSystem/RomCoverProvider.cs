using System;
using System.Collections.Generic;

namespace XFiles.FileSystem
{
    public static class RomCoverProvider
    {
        /// <summary>
        /// Build title variations for LibRetro matching.
        /// No-Intro names like "Super Mario Bros (USA)" may not match LibRetro naming.
        /// </summary>
        public static List<string> BuildTitleVariations(string title)
        {
            var variations = new List<string>();
            string clean = title.Replace("/", " -").Replace("\\", " -").Trim();
            variations.Add(clean);

            // Progressively strip all parenthesized/bracketed groups from right to left
            // "Alien Brigade (1990) (Atari) [!]" → "Alien Brigade (1990) (Atari)" → "Alien Brigade (1990)" → "Alien Brigade"
            string current = clean;
            while (current.Length > 0)
            {
                // Strip last (...) group
                int lastParen = current.LastIndexOf('(');
                int lastBracket = current.LastIndexOf('[');
                int stripIdx = Math.Max(lastParen, lastBracket);
                if (stripIdx <= 0) break;

                string stripped = current.Substring(0, stripIdx).TrimEnd();
                if (stripped.Length == 0 || stripped == current) break;
                current = stripped;
                variations.Add(current);
            }

            // Try base name without trailing dot
            if (current.Length > 0 && current != clean)
                variations.Add(current + ".");

            // Deduplicate (keep order)
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var v in variations)
            {
                if (seen.Add(v))
                    result.Add(v);
            }
            return result;
        }

        public static readonly Dictionary<string, string> LibRetroSystemNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["NES"] = "Nintendo - Nintendo Entertainment System",
            ["SNES"] = "Nintendo - Super Nintendo Entertainment System",
            ["Game Boy"] = "Nintendo - Game Boy",
            ["Game Boy Color"] = "Nintendo - Game Boy Color",
            ["GBA"] = "Nintendo - Game Boy Advance",
            ["Genesis"] = "Sega - Mega Drive - Genesis",
            ["Master System"] = "Sega - Master System",
            ["Game Gear"] = "Sega - Game Gear",
            ["PC Engine"] = "NEC - PC Engine",
            ["Atari 2600"] = "Atari - 2600",
            ["Atari 5200"] = "Atari - 5200",
            ["Atari 7800"] = "Atari - 7800",
            ["Atari Jaguar"] = "Atari - Jaguar",
            ["Atari Lynx"] = "Atari - Lynx",
            ["ColecoVision"] = "Coleco - ColecoVision",
            ["Intellivision"] = "Mattel - Intellivision",
            ["SG-1000"] = "Sega - SG-1000",
            ["Vectrex"] = "GCE - Vectrex",
            ["Nintendo 64"] = "Nintendo - Nintendo 64",
            ["Nintendo DS"] = "Nintendo - Nintendo DS",
            ["Nintendo 3DS"] = "Nintendo - Nintendo 3DS",
            ["Virtual Boy"] = "Nintendo - Virtual Boy",
            ["GameCube"] = "Nintendo - GameCube",
            ["Dreamcast"] = "Sega - Dreamcast",
            ["Saturn"] = "Sega - Saturn",
            ["PlayStation"] = "Sony - PlayStation",
            ["Neo Geo Pocket"] = "SNK - Neo Geo Pocket",
            ["Neo Geo"] = "SNK - Neo Geo",
            ["WonderSwan"] = "Bandai - WonderSwan",
            ["Sega 32X"] = "Sega - Sega 32X",
            ["Sega CD"] = "Sega - Sega CD",
            ["Wii"] = "Nintendo - Wii",
            ["PSP"] = "Sony - PlayStation Portable",
            ["Switch"] = "Nintendo - Switch",
        };
    }
}
