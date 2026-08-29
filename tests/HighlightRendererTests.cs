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
        public void GetHighlightLang_MultiCaseLanguageGroups()
        {
            Assert.AreEqual("javascript", HighlightRenderer.GetHighlightLang(".jsx"));
            Assert.AreEqual("javascript", HighlightRenderer.GetHighlightLang(".mjs"));
            Assert.AreEqual("javascript", HighlightRenderer.GetHighlightLang(".cjs"));
            Assert.AreEqual("ruby", HighlightRenderer.GetHighlightLang(".rb"));
            Assert.AreEqual("kotlin", HighlightRenderer.GetHighlightLang(".kt"));
            Assert.AreEqual("kotlin", HighlightRenderer.GetHighlightLang(".kts"));
            Assert.AreEqual("bash", HighlightRenderer.GetHighlightLang(".sh"));
            Assert.AreEqual("bash", HighlightRenderer.GetHighlightLang(".bash"));
            Assert.AreEqual("bash", HighlightRenderer.GetHighlightLang(".zsh"));
            Assert.AreEqual("bash", HighlightRenderer.GetHighlightLang(".fish"));
            Assert.AreEqual("powershell", HighlightRenderer.GetHighlightLang(".ps1"));
            Assert.AreEqual("powershell", HighlightRenderer.GetHighlightLang(".psm1"));
            Assert.AreEqual("powershell", HighlightRenderer.GetHighlightLang(".psd1"));
            Assert.AreEqual("yaml", HighlightRenderer.GetHighlightLang(".yml"));
            Assert.AreEqual("markdown", HighlightRenderer.GetHighlightLang(".md"));
            Assert.AreEqual("markdown", HighlightRenderer.GetHighlightLang(".markdown"));
            Assert.AreEqual("html", HighlightRenderer.GetHighlightLang(".html"));
            Assert.AreEqual("html", HighlightRenderer.GetHighlightLang(".htm"));
            Assert.AreEqual("html", HighlightRenderer.GetHighlightLang(".xhtml"));
            Assert.AreEqual("python", HighlightRenderer.GetHighlightLang(".pyw"));
            Assert.AreEqual("python", HighlightRenderer.GetHighlightLang(".pyi"));
            Assert.AreEqual("sql", HighlightRenderer.GetHighlightLang(".sql"));
            Assert.AreEqual("go", HighlightRenderer.GetHighlightLang(".go"));
            Assert.AreEqual("lua", HighlightRenderer.GetHighlightLang(".lua"));
            Assert.AreEqual("perl", HighlightRenderer.GetHighlightLang(".pl"));
            Assert.AreEqual("perl", HighlightRenderer.GetHighlightLang(".pm"));
            Assert.AreEqual("swift", HighlightRenderer.GetHighlightLang(".swift"));
            Assert.AreEqual("dart", HighlightRenderer.GetHighlightLang(".dart"));
            Assert.AreEqual("r", HighlightRenderer.GetHighlightLang(".r"));
            Assert.AreEqual("css", HighlightRenderer.GetHighlightLang(".css"));
            Assert.AreEqual("scss", HighlightRenderer.GetHighlightLang(".scss"));
            Assert.AreEqual("less", HighlightRenderer.GetHighlightLang(".less"));
            Assert.AreEqual("xml", HighlightRenderer.GetHighlightLang(".xml"));
            Assert.AreEqual("json", HighlightRenderer.GetHighlightLang(".jsonc"));
            Assert.AreEqual("json", HighlightRenderer.GetHighlightLang(".json5"));
            Assert.AreEqual("latex", HighlightRenderer.GetHighlightLang(".tex"));
            Assert.AreEqual("latex", HighlightRenderer.GetHighlightLang(".latex"));
            Assert.AreEqual("dockerfile", HighlightRenderer.GetHighlightLang(".dockerfile"));
            Assert.AreEqual("ini", HighlightRenderer.GetHighlightLang(".ini"));
            Assert.AreEqual("ini", HighlightRenderer.GetHighlightLang(".cfg"));
            Assert.AreEqual("ini", HighlightRenderer.GetHighlightLang(".conf"));
            Assert.AreEqual("toml", HighlightRenderer.GetHighlightLang(".toml"));
            Assert.AreEqual("c", HighlightRenderer.GetHighlightLang(".c"));
            Assert.AreEqual("c", HighlightRenderer.GetHighlightLang(".h"));
            Assert.AreEqual("cpp", HighlightRenderer.GetHighlightLang(".cpp"));
            Assert.AreEqual("cpp", HighlightRenderer.GetHighlightLang(".cc"));
            Assert.AreEqual("cpp", HighlightRenderer.GetHighlightLang(".cxx"));
            Assert.AreEqual("cpp", HighlightRenderer.GetHighlightLang(".hpp"));
            Assert.AreEqual("cpp", HighlightRenderer.GetHighlightLang(".hxx"));
            Assert.AreEqual("fsharp", HighlightRenderer.GetHighlightLang(".fs"));
            Assert.AreEqual("fsharp", HighlightRenderer.GetHighlightLang(".fsx"));
            Assert.AreEqual("fsharp", HighlightRenderer.GetHighlightLang(".fsi"));
            Assert.AreEqual("vbnet", HighlightRenderer.GetHighlightLang(".vb"));
            Assert.AreEqual("protobuf", HighlightRenderer.GetHighlightLang(".proto"));
            Assert.AreEqual("graphql", HighlightRenderer.GetHighlightLang(".graphql"));
            Assert.AreEqual("graphql", HighlightRenderer.GetHighlightLang(".gql"));
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
