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

                // Empty content without BOM → 0-byte file. LoadAsync returns null for 0-byte.
                var result = await TextEditorService.LoadAsync(path);
                Assert.IsNull(result);
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
                Assert.IsTrue(result.HasBom);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        // ── BOM preservation round-trip ──────────────────────────

        [TestMethod]
        public async Task Save_WithoutBom_ProducesNoBom()
        {
            string path = Path.Combine(Path.GetTempPath(), $"xfiles_test_{Guid.NewGuid():N}.txt");
            try
            {
                bool saved = await TextEditorService.SaveAsync(path, "{\"key\":\"value\"}", LineEndingStyle.LF, writeBom: false);
                Assert.IsTrue(saved);

                byte[] raw = File.ReadAllBytes(path);
                // No BOM prefix
                Assert.AreNotEqual(0xEF, raw[0]);
                Assert.AreNotEqual(0xBB, raw[1]);
                Assert.AreNotEqual(0xBF, raw[2]);

                var result = await TextEditorService.LoadAsync(path);
                Assert.IsNotNull(result);
                Assert.IsFalse(result.HasBom);
                Assert.AreEqual("{\"key\":\"value\"}", result.Text);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [TestMethod]
        public async Task Save_WithBom_ProducesBom()
        {
            string path = Path.Combine(Path.GetTempPath(), $"xfiles_test_{Guid.NewGuid():N}.txt");
            try
            {
                bool saved = await TextEditorService.SaveAsync(path, "hello", LineEndingStyle.LF, writeBom: true);
                Assert.IsTrue(saved);

                byte[] raw = File.ReadAllBytes(path);
                Assert.AreEqual(0xEF, raw[0]);
                Assert.AreEqual(0xBB, raw[1]);
                Assert.AreEqual(0xBF, raw[2]);

                var result = await TextEditorService.LoadAsync(path);
                Assert.IsNotNull(result);
                Assert.IsTrue(result.HasBom);
                Assert.AreEqual("hello", result.Text);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [TestMethod]
        public async Task RoundTrip_Utf8NoBom_PreservesNoBom()
        {
            string path = Path.Combine(Path.GetTempPath(), $"xfiles_test_{Guid.NewGuid():N}.json");
            try
            {
                // Simulate the user's scenario: JSON file without BOM
                string json = "{\n  \"name\": \"test\",\n  \"enabled\": true\n}";
                bool saved = await TextEditorService.SaveAsync(path, json, LineEndingStyle.LF, writeBom: false);
                Assert.IsTrue(saved);

                var loadResult = await TextEditorService.LoadAsync(path);
                Assert.IsNotNull(loadResult);
                Assert.IsFalse(loadResult.HasBom);
                Assert.AreEqual("UTF-8", loadResult.EncodingName);

                // Re-save preserving BOM state (as the overlay now does)
                bool saved2 = await TextEditorService.SaveAsync(path, loadResult.Text, loadResult.LineEnding, loadResult.HasBom);
                Assert.IsTrue(saved2);

                // Verify no BOM in final file
                byte[] raw = File.ReadAllBytes(path);
                Assert.AreEqual((byte)'{', raw[0]);

                var finalResult = await TextEditorService.LoadAsync(path);
                Assert.IsNotNull(finalResult);
                Assert.IsFalse(finalResult.HasBom);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [TestMethod]
        public async Task RoundTrip_Utf8WithBom_PreservesBom()
        {
            string path = Path.Combine(Path.GetTempPath(), $"xfiles_test_{Guid.NewGuid():N}.txt");
            try
            {
                // Start with a BOM file
                byte[] bom = { 0xEF, 0xBB, 0xBF };
                byte[] content = Encoding.UTF8.GetBytes("line1\nline2\n");
                byte[] output = new byte[bom.Length + content.Length];
                Buffer.BlockCopy(bom, 0, output, 0, bom.Length);
                Buffer.BlockCopy(content, 0, output, bom.Length, content.Length);
                File.WriteAllBytes(path, output);

                var loadResult = await TextEditorService.LoadAsync(path);
                Assert.IsNotNull(loadResult);
                Assert.IsTrue(loadResult.HasBom);

                // Re-save preserving BOM state
                bool saved = await TextEditorService.SaveAsync(path, loadResult.Text, loadResult.LineEnding, loadResult.HasBom);
                Assert.IsTrue(saved);

                byte[] raw = File.ReadAllBytes(path);
                Assert.AreEqual(0xEF, raw[0]);
                Assert.AreEqual(0xBB, raw[1]);
                Assert.AreEqual(0xBF, raw[2]);

                var finalResult = await TextEditorService.LoadAsync(path);
                Assert.IsNotNull(finalResult);
                Assert.IsTrue(finalResult.HasBom);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [TestMethod]
        public async Task Save_DefaultWriteBom_IsFalse()
        {
            string path = Path.Combine(Path.GetTempPath(), $"xfiles_test_{Guid.NewGuid():N}.txt");
            try
            {
                // Default (no writeBom arg) should NOT produce BOM
                bool saved = await TextEditorService.SaveAsync(path, "test", LineEndingStyle.LF);
                Assert.IsTrue(saved);

                byte[] raw = File.ReadAllBytes(path);
                Assert.AreEqual((byte)'t', raw[0]);

                var result = await TextEditorService.LoadAsync(path);
                Assert.IsNotNull(result);
                Assert.IsFalse(result.HasBom);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
