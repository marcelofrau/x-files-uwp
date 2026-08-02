using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.FileSystem;

namespace XFiles.Tests
{
    [TestClass]
    public class HighlightRendererTests
    {
        [TestMethod]
        public void GetHighlightLang_KnownExtensions()
        {
            Assert.AreEqual("csharp", HighlightRenderer.GetHighlightLang(".cs"));
            Assert.AreEqual("javascript", HighlightRenderer.GetHighlightLang(".js"));
            Assert.AreEqual("typescript", HighlightRenderer.GetHighlightLang(".ts"));
            Assert.AreEqual("python", HighlightRenderer.GetHighlightLang(".py"));
            Assert.AreEqual("rust", HighlightRenderer.GetHighlightLang(".rs"));
            Assert.AreEqual("json", HighlightRenderer.GetHighlightLang(".json"));
        }

        [TestMethod]
        public void GetHighlightLang_CaseInsensitiveAndNoDot()
        {
            Assert.AreEqual("csharp", HighlightRenderer.GetHighlightLang("CS"));
            Assert.AreEqual("csharp", HighlightRenderer.GetHighlightLang("Cs"));
            Assert.AreEqual("java", HighlightRenderer.GetHighlightLang(".JAVA"));
        }

        [TestMethod]
        public void GetHighlightLang_UnknownOrEmpty_ReturnsEmpty()
        {
            Assert.AreEqual("", HighlightRenderer.GetHighlightLang(".xyz"));
            Assert.AreEqual("", HighlightRenderer.GetHighlightLang(""));
            Assert.AreEqual("", HighlightRenderer.GetHighlightLang(null));
            Assert.AreEqual("", HighlightRenderer.GetHighlightLang("."));
        }

        [TestMethod]
        public void HtmlEncode_EscapesReservedChars()
        {
            string result = HighlightRenderer.HtmlEncode("<a href=\"x\">&</a>");
            Assert.AreEqual("&lt;a href=&quot;x&quot;&gt;&amp;&lt;/a&gt;", result);
        }

        [TestMethod]
        public void HtmlEncode_NullOrEmpty_ReturnsEmpty()
        {
            Assert.AreEqual("", HighlightRenderer.HtmlEncode(null));
            Assert.AreEqual("", HighlightRenderer.HtmlEncode(""));
        }

        [TestMethod]
        public void HtmlEncode_PlainText_Unchanged()
        {
            Assert.AreEqual("plain text 123", HighlightRenderer.HtmlEncode("plain text 123"));
        }

        [TestMethod]
        public void BuildSvgHtml_EmbedsBase64DataUri()
        {
            string result = HighlightRenderer.BuildSvgHtml("<svg/>");

            Assert.IsTrue(result.Contains("data:image/svg+xml;base64,"));
            Assert.IsTrue(result.Contains(ConvertBase64("<svg/>")));
        }

        [TestMethod]
        public void BuildHighlightHtml_IncludesEscapedCodeLangCssAndJs()
        {
            string result = HighlightRenderer.BuildHighlightHtml("&lt;b&gt;", "csharp", "CSS_BODY", "JS_BODY");

            Assert.IsTrue(result.Contains("<code class=\"csharp\">&lt;b&gt;</code>"));
            Assert.IsTrue(result.Contains("<style>CSS_BODY</style>"));
            Assert.IsTrue(result.Contains("<script>JS_BODY</script>"));
            Assert.IsTrue(result.Contains("hljs.highlightBlock"));
        }

        [TestMethod]
        public void BuildHighlightHtml_NullAssets_RendersEmpty()
        {
            string result = HighlightRenderer.BuildHighlightHtml("code", "cs", null, null);

            Assert.IsTrue(result.Contains("<style></style>"));
            Assert.IsTrue(result.Contains("<script></script>"));
        }

        private static string ConvertBase64(string value)
        {
            return System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value));
        }
    }
}
