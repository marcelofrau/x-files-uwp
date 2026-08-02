using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.FileSystem;

namespace XFiles.Tests
{
    [TestClass]
    public class FormattingTests
    {
        public FormattingTests()
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        }

        [TestMethod]
        public void FormatSize_Bytes_ReturnsByteValue()
        {
            Assert.AreEqual("0 B", Formatting.FormatSize(0));
            Assert.AreEqual("512 B", Formatting.FormatSize(512));
            Assert.AreEqual("1023 B", Formatting.FormatSize(1023));
        }

        [TestMethod]
        public void FormatSize_Kilobytes_OneDecimal()
        {
            Assert.AreEqual("1.0 KB", Formatting.FormatSize(1024));
            Assert.AreEqual("1.5 KB", Formatting.FormatSize(1536));
            Assert.AreEqual("256.0 KB", Formatting.FormatSize(256 * 1024));
            Assert.AreEqual("1024.0 KB", Formatting.FormatSize(1024 * 1024 - 1));
        }

        [TestMethod]
        public void FormatSize_Megabytes_OneDecimal()
        {
            Assert.AreEqual("1.0 MB", Formatting.FormatSize(1024 * 1024));
            Assert.AreEqual("2.5 MB", Formatting.FormatSize((long)(2.5 * 1024 * 1024)));
        }

        [TestMethod]
        public void FormatSize_Gigabytes_TwoDecimals()
        {
            Assert.AreEqual("1.00 GB", Formatting.FormatSize(1024L * 1024 * 1024));
            Assert.AreEqual("1.50 GB", Formatting.FormatSize((long)(1.5 * 1024 * 1024 * 1024)));
        }

        [TestMethod]
        public void FormatSize_Negative_ReturnsBytes()
        {
            Assert.AreEqual("-1 B", Formatting.FormatSize(-1));
        }

        [TestMethod]
        public void FormatBytes_MatchesFormatSize()
        {
            Assert.AreEqual(Formatting.FormatSize(1536), Formatting.FormatBytes(1536));
            Assert.AreEqual(Formatting.FormatSize(1024L * 1024 * 1024), Formatting.FormatBytes(1024L * 1024 * 1024));
        }

        [TestMethod]
        public void FormatFsTime_UnderHour_MinutesSeconds()
        {
            Assert.AreEqual("0:00", Formatting.FormatFsTime(TimeSpan.Zero));
            Assert.AreEqual("1:05", Formatting.FormatFsTime(TimeSpan.FromSeconds(65)));
            Assert.AreEqual("59:59", Formatting.FormatFsTime(TimeSpan.FromSeconds(3599)));
        }

        [TestMethod]
        public void FormatFsTime_OverHour_HoursMinutesSeconds()
        {
            Assert.AreEqual("1:01:01", Formatting.FormatFsTime(TimeSpan.FromSeconds(3661)));
            Assert.AreEqual("2:00:00", Formatting.FormatFsTime(TimeSpan.FromHours(2)));
        }

        [TestMethod]
        public void FormatCount_Null_ZeroItems()
        {
            Assert.AreEqual("0 items", Formatting.FormatCount(null));
        }

        [TestMethod]
        public void FormatCount_Empty_ZeroItems()
        {
            Assert.AreEqual("0 items", Formatting.FormatCount(new List<FileEntry>()));
        }

        [TestMethod]
        public void FormatCount_MixedFoldersAndFiles_Plural()
        {
            var entries = new List<FileEntry>
            {
                new FileEntry { Name = "a", IsDirectory = true },
                new FileEntry { Name = "b", IsDirectory = true },
                new FileEntry { Name = "c", IsDirectory = false },
                new FileEntry { Name = "d", IsDirectory = false },
                new FileEntry { Name = "e", IsDirectory = false },
            };

            Assert.AreEqual("2 folders, 3 files", Formatting.FormatCount(entries));
        }

        [TestMethod]
        public void FormatCount_Singular()
        {
            var entries = new List<FileEntry>
            {
                new FileEntry { Name = "a", IsDirectory = true },
                new FileEntry { Name = "b", IsDirectory = false },
            };

            Assert.AreEqual("1 folder, 1 file", Formatting.FormatCount(entries));
        }

        [TestMethod]
        public void FormatCount_ExcludesParentDotDot()
        {
            var entries = new List<FileEntry>
            {
                new FileEntry { Name = "..", IsDirectory = true },
                new FileEntry { Name = "x", IsDirectory = false },
            };

            Assert.AreEqual("1 file", Formatting.FormatCount(entries));
        }
    }
}
