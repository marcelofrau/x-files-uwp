using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace XFiles.FileSystem
{
    public enum FileTier
    {
        FullEdit,
        ReadOnly
    }

    public enum LineEndingStyle
    {
        LF,
        CRLF,
        CR
    }

    public class TextEditorLoadResult
    {
        public string Text { get; set; }
        public Encoding DetectedEncoding { get; set; }
        public long FileSize { get; set; }
        public FileTier Tier { get; set; }
        public bool IsBinary { get; set; }
        public LineEndingStyle LineEnding { get; set; }
        public string EncodingName { get; set; }
    }

    /// <summary>
    /// File I/O + encoding detection for the text editor.
    /// Uses Win2 P/Invoke (CreateFile2FromAppW + WriteFile) for UWP sandbox compatibility.
    /// </summary>
    public static class TextEditorService
    {
        public const long FullEditMaxBytes = 4 * 1024 * 1024;
        public const int SyncDebounceMs = 50;

        #region P/Invoke

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint FILE_SHARE_DELETE = 0x00000004;
        private const uint OPEN_EXISTING = 3;
        private const uint CREATE_ALWAYS = 2;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [DllImport("api-ms-win-core-file-fromapp-l1-1-0.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileFromAppW(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition,
            uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(
            IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToRead,
            out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(
            IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite,
            out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileSizeEx(IntPtr hFile, out long lpFileSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        #endregion

        /// <summary>
        /// Load a text file for editing. Detects encoding, file tier, line endings.
        /// Returns null on failure (file not found, permission denied, etc).
        /// </summary>
        public static async Task<TextEditorLoadResult> LoadAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    byte[] rawBytes = ReadFileWin32(filePath, 0);
                    if (rawBytes == null || rawBytes.Length == 0)
                    {
                        Log.Warn("TextEditorService.Load: cannot read {Path}", filePath);
                        return null;
                    }

                    long fileSize = rawBytes.Length;
                    FileTier tier = GetFileTier(fileSize);

                    // Binary detection: null byte in first 8KB (unless UTF-16 BOM)
                    bool isBinary = IsBinaryContent(rawBytes);

                    Encoding encoding;
                    string encodingName;
                    EncodingDetector.Detect(rawBytes, out encoding, out encodingName);

                    string text = encoding.GetString(rawBytes);

                    // Strip BOM from text if present
                    if (text.Length > 0 && text[0] == '\uFEFF')
                    {
                        text = text.Substring(1);
                    }

                    LineEndingStyle lineEnding = DetectLineEnding(text);

                    Log.Info("TextEditorService.Load: {Path} — {Size} bytes, {Encoding}, tier={Tier}, binary={Binary}, lineEnding={LineEnding}",
                        filePath, fileSize, encodingName, tier, isBinary, lineEnding);

                    return new TextEditorLoadResult
                    {
                        Text = text,
                        DetectedEncoding = encoding,
                        FileSize = fileSize,
                        Tier = tier,
                        IsBinary = isBinary,
                        LineEnding = lineEnding,
                        EncodingName = encodingName
                    };
                }
                catch (Exception ex)
                {
                    Log.Warn("TextEditorService.Load exception", ex);
                    return null;
                }
            });
        }

        /// <summary>
        /// Save text content to file. Always writes UTF-8 with BOM.
        /// Converts line endings back to the detected style.
        /// </summary>
        public static async Task<bool> SaveAsync(string filePath, string content, LineEndingStyle lineEnding = LineEndingStyle.LF)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Convert line endings back to original style
                    if (lineEnding == LineEndingStyle.CRLF)
                    {
                        content = content.Replace("\n", "\r\n");
                    }
                    else if (lineEnding == LineEndingStyle.CR)
                    {
                        content = content.Replace("\n", "\r");
                    }

                    // UTF-8 with BOM
                    byte[] bom = new byte[] { 0xEF, 0xBB, 0xBF };
                    byte[] utf8Bytes = Encoding.UTF8.GetBytes(content);
                    byte[] output = new byte[bom.Length + utf8Bytes.Length];
                    Buffer.BlockCopy(bom, 0, output, 0, bom.Length);
                    Buffer.BlockCopy(utf8Bytes, 0, output, bom.Length, utf8Bytes.Length);

                    bool ok = WriteFileWin32(filePath, output);
                    if (ok)
                    {
                        Log.Info("TextEditorService.Save: {Path} — {Bytes} bytes written (UTF-8 BOM)", filePath, output.Length);
                    }
                    else
                    {
                        Log.Warn("TextEditorService.Save: write failed for {Path}", filePath);
                    }
                    return ok;
                }
                catch (Exception ex)
                {
                    Log.Warn("TextEditorService.Save exception", ex);
                    return false;
                }
            });
        }

        /// <summary>
        /// Determine file tier based on size.
        /// </summary>
        public static FileTier GetFileTier(long size)
        {
            if (size <= FullEditMaxBytes) return FileTier.FullEdit;
            return FileTier.ReadOnly;
        }

        /// <summary>
        /// Get human-readable tier description.
        /// </summary>
        public static string GetTierDescription(FileTier tier)
        {
            switch (tier)
            {
                case FileTier.FullEdit: return "Full edit";
                case FileTier.ReadOnly: return "Read-only";
                default: return "";
            }
        }

        /// <summary>
        /// Detect dominant line ending style in text content.
        /// </summary>
        public static LineEndingStyle DetectLineEnding(string text)
        {
            int crlf = 0, lf = 0, cr = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        crlf++;
                        i++; // skip \n
                    }
                    else
                    {
                        cr++;
                    }
                }
                else if (text[i] == '\n')
                {
                    lf++;
                }
            }

            if (crlf > lf && crlf > cr) return LineEndingStyle.CRLF;
            if (cr > lf && cr > crlf) return LineEndingStyle.CR;
            return LineEndingStyle.LF;
        }

        /// <summary>
        /// Get highlight.js language name from file extension.
        /// Reuses the same mapping as MillerColumnsPage.GetHighlightLang.
        /// </summary>
        public static string GetHighlightLang(string extension)
        {
            if (string.IsNullOrEmpty(extension)) return "";
            string ext = extension.TrimStart('.').ToLowerInvariant();

            switch (ext)
            {
                case "js": case "mjs": case "cjs": return "javascript";
                case "ts": case "tsx": return "typescript";
                case "jsx": return "javascript";
                case "cs": return "csharp";
                case "rb": return "ruby";
                case "kt": case "kts": return "kotlin";
                case "rs": return "rust";
                case "sh": case "bash": case "zsh": case "fish": return "bash";
                case "ps1": case "psm1": case "psd1": return "powershell";
                case "yml": return "yaml";
                case "md": case "markdown": return "markdown";
                case "html": case "htm": case "xhtml": return "html";
                case "py": case "pyw": case "pyi": return "python";
                case "sql": return "sql";
                case "go": return "go";
                case "java": return "java";
                case "lua": return "lua";
                case "pl": case "pm": return "perl";
                case "swift": return "swift";
                case "dart": return "dart";
                case "r": return "r";
                case "css": return "css";
                case "scss": return "scss";
                case "less": return "less";
                case "xml": return "xml";
                case "json": case "jsonc": case "json5": return "json";
                case "tex": case "latex": return "latex";
                case "dockerfile": return "dockerfile";
                case "ini": case "cfg": case "conf": return "ini";
                case "toml": return "toml";
                case "c": case "h": return "c";
                case "cpp": case "cc": case "cxx": case "hpp": case "hxx": return "cpp";
                case "fs": case "fsx": case "fsi": return "fsharp";
                case "vb": return "vbnet";
                case "proto": return "protobuf";
                case "graphql": case "gql": return "graphql";
                default: return "";
            }
        }

        /// <summary>
        /// Format file size as human-readable string.
        /// </summary>
        public static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024):F1} MB";
        }

        #region Private helpers

        /// <summary>
        /// Check if content is likely binary (null bytes in first 8KB, excluding UTF-16 BOM).
        /// </summary>
        private static bool IsBinaryContent(byte[] bytes)
        {
            // If it has a recognized BOM, it's not binary
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return false;
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return false;
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return false;

            int checkLen = Math.Min(bytes.Length, 8192);
            for (int i = 0; i < checkLen; i++)
            {
                if (bytes[i] == 0x00) return true;
            }
            return false;
        }

        /// <summary>
        /// Read file via Win32 P/Invoke. If maxBytes is 0, reads entire file.
        /// </summary>
        private static byte[] ReadFileWin32(string filePath, long maxBytes)
        {
            IntPtr hFile = CreateFileFromAppW(filePath, GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);

            if (hFile == INVALID_HANDLE_VALUE)
            {
                int err = Marshal.GetLastWin32Error();
                Log.Warn("TextEditorService: CreateFileFromAppW failed for '{Path}' (error {Error})", filePath, err);
                return null;
            }

            try
            {
                long fileSize;
                if (!GetFileSizeEx(hFile, out fileSize))
                {
                    Log.Warn("TextEditorService: GetFileSizeEx failed for '{Path}'", filePath);
                    return null;
                }

                long bytesToRead = (maxBytes > 0 && fileSize > maxBytes) ? maxBytes : fileSize;
                if (bytesToRead <= 0) return new byte[0];

                byte[] buffer = new byte[bytesToRead];
                uint totalRead = 0;

                while (totalRead < bytesToRead)
                {
                    uint bytesRead;
                    uint chunk = (uint)Math.Min(bytesToRead - totalRead, int.MaxValue);

                    if (!ReadFile(hFile, buffer, chunk, out bytesRead, IntPtr.Zero) || bytesRead == 0)
                        break;

                    totalRead += bytesRead;
                }

                if (totalRead < buffer.Length)
                {
                    byte[] trimmed = new byte[totalRead];
                    Array.Copy(buffer, trimmed, totalRead);
                    return trimmed;
                }

                return buffer;
            }
            finally
            {
                CloseHandle(hFile);
            }
        }

        /// <summary>
        /// Write bytes to file via Win32 P/Invoke. Creates or overwrites.
        /// </summary>
        private static bool WriteFileWin32(string filePath, byte[] data)
        {
            IntPtr hFile = CreateFileFromAppW(filePath, GENERIC_WRITE, 0,
                IntPtr.Zero, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);

            if (hFile == INVALID_HANDLE_VALUE)
            {
                int err = Marshal.GetLastWin32Error();
                Log.Warn("TextEditorService: CreateFileFromAppW write failed for '{Path}' (error {Error})", filePath, err);
                return false;
            }

            try
            {
                uint totalWritten = 0;
                while (totalWritten < data.Length)
                {
                    uint bytesToWrite = (uint)Math.Min(data.Length - totalWritten, int.MaxValue);
                    uint bytesWritten;

                    if (!WriteFile(hFile, data, bytesToWrite, out bytesWritten, IntPtr.Zero) || bytesWritten == 0)
                    {
                        int err = Marshal.GetLastWin32Error();
                        Log.Warn("TextEditorService: WriteFile failed for '{Path}' (error {Error})", filePath, err);
                        return false;
                    }

                    totalWritten += bytesWritten;
                }

                return true;
            }
            finally
            {
                CloseHandle(hFile);
            }
        }

        #endregion
    }
}
