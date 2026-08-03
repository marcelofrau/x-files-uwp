using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.FileSystem;
using XFiles.Services;

namespace XFiles.Tests
{
    [TestClass]
    public class PortalCoreTests
    {
        // --- CombinePortalPath ---

        [TestMethod]
        public void CombinePortalPath_RootPlusChild()
        {
            Assert.AreEqual("\\Settings", PortalCore.CombinePortalPath("\\", "Settings"));
        }

        [TestMethod]
        public void CombinePortalPath_NullParent_RootChild()
        {
            Assert.AreEqual("\\Settings", PortalCore.CombinePortalPath(null, "Settings"));
        }

        [TestMethod]
        public void CombinePortalPath_EmptyParent_RootChild()
        {
            Assert.AreEqual("\\Settings", PortalCore.CombinePortalPath("", "Settings"));
        }

        [TestMethod]
        public void CombinePortalPath_NestedChild()
        {
            Assert.AreEqual("\\Settings\\Sub", PortalCore.CombinePortalPath("\\Settings", "Sub"));
        }

        [TestMethod]
        public void CombinePortalPath_DeepNesting()
        {
            Assert.AreEqual("\\Settings\\A\\B\\C", PortalCore.CombinePortalPath("\\Settings\\A\\B", "C"));
        }

        // --- IsDirectoryType ---

        [TestMethod]
        public void IsDirectoryType_DirectoryBit_True()
        {
            Assert.IsTrue(PortalCore.IsDirectoryType(0x10));
        }

        [TestMethod]
        public void IsDirectoryType_DirectoryBitWithFlags_True()
        {
            Assert.IsTrue(PortalCore.IsDirectoryType(0x1F));
        }

        [TestMethod]
        public void IsDirectoryType_FileType_False()
        {
            Assert.IsFalse(PortalCore.IsDirectoryType(0x00));
        }

        [TestMethod]
        public void IsDirectoryType_FileTypeWithBits_False()
        {
            Assert.IsFalse(PortalCore.IsDirectoryType(0x01));
        }

        // --- CompareDirectoryEntries ---

        [TestMethod]
        public void CompareDirectoryEntries_DirsFirst()
        {
            Assert.IsTrue(PortalCore.CompareDirectoryEntries(true, "z", false, "a") < 0);
            Assert.IsTrue(PortalCore.CompareDirectoryEntries(false, "a", true, "z") > 0);
        }

        [TestMethod]
        public void CompareDirectoryEntries_Alphabetical_CaseInsensitive()
        {
            Assert.IsTrue(PortalCore.CompareDirectoryEntries(true, "B", true, "a") > 0);
            Assert.IsTrue(PortalCore.CompareDirectoryEntries(false, "a", false, "B") < 0);
        }

        [TestMethod]
        public void CompareDirectoryEntries_Equal_Zero()
        {
            Assert.AreEqual(0, PortalCore.CompareDirectoryEntries(true, "Same", true, "same"));
        }

        // --- Query builders ---

        [TestMethod]
        public void BuildListFilesQuery_FullParams()
        {
            string q = PortalCore.BuildListFilesQuery("LocalState", "Pkg_1", "\\Data");
            Assert.AreEqual(
                "/api/filesystem/apps/files?knownfolderid=LocalState&packagefullname=Pkg_1&path=%5CData",
                q);
        }

        [TestMethod]
        public void BuildListFilesQuery_NullPackage_EmptyParam()
        {
            string q = PortalCore.BuildListFilesQuery("LocalState", null, "\\");
            Assert.AreEqual(
                "/api/filesystem/apps/files?knownfolderid=LocalState&packagefullname=&path=%5C",
                q);
        }

        [TestMethod]
        public void BuildListFilesQuery_EscapesSpecialChars()
        {
            string q = PortalCore.BuildListFilesQuery("LocalState", "A B&C", "\\D E\\F");
            Assert.IsTrue(q.Contains("knownfolderid=LocalState"));
            Assert.IsTrue(q.Contains("packagefullname=A%20B%26C"));
            Assert.IsTrue(q.Contains("path=%5CD%20E%5CF"));
        }

        [TestMethod]
        public void BuildDownloadFileQuery_FilenameSeparateParam()
        {
            string q = PortalCore.BuildDownloadFileQuery("LocalState", "Pkg_1", "\\Data", "log.txt");
            Assert.AreEqual(
                "/api/filesystem/apps/file?knownfolderid=LocalState&filename=log.txt&packagefullname=Pkg_1&path=%5CData",
                q);
        }

        [TestMethod]
        public void BuildDownloadFileQuery_EscapesFilename()
        {
            string q = PortalCore.BuildDownloadFileQuery("LocalState", "Pkg_1", "\\", "my file.log");
            Assert.IsTrue(q.Contains("filename=my%20file.log"));
            Assert.IsTrue(q.Contains("path=%5C"));
        }

