using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.FileSystem;

namespace XFiles.Tests
{
    [TestClass]
    public class EncodingDetectorTests
    {
        [TestMethod]
        public void Detect_Utf8WithBom_ReturnsUtf8()
        {
            byte[] bytes = { 0xEF, 0xBB, 0xBF, 0x68, 0x69 };

            EncodingDetector.Detect(bytes, out var encoding, out var name);

            Assert.AreEqual("UTF-8", name);
            Assert.AreEqual("hi", encoding.GetString(bytes, 3, bytes.Length - 3));
            Assert.IsTrue(encoding.GetPreamble().Length > 0);
        }

        [TestMethod]
        public void Detect_Utf16LeBom_ReturnsUtf16Le()
        {
            byte[] bytes = Encoding.Unicode.GetPreamble()
                .Concat(System.Text.Encoding.Unicode.GetBytes("hi")).ToArray();

            EncodingDetector.Detect(bytes, out var encoding, out var name);

            Assert.AreEqual("UTF-16 LE", name);
            Assert.AreEqual("hi", encoding.GetString(bytes, 2, bytes.Length - 2));
        }

        [TestMethod]
        public void Detect_Utf32LeBom_ReturnsUtf32Le()
        {
            byte[] bytes = { 0xFF, 0xFE, 0x00, 0x00, (byte)'h', 0x00, 0x00, 0x00 };

            EncodingDetector.Detect(bytes, out var encoding, out var name);

            Assert.AreEqual("UTF-32 LE", name);
            Assert.AreEqual("h", encoding.GetString(bytes, 4, 4));
        }

        [TestMethod]
        public void Detect_Utf16BeBom_ReturnsUtf16Be()
        {
            byte[] bytes = { 0xFE, 0xFF, 0x00, (byte)'h', 0x00, (byte)'i' };

            EncodingDetector.Detect(bytes, out var encoding, out var name);

            Assert.AreEqual("UTF-16 BE", name);
            Assert.AreEqual("hi", encoding.GetString(bytes, 2, 4));
        }

        [TestMethod]
        public void Detect_Utf32BeBom_ReturnsUtf32Be()
        {
            byte[] bytes = { 0x00, 0x00, 0xFE, 0xFF, 0x00, 0x00, 0x00, (byte)'h' };

            EncodingDetector.Detect(bytes, out var encoding, out var name);

            Assert.AreEqual("UTF-32 BE", name);
        }

        [TestMethod]
        public void Detect_PlainAsciiNoBom_ReturnsUtf8()
        {
            byte[] bytes = Encoding.UTF8.GetBytes("hello world");

            EncodingDetector.Detect(bytes, out var encoding, out var name);

            Assert.AreEqual("UTF-8", name);
            Assert.AreEqual("hello world", encoding.GetString(bytes));
        }

        [TestMethod]
        public void Detect_Utf8NoBomMultibyte_ReturnsUtf8()
        {
            byte[] bytes = Encoding.UTF8.GetBytes("café");

            EncodingDetector.Detect(bytes, out var encoding, out var name);

            Assert.AreEqual("UTF-8", name);
            Assert.AreEqual("café", encoding.GetString(bytes));
        }

        [TestMethod]
        public void Detect_InvalidUtf8_FallsBackToWindows1252()
        {
            byte[] bytes = { 0xC3, 0x28, 0x61 }; // invalid UTF-8 continuation

            EncodingDetector.Detect(bytes, out var encoding, out var name);

            Assert.AreEqual("Windows-1252", name);
        }

        [TestMethod]
        public void Detect_Utf16NoBom_NullByteHeuristic()
        {
            byte[] bytes = { 0x48, 0x00, 0x69, 0x00 }; // "Hi" UTF-16 LE, no BOM

            EncodingDetector.Detect(bytes, out var encoding, out var name);

            Assert.AreEqual("UTF-16 LE", name);
        }

        [TestMethod]
        public void IsValidUtf8_ValidMultibyte_ReturnsTrue()
        {
            byte[] bytes = Encoding.UTF8.GetBytes("€ café"); // 3-byte sequences
            Assert.IsTrue(EncodingDetector.IsValidUtf8(bytes));
        }

        [TestMethod]
        public void IsValidUtf8_TruncatedSequence_ReturnsFalse()
        {
            byte[] bytes = { 0xE2, 0x82 }; // truncated 3-byte sequence
            Assert.IsFalse(EncodingDetector.IsValidUtf8(bytes));
        }

        [TestMethod]
        public void IsValidUtf8_OverlongEncoding_ReturnsFalse()
        {
            byte[] bytes = { 0xC0, 0x80 }; // overlong NUL
            Assert.IsFalse(EncodingDetector.IsValidUtf8(bytes));
        }

        [TestMethod]
        public void IsValidUtf8_LoneContinuationByte_ReturnsFalse()
        {
            byte[] bytes = { 0x80, 0x61 };
            Assert.IsFalse(EncodingDetector.IsValidUtf8(bytes));
        }
    }
}
