using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.FileSystem;

namespace XFiles.Tests
{
    [TestClass]
    public class ArchiveSubtreeStatsTests
    {
        private static (string, bool, long) F(string path, long size = 0, bool isDir = false) => (path, isDir, size);

        private static readonly (string path, bool isDir, long size)[] Sample =
        {
            F("readme.txt", 100),
            F("Game/ROM.sfc", 500),
            F("Game/Box/front.png", 700),
            F("Game/manual/page1.txt", 50),
            F("Music/track1.ogg", 300),
            F("Music/", 0, true),           // explicit dir entry
            F("Empty/", 0, true),
        };

        [TestMethod]
        public void Compute_ArchiveRoot_CountsEverything()
        {
            var stats = ArchiveSubtreeStats.Compute(Sample, "");

            Assert.AreEqual(5, stats.FileCount);
            Assert.AreEqual(5, stats.FolderCount);
        }

        [TestMethod]
        public void Compute_ArchiveRoot_TotalsAllBytes()
        {
            var stats = ArchiveSubtreeStats.Compute(Sample, "");
            Assert.AreEqual(100 + 500 + 700 + 50 + 300, stats.TotalBytes);
        }

        [TestMethod]
        public void Compute_ArchiveRoot_InfersFolders()
        {
            var stats = ArchiveSubtreeStats.Compute(Sample, "");

            // Game, Music (explicit), Empty (explicit), Game/Box, Game/manual
            Assert.AreEqual(5, stats.FolderCount);
        }

        [TestMethod]
        public void Compute_Subtree_CountsOnlyDescendants()
        {
            var stats = ArchiveSubtreeStats.Compute(Sample, "Game");

            Assert.AreEqual(3, stats.FileCount);
            Assert.AreEqual(500 + 700 + 50, stats.TotalBytes);
            // Box + manual below Game; Game itself is not counted
            Assert.AreEqual(2, stats.FolderCount);
        }

        [TestMethod]
        public void Compute_DeepSubtree_NoMatch_Zeroes()
        {
            var stats = ArchiveSubtreeStats.Compute(Sample, "MissingDir");

            Assert.AreEqual(0, stats.FileCount);
            Assert.AreEqual(0, stats.FolderCount);
            Assert.AreEqual(0, stats.TotalBytes);
        }

        [TestMethod]
        public void Compute_EntryFileMatchingPrefixedSubtree_IsNotCounted()
        {
            // "MusicBox/..." must not be part of subtree "Music"
            var entries = new[]
            {
                F("MusicBox/notes.txt", 42),
                F("Music/a.ogg", 10),
            };
            var stats = ArchiveSubtreeStats.Compute(entries, "Music");

            Assert.AreEqual(1, stats.FileCount);
            Assert.AreEqual(10, stats.TotalBytes);
        }

        [TestMethod]
        public void Compute_CaseInsensitivePaths()
        {
            var stats = ArchiveSubtreeStats.Compute(Sample, "GAME");

            Assert.AreEqual(3, stats.FileCount);
        }

        [TestMethod]
        public void Compute_BackslashPaths_Normalized()
        {
            var entries = new[]
            {
                F("Game\\sub\\file.bin", 99),
                F("Game\\other.bin", 1),
            };
            var stats = ArchiveSubtreeStats.Compute(entries, "Game/sub");

            Assert.AreEqual(1, stats.FileCount);
            Assert.AreEqual(99, stats.TotalBytes);
        }

        [TestMethod]
        public void Compute_NullEntries_ReturnsZeroes()
        {
            var stats = ArchiveSubtreeStats.Compute(null, "");
            Assert.AreEqual(0, stats.FileCount);
            Assert.AreEqual(0, stats.TotalBytes);
        }

        [TestMethod]
        public void Compute_NullInternalPath_MeansRoot()
        {
            var stats = ArchiveSubtreeStats.Compute(Sample, null);
            Assert.AreEqual(5, stats.FileCount);
        }
    }
}