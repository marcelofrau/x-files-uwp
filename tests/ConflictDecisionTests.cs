using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.FileSystem;

namespace XFiles.Tests
{
    [TestClass]
    public class ConflictDecisionTests
    {
        [TestMethod]
        public void Values_AreDistinct()
        {
            Assert.AreNotEqual(ConflictDecision.ReplaceAll, ConflictDecision.RenameAll);
            Assert.AreNotEqual(ConflictDecision.ReplaceAll, ConflictDecision.Cancel);
            Assert.AreNotEqual(ConflictDecision.ReplaceAll, ConflictDecision.Resume);
            Assert.AreNotEqual(ConflictDecision.RenameAll, ConflictDecision.Cancel);
            Assert.AreNotEqual(ConflictDecision.RenameAll, ConflictDecision.Resume);
            Assert.AreNotEqual(ConflictDecision.Resume, ConflictDecision.Cancel);
        }
    }
}
