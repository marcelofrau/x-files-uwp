using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.FileSystem;

namespace XFiles.Tests
{
    [TestClass]
    public class Win32FileWriteStreamTests
    {
        private string _dir;

        [TestInitialize]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "xfiles-win32wfs-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TestCleanup]
        public void TearDown()
        {
            try { Directory.Delete(_dir, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private string OutPath(string name = "out.bin") => Path.Combine(_dir, name);

        [TestMethod]
        public void Create_ThenWrite_FileMatchesBytes()
        {
            byte[] data = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
            string path = OutPath();

            using (var s = Win32FileWriteStream.Create(path))
            {
                Assert.IsNotNull(s);
                s.Write(data, 0, data.Length);
                Assert.AreEqual(256, s.Position);
            }

            Assert.IsTrue(File.ReadAllBytes(path).SequenceEqual(data));
        }

        [TestMethod]
        public void Write_NonZeroOffset_WritesOnlyFromOffset()
        {
            byte[] full = Enumerable.Repeat((byte)0x55, 64).ToArray();
            byte[] slice = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
            string path = OutPath();

            using (var s = Win32FileWriteStream.Create(path))
            {
                s.Write(full, 0, 64);
                s.Position = 0;
                s.Write(slice, 8, 8); // write slice[8..15] at position 0
            }

            byte[] expected = Enumerable.Range(0, 64).Select(i => i < 8 ? (byte)(8 + i) : (byte)0x55).ToArray();
            Assert.IsTrue(File.ReadAllBytes(path).SequenceEqual(expected));
        }

        [TestMethod]
        public void Write_MultipleChunks_AccumulatePosition()
        {
            string path = OutPath();
            using (var s = Win32FileWriteStream.Create(path))
            {
                s.Write(new byte[100], 0, 100);
                s.Write(new byte[50], 0, 50);
                Assert.AreEqual(150, s.Position);
                s.Write(new byte[50], 0, 50);
                Assert.AreEqual(200, s.Position);
            }
            Assert.AreEqual(200, new FileInfo(path).Length);
        }

        [TestMethod]
        public void Write_ThenSeek_PartialOverwrite()
        {
            string path = OutPath();
            using (var s = Win32FileWriteStream.Create(path))
            {
                s.Write(new byte[] { (byte)'A', (byte)'A', (byte)'A', (byte)'A', (byte)'A', (byte)'A' }, 0, 6);
                Assert.AreEqual(2, s.Seek(2, SeekOrigin.Begin));
                s.Write(new byte[] { (byte)'B', (byte)'B' }, 0, 2);
            }
            Assert.AreEqual("AABBAA", File.ReadAllText(path));
        }

        [TestMethod]
        public void Seek_Current_And_End()
        {
            string path = OutPath();
            using (var s = Win32FileWriteStream.Create(path))
            {
                s.Write(new byte[16], 0, 16);
                Assert.AreEqual(24, s.Seek(8, SeekOrigin.Current));
                Assert.AreEqual(16, s.Seek(0, SeekOrigin.End));
                Assert.AreEqual(24, s.Seek(8, SeekOrigin.Current));
                Assert.AreEqual(24, s.Position);
            }
        }

        [TestMethod]
        public void Seek_InvalidOrigin_Throws()
        {
            string path = OutPath();
            using (var s = Win32FileWriteStream.Create(path))
            {
                Assert.ThrowsException<ArgumentException>(() => s.Seek(0, (SeekOrigin)99));
            }
        }

        [TestMethod]
        public void Create_InvalidDirectory_ReturnsNull()
        {
            using (var s = Win32FileWriteStream.Create(Path.Combine(_dir, "missing", "out.bin")))
            {
                Assert.IsNull(s);
            }
        }

        [TestMethod]
        public void WriteStream_IsWriteOnly()
        {
            string path = OutPath();
            using (var s = Win32FileWriteStream.Create(path))
            {
                Assert.IsTrue(s.CanWrite);
                Assert.IsFalse(s.CanRead);
                Assert.IsTrue(s.CanSeek);
                Assert.ThrowsException<NotSupportedException>(() => s.Read(new byte[4], 0, 4));
                Assert.ThrowsException<NotSupportedException>(() => s.SetLength(10));
                Assert.ThrowsException<NotSupportedException>(() => { var _ = s.Length; });
            }
        }

        [TestMethod]
        public void Write_AfterDispose_Throws()
        {
            string path = OutPath();
            var s = Win32FileWriteStream.Create(path);
            s.Dispose();
            Assert.IsFalse(s.CanWrite);
            Assert.ThrowsException<ObjectDisposedException>(() => s.Write(new byte[4], 0, 4));
        }

        [TestMethod]
        public void Create_TruncatesExistingFile()
        {
            string path = OutPath();
            File.WriteAllBytes(path, new byte[500]);
            using (var s = Win32FileWriteStream.Create(path))
            {
                s.Write(new byte[10], 0, 10);
            }
            Assert.AreEqual(10, new FileInfo(path).Length);
        }
    }
}
