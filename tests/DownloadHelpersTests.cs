using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.Services;

namespace XFiles.Tests
{
    [TestClass]
    public class DownloadHelpersTests
    {
        [TestMethod]
        public void IsMega_MegaNz_ReturnsTrue() =>
            Assert.IsTrue(DownloadHelpers.IsMega("https://mega.nz/file/abc123"));

        [TestMethod]
        public void IsMega_MegaCoNz_ReturnsTrue() =>
            Assert.IsTrue(DownloadHelpers.IsMega("https://mega.co.nz/#!xyz"));

        [TestMethod]
        public void IsMega_OtherSite_ReturnsFalse() =>
            Assert.IsFalse(DownloadHelpers.IsMega("https://drive.google.com/file/d/123"));

        [TestMethod]
        public void GoogleDrive_FileD_ExtractsId()
        {
            bool ok = DownloadHelpers.TryResolveGoogleDrive(
                "https://drive.google.com/file/d/1AbCdEfGhIjK/view?usp=sharing", out var direct);
            Assert.IsTrue(ok);
            Assert.AreEqual("https://drive.usercontent.google.com/download?id=1AbCdEfGhIjK&export=download&confirm=t", direct);
        }

        [TestMethod]
        public void GoogleDrive_QueryId_ExtractsId()
        {
            bool ok = DownloadHelpers.TryResolveGoogleDrive(
                "https://drive.google.com/open?id=1XyZ_abc", out var direct);
            Assert.IsTrue(ok);
            Assert.IsTrue(direct.Contains("id=1XyZ_abc"));
        }

        [TestMethod]
        public void GoogleDrive_Usercontent_AlreadyDirect()
        {
            string url = "https://drive.usercontent.google.com/download?id=abc123&export=download&confirm=t";
            bool ok = DownloadHelpers.TryResolveGoogleDrive(url, out var direct);
            Assert.IsTrue(ok);
            Assert.AreEqual(url, direct);
        }

        [TestMethod]
        public void GoogleDrive_NonGoogleUrl_ReturnsFalse() =>
            Assert.IsFalse(DownloadHelpers.TryResolveGoogleDrive("https://dropbox.com/s/xyz", out _));

        [TestMethod]
        public void OneDrive_1drvMs_Rewrites()
        {
            bool ok = DownloadHelpers.TryResolveOneDrive(
                "https://1drv.ms/u/s!abc123", out var direct);
            Assert.IsTrue(ok);
            Assert.IsTrue(direct.StartsWith("https://api.onedrive.com/v1.0/shares/u!"));
            Assert.IsTrue(direct.EndsWith("/root/content"));
        }

        [TestMethod]
        public void OneDrive_LiveCom_Rewrites()
        {
            bool ok = DownloadHelpers.TryResolveOneDrive(
                "https://onedrive.live.com/redir?resid=ABC", out var direct);
            Assert.IsTrue(ok);
            Assert.IsTrue(direct.StartsWith("https://api.onedrive.com/v1.0/shares/u!"));
        }

        [TestMethod]
        public void OneDrive_NonOneDrive_ReturnsFalse() =>
            Assert.IsFalse(DownloadHelpers.TryResolveOneDrive("https://google.com", out _));

        [TestMethod]
        public void Dropbox_ShareLink_AddsDl1()
        {
            bool ok = DownloadHelpers.TryResolveDropbox(
                "https://www.dropbox.com/scl/fi/abc/file.txt?rlkey=xyz", out var direct);
            Assert.IsTrue(ok);
            Assert.IsTrue(direct.Contains("dl=1"));
            Assert.IsTrue(direct.StartsWith("https://dl.dropbox.com/"));
        }

        [TestMethod]
        public void Dropbox_AlreadyHasDl1_DoesNotDuplicate()
        {
            string url = "https://dl.dropbox.com/s/abc/file.txt?dl=1";
            bool ok = DownloadHelpers.TryResolveDropbox(url, out var direct);
            Assert.IsTrue(ok);
            int count = 0; int idx = 0;
            while ((idx = direct.IndexOf("dl=1", idx)) >= 0) { count++; idx++; }
            Assert.AreEqual(1, count, "dl=1 should appear exactly once");
        }

        [TestMethod]
        public void Dropbox_NonDropbox_ReturnsFalse() =>
            Assert.IsFalse(DownloadHelpers.TryResolveDropbox("https://google.com/file", out _));

        [TestMethod]
        public void Gofile_ValidCode_ExtractsCode()
        {
            bool ok = DownloadHelpers.TryGetGofileCode("https://gofile.io/d/abc123", out var code);
            Assert.IsTrue(ok);
            Assert.AreEqual("abc123", code);
        }

        [TestMethod]
        public void Gofile_NonGofile_ReturnsFalse() =>
            Assert.IsFalse(DownloadHelpers.TryGetGofileCode("https://example.com/file", out _));

        [TestMethod]
        public void ContentDisposition_FilenameStar_RFC5987()
        {
            string result = DownloadHelpers.ParseContentDispositionFileName(
                "attachment; filename*=UTF-8''my%20file.txt");
            Assert.AreEqual("my file.txt", result);
        }

        [TestMethod]
        public void ContentDisposition_FilenameQuoted()
        {
            string result = DownloadHelpers.ParseContentDispositionFileName(
                "attachment; filename=\"report.pdf\"");
            Assert.AreEqual("report.pdf", result);
        }

