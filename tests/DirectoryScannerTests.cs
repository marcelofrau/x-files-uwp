using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.FileSystem;

namespace XFiles.Tests
{
    [TestClass]
    public class DirectoryScannerTests
    {
        [TestMethod]
        public void AppendSorted_PreservesParentEntryAtIndexZero()
        {
            var entries = new List<FileEntry> { new FileEntry { Name = "..", IsDirectory = true } };
            var dirs = new List<FileEntry>
            {
                new FileEntry { Name = "zeta", IsDirectory = true },
                new FileEntry { Name = "Alpha", IsDirectory = true }
            };
            var files = new List<FileEntry>
            {
                new FileEntry { Name = "b.txt" },
                new FileEntry { Name = "a.txt" }
            };

            DirectoryEntryOrder.AppendSorted(entries, dirs, files);

            Assert.AreEqual(5, entries.Count);
            Assert.AreEqual("..", entries[0].Name);
            Assert.AreEqual("Alpha", entries[1].Name);
            Assert.AreEqual("zeta", entries[2].Name);
            Assert.AreEqual("a.txt", entries[3].Name);
            Assert.AreEqual("b.txt", entries[4].Name);
        }

        [TestMethod]
        public void NativeFs_RootKeyAndTryEnumerate_WorksWithPlainFallback()
        {
            Assert.AreEqual("E:\\", NativeFs.RootKey("e:\\Games\\PS2"));
            Assert.AreEqual("\\\\NAS\\ROMS", NativeFs.RootKey("\\\\nas\\roms\\snes"));
            Assert.AreEqual("", NativeFs.RootKey("relative/path"));

            string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "xfiles-nativefs-" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(tempDir);
            try
            {
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(tempDir, "rom.bin"), new byte[] { 1, 2, 3 });
                NativeFs.RememberBackend(tempDir, FsBackend.Plain);
                Assert.AreEqual(FsBackend.Plain, NativeFs.PreferredBackend(tempDir));

                bool ok = NativeFs.TryEnumerate(tempDir, out List<FsEntry> found, out int err, out FsBackend used);
                Assert.IsTrue(ok, $"TryEnumerate failed with err={err}");
                Assert.AreEqual(FsBackend.Plain, used);
                Assert.AreEqual(1, found.Count);
                Assert.AreEqual("rom.bin", found[0].Name);
                Assert.AreEqual(3L, found[0].Size);
            }
            finally
            {
                try { System.IO.Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
