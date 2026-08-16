using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using XFiles.Network;

namespace XFiles.Tests
{
    [TestClass]
    public class HostKeyTrustStoreTests
    {
        [TestMethod]
        public void Accept_Then_IsTrusted_True_ForSameFingerprint()
        {
            var store = new HostKeyTrustStore();
            Assert.IsFalse(store.IsTrusted("server.example:22", "SHA256:abc"));

            store.Accept("server.example:22", "SHA256:abc");

            Assert.IsTrue(store.IsTrusted("server.example:22", "SHA256:abc"));
        }

        [TestMethod]
        public void IsTrusted_False_WhenFingerprintDiffers()
        {
            var store = new HostKeyTrustStore();
            store.Accept("server.example:22", "SHA256:abc");

            Assert.IsFalse(store.IsTrusted("server.example:22", "SHA256:DIFFERENT"));
        }

        [TestMethod]
        public void IsTrusted_CaseInsensitiveHostAndFingerprint()
        {
            var store = new HostKeyTrustStore();
            store.Accept("Server.Example:22", "sha256:ABC");

            Assert.IsTrue(store.IsTrusted("server.example:22", "SHA256:abc"));
        }

        [TestMethod]
        public void IsTrusted_False_ForUnknownHost()
        {
            var store = new HostKeyTrustStore();
            Assert.IsFalse(store.IsTrusted("unknown:22", "SHA256:abc"));
        }

        [TestMethod]
        public void Forget_RemovesTrust()
        {
            var store = new HostKeyTrustStore();
            store.Accept("server.example:22", "SHA256:abc");

            store.Forget("server.example:22");

            Assert.IsFalse(store.IsTrusted("server.example:22", "SHA256:abc"));
        }

        [TestMethod]
        public void GetFingerprint_ReturnsAcceptedValue()
        {
            var store = new HostKeyTrustStore();
            store.Accept("server.example:22", "SHA256:abc");

            Assert.AreEqual("SHA256:abc", store.GetFingerprint("server.example:22"));
            Assert.IsNull(store.GetFingerprint("other:22"));
        }

        [TestMethod]
        public void Persists_Across_Instances()
        {
            string path = Path.Combine(Path.GetTempPath(),
                "xfiles-hostkeys-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var store = new HostKeyTrustStore(path);
                store.Accept("server.example:22", "SHA256:abc");
                store.Accept("second.example:2222", "SHA256:def");

                var reloaded = new HostKeyTrustStore(path);

                Assert.IsTrue(reloaded.IsTrusted("server.example:22", "SHA256:abc"));
                Assert.IsTrue(reloaded.IsTrusted("second.example:2222", "SHA256:def"));
                Assert.IsFalse(reloaded.IsTrusted("server.example:22", "SHA256:DIFFERENT"));
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }

        [TestMethod]
        public void Loads_Without_File_WhenPathMissing()
        {
            var store = new HostKeyTrustStore("Z:\\nonexistent\\dir\\host-keys.json");
            Assert.IsFalse(store.IsTrusted("server.example:22", "SHA256:abc"));
        }
    }
}
