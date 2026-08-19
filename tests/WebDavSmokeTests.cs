using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XFiles.Tests
{
    /// <summary>
    /// Real-server smoke test for the WebDAV stack. Skipped (Inconclusive)
    /// unless the environment points at a reachable server — the docker smoke
    /// container from tools/network-smoke is the intended target:
    ///   X_FILES_WEBDAV_HOST   (required, e.g. 127.0.0.1)
    ///   X_FILES_WEBDAV_PORT   (optional, default 8081)
    ///   X_FILES_WEBDAV_USER   (optional, default user)
    ///   X_FILES_WEBDAV_PASS   (optional, default pass)
    ///   X_FILES_WEBDAV_START  (optional start folder, default /)
    ///   X_FILES_WEBDAV_WRITE  (optional writable folder for write-back)
    /// Exercises the WebDAV surface: PROPFIND list, GET read, and (when a
    /// writable folder is given) PUT write-readback-delete.
    /// </summary>
    [TestClass]
    public class WebDavSmokeTests
    {
        private static readonly XNamespace D = "DAV:";

        private static string BaseUrl()
        {
            string host = Environment.GetEnvironmentVariable("X_FILES_WEBDAV_HOST");
            int port = int.TryParse(Environment.GetEnvironmentVariable("X_FILES_WEBDAV_PORT"), out int p)
                ? p : 8081;
            return $"http://{host}:{port}";
        }

        private static string User() => Environment.GetEnvironmentVariable("X_FILES_WEBDAV_USER") ?? "user";
        private static string Pass() => Environment.GetEnvironmentVariable("X_FILES_WEBDAV_PASS") ?? "pass";
        private static string Start() => Environment.GetEnvironmentVariable("X_FILES_WEBDAV_START") ?? "/";

        private static HttpClient NewClient()
        {
            var handler = new HttpClientHandler
            {
                Credentials = new NetworkCredential(User(), Pass())
            };
            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        }

        private static string SkipUnless()
        {
            string host = Environment.GetEnvironmentVariable("X_FILES_WEBDAV_HOST");
            if (string.IsNullOrEmpty(host))
            {
                Assert.Inconclusive("X_FILES_WEBDAV_HOST not set — WebDAV smoke test skipped.");
                return null;
            }
            return host;
        }

        /// <summary>
        /// PROPFIND smoke test: lists the start folder and verifies at least
        /// one non-collection entry with positive content length exists.
        /// </summary>
        [TestMethod]
        public async Task RealWebDav_Propfind_List()
        {
            if (SkipUnless() == null) return;

            using var client = NewClient();
            string url = BaseUrl() + Start();

            using var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), url);
            request.Headers.Add("Depth", "1");
            using var response = await client.SendAsync(request);
            Assert.IsTrue(response.IsSuccessStatusCode,
                $"PROPFIND returned {(int)response.StatusCode} {response.StatusCode}");

            string xml = await response.Content.ReadAsStringAsync();
            var doc = XDocument.Parse(xml);
            var responses = doc.Descendants(D + "response").ToList();
            Assert.IsTrue(responses.Count > 0, "PROPFIND returned no entries.");

            var files = responses.Where(r =>
            {
                var prop = r.Element(D + "propstat")?.Element(D + "prop");
                var collection = prop?.Element(D + "resourcetype")?.Element(D + "collection");
                return collection == null;
            }).ToList();
            Assert.IsTrue(files.Count > 0, "No file entries in PROPFIND response.");

            var bigFile = files.FirstOrDefault(r =>
            {
                var len = r.Element(D + "propstat")?.Element(D + "prop")?.Element(D + "getcontentlength");
                return len != null && long.TryParse(len.Value, out long n) && n > 0;
            });
            Assert.IsNotNull(bigFile, "No file with positive content length.");
        }

        /// <summary>
        /// GET smoke test: downloads the first seed file and verifies non-zero bytes.
        /// </summary>
        [TestMethod]
        public async Task RealWebDav_Get_Read()
        {
            if (SkipUnless() == null) return;

            using var client = NewClient();
            string listUrl = BaseUrl() + Start();

            using var listReq = new HttpRequestMessage(new HttpMethod("PROPFIND"), listUrl);
            listReq.Headers.Add("Depth", "1");
            using var listResp = await client.SendAsync(listReq);
            string xml = await listResp.Content.ReadAsStringAsync();
            var doc = XDocument.Parse(xml);

            var fileHref = doc.Descendants(D + "response")
                .Select(r =>
                {
                    var href = r.Element(D + "href")?.Value;
                    var prop = r.Element(D + "propstat")?.Element(D + "prop");
                    var collection = prop?.Element(D + "resourcetype")?.Element(D + "collection");
                    var len = prop?.Element(D + "getcontentlength")?.Value;
                    return new { Href = href, IsCollection = collection != null, Length = len };
                })
                .FirstOrDefault(f => !f.IsCollection && f.Length != null &&
                                     long.TryParse(f.Length, out long n) && n > 0);

            Assert.IsNotNull(fileHref, "No seed file to download.");

            string fileUrl = BaseUrl() + Uri.UnescapeDataString(fileHref.Href);
            using var getResp = await client.GetAsync(fileUrl);
            Assert.IsTrue(getResp.IsSuccessStatusCode,
                $"GET returned {(int)getResp.StatusCode} {getResp.StatusCode}");

            byte[] body = await getResp.Content.ReadAsByteArrayAsync();
            Assert.IsTrue(body.Length > 0, "GET returned empty body.");
        }

        /// <summary>
        /// PUT write-back smoke test: uploads a temp file, downloads it back,
        /// verifies the bytes, then deletes it via DELETE.
        /// </summary>
        [TestMethod]
        public async Task RealWebDav_Put_WriteReadBack()
        {
            string writeFolder = Environment.GetEnvironmentVariable("X_FILES_WEBDAV_WRITE");
            if (SkipUnless() == null) return;
            if (string.IsNullOrEmpty(writeFolder))
            {
                Assert.Inconclusive("X_FILES_WEBDAV_WRITE not set (writable folder) — write-back smoke test skipped.");
                return;
            }

            string remoteName = "xfiles-smoke-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt";
            string remotePath = writeFolder.TrimEnd('/') + "/" + remoteName;
            string content = "X-Files WebDAV smoke write-readback\n" + Guid.NewGuid();

            using var client = NewClient();
            try
            {
                // PUT
                using var putReq = new HttpRequestMessage(HttpMethod.Put, BaseUrl() + remotePath);
                putReq.Content = new StringContent(content, Encoding.UTF8, "text/plain");
                using var putResp = await client.SendAsync(putReq);
                Assert.IsTrue(putResp.IsSuccessStatusCode,
                    $"PUT returned {(int)putResp.StatusCode} {putResp.StatusCode}");

                // GET back
                using var getResp = await client.GetAsync(BaseUrl() + remotePath);
                Assert.IsTrue(getResp.IsSuccessStatusCode,
                    $"GET after PUT returned {(int)getResp.StatusCode} {getResp.StatusCode}");
                string back = await getResp.Content.ReadAsStringAsync();
                Assert.AreEqual(content, back, "Round-tripped content mismatch.");

                // DELETE
                using var delResp = await client.SendAsync(
                    new HttpRequestMessage(HttpMethod.Delete, BaseUrl() + remotePath));
                Assert.IsTrue(delResp.IsSuccessStatusCode,
                    $"DELETE returned {(int)delResp.StatusCode} {delResp.StatusCode}");

                // Verify gone
                using var headResp = await client.SendAsync(
                    new HttpRequestMessage(HttpMethod.Head, BaseUrl() + remotePath));
                Assert.IsFalse(headResp.IsSuccessStatusCode,
                    $"File still accessible after DELETE ({(int)headResp.StatusCode}).");
            }
            finally
            {
                // Cleanup: try DELETE even if test failed
                try { await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, BaseUrl() + remotePath)); } catch { }
            }
        }
    }
}
