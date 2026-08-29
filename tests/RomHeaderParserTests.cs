using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.FileSystem;

namespace XFiles.Tests
{
    [TestClass]
    public class RomHeaderParserTests
    {
        [TestMethod]
        public void IsRomFile_KnownExtensions_True()
        {
            Assert.IsTrue(RomHeaderParser.IsRomFile(".nes"));
            Assert.IsTrue(RomHeaderParser.IsRomFile(".sfc"));
            Assert.IsTrue(RomHeaderParser.IsRomFile(".gb"));
            Assert.IsTrue(RomHeaderParser.IsRomFile(".gba"));
            Assert.IsTrue(RomHeaderParser.IsRomFile(".n64"));
            Assert.IsTrue(RomHeaderParser.IsRomFile(".chd"));
        }

        [TestMethod]
        public void IsRomFile_UnknownExtension_False()
        {
            Assert.IsFalse(RomHeaderParser.IsRomFile(".iso"));
            Assert.IsFalse(RomHeaderParser.IsRomFile(".exe"));
            Assert.IsFalse(RomHeaderParser.IsRomFile(null));
        }

        [TestMethod]
        public void TryParseTitle_NesHeader_ExtractsTitle()
        {
            byte[] data = new byte[32];
            data[0] = (byte)'N'; data[1] = (byte)'E'; data[2] = (byte)'S'; data[3] = 0x1A;
            WriteAscii(data, 0x10, "MEGAMAN 2");

            bool ok = RomHeaderParser.TryParseTitle(data, ".nes", out string title, out string system);

            Assert.IsTrue(ok);
            Assert.AreEqual("NES", system);
            Assert.AreEqual("MEGAMAN 2", title);
        }

        [TestMethod]
        public void TryParseTitle_NesWithoutMagic_Fails()
        {
            byte[] data = new byte[32];
            WriteAscii(data, 0x10, "MEGAMAN 2");

            bool ok = RomHeaderParser.TryParseTitle(data, ".nes", out _, out _);

            Assert.IsFalse(ok);
        }

        [TestMethod]
        public void TryParseTitle_SnesHiRom_ExtractsTitle()
        {
            byte[] data = new byte[0x10000];
            WriteAscii(data, 0xFFC0, "STREET FIGHTER II");

            bool ok = RomHeaderParser.TryParseTitle(data, ".sfc", out string title, out string system);

            Assert.IsTrue(ok);
            Assert.AreEqual("SNES", system);
            Assert.AreEqual("STREET FIGHTER II", title);
        }

        [TestMethod]
        public void TryParseTitle_GameBoy_ExtractsTitle()
        {
            byte[] data = new byte[0x144];
            WriteAscii(data, 0x134, "POKEMON RED");

            bool ok = RomHeaderParser.TryParseTitle(data, ".gb", out string title, out string system);

            Assert.IsTrue(ok);
            Assert.AreEqual("Game Boy", system);
            Assert.AreEqual("POKEMON RED", title);
        }

        [TestMethod]
        public void TryParseTitle_Gba_ExtractsTitle()
        {
            byte[] data = new byte[0xAC];
            WriteAscii(data, 0xA0, "MARIO KART");

            bool ok = RomHeaderParser.TryParseTitle(data, ".gba", out string title, out string system);

            Assert.IsTrue(ok);
            Assert.AreEqual("GBA", system);
            Assert.AreEqual("MARIO KART", title);
        }

        [TestMethod]
        public void TryParseTitle_Genesis_BigEndianPairs()
        {
            byte[] data = new byte[0x150];
            const string gameName = "SONIC";
            for (int i = 0; i < gameName.Length; i++)
            {
                data[0x120 + i * 2] = 0x00;        // high byte
                data[0x120 + i * 2 + 1] = (byte)gameName[i]; // low byte = ASCII
            }

            bool ok = RomHeaderParser.TryParseTitle(data, ".gen", out string title, out string system);

            Assert.IsTrue(ok);
            Assert.AreEqual("Genesis/Mega Drive", system);
            Assert.AreEqual("SONIC", title);
        }

