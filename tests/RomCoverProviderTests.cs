using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.FileSystem;

namespace XFiles.Tests
{
    [TestClass]
    public class RomCoverProviderTests
    {
        [TestMethod]
        public void BuildTitleVariations_NoIntroName_StripsRegionGroup()
        {
            var variations = RomCoverProvider.BuildTitleVariations("Super Mario Bros (USA)");

            CollectionAssert.AreEqual(
                new[] { "Super Mario Bros (USA)", "Super Mario Bros", "Super Mario Bros." },
                variations);
        }

        [TestMethod]
        public void BuildTitleVariations_MultipleGroups_StripsRightToLeft()
        {
            var variations = RomCoverProvider.BuildTitleVariations("Alien Brigade (1990) (Atari) [!]");

            CollectionAssert.AreEqual(
                new[]
                {
                    "Alien Brigade (1990) (Atari) [!]",
                    "Alien Brigade (1990) (Atari)",
                    "Alien Brigade (1990)",
                    "Alien Brigade",
                    "Alien Brigade.",
                },
                variations);
        }

        [TestMethod]
        public void BuildTitleVariations_NoGroups_SingleVariation()
        {
            var variations = RomCoverProvider.BuildTitleVariations("Tetris");

            Assert.AreEqual(1, variations.Count);
            Assert.AreEqual("Tetris", variations[0]);
        }

        [TestMethod]
        public void BuildTitleVariations_Slashes_ReplacedWithDash()
        {
            var variations = RomCoverProvider.BuildTitleVariations("Sonic 2 (W)/Beta");

            Assert.IsTrue(variations[0].Contains(" -"));
            Assert.IsTrue(variations.Any(v => v == "Sonic 2"));
        }

        [TestMethod]
        public void BuildTitleVariations_BracketedGroup_Stripped()
        {
            var variations = RomCoverProvider.BuildTitleVariations("Adventure [p1]");

            CollectionAssert.AreEqual(new[] { "Adventure [p1]", "Adventure", "Adventure." }, variations);
        }

        [TestMethod]
        public void LibRetroSystemNames_CommonSystems()
        {
            Assert.AreEqual("Nintendo - Nintendo Entertainment System", RomCoverProvider.LibRetroSystemNames["NES"]);
            Assert.AreEqual("Nintendo - Super Nintendo Entertainment System", RomCoverProvider.LibRetroSystemNames["SNES"]);
            Assert.AreEqual("Nintendo - Game Boy Advance", RomCoverProvider.LibRetroSystemNames["GBA"]);
            Assert.AreEqual("Sega - Mega Drive - Genesis", RomCoverProvider.LibRetroSystemNames["Genesis"]);
        }

        [TestMethod]
        public void LibRetroSystemNames_CaseInsensitiveLookup()
        {
            Assert.AreEqual(RomCoverProvider.LibRetroSystemNames["NES"], RomCoverProvider.LibRetroSystemNames["nes"]);
            Assert.AreEqual(RomCoverProvider.LibRetroSystemNames["GBA"], RomCoverProvider.LibRetroSystemNames["gBa"]);
        }

        [TestMethod]
        public void LibRetroSystemNames_UnknownSystem_NotPresent()
        {
            Assert.IsFalse(RomCoverProvider.LibRetroSystemNames.ContainsKey("ZX Spectrum"));
        }
    }
}
