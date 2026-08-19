using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.FileSystem;

namespace XFiles.Tests
{
    [TestClass]
    public class TextEditorServiceTests
    {
        // ── DetectLineEnding ──────────────────────────────────────

        [TestMethod]
        public void DetectLineEnding_LF_ReturnsLF()
        {
            Assert.AreEqual(LineEndingStyle.LF, TextEditorService.DetectLineEnding("line1\nline2\n"));
        }

        [TestMethod]
        public void DetectLineEnding_CRLF_ReturnsCRLF()
        {
            Assert.AreEqual(LineEndingStyle.CRLF, TextEditorService.DetectLineEnding("line1\r\nline2\r\n"));
        }

        [TestMethod]
        public void DetectLineEnding_CR_ReturnsCR()
        {
            Assert.AreEqual(LineEndingStyle.CR, TextEditorService.DetectLineEnding("line1\rline2\r"));
        }

        [TestMethod]
        public void DetectLineEnding_Mixed_DominantWins()
        {
            // 5 CRLF vs 2 LF → CRLF
            Assert.AreEqual(LineEndingStyle.CRLF, TextEditorService.DetectLineEnding("a\r\nb\r\nc\r\nd\r\ne\r\nf\ng"));
        }

        [TestMethod]
        public void DetectLineEnding_EmptyString_ReturnsLF()
        {
            Assert.AreEqual(LineEndingStyle.LF, TextEditorService.DetectLineEnding(""));
        }

        [TestMethod]
        public void DetectLineEnding_NoLineEndings_ReturnsLF()
        {
            Assert.AreEqual(LineEndingStyle.LF, TextEditorService.DetectLineEnding("single line"));
        }

        [TestMethod]
        public void DetectLineEnding_Tie_LF_Wins()
        {
            // Equal LF and CRLF → LF (default)
            Assert.AreEqual(LineEndingStyle.LF, TextEditorService.DetectLineEnding("a\nb\nc\r\nd"));
        }

        // ── GetHighlightLang ──────────────────────────────────────

        [TestMethod]
        public void GetHighlightLang_CommonExtensions()
        {
            Assert.AreEqual("javascript", TextEditorService.GetHighlightLang(".js"));
            Assert.AreEqual("typescript", TextEditorService.GetHighlightLang(".ts"));
            Assert.AreEqual("python", TextEditorService.GetHighlightLang(".py"));
            Assert.AreEqual("csharp", TextEditorService.GetHighlightLang(".cs"));
            Assert.AreEqual("java", TextEditorService.GetHighlightLang(".java"));
            Assert.AreEqual("go", TextEditorService.GetHighlightLang(".go"));
            Assert.AreEqual("rust", TextEditorService.GetHighlightLang(".rs"));
            Assert.AreEqual("cpp", TextEditorService.GetHighlightLang(".cpp"));
            Assert.AreEqual("c", TextEditorService.GetHighlightLang(".c"));
            Assert.AreEqual("html", TextEditorService.GetHighlightLang(".html"));
            Assert.AreEqual("css", TextEditorService.GetHighlightLang(".css"));
            Assert.AreEqual("json", TextEditorService.GetHighlightLang(".json"));
            Assert.AreEqual("xml", TextEditorService.GetHighlightLang(".xml"));
            Assert.AreEqual("yaml", TextEditorService.GetHighlightLang(".yml"));
            Assert.AreEqual("sql", TextEditorService.GetHighlightLang(".sql"));
            Assert.AreEqual("markdown", TextEditorService.GetHighlightLang(".md"));
            Assert.AreEqual("bash", TextEditorService.GetHighlightLang(".sh"));
            Assert.AreEqual("powershell", TextEditorService.GetHighlightLang(".ps1"));
            Assert.AreEqual("toml", TextEditorService.GetHighlightLang(".toml"));
            Assert.AreEqual("lua", TextEditorService.GetHighlightLang(".lua"));
        }

        [TestMethod]
        public void GetHighlightLang_UnknownExtension_ReturnsEmpty()
        {
            Assert.AreEqual("", TextEditorService.GetHighlightLang(".xyz"));
            Assert.AreEqual("", TextEditorService.GetHighlightLang(".doc"));
        }

        [TestMethod]
        public void GetHighlightLang_NullOrEmpty_ReturnsEmpty()
        {
            Assert.AreEqual("", TextEditorService.GetHighlightLang(null));
            Assert.AreEqual("", TextEditorService.GetHighlightLang(""));
        }

        [TestMethod]
        public void GetHighlightLang_WithoutDot_Works()
        {
            Assert.AreEqual("javascript", TextEditorService.GetHighlightLang("js"));
            Assert.AreEqual("python", TextEditorService.GetHighlightLang("py"));
        }

        // ── GetFileTier / GetTierDescription ──────────────────────

        [TestMethod]
        public void GetFileTier_BelowThreshold_ReturnsFullEdit()
        {
            Assert.AreEqual(FileTier.FullEdit, TextEditorService.GetFileTier(0));
            Assert.AreEqual(FileTier.FullEdit, TextEditorService.GetFileTier(1024));
            Assert.AreEqual(FileTier.FullEdit, TextEditorService.GetFileTier(TextEditorService.FullEditMaxBytes));
        }

        [TestMethod]
        public void GetFileTier_AboveThreshold_ReturnsReadOnly()
        {
            Assert.AreEqual(FileTier.ReadOnly, TextEditorService.GetFileTier(TextEditorService.FullEditMaxBytes + 1));
            Assert.AreEqual(FileTier.ReadOnly, TextEditorService.GetFileTier(100 * 1024 * 1024));
        }