        [TestMethod]
        public void TryParseTitle_N64BigEndian_ExtractsTitle()
        {
            byte[] data = new byte[0x40];
            data[0] = 0x80; data[1] = 0x37; data[2] = 0x12; data[3] = 0x40;
            WriteAscii(data, 0x20, "SUPER MARIO 64");

            bool ok = RomHeaderParser.TryParseTitle(data, ".z64", out string title, out string system);

            Assert.IsTrue(ok);
            Assert.AreEqual("Nintendo 64", system);
            Assert.AreEqual("SUPER MARIO 64", title);
        }

        [TestMethod]
        public void TryParseTitle_N64ByteSwapped_ReturnsTrue()
        {
            byte[] data = new byte[0x40];
            data[0] = 0x37; data[1] = 0x80; data[2] = 0x40; data[3] = 0x12; // .n64 swapped magic

            bool ok = RomHeaderParser.TryParseTitle(data, ".n64", out _, out string system);

            Assert.IsTrue(ok);
            Assert.AreEqual("Nintendo 64", system);
        }

        [TestMethod]
        public void TryParseTitle_NdsTitleAtStart()
        {
            byte[] data = new byte[16];
            WriteAscii(data, 0x00, "MARIOKART DS");

            bool ok = RomHeaderParser.TryParseTitle(data, ".nds", out string title, out string system);

            Assert.IsTrue(ok);
            Assert.AreEqual("Nintendo DS", system);
            Assert.AreEqual("MARIOKART DS", title);
        }

        [TestMethod]
        public void TryParseTitle_3ds_SystemOnly()
        {
            bool ok = RomHeaderParser.TryParseTitle(new byte[16], ".3ds", out string title, out string system);

            Assert.IsTrue(ok);
            Assert.AreEqual("Nintendo 3DS", system);
            Assert.IsNull(title);
        }

        [TestMethod]
        public void TryParseTitle_TooShort_Fails()
        {
            Assert.IsFalse(RomHeaderParser.TryParseTitle(new byte[8], ".nes", out _, out _));
        }

        [TestMethod]
        public void TryParseTitle_UnknownExtension_Fails()
        {
            Assert.IsFalse(RomHeaderParser.TryParseTitle(new byte[32], ".foo", out _, out _));
        }

        [TestMethod]
        public void TryParseTitle_MasterSystem_ExtractsTitle()
        {
            byte[] data = new byte[0x7FF0 + 32];
            WriteAscii(data, 0x7FF0, "PHANTASY STAR");

            bool ok = RomHeaderParser.TryParseTitle(data, ".sms", out string title, out string system);

            Assert.IsTrue(ok);
            Assert.AreEqual("Master System", system);
            Assert.AreEqual("PHANTASY STAR", title);
        }

        [TestMethod]
        public void TryParseTitle_GameGear_ExtractsTitle()
        {
            byte[] data = new byte[0x7FF0 + 32];
            WriteAscii(data, 0x7FF0, "SONIC THE HEDGEHOG");

            bool ok = RomHeaderParser.TryParseTitle(data, ".gg", out string title, out string system);

            Assert.IsTrue(ok);
            Assert.AreEqual("Game Gear", system);
            Assert.AreEqual("SONIC THE HEDGEHOG", title);
        }

        [TestMethod]
        public void TryParseTitle_PcEngine_ExtractsTitle()
        {
            byte[] data = new byte[0x140];
            WriteAscii(data, 0x120, "BOMBERMAN");

            bool ok = RomHeaderParser.TryParseTitle(data, ".pce", out string title, out string system);

            Assert.IsTrue(ok);
            Assert.AreEqual("PC Engine/TurboGrafx-16", system);
            Assert.AreEqual("BOMBERMAN", title);
        }

        [TestMethod]
        public void TryParseTitle_PcEngine_Tg16Alias_SameSystem()
        {
            byte[] data = new byte[0x140];
            WriteAscii(data, 0x120, "R-TYPE");
            bool ok = RomHeaderParser.TryParseTitle(data, ".tg16", out _, out string system);
            Assert.IsTrue(ok);
            Assert.AreEqual("PC Engine/TurboGrafx-16", system);
        }

        [TestMethod]
        public void TryParseTitle_Atari2600_SystemOnly()
        {
            bool ok = RomHeaderParser.TryParseTitle(new byte[32], ".a26", out string title, out string system);
            Assert.IsTrue(ok);
            Assert.AreEqual("Atari 2600", system);
            Assert.IsNull(title);
        }

