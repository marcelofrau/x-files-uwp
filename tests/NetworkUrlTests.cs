using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.Network;

namespace XFiles.Tests
{
    [TestClass]
    public class NetworkUrlTests
    {
        // --- Compose ---

        [TestMethod]
        public void Compose_FullFields()
        {
            var c = new NetworkServerConfig
            {
                Protocol = NetworkProtocol.Smb,
                Host = "192.168.1.50",
                Username = "alice",
                Share = "music"
            };
            Assert.AreEqual("smb://alice@192.168.1.50/music", NetworkUrl.Compose(c));
        }

        [TestMethod]
        public void Compose_NoUsername()
        {
            var c = new NetworkServerConfig { Host = "nas", Share = "media" };
            Assert.AreEqual("smb://nas/media", NetworkUrl.Compose(c));
        }

        [TestMethod]
        public void Compose_NoShare()
        {
            var c = new NetworkServerConfig { Host = "192.168.1.50" };
            Assert.AreEqual("smb://192.168.1.50", NetworkUrl.Compose(c));
        }

        [TestMethod]
        public void Compose_UsernameOnly_NoShare()
        {
            var c = new NetworkServerConfig { Host = "nas", Username = "alice" };
            Assert.AreEqual("smb://alice@nas", NetworkUrl.Compose(c));
        }

        [TestMethod]
        public void Compose_HostLowercased()
        {
            var c = new NetworkServerConfig { Host = "MY-NAS.local", Share = "Music" };
            Assert.AreEqual("smb://my-nas.local/Music", NetworkUrl.Compose(c));
        }

        [TestMethod]
        public void Compose_TrimsWhitespace()
        {
            var c = new NetworkServerConfig { Host = "  nas  ", Username = "  bob  ", Share = "  share  " };
            Assert.AreEqual("smb://bob@nas/share", NetworkUrl.Compose(c));
        }

        [TestMethod]
        public void Compose_ShareWithLeadingSlash_Normalized()
        {
            var c = new NetworkServerConfig { Host = "nas", Share = "/music" };
            Assert.AreEqual("smb://nas/music", NetworkUrl.Compose(c));
        }

        [TestMethod]
        public void Compose_EmptyHost_Null()
        {
            var c = new NetworkServerConfig { Host = "   " };
            Assert.IsNull(NetworkUrl.Compose(c));
        }

        [TestMethod]
        public void Compose_NullConfig_Null()
        {
            Assert.IsNull(NetworkUrl.Compose(null));
        }

        // --- Parse ---

        [TestMethod]
        public void Parse_Full()
        {
            var c = NetworkUrl.Parse("smb://alice@192.168.1.50/music");
            Assert.IsNotNull(c);
            Assert.AreEqual(NetworkProtocol.Smb, c.Protocol);
            Assert.AreEqual("alice", c.Username);
            Assert.AreEqual("192.168.1.50", c.Host);
            Assert.AreEqual("music", c.Share);
        }

        [TestMethod]
        public void Parse_NoUserNoShare()
        {
            var c = NetworkUrl.Parse("smb://nas");
            Assert.IsNotNull(c);
            Assert.IsNull(c.Username);
            Assert.AreEqual("nas", c.Host);
            Assert.IsNull(c.Share);
        }

        [TestMethod]
        public void Parse_UsernameOnly_NoShare()
        {
            var c = NetworkUrl.Parse("smb://bob@nas");
            Assert.IsNotNull(c);
            Assert.AreEqual("bob", c.Username);
            Assert.AreEqual("nas", c.Host);
            Assert.IsNull(c.Share);
        }

        [TestMethod]
        public void Parse_UnknownScheme_Null()
        {
            Assert.IsNull(NetworkUrl.Parse("ftp://user@host/share"));
        }

        [TestMethod]
        public void Parse_NoScheme_Null()
        {
            Assert.IsNull(NetworkUrl.Parse("host/share"));
        }

        [TestMethod]
        public void Parse_EmptyHost_Null()
        {
            Assert.IsNull(NetworkUrl.Parse("smb://"));
            Assert.IsNull(NetworkUrl.Parse("smb://@/share"));
        }

        [TestMethod]
        public void Parse_NullOrEmpty_Null()
        {
            Assert.IsNull(NetworkUrl.Parse(null));
            Assert.IsNull(NetworkUrl.Parse(""));
            Assert.IsNull(NetworkUrl.Parse("   "));
        }

        [TestMethod]
        public void Parse_TrailingSlash_EmptyShare()
        {
            var c = NetworkUrl.Parse("smb://nas/");
            Assert.IsNotNull(c);
            Assert.AreEqual("nas", c.Host);
            Assert.IsNull(c.Share);
        }

        // --- Round trip ---

        [TestMethod]
        public void Compose_Parse_RoundTrip()
        {
            var original = new NetworkServerConfig
            {
                Protocol = NetworkProtocol.Smb,
                Host = "NAS-01.local",
                Username = "alice",
                Share = "music"
            };
            var parsed = NetworkUrl.Parse(NetworkUrl.Compose(original));
            Assert.IsNotNull(parsed);
            Assert.AreEqual(original.Protocol, parsed.Protocol);
            Assert.AreEqual(original.Username, parsed.Username);
            Assert.AreEqual("nas-01.local", parsed.Host);
            Assert.AreEqual(original.Share, parsed.Share);
        }

        // --- Defaults / identity ---

        [TestMethod]
        public void DefaultPort_Smb_445()
        {
            Assert.AreEqual(445, NetworkUrl.DefaultPort(NetworkProtocol.Smb));
        }

        [TestMethod]
        public void VaultResource_EqualsCanonical()
        {
            var c = new NetworkServerConfig { Host = "nas", Username = "alice", Share = "media" };
            Assert.AreEqual(NetworkUrl.Compose(c), NetworkUrl.VaultResource(c));
        }

        [TestMethod]
        public void EffectivePort_DefaultWhenZero()
        {
            var c = new NetworkServerConfig { Protocol = NetworkProtocol.Smb };
            Assert.AreEqual(445, c.EffectivePort);
        }

        [TestMethod]
        public void EffectivePort_ExplicitOverride()
        {
            var c = new NetworkServerConfig { Protocol = NetworkProtocol.Smb, Port = 1445 };
            Assert.AreEqual(1445, c.EffectivePort);
        }

        // --- Display / sort ---

        [TestMethod]
        public void DisplayName_FriendlyOverUrl()
        {
            var c = new NetworkServerConfig { Host = "nas", DisplayName = "My NAS" };
            Assert.AreEqual("My NAS", NetworkUrl.DisplayName(c));
        }

        [TestMethod]
        public void DisplayName_NoFriendly_ComposedUrl()
        {
            var c = new NetworkServerConfig { Host = "nas", Username = "bob", Share = "media" };
            Assert.AreEqual("smb://bob@nas/media", NetworkUrl.DisplayName(c));
        }

        [TestMethod]
        public void SortKey_Lowercases()
        {
            var c = new NetworkServerConfig { Host = "nas", DisplayName = "My NAS" };
            Assert.AreEqual("my nas", NetworkUrl.SortKey(c));
        }
    }
}
