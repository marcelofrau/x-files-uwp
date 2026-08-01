using System;
using System.Text;

namespace XFiles.FileSystem
{
    /// <summary>
    /// Pure encoding detection logic (BOM + content heuristics).
    /// Extracted from TextEditorService so it can be unit-tested without UWP
    /// dependencies (linked into tests/XFiles.Tests via <Compile Include>).
    /// </summary>
    public static class EncodingDetector
    {
        /// <summary>
        /// Detect encoding from BOM bytes and content analysis.
        /// </summary>
        public static void Detect(byte[] bytes, out Encoding encoding, out string encodingName)
        {
            // 1. BOM detection
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                encoding = new UTF8Encoding(true); // UTF-8 with BOM
                encodingName = "UTF-8";
                return;
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                if (bytes.Length >= 4 && bytes[2] == 0x00 && bytes[3] == 0x00)
                {
                    encoding = Encoding.UTF32;
                    encodingName = "UTF-32 LE";
                    return;
                }
                encoding = Encoding.Unicode; // UTF-16 LE
                encodingName = "UTF-16 LE";
                return;
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                encoding = Encoding.BigEndianUnicode; // UTF-16 BE
                encodingName = "UTF-16 BE";
                return;
            }

            if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
            {
                encoding = Encoding.GetEncoding(12001); // UTF-32 BE
                encodingName = "UTF-32 BE";
                return;
            }

            // 2. Null-byte heuristic → likely UTF-16
            int nullCount = 0;
            int checkLen = Math.Min(bytes.Length, 512);
            for (int i = 0; i < checkLen; i++)
            {
                if (bytes[i] == 0x00) nullCount++;
            }

            if (nullCount > 0)
            {
                // Try UTF-16 LE
                try
                {
                    var utf16le = Encoding.Unicode;
                    string test = utf16le.GetString(bytes, 0, Math.Min(bytes.Length, 1024));
                    int validCount = 0;
                    foreach (char c in test)
                    {
                        if (c == '\t' || c == '\n' || c == '\r' ||
                            (c >= 0x20 && c <= 0x7E) ||
                            (c >= 0xA0 && c <= 0xFFFD))
                            validCount++;
                    }

                    if (validCount > test.Length * 0.8)
                    {
                        encoding = utf16le;
                        encodingName = "UTF-16 LE";
                        return;
                    }
                }
                catch { }
            }

            // 3. UTF-8 validation
            if (IsValidUtf8(bytes))
            {
                encoding = new UTF8Encoding(false); // UTF-8 without BOM
                encodingName = "UTF-8";
                return;
            }

            // 4. Fallback: Windows-1252
            encoding = Encoding.GetEncoding(1252);
            encodingName = "Windows-1252";
        }

        /// <summary>
        /// Check if byte array is valid UTF-8.
        /// </summary>
        public static bool IsValidUtf8(byte[] bytes)
        {
            int i = 0;
            int len = bytes.Length;
            while (i < len)
            {
                byte b = bytes[i];
                int seqLen;

                if (b <= 0x7F) { seqLen = 1; }
                else if ((b & 0xE0) == 0xC0) { seqLen = 2; }
                else if ((b & 0xF0) == 0xE0) { seqLen = 3; }
                else if ((b & 0xF8) == 0xF0) { seqLen = 4; }
                else { return false; } // Invalid start byte

                if (i + seqLen > len) return false;

                // Check continuation bytes
                for (int j = 1; j < seqLen; j++)
                {
                    if ((bytes[i + j] & 0xC0) != 0x80) return false;
                }

                // Reject overlong sequences
                if (seqLen == 2 && (b & 0x1E) == 0) return false;
                if (seqLen == 3 && (b & 0x0F) < 2) return false;
                if (seqLen == 4 && (b & 0x07) < 1) return false;

                i += seqLen;
            }

            return true;
        }
    }
}