        [TestMethod]
        public void ContentDisposition_FilenameUnquoted()
        {
            string result = DownloadHelpers.ParseContentDispositionFileName(
                "attachment; filename=data.csv");
            Assert.AreEqual("data.csv", result);
        }

        [TestMethod]
        public void ContentDisposition_Empty_ReturnsNull() =>
            Assert.IsNull(DownloadHelpers.ParseContentDispositionFileName(""));

        [TestMethod]
        public void ResolveFileName_FromContentDisposition()
        {
            string name = DownloadHelpers.ResolveFileName(
                "https://example.com/download?id=123",
                "attachment; filename=\"file.zip\"");
            Assert.AreEqual("file.zip", name);
        }

        [TestMethod]
        public void ResolveFileName_FromUrl()
        {
            string name = DownloadHelpers.ResolveFileName(
                "https://example.com/files/document.pdf", null);
            Assert.AreEqual("document.pdf", name);
        }

        [TestMethod]
        public void ResolveFileName_Fallback()
        {
            string name = DownloadHelpers.ResolveFileName("https://example.com/", null);
            Assert.AreEqual("download", name);
        }

        [TestMethod]
        public void GetUniquePath_NoConflict_ReturnsSame()
        {
            string tmpFile = Path.Combine(Path.GetTempPath(), "xfiles_dl_" + Path.GetRandomFileName());
            Assert.AreEqual(tmpFile, DownloadHelpers.GetUniquePath(tmpFile));
        }

        [TestMethod]
        public void GetUniquePath_Conflict_ReturnsCandidate()
        {
            string tmpFile = Path.Combine(Path.GetTempPath(), "xfiles_dl_" + Path.GetRandomFileName());
            File.WriteAllText(tmpFile, "x");
            try
            {
                string unique = DownloadHelpers.GetUniquePath(tmpFile);
                Assert.AreNotEqual(tmpFile, unique);
                Assert.IsFalse(File.Exists(unique), "Unique path should not exist yet");
                Assert.IsTrue(unique.Contains("(1)"));
            }
            finally
            {
                File.Delete(tmpFile);
            }
        }

        [TestMethod]
        public void SanitizeFileName_CleansInvalidChars()
        {
            string result = DownloadHelpers.SanitizeFileName("file<name>:with\"bad");
            Assert.IsFalse(result.Contains("<"));
            Assert.IsFalse(result.Contains(">"));
            Assert.IsFalse(result.Contains(":"));
        }

        [TestMethod]
        public void SanitizeFileName_NullOrWhitespace_ReturnsNull()
        {
            Assert.IsNull(DownloadHelpers.SanitizeFileName(null));
            Assert.IsNull(DownloadHelpers.SanitizeFileName("   "));
        }

        [TestMethod]
        public void FromUrlLastSegment_ExtractsFileName()
        {
            string result = DownloadHelpers.FromUrlLastSegment("https://example.com/files/readme.md");
            Assert.AreEqual("readme.md", result);
        }

        [TestMethod]
        public void FromUrlLastSegment_TrailingSlash_ReturnsNull() =>
            Assert.IsNull(DownloadHelpers.FromUrlLastSegment("https://example.com/files/"));

        [TestMethod]
        public void FromUrlLastSegment_InvalidUrl_ReturnsNull() =>
            Assert.IsNull(DownloadHelpers.FromUrlLastSegment("not a url at all"));

        [TestMethod]
        public void ResolveFileName_SuggestedName_UsedAsFallback()
        {
            string name = DownloadHelpers.ResolveFileName(
                "https://gofile.io/d/abc123", null, "my-file.zip");
            Assert.AreEqual("my-file.zip", name);
        }

        [TestMethod]
        public void ResolveFileName_SuggestedName_NotOverrideContentDisposition()
        {
            string name = DownloadHelpers.ResolveFileName(
                "https://gofile.io/d/abc123",
                "attachment; filename=\"override.zip\"",
                "my-file.zip");
            Assert.AreEqual("override.zip", name);
        }

        [TestMethod]
        public void ResolveFileName_SuggestedName_WinsOverUrlPath()
        {
            string name = DownloadHelpers.ResolveFileName(
                "https://example.com/files/doc.pdf",
                null,
                "my-file.zip");
            Assert.AreEqual("my-file.zip", name);
        }

        [TestMethod]
        public void ResolveFileName_SuggestedName_NullFallsBackToUrl()
        {
            string name = DownloadHelpers.ResolveFileName(
                "https://example.com/files/doc.pdf",
                null,
                null);
            Assert.AreEqual("doc.pdf", name);
        }

        [TestMethod]
        public void ResolveFileName_SuggestedName_InvalidChars_Sanitized()
        {
            string name = DownloadHelpers.ResolveFileName(
                "https://gofile.io/d/abc123", null, "file<>with:bad");
            Assert.AreEqual("file__with_bad", name);
        }

        [TestMethod]
        public void ResolveFileName_SuggestedName_Whitespace_FallsBackToUrl()
        {
            string name = DownloadHelpers.ResolveFileName(
                "https://example.com/files/doc.pdf", null, "   ");
            Assert.AreEqual("doc.pdf", name);
        }

        [TestMethod]
        public void ResolveFileName_SuggestedName_GofileHashUrl_UsesSuggested()
        {
            string name = DownloadHelpers.ResolveFileName(
                "https://store1.gofile.io/download/webp/abc123/xyz789",
                null,
                "slax-64bit-slackware-15.0.4.iso");
            Assert.AreEqual("slax-64bit-slackware-15.0.4.iso", name);
        }
    }
}
