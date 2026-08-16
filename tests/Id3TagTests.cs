using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.FileSystem;

namespace XFiles.Tests
{
    [TestClass]
    public class Id3TagTests
    {
        [TestMethod]
        public void ReadFromFile_ValidV23Tag_ParsesFrames()
        {
            string tmpPath = Path.Combine(Path.GetTempPath(), $"id3test_{Guid.NewGuid():N}.mp3");
            try
            {
                File.WriteAllBytes(tmpPath, BuildV23Mp3("My Title", "My Artist", "My Album", "3", "180000"));

                var tag = Id3Tag.ReadFromFile(tmpPath);

                Assert.IsNotNull(tag);
                Assert.AreEqual("My Title", tag.Title);
                Assert.AreEqual("My Artist", tag.Artist);
                Assert.AreEqual("My Album", tag.Album);
                Assert.AreEqual("3", tag.TrackNumber);
                Assert.AreEqual(180, tag.DurationSeconds);
            }
            finally
            {
                if (File.Exists(tmpPath)) File.Delete(tmpPath);
            }
        }

        [TestMethod]
        public void ReadFromFile_NonMp3Header_ReturnsNull()
        {
            string tmpPath = Path.Combine(Path.GetTempPath(), $"id3test_{Guid.NewGuid():N}.mp3");
            try
            {
                File.WriteAllBytes(tmpPath, Encoding.ASCII.GetBytes("RIFF........WAVEfmt "));

                var tag = Id3Tag.ReadFromFile(tmpPath);

                Assert.IsNull(tag);
            }
            finally
            {
                if (File.Exists(tmpPath)) File.Delete(tmpPath);
            }
        }

        [TestMethod]
        public void ReadFromFile_MissingFile_ReturnsNull()
        {
            string missing = Path.Combine(Path.GetTempPath(), $"id3test_{Guid.NewGuid():N}.mp3");
            var tag = Id3Tag.ReadFromFile(missing);
            Assert.IsNull(tag);
        }

        [TestMethod]
        public void ReadFromFile_UnicodeTextFrame_Decodes()
        {
            string tmpPath = Path.Combine(Path.GetTempPath(), $"id3test_{Guid.NewGuid():N}.mp3");
            try
            {
                // UTF-16 LE (encoding byte 0x01) title frame
                var title = Encoding.Unicode.GetBytes("Álbum Êxodo");
                var frameData = new byte[1 + title.Length];
                frameData[0] = 0x01;
                Array.Copy(title, 0, frameData, 1, title.Length);
                var frame = MakeFrame("TIT2", frameData);
                var tagBytes = BuildV23(frame);
                File.WriteAllBytes(tmpPath, tagBytes);

                var tag = Id3Tag.ReadFromFile(tmpPath);

                Assert.IsNotNull(tag);
                Assert.AreEqual("Álbum Êxodo", tag.Title);
            }
            finally
            {
                if (File.Exists(tmpPath)) File.Delete(tmpPath);
            }
        }

        [TestMethod]
        public void ReadFromStream_ValidV23Tag_ParsesFrames()
        {
            byte[] mp3 = BuildV23Mp3("Stream Title", "Stream Artist", "Stream Album", "7", "90000");
            using (var stream = new MemoryStream(mp3))
            {
                var tag = Id3Tag.ReadFromStream(stream);

                Assert.IsNotNull(tag);
                Assert.AreEqual("Stream Title", tag.Title);
                Assert.AreEqual("Stream Artist", tag.Artist);
                Assert.AreEqual("Stream Album", tag.Album);
                Assert.AreEqual("7", tag.TrackNumber);
                Assert.AreEqual(90, tag.DurationSeconds);
            }
        }

        [TestMethod]
        public void ReadFromStream_NonId3Header_ReturnsNull()
        {
            using (var stream = new MemoryStream(Encoding.ASCII.GetBytes("RIFF........WAVEfmt ")))
            {
                var tag = Id3Tag.ReadFromStream(stream);
                Assert.IsNull(tag);
            }
        }

        [TestMethod]
        public void ReadFromStream_Unseekable_ReturnsNull()
        {
            using (var stream = new NonSeekableStream(Encoding.ASCII.GetBytes("ID3anything")))
            {
                var tag = Id3Tag.ReadFromStream(stream);
                Assert.IsNull(tag);
            }
        }

        private static byte[] BuildV23Mp3(string title, string artist, string album, string track, string duration)
        {
            var body = new byte[0];
            body = Concat(body, MakeFrame("TIT2", TextFrame(title)));
            body = Concat(body, MakeFrame("TPE1", TextFrame(artist)));
            body = Concat(body, MakeFrame("TALB", TextFrame(album)));
            body = Concat(body, MakeFrame("TRCK", TextFrame(track)));
            body = Concat(body, MakeFrame("TLEN", TextFrame(duration)));

            var tag = BuildV23(body);
            // Append a few dummy MP3 frame bytes (audio payload)
            return Concat(tag, new byte[] { 0xFF, 0xFB, 0x90, 0x00, 0x01, 0x02, 0x03, 0x04 });
        }

        private static byte[] BuildV23(byte[] frames)
        {
            // "ID3" + version 3.0 + flags 0 + synchsafe size
            var header = new byte[10];
            header[0] = (byte)'I'; header[1] = (byte)'D'; header[2] = (byte)'3';
            header[3] = 3; header[4] = 0; header[5] = 0;
            int size = frames.Length;
            header[6] = (byte)((size >> 21) & 0x7F);
            header[7] = (byte)((size >> 14) & 0x7F);
            header[8] = (byte)((size >> 7) & 0x7F);
            header[9] = (byte)(size & 0x7F);
            return Concat(header, frames);
        }

        private static byte[] TextFrame(string text)
        {
            var textBytes = Encoding.GetEncoding("iso-8859-1").GetBytes(text);
            var frame = new byte[1 + textBytes.Length];
            frame[0] = 0x00; // ISO-8859-1
            Array.Copy(textBytes, 0, frame, 1, textBytes.Length);
            return frame;
        }

        private static byte[] MakeFrame(string id, byte[] frameData)
        {
            var frame = new byte[10 + frameData.Length];
            byte[] idBytes = Encoding.ASCII.GetBytes(id);
            Array.Copy(idBytes, 0, frame, 0, 4);
            // ID3v2.3: big-endian size
            frame[4] = (byte)(frameData.Length >> 24);
            frame[5] = (byte)(frameData.Length >> 16);
            frame[6] = (byte)(frameData.Length >> 8);
            frame[7] = (byte)(frameData.Length);
            frame[8] = 0; frame[9] = 0; // no flags
            Array.Copy(frameData, 0, frame, 10, frameData.Length);
            return frame;
        }

        private static byte[] Concat(byte[] a, byte[] b)
        {
            var result = new byte[a.Length + b.Length];
            Array.Copy(a, 0, result, 0, a.Length);
            Array.Copy(b, 0, result, a.Length, b.Length);
            return result;
        }

        private sealed class NonSeekableStream : MemoryStream
        {
            public NonSeekableStream(byte[] data) : base(data) { }
            public override bool CanSeek => false;
        }
    }
}
