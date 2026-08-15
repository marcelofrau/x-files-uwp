using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.Network;

namespace XFiles.Tests
{
    [TestClass]
    public class NetworkPathUtilTests
    {
        // ---- Join ----

        [DataTestMethod]
        [DataRow("", "file.txt", "file.txt")]
        [DataRow(null, "file.txt", "file.txt")]
        [DataRow("dir", "file.txt", "dir\\file.txt")]
        [DataRow("dir\\", "file.txt", "dir\\file.txt")]
        [DataRow("a\\b\\", "c.txt", "a\\b\\c.txt")]
        [DataRow("dir", "", "dir\\")]
        public void Join_CombinesBackslashPaths(string dir, string name, string expected)
        {
            Assert.AreEqual(expected, NetworkPathUtil.Join(dir, name));
        }

        // ---- PathForItem ----

        [TestMethod]
        public void PathForItem_EmptyRel_ReturnsBasePath()
        {
            Assert.AreEqual("movies", NetworkPathUtil.PathForItem("movies", ""));
            Assert.AreEqual("movies", NetworkPathUtil.PathForItem("movies", null));
        }

        [TestMethod]
        public void PathForItem_RelJoinsUnderBase()
        {
            Assert.AreEqual("movies\\sub\\clip.mkv", NetworkPathUtil.PathForItem("movies", "sub\\clip.mkv"));
        }

        // ---- Parent ----

        [DataTestMethod]
        [DataRow("", "")]
        [DataRow(null, "")]
        [DataRow("file.txt", "")]
        [DataRow("a\\file.txt", "a")]
        [DataRow("a\\b\\file.txt", "a\\b")]
        [DataRow("a\\b\\", "a\\b")]
        public void Parent_ReturnsParentDirectory(string path, string expected)
        {
            Assert.AreEqual(expected, NetworkPathUtil.Parent(path));
        }
    }
}