        [TestMethod]
        public void TryParseTitle_Atari5200_SystemOnly()
        {
            bool ok = RomHeaderParser.TryParseTitle(new byte[32], ".a52", out string title, out string system);
            Assert.IsTrue(ok);
            Assert.AreEqual("Atari 5200", system);
            Assert.IsNull(title);
        }

        [TestMethod]
        public void TryParseTitle_Atari7800_SystemOnly()
        {
            bool ok = RomHeaderParser.TryParseTitle(new byte[32], ".a78", out string title, out string system);
            Assert.IsTrue(ok);
            Assert.AreEqual("Atari 7800", system);
            Assert.IsNull(title);
        }

        [TestMethod]
        public void TryParseTitle_Atari7800_OptionalHeader_ExtractsTitle()
        {
            byte[] data = new byte[128];
            data[0] = 0x01;
            WriteAscii(data, 1, "ATARI7800");
            WriteAscii(data, 17, "FOOD FIGHT");

            bool ok = RomHeaderParser.TryParseTitle(data, ".a78", out string title, out string system);

            Assert.IsTrue(ok);
            Assert.AreEqual("Atari 7800", system);
            Assert.AreEqual("FOOD FIGHT", title);
        }

        [TestMethod]
        public void TryParseTitle_ColecoVision_SystemOnly()
        {
            bool ok = RomHeaderParser.TryParseTitle(new byte[32], ".col", out string title, out string system);
            Assert.IsTrue(ok);
            Assert.AreEqual("ColecoVision", system);
            Assert.IsNull(title);
        }

        [TestMethod]
        public void TryParseTitle_Intellivision_SystemOnly()
        {
            bool ok = RomHeaderParser.TryParseTitle(new byte[32], ".int", out string title, out string system);
            Assert.IsTrue(ok);
            Assert.AreEqual("Intellivision", system);
            Assert.IsNull(title);
        }

        [TestMethod]
        public void TryParseTitle_Sg1000_SystemOnly()
        {
            bool ok = RomHeaderParser.TryParseTitle(new byte[32], ".sg", out string title, out string system);
            Assert.IsTrue(ok);
            Assert.AreEqual("SG-1000", system);
            Assert.IsNull(title);
        }

        [TestMethod]
        public void TryParseTitle_Msx_WithHeader_ExtractsTitle()
        {
            byte[] data = new byte[0x18];
            data[0] = (byte)'A'; data[1] = (byte)'B';
            WriteAscii(data, 0x10, "ALESTE");

            bool ok = RomHeaderParser.TryParseTitle(data, ".msx", out string title, out string system);

            Assert.IsTrue(ok);
            Assert.AreEqual("MSX", system);
            Assert.AreEqual("ALESTE", title);
        }

        [TestMethod]
        public void TryParseTitle_Msx_WithoutHeader_SystemOnly()
        {
            bool ok = RomHeaderParser.TryParseTitle(new byte[0x16], ".msx", out string title, out string system);
            Assert.IsTrue(ok);
            Assert.AreEqual("MSX", system);
            Assert.IsNull(title);
        }

        [TestMethod]
        public void TryParseTitle_Jaguar_WithHeader_ExtractsTitle()
        {
            byte[] data = new byte[0x38];
            WriteAscii(data, 0x18, "TEMPERATURE RUN");

            bool ok = RomHeaderParser.TryParseTitle(data, ".j64", out string title, out string system);

            Assert.IsTrue(ok);
            Assert.AreEqual("Atari Jaguar", system);
            Assert.AreEqual("TEMPERATURE RUN", title);
        }

        [TestMethod]
        public void TryParseTitle_Jaguar_TooShort_SystemOnly()
        {
            bool ok = RomHeaderParser.TryParseTitle(new byte[0x30], ".jag", out string title, out string system);
            Assert.IsTrue(ok);
            Assert.AreEqual("Atari Jaguar", system);
            Assert.IsNull(title);
        }

        [TestMethod]
        public void TryParseTitle_Lynx_WithHeader_ExtractsTitle()
        {
            byte[] data = new byte[42];
            WriteAscii(data, 10, "CHIPS CHALLENGE");

            bool ok = RomHeaderParser.TryParseTitle(data, ".lnx", out string title, out string system);

            Assert.IsTrue(ok);
            Assert.AreEqual("Atari Lynx", system);
            Assert.AreEqual("CHIPS CHALLENGE", title);
        }

