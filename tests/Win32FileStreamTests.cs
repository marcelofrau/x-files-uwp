using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.FileSystem;

namespace XFiles.Tests
{
    [TestClass]
    public class Win32FileStreamTests
    {
        private string _dir;

        [TestInitialize]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "xfiles-win32fs-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TestCleanup]
        public void TearDown()
        {
            try { Directory.Delete(_dir, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private string WriteTemp(byte[] content, string name = "file.bin")
        {
            string path = Path.Combine(_dir, name);
            File.WriteAllBytes(path, content);
            return path;
        }

        private static byte[] Pattern(int count)
        {
            var data = new byte[count];
            for (int i = 0; i < count; i++)
                data[i] = (byte)(i * 31 % 251);
            return data;
        }

        [TestMethod]
        public void Read_SequentialChunks_MatchSource()
        {
            byte[] src = Pattern(1000);
            string path = WriteTemp(src);

            using (var s = Win32FileStream.OpenRead(path))
            {
                Assert.IsNotNull(s);
                var buf = new byte[400];
                int read1 = s.Read(buf, 0, buf.Length);
                Assert.AreEqual(400, read1);
                Assert.IsTrue(src.Take(400).SequenceEqual(buf));

                Assert.AreEqual(400, s.Position);
                Assert.AreEqual(1000, s.Length);

                var buf2 = new byte[600];
                int read2 = s.Read(buf2, 0, buf2.Length);
                Assert.AreEqual(600, read2);
                Assert.IsTrue(src.Skip(400).SequenceEqual(buf2));

                Assert.AreEqual(1000, s.Position);
                Assert.AreEqual(0, s.Read(buf2, 0, buf2.Length));
            }
        }

        [TestMethod]
        public void Read_PastEnd_ReturnsZero()
        {
            string path = WriteTemp(Pattern(10));
            using (var s = Win32FileStream.OpenRead(path))
            {
                var buf = new byte[128];
                Assert.AreEqual(10, s.Read(buf, 0, buf.Length));
                Assert.AreEqual(0, s.Read(buf, 0, buf.Length));
                Assert.AreEqual(10, s.Position);
            }
        }

        [TestMethod]
        public void Read_NonZeroOffset_FillsBufferAtOffset()
        {
            string path = WriteTemp(Pattern(64));
            using (var s = Win32FileStream.OpenRead(path))
            {
                var buf = Enumerable.Repeat((byte)0xAA, 64).ToArray();
                int read = s.Read(buf, 16, 48);
                Assert.AreEqual(48, read);
                Assert.AreEqual(48, s.Position);

                Assert.IsTrue(buf.Take(16).All(b => b == 0xAA));
                Assert.IsTrue(buf.Skip(16).SequenceEqual(Pattern(64).Take(48)));
                Assert.IsTrue(buf.Skip(64).All(b => b == 0xAA));
            }
        }

        [TestMethod]
        public void Position_Setter_SeekToOffset()
        {
            string path = WriteTemp(Pattern(256));
            using (var s = Win32FileStream.OpenRead(path))
            {
                s.Position = 128;
                var buf = new byte[8];
                s.Read(buf, 0, 8);
                Assert.IsTrue(buf.SequenceEqual(Pattern(256).Skip(128).Take(8)));
            }
        }

        [TestMethod]
        public void Seek_Begin_Current_End_ReturnPositions()
        {
            string path = WriteTemp(Pattern(100));
            using (var s = Win32FileStream.OpenRead(path))
            {
                Assert.AreEqual(20, s.Seek(20, SeekOrigin.Begin));
                Assert.AreEqual(50, s.Seek(30, SeekOrigin.Current));
                Assert.AreEqual(100, s.Seek(0, SeekOrigin.End));
                Assert.AreEqual(80, s.Seek(-20, SeekOrigin.End));
                Assert.AreEqual(80, s.Position);
            }
        }

        [TestMethod]
        public void Seek_InvalidOrigin_Throws()
        {
            string path = WriteTemp(Pattern(10));
            using (var s = Win32FileStream.OpenRead(path))
            {
                Assert.ThrowsException<ArgumentException>(() => s.Seek(0, (SeekOrigin)99));
            }
        }

        [TestMethod]
        public void OpenRead_MissingFile_ReturnsNull()
        {
            using (var s = Win32FileStream.OpenRead(Path.Combine(_dir, "nope.bin")))
            {
                Assert.IsNull(s);
            }
        }

        [TestMethod]
        public void ReadStream_IsReadOnly()
        {
            string path = WriteTemp(Pattern(10));
            using (var s = Win32FileStream.OpenRead(path))
            {
                Assert.IsFalse(s.CanWrite);
                Assert.IsTrue(s.CanRead);
                Assert.IsTrue(s.CanSeek);
                Assert.ThrowsException<NotSupportedException>(() => s.Write(new byte[4], 0, 4));
                Assert.ThrowsException<NotSupportedException>(() => s.SetLength(20));
            }
        }

        [TestMethod]
        public void Read_AfterDispose_Throws()
        {
            string path = WriteTemp(Pattern(10));
            var s = Win32FileStream.OpenRead(path);
            s.Dispose();
            Assert.IsFalse(s.CanRead);
            Assert.ThrowsException<ObjectDisposedException>(() => s.Read(new byte[4], 0, 4));
        }

        [TestMethod]
        public void RoundTrip_LargeFile_InChunks_Matches()
        {
            byte[] src = Pattern(1024 * 1024 + 137); // > 1MB, odd remainder
            string inPath = WriteTemp(src, "in.bin");
            string outPath = Path.Combine(_dir, "out.bin");

            using (var reader = Win32FileStream.OpenRead(inPath))
            using (var writer = Win32FileWriteStream.Create(outPath))
            {
                Assert.IsNotNull(reader);
                Assert.IsNotNull(writer);
                var buf = new byte[64 * 1024];
                int n;
                while ((n = reader.Read(buf, 0, buf.Length)) > 0)
                    writer.Write(buf, 0, n);
            }

            byte[] result = File.ReadAllBytes(outPath);
            Assert.AreEqual(src.Length, result.Length);
            Assert.IsTrue(src.SequenceEqual(result));
        }
    }
}
