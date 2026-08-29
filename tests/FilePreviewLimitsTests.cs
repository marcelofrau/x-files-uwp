using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.FileSystem;

namespace XFiles.Tests
{
    [TestClass]
    public class FilePreviewLimitsTests
    {
        [TestMethod]
        public void ShouldDeferNetworkArchivePreview_SmallArchive_ReturnsFalse()
        {
            Assert.IsFalse(FilePreviewLimits.ShouldDeferNetworkArchivePreview(0));
            Assert.IsFalse(FilePreviewLimits.ShouldDeferNetworkArchivePreview(1024));
            Assert.IsFalse(FilePreviewLimits.ShouldDeferNetworkArchivePreview(64L * 1024 * 1024));
        }

        [TestMethod]
        public void ShouldDeferNetworkArchivePreview_AtThreshold_ReturnsFalse()
        {
            long threshold = FilePreviewLimits.MaxNetworkArchivePreviewBytes;
            Assert.IsFalse(FilePreviewLimits.ShouldDeferNetworkArchivePreview(threshold));
        }

        [TestMethod]
        public void ShouldDeferNetworkArchivePreview_LargeArchive_ReturnsTrue()
        {
            Assert.IsTrue(FilePreviewLimits.ShouldDeferNetworkArchivePreview(
                FilePreviewLimits.MaxNetworkArchivePreviewBytes + 1));
            Assert.IsTrue(FilePreviewLimits.ShouldDeferNetworkArchivePreview(500L * 1024 * 1024));
            Assert.IsTrue(FilePreviewLimits.ShouldDeferNetworkArchivePreview(1L * 1024 * 1024 * 1024));
        }

        [TestMethod]
        public void MaxImageBytes_IsFiftyMegabytes()
        {
            Assert.AreEqual(50L * 1024 * 1024, FilePreviewLimits.MaxImageBytes);
        }
    }
}