        [TestMethod]
        public void TryParseTitle_Lynx_TooShort_SystemOnly()
        {
            bool ok = RomHeaderParser.TryParseTitle(new byte[30], ".lnx", out string title, out string system);
            Assert.IsTrue(ok);
            Assert.AreEqual("Atari Lynx", system);
            Assert.IsNull(title);
        }

        [TestMethod]
        public void TryParseTitle_ZxSpectrumSna_SystemOnly()
        {
            bool ok = RomHeaderParser.TryParseTitle(new byte[32], ".sna", out string title, out string system);
            Assert.IsTrue(ok);
            Assert.AreEqual("ZX Spectrum", system);
            Assert.IsNull(title);
        }

        [TestMethod]
        public void TryParseTitle_ZxSpectrumZ80_SystemOnly()
        {
            bool ok = RomHeaderParser.TryParseTitle(new byte[32], ".z80", out string title, out string system);
            Assert.IsTrue(ok);
            Assert.AreEqual("ZX Spectrum", system);
            Assert.IsNull(title);
        }

        [TestMethod]
        public void TryParseTitle_Vectrex_SystemOnly()
        {
            bool ok = RomHeaderParser.TryParseTitle(new byte[32], ".vec", out string title, out string system);
            Assert.IsTrue(ok);
            Assert.AreEqual("Vectrex", system);
            Assert.IsNull(title);
        }

        [TestMethod]
        public void TryParseTitle_WonderSwan_SystemOnly()
        {
            bool ok = RomHeaderParser.TryParseTitle(new byte[32], ".ws", out string title, out string system);
            Assert.IsTrue(ok);
            Assert.AreEqual("WonderSwan", system);
            Assert.IsNull(title);
        }

        [TestMethod]
        public void TryParseTitle_WonderSwanColor_SystemOnly()
        {
            bool ok = RomHeaderParser.TryParseTitle(new byte[32], ".wsc", out string title, out string system);
            Assert.IsTrue(ok);
            Assert.AreEqual("WonderSwan", system);
            Assert.IsNull(title);
        }

        [TestMethod]
        public void TryParseTitle_GameCube_SystemOnly()
        {
            bool ok = RomHeaderParser.TryParseTitle(new byte[32], ".gcm", out string title, out string system);
            Assert.IsTrue(ok);
            Assert.AreEqual("GameCube", system);
            Assert.IsNull(title);
        }

        [TestMethod]
        public void TryParseTitle_Dreamcast_Gdi_SystemOnly()
        {
            bool ok = RomHeaderParser.TryParseTitle(new byte[32], ".gdi", out string title, out string system);
            Assert.IsTrue(ok);
            Assert.AreEqual("Dreamcast", system);
            Assert.IsNull(title);
        }

        [TestMethod]
        public void TryParseTitle_Dreamcast_Cdi_SystemOnly()
        {
            bool ok = RomHeaderParser.TryParseTitle(new byte[32], ".cdi", out string title, out string system);
            Assert.IsTrue(ok);
            Assert.AreEqual("Dreamcast", system);
            Assert.IsNull(title);
        }

        [TestMethod]
        public void TryParseTitle_Chd_SystemOnly()
        {
            bool ok = RomHeaderParser.TryParseTitle(new byte[32], ".chd", out string title, out string system);
            Assert.IsTrue(ok);
            Assert.AreEqual("ROM", system);
            Assert.IsNull(title);
        }

        [TestMethod]
        public void TryParseTitle_NeoGeoPocket_ExtractsTitle()
        {
            byte[] data = new byte[0x30];
            WriteAscii(data, 0x20, "PUZZLE LINK");

            bool ok = RomHeaderParser.TryParseTitle(data, ".ngp", out string title, out string system);

            Assert.IsTrue(ok);
            Assert.AreEqual("Neo Geo Pocket", system);
            Assert.AreEqual("PUZZLE LINK", title);
        }

