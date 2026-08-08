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
    }
}
