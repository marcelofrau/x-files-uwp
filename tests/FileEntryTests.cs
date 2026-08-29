using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.FileSystem;

namespace XFiles.Tests
{
    [TestClass]
    public class FileEntryTests
    {
        [TestMethod]
        public void IsRootContainer_Drive_True()
        {
            var e = new FileEntry { Name = "C:", IsDrive = true, IsDirectory = true };
            Assert.IsTrue(e.IsRootContainer);
        }

        [TestMethod]
        public void IsRootContainer_PortalNoPath_True()
        {
            var e = new FileEntry { Name = "User Folders", IsPortal = true, PortalPath = null };
            Assert.IsTrue(e.IsRootContainer);
        }

        [TestMethod]
        public void IsRootContainer_PortalWithPath_False()
        {
            var e = new FileEntry { Name = "Documents", IsPortal = true, PortalPath = "\\Documents" };
            Assert.IsFalse(e.IsRootContainer);
        }

        [TestMethod]
        public void IsRootContainer_AppDataDir_True()
        {
            var e = new FileEntry { Name = "AppData", IsDirectory = true, IsPortal = false };
            Assert.IsTrue(e.IsRootContainer);
        }

        [TestMethod]
        public void IsRootContainer_AppDataCaseInsensitive_True()
        {
            var e = new FileEntry { Name = "appdata", IsDirectory = true, IsPortal = false };
            Assert.IsTrue(e.IsRootContainer);
        }

        [TestMethod]
        public void IsRootContainer_AppDataFile_False()
        {
            // Name AppData but not a directory -> not a root container
            var e = new FileEntry { Name = "AppData", IsDirectory = false, IsPortal = false };
            Assert.IsFalse(e.IsRootContainer);
        }

        [TestMethod]
        public void IsRootContainer_RegularFile_False()
        {
            var e = new FileEntry { Name = "readme.txt", IsDirectory = false };
            Assert.IsFalse(e.IsRootContainer);
        }

        [TestMethod]
        public void IsRootContainer_RegularDir_False()
        {
            var e = new FileEntry { Name = "Games", IsDirectory = true, IsPortal = false };
            Assert.IsFalse(e.IsRootContainer);
        }

        [TestMethod]
        public void IsRootContainer_DriveOverridesName_False()
        {
            // Drive flag is checked first and wins even if it's also "AppData"-named folder
            var e = new FileEntry { Name = "AppData", IsDrive = true };
            Assert.IsTrue(e.IsRootContainer);
        }

        [TestMethod]
        public void Defaults_ActionKindNone_AndProtocolSmb()
        {
            var e = new FileEntry { Name = "x" };
            Assert.AreEqual(ActionKind.None, e.ActionKind);
            Assert.AreEqual("smb", e.NetworkProtocol.ToString().ToLowerInvariant());
            Assert.AreEqual(-1, e.ChiptuneTrackIndex);
        }
    }
}