        [TestMethod]
        public void TryParseTitle_NeoGeoPocketColor_ExtractsTitle()
        {
            byte[] data = new byte[0x30];
            WriteAscii(data, 0x20, "SNK VS CAPCOM");

            bool ok = RomHeaderParser.TryParseTitle(data, ".ngc", out string title, out string system);

            Assert.IsTrue(ok);
            Assert.AreEqual("Neo Geo Pocket", system);
            Assert.AreEqual("SNK VS CAPCOM", title);
        }

        [TestMethod]
        public void TryParseTitle_NeoGeoPocket_TooShort_Fails()
        {
            Assert.IsFalse(RomHeaderParser.TryParseTitle(new byte[0x20], ".ngp", out _, out _));
        }

        [TestMethod]
        public void TryParseTitle_SnesLoRom_ExtractsTitle()
        {
            byte[] data = new byte[0x10000];
            WriteAscii(data, 0x7FC0, "FINAL FANTASY 3");

            bool ok = RomHeaderParser.TryParseTitle(data, ".sfc", out string title, out string system);

            Assert.IsTrue(ok);
            Assert.AreEqual("SNES", system);
            Assert.AreEqual("FINAL FANTASY 3", title);
        }

        [TestMethod]
        public void TryParseTitle_Snes_NoTitle_Fails()
        {
            bool ok = RomHeaderParser.TryParseTitle(new byte[0x10000], ".sfc", out _, out _);
            Assert.IsFalse(ok);
        }

        [TestMethod]
        public void TryParseTitle_N64V64WordSwapped_ExtractsTitle()
        {
            byte[] data = new byte[0x40];
            byte[] be = new byte[0x40];
            be[0] = 0x80; be[1] = 0x37; be[2] = 0x12; be[3] = 0x40;
            WriteAscii(be, 0x20, "WAVE RACE 64");

            for (int i = 0; i < 0x3C; i += 4)
            {
                data[i] = be[i + 3]; data[i + 1] = be[i + 2]; data[i + 2] = be[i + 1]; data[i + 3] = be[i];
            }

            bool ok = RomHeaderParser.TryParseTitle(data, ".v64", out string title, out string system);

            Assert.IsTrue(ok);
            Assert.AreEqual("Nintendo 64", system);
            Assert.AreEqual("WAVE RACE 64", title);
        }

        [TestMethod]
        public void TryParseTitle_N64ByteSwapped_ExtractsTitle()
        {
            byte[] data = new byte[0x40];
            byte[] be = new byte[0x40];
            be[0] = 0x80; be[1] = 0x37; be[2] = 0x12; be[3] = 0x40;
            WriteAscii(be, 0x20, "BANJO KAZOOIE");

            for (int i = 0; i < 0x3E; i += 2)
            {
                data[i] = be[i + 1]; data[i + 1] = be[i];
            }

            bool ok = RomHeaderParser.TryParseTitle(data, ".n64", out string title, out string system);

            Assert.IsTrue(ok);
            Assert.AreEqual("Nintendo 64", system);
            Assert.AreEqual("BANJO KAZOOIE", title);
        }

        [TestMethod]
        public void TryParseTitle_N64NoMagic_SystemOnlyNoTitle()
        {
            byte[] data = new byte[0x40];
            data[0] = 0x00; data[1] = 0x00; data[2] = 0x00; data[3] = 0x00;

            bool ok = RomHeaderParser.TryParseTitle(data, ".z64", out string title, out string system);

            Assert.IsTrue(ok);
            Assert.AreEqual("Nintendo 64", system);
            Assert.IsNull(title);
        }

        [TestMethod]
        public void TryParseTitle_N64TooShort_Fails()
        {
            Assert.IsFalse(RomHeaderParser.TryParseTitle(new byte[0x30], ".z64", out _, out _));
        }

        [TestMethod]
        public void TryParseTitle_GameBoyColor_System()
        {
            byte[] data = new byte[0x144];
            WriteAscii(data, 0x134, "POKEMON CRYS");
            data[0x143] = 0xC0;

            bool ok = RomHeaderParser.TryParseTitle(data, ".gbc", out string title, out string system);

            Assert.IsTrue(ok);
            Assert.AreEqual("Game Boy Color", system);
            Assert.AreEqual("POKEMON CRY", title);
        }

        private static void WriteAscii(byte[] data, int offset, string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            for (int i = 0; i < bytes.Length && offset + i < data.Length; i++)
                data[offset + i] = bytes[i];
        }
    }
}
