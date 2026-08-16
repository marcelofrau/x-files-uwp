using System.Linq;
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

        // ---- NameCandidates ----

        [TestMethod]
        public void NameCandidates_StartsWithNameSpaceOne()
        {
            var candidates = NetworkPathUtil.NameCandidates("Music", "song.mp3");
            Assert.AreEqual("Music\\song (1).mp3", candidates.First());
        }

        [TestMethod]
        public void NameCandidates_KeepsMultipleExtensionsIntact()
        {
            var candidates = NetworkPathUtil.NameCandidates("dir", "archive.tar.gz").Take(3).ToArray();
            Assert.AreEqual("dir\\archive.tar (1).gz", candidates[0]);
            Assert.AreEqual("dir\\archive.tar (2).gz", candidates[1]);
            Assert.AreEqual("dir\\archive.tar (3).gz", candidates[2]);
        }

        [TestMethod]
        public void NameCandidates_NoExtension_AppendsAfterStem()
        {
            var candidates = NetworkPathUtil.NameCandidates("", "README").Take(2).ToArray();
            Assert.AreEqual("README (1)", candidates[0]);
            Assert.AreEqual("README (2)", candidates[1]);
        }
    }
}
