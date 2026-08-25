using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.Network;

namespace XFiles.Tests
{
    [TestClass]
    public class WebDavSessionTests
    {
        private const string MinimalPropfindXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<D:multistatus xmlns:D=""DAV:"">
  <D:response>
    <D:href>/docs/</D:href>
    <D:propstat>
      <D:prop>
        <D:resourcetype><D:collection/></D:resourcetype>
        <D:getlastmodified>Fri, 15 Aug 2025 12:00:00 GMT</D:getlastmodified>
      </D:prop>
      <D:status>HTTP/1.1 200 OK</D:status>
    </D:propstat>
  </D:response>
  <D:response>
    <D:href>/docs/readme.txt</D:href>
    <D:propstat>
      <D:prop>
        <D:resourcetype/>
        <D:getcontentlength>4096</D:getcontentlength>
        <D:getlastmodified>Sat, 16 Aug 2025 10:30:00 GMT</D:getlastmodified>
      </D:prop>
      <D:status>HTTP/1.1 200 OK</D:status>
    </D:propstat>
  </D:response>
  <D:response>
    <D:href>/</D:href>
    <D:propstat>
      <D:prop>
        <D:resourcetype><D:collection/></D:resourcetype>
      </D:prop>
      <D:status>HTTP/1.1 200 OK</D:status>
    </D:propstat>
  </D:response>
</D:multistatus>";

        [TestMethod]
        public void ParsePropfind_Listing_DirectoryAndFile()
        {
            List<NetworkFileEntry> entries = WebDavSession.ParsePropfindListing(MinimalPropfindXml, "/");

            Assert.AreEqual(2, entries.Count);

            // Directory
            Assert.AreEqual("docs", entries[0].Name);
            Assert.IsTrue(entries[0].IsDirectory);
            Assert.AreEqual(0, entries[0].Size);

            // File
            Assert.AreEqual("readme.txt", entries[1].Name);
            Assert.IsFalse(entries[1].IsDirectory);
            Assert.AreEqual(4096, entries[1].Size);
        }

        [TestMethod]
        public void ParsePropfind_SkipsParentDirectory()
        {
            List<NetworkFileEntry> entries = WebDavSession.ParsePropfindListing(MinimalPropfindXml, "/");

            // The root "/" response should be skipped (matches parentPath)
            foreach (var entry in entries)
                Assert.AreNotEqual("/", entry.Name);
        }

        [TestMethod]
        public void ParsePropfind_EmptyListing()
        {
            string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<D:multistatus xmlns:D=""DAV:"">
</D:multistatus>";

            List<NetworkFileEntry> entries = WebDavSession.ParsePropfindListing(xml, "/empty");
            Assert.AreEqual(0, entries.Count);
        }

        private const string UrlEncodedPropfindXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<D:multistatus xmlns:D=""DAV:"">
  <D:response>
    <D:href>/my%20folder/</D:href>
    <D:propstat>
      <D:prop>
        <D:resourcetype><D:collection/></D:resourcetype>
      </D:prop>
      <D:status>HTTP/1.1 200 OK</D:status>
    </D:propstat>
  </D:response>
  <D:response>
    <D:href>/photo%20%28copy%29.jpg</D:href>
    <D:propstat>
      <D:prop>
        <D:resourcetype/>
        <D:getcontentlength>1024000</D:getcontentlength>
      </D:prop>
      <D:status>HTTP/1.1 200 OK</D:status>
    </D:propstat>
  </D:response>
</D:multistatus>";

        [TestMethod]
        public void ParsePropfind_UrlEncodedNames()
        {
            List<NetworkFileEntry> entries = WebDavSession.ParsePropfindListing(UrlEncodedPropfindXml, "/");

            Assert.AreEqual(2, entries.Count);
            Assert.AreEqual("my folder", entries[0].Name);
            Assert.IsTrue(entries[0].IsDirectory);

            Assert.AreEqual("photo (copy).jpg", entries[1].Name);
            Assert.IsFalse(entries[1].IsDirectory);
            Assert.AreEqual(1024000, entries[1].Size);
        }

        [TestMethod]
        public void ParsePropfind_NestedParentPath()
        {
            string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<D:multistatus xmlns:D=""DAV:"">
  <D:response>
    <D:href>/photos/</D:href>
    <D:propstat>
      <D:prop><D:resourcetype><D:collection/></D:resourcetype></D:prop>
      <D:status>HTTP/1.1 200 OK</D:status>
    </D:propstat>
  </D:response>
  <D:response>
    <D:href>/photos/vacation/</D:href>
    <D:propstat>
      <D:prop><D:resourcetype><D:collection/></D:resourcetype></D:prop>
      <D:status>HTTP/1.1 200 OK</D:status>
    </D:propstat>
  </D:response>
  <D:response>
    <D:href>/photos/vacation/beach.jpg</D:href>
    <D:propstat>
      <D:prop>
        <D:resourcetype/>
        <D:getcontentlength>2048</D:getcontentlength>
      </D:prop>
      <D:status>HTTP/1.1 200 OK</D:status>
    </D:propstat>
  </D:response>
</D:multistatus>";

            List<NetworkFileEntry> entries = WebDavSession.ParsePropfindListing(xml, "/photos");

            Assert.AreEqual(2, entries.Count);
            Assert.AreEqual("vacation", entries[0].Name);
            Assert.IsTrue(entries[0].IsDirectory);

            Assert.AreEqual("beach.jpg", entries[1].Name);
            Assert.IsFalse(entries[1].IsDirectory);
            Assert.AreEqual(2048, entries[1].Size);
        }
        #region EffectivePath

        [TestMethod]
        public void EffectivePath_NullPath_ReturnsShare()
        {
            Assert.AreEqual("media", WebDavSession.EffectivePath("media", null));
        }

        [TestMethod]
        public void EffectivePath_EmptyPath_ReturnsShare()
        {
            Assert.AreEqual("media", WebDavSession.EffectivePath("media", ""));
        }

        [TestMethod]
        public void EffectivePath_PathOverridesShare()
        {
            Assert.AreEqual("/media/music", WebDavSession.EffectivePath("media", "/media/music"));
        }

        [TestMethod]
        public void EffectivePath_NullShareAndPath_ReturnsEmpty()
        {
            Assert.AreEqual("", WebDavSession.EffectivePath(null, null));
        }

        [TestMethod]
        public void EffectivePath_NullShare_EmptyPath_ReturnsEmpty()
        {
            Assert.AreEqual("", WebDavSession.EffectivePath(null, ""));
        }

        [TestMethod]
        public void EffectivePath_EmptyShare_PathReturned()
        {
            Assert.AreEqual("/data/files", WebDavSession.EffectivePath("", "/data/files"));
        }

        #endregion
    }
}
