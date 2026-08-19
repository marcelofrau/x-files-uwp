using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.Network;

namespace XFiles.Tests
{
    [TestClass]
    public class NetworkOperationExceptionTests
    {
        [TestMethod]
        public void FriendlyMessage_TimedOut_ReturnsTimeout()
        {
            var msg = NetworkOperationException.FriendlyMessage(NetworkOperationReason.TimedOut);
            Assert.AreEqual("Network timed out — the server did not respond in time.", msg);
        }

        [TestMethod]
        public void FriendlyMessage_AccessDenied_ReturnsPermissionHint()
        {
            var msg = NetworkOperationException.FriendlyMessage(NetworkOperationReason.AccessDenied);
            Assert.AreEqual("Access denied — check the location's permissions.", msg);
        }

        [TestMethod]
        public void FriendlyMessage_AuthFailed_ReturnsCredentialHint()
        {
            var msg = NetworkOperationException.FriendlyMessage(NetworkOperationReason.AuthFailed);
            Assert.AreEqual("Authentication failed — check user and password.", msg);
        }

        [TestMethod]
        public void FriendlyMessage_NotFound_ReturnsNotFoundHint()
        {
            var msg = NetworkOperationException.FriendlyMessage(NetworkOperationReason.NotFound);
            Assert.AreEqual("Share or path not found.", msg);
        }

        [TestMethod]
        public void FriendlyMessage_Cancelled_ReturnsCancelled()
        {
            var msg = NetworkOperationException.FriendlyMessage(NetworkOperationReason.Cancelled);
            Assert.AreEqual("Operation cancelled.", msg);
        }

        [TestMethod]
        public void FriendlyMessage_Unreachable_NoDetail_ReturnsGenericMessage()
        {
            var msg = NetworkOperationException.FriendlyMessage(NetworkOperationReason.Unreachable);
            Assert.AreEqual("Could not reach the server.", msg);
        }

        [TestMethod]
        public void FriendlyMessage_Unreachable_WithCleanDetail_AppendsHint()
        {
            var msg = NetworkOperationException.FriendlyMessage(
                NetworkOperationReason.Unreachable, "Connection refused");
            Assert.AreEqual("Could not reach the server — Connection refused.", msg);
        }

        [TestMethod]
        public void FriendlyMessage_Unreachable_WithNullDetail_NoHint()
        {
            var msg = NetworkOperationException.FriendlyMessage(
                NetworkOperationReason.Unreachable, null);
            Assert.AreEqual("Could not reach the server.", msg);
        }

        [TestMethod]
        public void FriendlyMessage_Unreachable_WithEmptyDetail_NoHint()
        {
            var msg = NetworkOperationException.FriendlyMessage(
                NetworkOperationReason.Unreachable, "");
            Assert.AreEqual("Could not reach the server.", msg);
        }

        [TestMethod]
        public void FriendlyMessage_Unreachable_WithInternalException_StripHint()
        {
            // .NET internal noise should be stripped (too long, contains type names, etc.)
            var detail = "System.ObjectDisposedException: Cannot access a disposed object.\r\nObject name: 'Smb2Client'.";
            var msg = NetworkOperationException.FriendlyMessage(
                NetworkOperationReason.Unreachable, detail);
            Assert.AreEqual("Could not reach the server.", msg);
        }

        [TestMethod]
        public void FriendlyMessage_Unreachable_StackTrace_StripHint()
        {
            var detail = "   at SMBLibrary.SMB2Client.Connect(Boolean isMultiChannel)\r\n   at XFiles.Network.SmbSession.ConnectAsync";
            var msg = NetworkOperationException.FriendlyMessage(
                NetworkOperationReason.Unreachable, detail);
            Assert.AreEqual("Could not reach the server.", msg);
        }

        [TestMethod]
        public void FriendlyMessage_Unreachable_DetailExceeds140Chars_StripHint()
        {
            var detail = new string('A', 141);
            var msg = NetworkOperationException.FriendlyMessage(
                NetworkOperationReason.Unreachable, detail);
            Assert.AreEqual("Could not reach the server.", msg);
        }

        [TestMethod]
        public void FriendlyMessage_Unreachable_WithNewlines_TakesFirstLine()
        {
            var detail = "Connection refused\nsecond line ignored";
            var msg = NetworkOperationException.FriendlyMessage(
                NetworkOperationReason.Unreachable, detail);
            Assert.AreEqual("Could not reach the server — Connection refused.", msg);
        }

        [TestMethod]
        public void FriendlyMessage_Unreachable_InnerException_StripHint()
        {
            var detail = "Something went wrong ---> inner exception text";
            var msg = NetworkOperationException.FriendlyMessage(
                NetworkOperationReason.Unreachable, detail);
            Assert.AreEqual("Could not reach the server.", msg);
        }

        [TestMethod]
        public void Exception_PreservesReason()
        {
            var ex = new NetworkOperationException(
                NetworkOperationReason.AccessDenied, "share not found");
            Assert.AreEqual(NetworkOperationReason.AccessDenied, ex.Reason);
            Assert.AreEqual("share not found", ex.Message);
        }

        [TestMethod]
        public void Exception_WithInnerException()
        {
            var inner = new System.IO.IOException("disk error");
            var ex = new NetworkOperationException(
                NetworkOperationReason.Unreachable, "failed", inner);
            Assert.AreSame(inner, ex.InnerException);
        }
    }
}