        // --- ShortFamilyName ---

        [TestMethod]
        public void ShortFamilyName_StripsPublisherAndTakesLastSegment()
        {
            Assert.AreEqual("XboxApp", PortalCore.ShortFamilyName("Microsoft.XboxApp_8wekyb3d8bbwe"));
        }

        [TestMethod]
        public void ShortFamilyName_MultiDot_TakesLastSegment()
        {
            Assert.AreEqual("Host", PortalCore.ShortFamilyName("Microsoft.Sevices.Host_8wekyb3d8bbwe"));
        }

        [TestMethod]
        public void ShortFamilyName_NoUnderscore_SingleSegment()
        {
            Assert.AreEqual("MyApp", PortalCore.ShortFamilyName("MyApp"));
        }

        [TestMethod]
        public void ShortFamilyName_NoDotAfterStrip_ReturnsWholeCore()
        {
            Assert.AreEqual("SomethingApp", PortalCore.ShortFamilyName("SomethingApp_8wekyb3d8bbwe"));
        }

        [TestMethod]
        public void ShortFamilyName_NullOrEmpty_ReturnsNull()
        {
            Assert.IsNull(PortalCore.ShortFamilyName(null));
            Assert.IsNull(PortalCore.ShortFamilyName(""));
        }

        // --- BuildPackageDisplayName ---

        [TestMethod]
        public void BuildPackageDisplayName_FirstUse_BaseName()
        {
            var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Assert.AreEqual("My App", PortalCore.BuildPackageDisplayName("My App", used, "Foo.MyApp_pub"));
            Assert.AreEqual(1, used["My App"]);
        }

        [TestMethod]
        public void BuildPackageDisplayName_Collision_ShortFamilyNameSuffix()
        {
            var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            PortalCore.BuildPackageDisplayName("My App", used, "Foo.MyApp_pub");
            Assert.AreEqual("My App (MyApp)", PortalCore.BuildPackageDisplayName("My App", used, "Foo.MyApp_pub"));
        }

        [TestMethod]
        public void BuildPackageDisplayName_CollisionNoFamilyName_CounterSuffix()
        {
            var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            PortalCore.BuildPackageDisplayName("My App", used, null);
            Assert.AreEqual("My App (2)", PortalCore.BuildPackageDisplayName("My App", used, null));
        }

        [TestMethod]
        public void BuildPackageDisplayName_CollisionCaseInsensitive_CountsUp()
        {
            var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            PortalCore.BuildPackageDisplayName("My App", used, null);
            Assert.AreEqual("my app (2)", PortalCore.BuildPackageDisplayName("my app", used, null));
            Assert.AreEqual("MY APP (3)", PortalCore.BuildPackageDisplayName("MY APP", used, null));
        }

        [TestMethod]
        public void BuildPackageDisplayName_TenCollisions_CounterBeyondTen()
        {
            var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < 9; i++)
                PortalCore.BuildPackageDisplayName("App", used, null);
            Assert.AreEqual("App (10)", PortalCore.BuildPackageDisplayName("App", used, null));
        }

        // --- GetCacheKey ---

        [TestMethod]
        public void GetCacheKey_SameFields_SameKey()
        {
            var a = MakeEntry("a.txt", 100, 1000);
            var b = MakeEntry("a.txt", 100, 1000);
            Assert.AreEqual(PortalCore.GetCacheKey(a), PortalCore.GetCacheKey(b));
        }

        [TestMethod]
        public void GetCacheKey_SizeChange_ChangesKey()
        {
            Assert.AreNotEqual(
                PortalCore.GetCacheKey(MakeEntry("a.txt", 100, 1000)),
                PortalCore.GetCacheKey(MakeEntry("a.txt", 101, 1000)));
        }

        [TestMethod]
        public void GetCacheKey_DateChange_ChangesKey()
        {
            Assert.AreNotEqual(
                PortalCore.GetCacheKey(MakeEntry("a.txt", 100, 1000)),
                PortalCore.GetCacheKey(MakeEntry("a.txt", 100, 1001)));
        }

        [TestMethod]
        public void GetCacheKey_PathChange_ChangesKey()
        {
            Assert.AreNotEqual(
                PortalCore.GetCacheKey(MakeEntry("\\A", "a.txt", 100, 1000)),
                PortalCore.GetCacheKey(MakeEntry("\\B", "a.txt", 100, 1000)));
        }