        [TestMethod]
        public void GetTierDescription_ReturnsNonEmpty()
        {
            Assert.IsFalse(string.IsNullOrEmpty(TextEditorService.GetTierDescription(FileTier.FullEdit)));
            Assert.IsFalse(string.IsNullOrEmpty(TextEditorService.GetTierDescription(FileTier.ReadOnly)));
        }

        // ── FormatFileSize ────────────────────────────────────────

        [TestMethod]
        public void FormatFileSize_Bytes()
        {
            Assert.AreEqual("0 B", TextEditorService.FormatFileSize(0));
            Assert.AreEqual("512 B", TextEditorService.FormatFileSize(512));
        }

        [TestMethod]
        public void FormatFileSize_KB()
        {
            Assert.AreEqual("1.0 KB", TextEditorService.FormatFileSize(1024));
            Assert.AreEqual("1.5 KB", TextEditorService.FormatFileSize(1536));
        }

        [TestMethod]
        public void FormatFileSize_MB()
        {
            Assert.AreEqual("1.0 MB", TextEditorService.FormatFileSize(1024 * 1024));
            Assert.AreEqual("2.5 MB", TextEditorService.FormatFileSize((long)(2.5 * 1024 * 1024)));
        }

        // ── Load/Save round-trip (Win32 I/O via temp files) ───────

        [TestMethod]
        public async Task SaveAndLoad_RoundTrip_LF()
        {
            string path = Path.Combine(Path.GetTempPath(), $"xfiles_test_{Guid.NewGuid():N}.txt");
            try
            {
                string content = "line1\nline2\nline3\n";
                bool saved = await TextEditorService.SaveAsync(path, content, LineEndingStyle.LF);
                Assert.IsTrue(saved);
                Assert.IsTrue(File.Exists(path));

                var result = await TextEditorService.LoadAsync(path);
                Assert.IsNotNull(result);
                Assert.AreEqual("line1\nline2\nline3\n", result.Text);
                Assert.AreEqual(LineEndingStyle.LF, result.LineEnding);
                Assert.AreEqual(FileTier.FullEdit, result.Tier);
                Assert.IsFalse(result.IsBinary);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [TestMethod]
        public async Task SaveAndLoad_RoundTrip_CRLF()
        {
            string path = Path.Combine(Path.GetTempPath(), $"xfiles_test_{Guid.NewGuid():N}.txt");
            try
            {
                string content = "line1\nline2\n";
                bool saved = await TextEditorService.SaveAsync(path, content, LineEndingStyle.CRLF);
                Assert.IsTrue(saved);

                var result = await TextEditorService.LoadAsync(path);
                Assert.IsNotNull(result);
                // Save converts LF→CRLF, Load detects CRLF
                Assert.AreEqual(LineEndingStyle.CRLF, result.LineEnding);
                Assert.IsTrue(result.Text.Contains("\r\n"));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [TestMethod]
        public async Task SaveAndLoad_RoundTrip_CR()
        {
            string path = Path.Combine(Path.GetTempPath(), $"xfiles_test_{Guid.NewGuid():N}.txt");
            try
            {
                string content = "line1\nline2\n";
                bool saved = await TextEditorService.SaveAsync(path, content, LineEndingStyle.CR);
                Assert.IsTrue(saved);

                var result = await TextEditorService.LoadAsync(path);
                Assert.IsNotNull(result);
                Assert.AreEqual(LineEndingStyle.CR, result.LineEnding);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [TestMethod]
        public async Task Load_NonExistentFile_ReturnsNull()
        {
            var result = await TextEditorService.LoadAsync("Z:\\nonexistent\\file.txt");
            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task Save_EmptyContent_Succeeds()
        {
            string path = Path.Combine(Path.GetTempPath(), $"xfiles_test_{Guid.NewGuid():N}.txt");
            try
            {
                bool saved = await TextEditorService.SaveAsync(path, "", LineEndingStyle.LF);
                Assert.IsTrue(saved);

                var result = await TextEditorService.LoadAsync(path);
                Assert.IsNotNull(result);
                Assert.AreEqual("", result.Text);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [TestMethod]
        public async Task Load_BinaryFile_Detected()
        {
            string path = Path.Combine(Path.GetTempPath(), $"xfiles_test_{Guid.NewGuid():N}.bin");
            try
            {
                byte[] data = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x00, 0x05 };
                File.WriteAllBytes(path, data);

                var result = await TextEditorService.LoadAsync(path);
                Assert.IsNotNull(result);
                Assert.IsTrue(result.IsBinary);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [TestMethod]
        public async Task Load_Utf8Bom_StripsBom()
        {
            string path = Path.Combine(Path.GetTempPath(), $"xfiles_test_{Guid.NewGuid():N}.txt");
            try
            {
                byte[] bom = { 0xEF, 0xBB, 0xBF };
                byte[] content = Encoding.UTF8.GetBytes("hello world");
                byte[] output = new byte[bom.Length + content.Length];
                Buffer.BlockCopy(bom, 0, output, 0, bom.Length);
                Buffer.BlockCopy(content, 0, output, bom.Length, content.Length);
                File.WriteAllBytes(path, output);

                var result = await TextEditorService.LoadAsync(path);
                Assert.IsNotNull(result);
                Assert.IsFalse(result.IsBinary); // UTF-8 BOM is recognized
                Assert.IsTrue(result.Text.StartsWith("hello world"));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
