using System;
using System.Collections.Generic;

namespace XFiles.FileSystem
{
    /// <summary>
    /// Parse ROM headers to extract game title and system name.
    /// Supports NES, SNES, Game Boy, Game Boy Color, GBA, Genesis/Mega Drive,
    /// Master System, Game Gear, PC Engine/TurboGrafx-16, Atari 2600/5200/7800/Jaguar/Lynx,
    /// ColecoVision, Intellivision, Sega SG-1000, MSX, ZX Spectrum, Vectrex,
    /// N64, NDS, 3DS, Virtual Boy, Neo Geo Pocket, WonderSwan.
    /// </summary>
    public static class RomHeaderParser
    {
        private static readonly HashSet<string> RomExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".nes", ".sfc",
                ".gb", ".gbc", ".gba",
                ".gen", ".md",
                ".sms", ".gg",
                ".pce", ".tg16",
                ".a26", ".a52", ".a78",
                ".j64", ".jag",
                ".lnx",
                ".col", ".int",
                ".sg", ".msx",
                ".sna", ".z80",
                ".vec",
                ".n64", ".z64", ".v64",
                ".nds",
                ".3ds",
                ".vb",
                ".ngp", ".ngc",
                ".ws", ".wsc",
                ".gcm",
                ".gdi", ".cdi",
                ".chd"
            };

        public static bool IsRomFile(string ext)
        {
            return !string.IsNullOrEmpty(ext) && RomExtensions.Contains(ext);
        }

