using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace XFiles.FileSystem
{
    public class Id3Tag
    {
        public string Title;
        public string Artist;
        public string Album;
        public string Genre;
        public string Year;
        public string TrackNumber;
        public int DurationSeconds;
        public byte[] AlbumArt;
        public string AlbumArtMime;

        private const int MaxTagSize = 2 * 1024 * 1024;

        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint FILE_SHARE_DELETE = 0x00000004;
        private const uint OPEN_EXISTING = 3;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [DllImport("api-ms-win-core-file-fromapp-l1-1-0.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileFromAppW(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition,
            uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(IntPtr hFile, byte[] lpBuffer,
            uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileSizeEx(IntPtr hFile, out long lpFileSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private static byte[] ReadFileBytes(string path, int maxBytes)
        {
            IntPtr hFile = CreateFileFromAppW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, IntPtr.Zero,
                OPEN_EXISTING, 0, IntPtr.Zero);
            if (hFile == INVALID_HANDLE_VALUE) return null;
            try
            {
                long size;
                if (!GetFileSizeEx(hFile, out size)) return null;
                int toRead = (int)Math.Min(size, maxBytes);
                byte[] buf = new byte[toRead];
                uint totalRead = 0;
                while (totalRead < toRead)
                {
                    uint bytesRead;
                    if (!ReadFile(hFile, buf, (uint)(toRead - totalRead), out bytesRead, IntPtr.Zero)
                        || bytesRead == 0) break;
                    totalRead += bytesRead;
                }
                if (totalRead == toRead) return buf;
                byte[] trimmed = new byte[totalRead];
                Array.Copy(buf, trimmed, totalRead);
                return trimmed;
            }
            finally { CloseHandle(hFile); }
        }

        public static Id3Tag ReadFromFile(string filePath)
        {
            byte[] header = ReadFileBytes(filePath, 10);
            if (header == null || header.Length < 10)
            {
                Log.Warning("Id3Tag: failed to read header from {Path} (bytes={Bytes})", filePath, header?.Length);
                return null;
            }
            if (header[0] != 'I' || header[1] != 'D' || header[2] != '3')
            {
                Log.Verbose("Id3Tag: no ID3v2 header in {Path} (first bytes: {B0} {B1} {B2})", filePath, header[0], header[1], header[2]);
                return null;
            }

            int id3Version = header[3]; // 3 = ID3v2.3, 4 = ID3v2.4
            int tagSize = SynchsafeToInt(header, 6);
            Log.Information("Id3Tag: ID3v2.{Version}, tagSize={Size}, reading {Read} bytes from {Path}",
                id3Version, tagSize, tagSize + 10, filePath);
            if (tagSize <= 0 || tagSize > MaxTagSize) return null;

            byte[] tagData = ReadFileBytes(filePath, tagSize + 10);
            if (tagData == null)
            {
                Log.Warning("Id3Tag: ReadFileBytes returned null for {Path}", filePath);
                return null;
            }
            Log.Information("Id3Tag: read {Length} bytes, parsing frames from offset 10", tagData.Length);

            var tag = ParseTag(tagData, tagSize, id3Version);
            Log.Information("Id3Tag: title={Title} artist={Artist} album={Album} genre={Genre} year={Year} track={Track} dur={Dur}s art={HasArt} in {Path}",
                tag?.Title, tag?.Artist, tag?.Album, tag?.Genre, tag?.Year, tag?.TrackNumber, tag?.DurationSeconds, tag?.AlbumArt != null, filePath);
            return tag;
        }

        private static Id3Tag ParseTag(byte[] data, int tagSize, int id3Version)
        {
            var tag = new Id3Tag();
            int pos = 10;
            int end = Math.Min(data.Length, 10 + tagSize);
            int frameCount = 0;

            while (pos + 10 <= end)
            {
                string frameId = Encoding.ASCII.GetString(data, pos, 4);
                if (data[pos] == 0) break;

                // ID3v2.3: big-endian. ID3v2.4: synchsafe.
                int frameSize = id3Version >= 4
                    ? SynchsafeToInt(data, pos + 4)
                    : (data[pos + 4] << 24) | (data[pos + 5] << 16) | (data[pos + 6] << 8) | data[pos + 7];

                Log.Verbose("Id3Tag: frame[{Count}] id={Id} size={Size} at pos={Pos}", frameCount, frameId, frameSize, pos);

                if (frameSize <= 0 || pos + 10 + frameSize > end) break;

                byte[] frameData = new byte[frameSize];
                Array.Copy(data, pos + 10, frameData, 0, frameSize);

                if (frameId == "TIT2")
                    tag.Title = ReadTextFrame(frameData);
                else if (frameId == "TPE1")
                    tag.Artist = ReadTextFrame(frameData);
                else if (frameId == "TALB")
                    tag.Album = ReadTextFrame(frameData);
                else if (frameId == "TCON")
                    tag.Genre = ReadTextFrame(frameData);
                else if (frameId == "TYER" || frameId == "TDRC")
                    tag.Year = ReadTextFrame(frameData);
                else if (frameId == "TRCK")
                    tag.TrackNumber = ReadTextFrame(frameData);
                else if (frameId == "TLEN")
                    tag.DurationSeconds = ParseDurationFrame(frameData);
                else if (frameId == "APIC" && tag.AlbumArt == null)
                    ReadApicFrame(frameData, tag);

                pos += 10 + frameSize;
                frameCount++;
            }

            Log.Information("Id3Tag: parsed {Count} frames, end={End}", frameCount, end);
            return tag;
        }

        private static string ReadTextFrame(byte[] data)
        {
            if (data.Length < 2) return null;
            byte encoding = data[0];
            int start = 1;
            int end = data.Length;

            // Find null terminator (single 0x00 for ISO/UTF-8, double 00 00 for UTF-16)
            if (encoding == 1 || encoding == 2)
            {
                for (int i = start; i + 1 < end; i += 2)
                {
                    if (data[i] == 0 && data[i + 1] == 0) { end = i; break; }
                }
            }
            else
            {
                for (int i = start; i < end; i++)
                {
                    if (data[i] == 0) { end = i; break; }
                }
            }

            if (end <= start) return null;
            int len = end - start;

            string text;
            if (encoding == 0)
                text = Encoding.GetEncoding("iso-8859-1").GetString(data, start, len);
            else if (encoding == 1)
                text = Encoding.Unicode.GetString(data, start, len);
            else if (encoding == 2)
                text = Encoding.BigEndianUnicode.GetString(data, start, len);
            else
                text = Encoding.UTF8.GetString(data, start, len);

            // Strip UTF-8 BOM (\uFEFF) if present — breaks search queries
            text = text.TrimStart('\uFEFF');
            return text.Trim();
        }

        private static void ReadApicFrame(byte[] data, Id3Tag tag)
        {
            if (data.Length < 2) return;
            int pos = 1;
            byte encoding = data[0];

            int mimeEnd = Array.IndexOf(data, (byte)0, pos);
            if (mimeEnd < 0) return;
            tag.AlbumArtMime = Encoding.ASCII.GetString(data, pos, mimeEnd - pos);
            pos = mimeEnd + 1;

            if (pos >= data.Length) return;
            pos++; // picture type

            int descEnd;
            if (encoding == 1 || encoding == 2)
            {
                descEnd = pos;
                while (descEnd + 1 < data.Length)
                {
                    if (data[descEnd] == 0 && data[descEnd + 1] == 0) { descEnd += 2; break; }
                    descEnd += 2;
                }
            }
            else
            {
                descEnd = Array.IndexOf(data, (byte)0, pos);
                if (descEnd < 0) descEnd = data.Length;
                else descEnd++;
            }

            if (descEnd >= data.Length) return;
            int artLen = data.Length - descEnd;
            if (artLen < 8) return;

            tag.AlbumArt = new byte[artLen];
            Array.Copy(data, descEnd, tag.AlbumArt, 0, artLen);
        }

        private static int SynchsafeToInt(byte[] data, int offset)
        {
            return (data[offset] << 21) | (data[offset + 1] << 14) |
                   (data[offset + 2] << 7) | data[offset + 3];
        }

        private static int ParseDurationFrame(byte[] data)
        {
            string text = ReadTextFrame(data);
            if (int.TryParse(text, out int ms))
                return ms / 1000;
            return 0;
        }
    }
}
