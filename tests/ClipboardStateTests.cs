using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.FileSystem;

namespace XFiles.Tests
{
    [TestClass]
    public class ClipboardStateTests
    {
        private static FileEntry Entry(string name) =>
            new FileEntry { Name = name, FullPath = @"X:\" + name };

        [TestCleanup]
        public void Cleanup()
        {
            ClipboardState.Clear();
        }

        [TestMethod]
        public void Empty_ByDefault()
        {
            Assert.AreEqual(0, ClipboardState.Count);
            Assert.IsFalse(ClipboardState.HasItems);
            Assert.AreEqual(0, ClipboardState.Entries.Count());
        }

        [TestMethod]
        public void Copy_StoresEntries()
        {
            var entries = new[] { Entry("a.txt"), Entry("b.txt") };
            ClipboardState.Copy(entries);

            Assert.AreEqual(2, ClipboardState.Count);
            Assert.IsTrue(ClipboardState.HasItems);
            Assert.IsTrue(ClipboardState.Entries.Any(e => e.Name == "a.txt"));
            Assert.IsTrue(ClipboardState.Entries.Any(e => e.Name == "b.txt"));
        }

        [TestMethod]
        public void Copy_ReplacesPreviousSelection()
        {
            ClipboardState.Copy(new[] { Entry("a.txt") });
            ClipboardState.Copy(new[] { Entry("c.txt") });

            Assert.AreEqual(1, ClipboardState.Count);
            Assert.IsTrue(ClipboardState.Entries.All(e => e.Name == "c.txt"));
        }

        [TestMethod]
        public void Clear_EmptiesClipboard()
        {
            ClipboardState.Copy(new[] { Entry("a.txt") });
            ClipboardState.Clear();

            Assert.AreEqual(0, ClipboardState.Count);
            Assert.IsFalse(ClipboardState.HasItems);
        }
    }
}