        /// <summary>
        /// Try to parse a ROM header from raw bytes.
        /// Returns true if a valid title was extracted; false means use filename fallback.
        /// </summary>
        public static bool TryParseTitle(byte[] headerBytes, string ext,
            out string title, out string system)
        {
            title = null;
            system = null;

            if (headerBytes == null || headerBytes.Length < 16)
                return false;

            switch (ext.ToLowerInvariant())
            {
                case ".nes":
                    return TryParseNes(headerBytes, out title, out system);
                case ".sfc":
                    return TryParseSnes(headerBytes, out title, out system);
                case ".gb":
                    return TryParseGameBoy(headerBytes, false, out title, out system);
                case ".gbc":
                    return TryParseGameBoy(headerBytes, true, out title, out system);
                case ".gba":
                    return TryParseGba(headerBytes, out title, out system);
                case ".gen":
                case ".md":
                    return TryParseGenesis(headerBytes, out title, out system);
                case ".sms":
                    return TryParseMasterSystem(headerBytes, out title, out system);
                case ".gg":
                    return TryParseGameGear(headerBytes, out title, out system);
                case ".pce":
                case ".tg16":
                    return TryParsePcEngine(headerBytes, out title, out system);
                case ".a26":
                    return TryParseAtari2600(headerBytes, out title, out system);
                case ".a52":
                    return TryParseAtari5200(headerBytes, out title, out system);
                case ".a78":
                    return TryParseAtari7800(headerBytes, out title, out system);
                case ".j64":
                case ".jag":
                    return TryParseJaguar(headerBytes, out title, out system);
                case ".lnx":
                    return TryParseLynx(headerBytes, out title, out system);
                case ".col":
                    return TryParseColecoVision(headerBytes, out title, out system);
                case ".int":
                    return TryParseIntellivision(headerBytes, out title, out system);
                case ".sg":
                    return TryParseSg1000(headerBytes, out title, out system);
                case ".msx":
                    return TryParseMsx(headerBytes, out title, out system);
                case ".sna":
                    return TryParseSpectrumSna(headerBytes, out title, out system);
                case ".z80":
                    return TryParseSpectrumZ80(headerBytes, out title, out system);
                case ".vec":
                    return TryParseVectrex(headerBytes, out title, out system);
                case ".n64":
                case ".z64":
                case ".v64":
                    return TryParseN64(headerBytes, ext, out title, out system);
                case ".nds":
                    return TryParseNds(headerBytes, out title, out system);
                case ".3ds":
                    system = "Nintendo 3DS";
                    title = null;
                    return true;
                case ".vb":
                    system = "Virtual Boy";
                    title = null;
                    return true;
                case ".ngp":
                case ".ngc":
                    return TryParseNeoGeoPocket(headerBytes, out title, out system);
                case ".ws":
                case ".wsc":
                    system = "WonderSwan";
                    title = null;
                    return true;
                case ".gcm":
                    system = "GameCube";
                    title = null;
                    return true;
                case ".gdi":
                case ".cdi":
                    system = "Dreamcast";
                    title = null;
                    return true;
                case ".chd":
                    system = "ROM";
                    title = null;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryParseNes(byte[] data, out string title, out string system)
        {
            system = "NES";
            title = null;

            // iNES magic: "NES" + 0x1A at offset 0x000
            if (data.Length < 16 ||
                data[0] != 0x4E || data[1] != 0x45 || data[2] != 0x53 || data[3] != 0x1A)
                return false;

            // Title at 0x010, 16 bytes, ASCII padded with 0x00
            title = ExtractAscii(data, 0x010, 16);
            return !string.IsNullOrEmpty(title);
        }

        private static bool TryParseSnes(byte[] data, out string title, out string system)
        {
            system = "SNES";

            // Try HiROM (0xFFC0) first — more common in modern games
            if (TryExtractSnesTitle(data, 0xFFC0, out title))
                return true;

            // Try LoROM (0x7FC0)
            if (TryExtractSnesTitle(data, 0x7FC0, out title))
                return true;

            return false;
        }

        private static bool TryExtractSnesTitle(byte[] data, int offset, out string title)
        {
            title = null;

            // SNES title: 21 bytes, ASCII (0x20-0x7E), padded with 0x20
            if (data.Length < offset + 21)
                return false;

            int validCount = 0;
            int totalNonZero = 0;

            for (int i = 0; i < 21; i++)
            {
                byte b = data[offset + i];
                if (b == 0x00 || b == 0x20)
                    continue;
                totalNonZero++;
                if (b >= 0x20 && b <= 0x7E)
                    validCount++;
            }

            // Must have at least some non-zero bytes and they must be printable ASCII
            if (totalNonZero == 0 || validCount < totalNonZero)
                return false;

            // Must have at least 2 meaningful characters
            if (validCount < 2)
                return false;

            title = ExtractAscii(data, offset, 21);
            return !string.IsNullOrEmpty(title);
        }

        private static bool TryParseGameBoy(byte[] data, bool isColor,
            out string title, out string system)
        {
            system = isColor ? "Game Boy Color" : "Game Boy";
            title = null;

            // Game Boy title at 0x134, 11 bytes ASCII
            // CGB flag at 0x143: 0x80 = DMG+CGB, 0xC0 = CGB only
            if (data.Length < 0x144)
                return false;

            title = ExtractAscii(data, 0x134, 11);
            return !string.IsNullOrEmpty(title);
        }

        private static bool TryParseGba(byte[] data, out string title, out string system)
        {
            system = "GBA";
            title = null;

            // GBA title at 0xA0, 12 bytes ASCII
            if (data.Length < 0xAC)
                return false;

            title = ExtractAscii(data, 0xA0, 12);
            return !string.IsNullOrEmpty(title);
        }

        private static bool TryParseGenesis(byte[] data, out string title, out string system)
        {
            system = "Genesis/Mega Drive";
            title = null;

            // Genesis title at 0x120, 48 bytes
            // Each character is 2 bytes big-endian (high byte usually 0x00, low byte = ASCII)
            // We use only the low byte of each pair
            if (data.Length < 0x150)
                return false;

            var chars = new char[24]; // 48 bytes / 2 = 24 characters
            int validCount = 0;

            for (int i = 0; i < 24; i++)
            {
                byte low = data[0x120 + i * 2 + 1];
                if (low >= 0x20 && low <= 0x7E)
                {
                    chars[validCount] = (char)low;
                    validCount++;
                }
                else if (low == 0x00)
                {
                    break; // padding
                }
            }

            if (validCount < 2)
                return false;

            title = new string(chars, 0, validCount).Trim();
            return title.Length >= 2;
        }

        private static bool TryParseMasterSystem(byte[] data, out string title, out string system)
        {
            system = "Master System";
            return TryParseSegaConsole(data, 0x7FF0, 32, out title);
        }

        private static bool TryParseGameGear(byte[] data, out string title, out string system)
        {
            system = "Game Gear";
            return TryParseSegaConsole(data, 0x7FF0, 32, out title);
        }

        private static bool TryParseSegaConsole(byte[] data, int offset, int maxLen,
            out string title)
        {
            title = null;

            if (data.Length < offset + maxLen)
                return false;

            title = ExtractAscii(data, offset, maxLen);
            return !string.IsNullOrEmpty(title);
        }

        private static bool TryParsePcEngine(byte[] data, out string title, out string system)
        {
            system = "PC Engine/TurboGrafx-16";
            title = null;

            // PC Engine title at 0x120, 32 bytes ASCII
            if (data.Length < 0x140)
                return false;

            title = ExtractAscii(data, 0x120, 32);
            return !string.IsNullOrEmpty(title);
        }

        private static bool TryParseAtari2600(byte[] data, out string title, out string system)
        {
            system = "Atari 2600";
            title = null;
            return true;
        }

        private static bool TryParseAtari5200(byte[] data, out string title, out string system)
        {
            system = "Atari 5200";
            title = null;
            return true;
        }

        private static bool TryParseAtari7800(byte[] data, out string title, out string system)
        {
            system = "Atari 7800";
            title = null;

            // Atari 7800 optional header: 0x01 + "ATARI7800" at offset 1
            if (data.Length >= 127 &&
                data[0] == 0x01 &&
                data[1] == 'A' && data[2] == 'T' && data[3] == 'A' &&
                data[4] == 'R' && data[5] == 'I' && data[6] == '7' &&
                data[7] == '8' && data[8] == '0' && data[9] == '0')
            {
                title = ExtractAscii(data, 17, 30);
            }
            return true;
        }

        private static bool TryParseColecoVision(byte[] data, out string title, out string system)
        {
            system = "ColecoVision";
            title = null;
            return true;
        }

        private static bool TryParseIntellivision(byte[] data, out string title, out string system)
        {
            system = "Intellivision";
            title = null;
            return true;
        }

        private static bool TryParseSg1000(byte[] data, out string title, out string system)
        {
            system = "SG-1000";
            title = null;
            return true;
        }

        private static bool TryParseMsx(byte[] data, out string title, out string system)
        {
            system = "MSX";
            title = null;

            // MSX ROM header: "AB" at offset 0, title at 0x10, 6 bytes
            if (data.Length >= 0x16 && data[0] == 'A' && data[1] == 'B')
            {
                title = ExtractAscii(data, 0x10, 6);
            }
            return true;
        }

        private static bool TryParseJaguar(byte[] data, out string title, out string system)
        {
            system = "Atari Jaguar";
            title = null;

            // Jaguar ROM header: game name at offset 0x18, 32 bytes ASCII
            if (data.Length >= 0x38)
            {
                title = ExtractAscii(data, 0x18, 32);
            }
            return true;
        }

        private static bool TryParseLynx(byte[] data, out string title, out string system)
        {
            system = "Atari Lynx";
            title = null;

            // Lynx cartridge header: cart name at offset 10, 32 bytes
            if (data.Length >= 42)
            {
                title = ExtractAscii(data, 10, 32);
            }
            return true;
        }

        private static bool TryParseSpectrumSna(byte[] data, out string title, out string system)
        {
            system = "ZX Spectrum";
            title = null;
            // SNA is a raw Z80 snapshot — no title, just return system
            return true;
        }

        private static bool TryParseSpectrumZ80(byte[] data, out string title, out string system)
        {
            system = "ZX Spectrum";
            title = null;

            // Z80 snapshot: header is 30 bytes, no title field
            // But we can detect version by extra header block
            return true;
        }

        private static bool TryParseVectrex(byte[] data, out string title, out string system)
        {
            system = "Vectrex";
            title = null;
            return true;
        }

        private static bool TryParseN64(byte[] data, string ext, out string title, out string system)
        {
            system = "Nintendo 64";
            title = null;

            if (data.Length < 0x40)
                return false;

            // N64 has different byte orderings:
            // .z64 (big-endian): 80 37 12 40
            // .n64 (byte-swapped): 37 80 40 12
            // .v64 (word-swapped): 40 12 37 80
            // Title is at offset 0x20, 20 bytes ASCII in all formats,
            // but for byte-swapped/word-swapped we need to un-swap
            byte[] header = data;
            if (data[0] == 0x37 && data[1] == 0x80)
            {
                // .n64 byte-swapped: swap each pair
                header = new byte[data.Length];
                Array.Copy(data, header, Math.Min(data.Length, 0x40));
                for (int i = 0; i < 0x3E; i += 2)
                {
                    byte tmp = header[i];
                    header[i] = header[i + 1];
                    header[i + 1] = tmp;
                }
            }
            else if (data[0] == 0x40 && data[1] == 0x12)
            {
                // .v64 word-swapped: swap 4-byte words
                header = new byte[data.Length];
                Array.Copy(data, header, Math.Min(data.Length, 0x40));
                for (int i = 0; i < 0x3C; i += 4)
                {
                    byte tmp0 = header[i]; header[i] = header[i + 3]; header[i + 3] = tmp0;
                    byte tmp1 = header[i + 1]; header[i + 1] = header[i + 2]; header[i + 2] = tmp1;
                }
            }

            // Verify big-endian magic: 80 37 12 40
            if (header[0] != 0x80 || header[1] != 0x37 || header[2] != 0x12 || header[3] != 0x40)
                return true; // System detected, no title

            title = ExtractAscii(header, 0x20, 20);
            return true;
        }

        private static bool TryParseNds(byte[] data, out string title, out string system)
        {
            system = "Nintendo DS";
            title = null;

            // NDS header: game title at 0x00, 12 bytes
            if (data.Length < 12)
                return false;

            title = ExtractAscii(data, 0x00, 12);
            return true;
        }

        private static bool TryParseNeoGeoPocket(byte[] data, out string title, out string system)
        {
            system = "Neo Geo Pocket";
            title = null;

            // NGP header: title at 0x20, 16 bytes ASCII
            if (data.Length < 0x30)
                return false;

            title = ExtractAscii(data, 0x20, 16);
            return true;
        }

        /// <summary>
        /// Extract ASCII string from byte array, trimming null bytes and spaces.
        /// Only includes printable characters (0x20-0x7E).
        /// </summary>
        private static string ExtractAscii(byte[] data, int offset, int maxLen)
        {
            int end = Math.Min(offset + maxLen, data.Length);
            int validCount = 0;

            for (int i = offset; i < end; i++)
            {
                byte b = data[i];
                if (b >= 0x20 && b <= 0x7E)
                    validCount++;
            }

            if (validCount < 2)
                return null;

            var chars = new char[validCount];
            int idx = 0;

            for (int i = offset; i < end && idx < validCount; i++)
            {
                byte b = data[i];
                if (b >= 0x20 && b <= 0x7E)
                    chars[idx++] = (char)b;
            }

            return new string(chars).Trim();
        }
    }
}