        [TestMethod]
        public void GetCacheKey_SixParamOverload_MatchesEntryOverload()
        {
            var e = MakeEntry("\\A", "a.txt", 100, 1000);
            Assert.AreEqual(
                PortalCore.GetCacheKey(e),
                PortalCore.GetCacheKey(e.KnownFolder, e.PackageFullName, e.PortalPath, e.Name, e.FileSize, e.DateCreated));
        }

        // --- ComputeCacheHash ---

        [TestMethod]
        public void ComputeCacheHash_40HexLowercase()
        {
            string hash = PortalCore.ComputeCacheHash("abc");
            Assert.AreEqual(40, hash.Length);
            Assert.AreEqual("a9993e364706816aba3e25717850c26c9cd0d89d", hash);
        }

        [TestMethod]
        public void ComputeCacheHash_Deterministic()
        {
            Assert.AreEqual(
                PortalCore.ComputeCacheHash("some|key"),
                PortalCore.ComputeCacheHash("some|key"));
        }

        [TestMethod]
        public void ComputeCacheHash_DifferentKeys_DifferentHashes()
        {
            Assert.AreNotEqual(
                PortalCore.ComputeCacheHash("k1"),
                PortalCore.ComputeCacheHash("k2"));
        }

        // --- SanitizeCacheExtension ---

        [TestMethod]
        public void SanitizeCacheExtension_Lowercases()
        {
            Assert.AreEqual(".jpg", PortalCore.SanitizeCacheExtension("photo.JPG"));
        }

        [TestMethod]
        public void SanitizeCacheExtension_KeepsMultiPart()
        {
            Assert.AreEqual(".gz", PortalCore.SanitizeCacheExtension("archive.tar.gz"));
        }

        [TestMethod]
        public void SanitizeCacheExtension_NoExtension_Empty()
        {
            Assert.AreEqual("", PortalCore.SanitizeCacheExtension("readme"));
        }

        [TestMethod]
        public void SanitizeCacheExtension_NullOrEmpty_Empty()
        {
            Assert.AreEqual("", PortalCore.SanitizeCacheExtension(null));
            Assert.AreEqual("", PortalCore.SanitizeCacheExtension(""));
        }

        [TestMethod]
        public void SanitizeCacheExtension_Overlong_Empty()
        {
            Assert.AreEqual("", PortalCore.SanitizeCacheExtension("file." + new string('x', 20)));
        }

        // --- ToPortalEntry ---

        [TestMethod]
        public void ToPortalEntry_MapsAllFields()
        {
            var dt = new DateTimeOffset(2025, 5, 1, 12, 0, 0, TimeSpan.Zero);
            var e = new FileEntry
            {
                Name = "settings.json",
                IsDirectory = false,
                SizeBytes = 42,
                LastModified = dt,
                PortalKnownFolder = "LocalState",
                PortalPackageFullName = "Pkg_1",
                PortalPath = "\\Data"
            };

            var p = PortalCore.ToPortalEntry(e);

            Assert.AreEqual("settings.json", p.Name);
            Assert.IsFalse(p.IsDirectory);
            Assert.AreEqual(42, p.FileSize);
            Assert.AreEqual(dt.ToFileTime(), p.DateCreated);
            Assert.AreEqual("LocalState", p.KnownFolder);
            Assert.AreEqual("Pkg_1", p.PackageFullName);
            Assert.AreEqual("\\Data", p.PortalPath);
        }

        [TestMethod]
        public void ToPortalEntry_NullPackage_EmptyString()
        {
            var e = new FileEntry { Name = "x", PortalKnownFolder = "LocalState", PortalPath = "\\" };
            var p = PortalCore.ToPortalEntry(e);
            Assert.AreEqual("", p.PackageFullName);
        }

        [TestMethod]
        public void ToPortalEntry_NullDate_ZeroFileTime()
        {
            var e = new FileEntry { Name = "x", PortalKnownFolder = "LocalState", PortalPath = "\\" };
            var p = PortalCore.ToPortalEntry(e);
            Assert.AreEqual(0, p.DateCreated);
        }

        [TestMethod]
        public void ToPortalEntry_DirectoryEntry_MapsFlag()
        {
            var e = new FileEntry { Name = "Assets", IsDirectory = true, PortalKnownFolder = "LocalState", PortalPath = "\\" };
            var p = PortalCore.ToPortalEntry(e);
            Assert.IsTrue(p.IsDirectory);
        }

        private static PortalFileEntry MakeEntry(string name, long size, long date)
            => MakeEntry("\\Data", name, size, date);

        private static PortalFileEntry MakeEntry(string path, string name, long size, long date)
        {
            return new PortalFileEntry
            {
                Name = name,
                FileSize = size,
                DateCreated = date,
                KnownFolder = "LocalState",
                PackageFullName = "Pkg_1",
                PortalPath = path
            };
        }
    }
}
