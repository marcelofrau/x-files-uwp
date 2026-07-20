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
        public byte[] AlbumArt;
        public string AlbumArtMime;

        private const int MaxTagSize = 2 * 1024 * 1024;

        private const uint GENERIC_READ = 0x80000000;
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
            IntPtr hFile = CreateFileFromAppW(path, GENERIC_READ, 0, IntPtr.Zero,
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
            if (header == null || header.Length < 10) return null;
            if (header[0] != 'I' || header[1] != 'D' || header[2] != '3') return null;

            int tagSize = SynchsafeToInt(header, 6);
            if (tagSize <= 0 || tagSize > MaxTagSize) return null;

            byte[] tagData = ReadFileBytes(filePath, tagSize + 10);
            if (tagData == null) return null;

            return ParseTag(tagData, tagSize);
        }

        private static Id3Tag ParseTag(byte[] data, int tagSize)
        {
            var tag = new Id3Tag();
            int pos = 10;
            int end = Math.Min(data.Length, 10 + tagSize);

            while (pos + 10 <= end)
            {
                string frameId = Encoding.ASCII.GetString(data, pos, 4);
                if (data[pos] == 0) break;

                int frameSize = SynchsafeToInt(data, pos + 4);
                if (frameSize <= 0 || pos + 10 + frameSize > end) break;

                byte[] frameData = new byte[frameSize];
                Array.Copy(data, pos + 10, frameData, 0, frameSize);

                if (frameId == "TIT2")
                    tag.Title = ReadTextFrame(frameData);
                else if (frameId == "TPE1")
                    tag.Artist = ReadTextFrame(frameData);
                else if (frameId == "TALB")
                    tag.Album = ReadTextFrame(frameData);
                else if (frameId == "APIC" && tag.AlbumArt == null)
                    ReadApicFrame(frameData, tag);

                pos += 10 + frameSize;
            }

            return tag;
        }

        private static string ReadTextFrame(byte[] data)
        {
            if (data.Length < 2) return null;
            byte encoding = data[0];
            string text;
            if (encoding == 0)
                text = Encoding.GetEncoding("iso-8859-1").GetString(data, 1, data.Length - 1);
            else if (encoding == 1)
                text = Encoding.Unicode.GetString(data, 1, data.Length - 1);
            else if (encoding == 2)
                text = Encoding.BigEndianUnicode.GetString(data, 1, data.Length - 1);
            else
                text = Encoding.UTF8.GetString(data, 1, data.Length - 1);

            return text.TrimEnd('\0', '\r', '\n');
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
    }
}
