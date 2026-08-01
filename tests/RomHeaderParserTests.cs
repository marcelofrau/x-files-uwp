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

        private static void WriteAscii(byte[] data, int offset, string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            for (int i = 0; i < bytes.Length && offset + i < data.Length; i++)
                data[offset + i] = bytes[i];
        }
    }
}
