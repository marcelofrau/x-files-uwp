using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.FileSystem;

namespace XFiles.Tests
{
    [TestClass]
    public class DiskSpaceGuardTests
    {
        public DiskSpaceGuardTests()
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        }

        [TestMethod]
        public void IsInsufficient_NotEnoughSpace_ReturnsTrue()
        {
            Assert.IsTrue(DiskSpaceGuard.IsInsufficient(100, 200));
        }

        [TestMethod]
        public void IsInsufficient_EnoughSpace_ReturnsFalse()
        {
            Assert.IsFalse(DiskSpaceGuard.IsInsufficient(300, 200));
            Assert.IsFalse(DiskSpaceGuard.IsInsufficient(200, 200));
        }

        [TestMethod]
        public void IsInsufficient_ZeroOrNegativeRequired_NeverBlocks()
        {
            Assert.IsFalse(DiskSpaceGuard.IsInsufficient(100, 0));
            Assert.IsFalse(DiskSpaceGuard.IsInsufficient(100, -5));
        }

        [TestMethod]
        public void IsInsufficient_UnknownFree_DoesNotBlock()
        {
            Assert.IsFalse(DiskSpaceGuard.IsInsufficient(-1, 200));
        }

        [TestMethod]
        public void BuildWarning_ContainsSizes()
        {
            string msg = DiskSpaceGuard.BuildWarning(1024L * 1024 * 500, 1024L * 1024 * 1024 * 2);
            StringAssert.Contains(msg, "need");
            StringAssert.Contains(msg, "free");
            StringAssert.Contains(msg, "GB");
        }
    }
}
