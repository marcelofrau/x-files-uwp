using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XFiles.Metadata;

namespace XFiles.Tests
{
    [TestClass]
    public class MediaMetadataProbeTests
    {
        // ---------- fixture builders ----------

        private static void SetBits(byte[] b, int bitStart, int count, long value)
        {
            for (int i = 0; i < count; i++)
            {
                int p = bitStart + i;
                int byteIdx = p >> 3;
                int bitIdx = 7 - (p & 7);
                long bit = (value >> (count - 1 - i)) & 1;
                if (bit == 1) b[byteIdx] |= (byte)(1 << bitIdx);
            }
        }

        private static byte[] Be32(int v) => new[]
        {
            (byte)((v >> 24) & 0xFF), (byte)((v >> 16) & 0xFF),
            (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF),
        };

        private static byte[] Be16(int v) => new[]
        {
            (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF),
        };

        private static byte[] Le32(int v) => new[]
        {
            (byte)(v & 0xFF), (byte)((v >> 8) & 0xFF),
            (byte)((v >> 16) & 0xFF), (byte)((v >> 24) & 0xFF),
        };

        private static byte[] Le16(int v) => new[]
        {
            (byte)(v & 0xFF), (byte)((v >> 8) & 0xFF),
        };

        private static void Put(byte[] dst, int offset, byte[] src)
        {
            Buffer.BlockCopy(src, 0, dst, offset, src.Length);
        }

        private static byte[] Concat(params byte[][] parts)
        {
            var result = new byte[parts.Sum(p => p.Length)];
            int o = 0;
            foreach (var p in parts) { Buffer.BlockCopy(p, 0, result, o, p.Length); o += p.Length; }
            return result;
        }

        private static byte[] Box(string type, params byte[][] payload)
        {
            int size = 8 + payload.Sum(p => p.Length);
            byte[] b = new byte[size];
            Put(b, 0, Be32(size));
            Put(b, 4, Encoding.ASCII.GetBytes(type));
            int o = 8;
            foreach (var p in payload) { Buffer.BlockCopy(p, 0, b, o, p.Length); o += p.Length; }
            return b;
        }

        // mvhd (version 0): creation, mod, timescale, duration
        private static byte[] Mvhd(int timescale, int duration) => Box("mvhd",
            new byte[] { 0, 0, 0, 0 }, Be32(0), Be32(0), Be32(timescale), Be32(duration));

        // tkhd (version 0): creates a video track with WxH
        private static byte[] Tkhd(int width, int height, int duration)
        {
            byte[] payload = Concat(
                Be32(0x00000007),                 // version+flags
                Be32(0), Be32(0),                 // creation, mod
                Be32(1), Be32(0),                 // trackId, reserved
                Be32(duration),                   // duration (movie timescale)
                new byte[8],                      // reserved
                new byte[2], new byte[2], Be16(0x0100), new byte[2], // layer, altGroup, volume=1.0, reserved
                new byte[36],                     // matrix
                Be32(width << 16), Be32(height << 16));
            return Box("tkhd", payload);
        }

        private static byte[] Mdhd() => Box("mdhd",
            new byte[] { 0, 0, 0, 0 }, Be32(0), Be32(0), Be32(1000), Be32(60000), new byte[2], new byte[2]);

        private static byte[] Hdlr(string handlerType) => Box("hdlr",
            new byte[] { 0, 0, 0, 0 }, Be32(0), Encoding.ASCII.GetBytes(handlerType), new byte[12]);

        private static byte[] Stsd(string fourcc)
        {
            byte[] entry = Concat(Be32(20), Encoding.ASCII.GetBytes(fourcc), new byte[6], Be16(1));
            byte[] payload = Concat(new byte[] { 0, 0, 0, 0 }, Be32(1), entry);
            return Box("stsd", payload);
        }

        private static byte[] Stsz(int sampleCount)
        {
            byte[] payload = Concat(new byte[] { 0, 0, 0, 0 }, Be32(0), Be32(sampleCount));
            return Box("stsz", payload);
        }

        private static byte[] BuildVideoMp4(int durationMs, int width, int height, int frameCount)
        {
            byte[] t = Tkhd(width, height, durationMs);
            byte[] stbl = Box("stbl", Stsd("avc1"), Stsz(frameCount));
            byte[] minf = Box("minf", stbl);
            byte[] mdia = Box("mdia", Mdhd(), Hdlr("vide"), minf);
            byte[] trak = Box("trak", t, mdia);
            byte[] mvhd = Mvhd(1000, durationMs);
            byte[] moov = Box("moov", mvhd, trak);
            return Concat(Box("ftyp", Encoding.ASCII.GetBytes("isom"), Be32(0), Encoding.ASCII.GetBytes("isom")), moov);
        }

        private static byte[] BuildAudioMp4(string fourcc)
        {
            byte[] stbl = Box("stbl", Stsd(fourcc));
            byte[] minf = Box("minf", stbl);
            byte[] mdia = Box("mdia", Mdhd(), Hdlr("soun"), minf);
            byte[] trak = Box("trak", Tkhd(1, 1, 60000), mdia);
            byte[] moov = Box("moov", Mvhd(1000, 60000), trak);
            return Concat(Box("ftyp", Encoding.ASCII.GetBytes("M4A "), Be32(0), Encoding.ASCII.GetBytes("M4A ")), moov);
        }

        // ---------- MP3 ----------

        [TestMethod]
        public void Mp3_WithId3v2_ParsesAudioInfo()
        {
            // ID3v2 header, synchsafe tag size = 40
            byte[] id3 = Concat(
                Encoding.ASCII.GetBytes("ID3"), new byte[] { 3, 0, 0 },
                new byte[] { 0, 0, 0, 40 }, new byte[40]);
            // MPEG-1 Layer III, 128kbps, 44100 Hz, stereo
            byte[] frame = { 0xFF, 0xFB, 0x90, 0x00 };
            byte[] data = Concat(id3, frame);

            var meta = MediaMetadataProbe.Probe(data, 1_000_000);

            Assert.AreEqual("MPEG-1 Layer III", meta.Codec);
            Assert.AreEqual(44100, meta.SampleRateHz);
            Assert.AreEqual(2, meta.Channels);
            Assert.AreEqual(128, meta.BitrateKbps);
            Assert.AreEqual(62_500, meta.DurationMs); // 1_000_000 bytes at 128kbps
        }

        [TestMethod]
        public void Mp3_WithoutTag_ParsesFromFirstFrame()
        {
            byte[] data = { 0xFF, 0xFB, 0x90, 0x00 };
            var meta = MediaMetadataProbe.Probe(data, 1_000_000);

            Assert.AreEqual("MPEG-1 Layer III", meta.Codec);
            Assert.AreEqual(44100, meta.SampleRateHz);
            Assert.AreEqual(128, meta.BitrateKbps);
        }

        [TestMethod]
        public void Mp3_Mono_Mpeg2Layer3_DifferentTable()
        {
            // MPEG-2 Layer III: ver=(0xF3>>3)&3 = 2, layer=(0xF3>>1)&3 = 1.
            // b2=0x14 -> brIdx=(0x14>>4)=1 (L3 v2: 8 kbps), srIdx=(0x14>>2)&3=1 (v2: 24000).
            // b3=0xE0 -> mode=(0xE0>>6)=3 -> mono.
            byte[] data = { 0xFF, 0xF3, 0x14, 0xE0 };
            var meta = MediaMetadataProbe.Probe(data, 1_000_000);

            Assert.AreEqual("MPEG-2 Layer III", meta.Codec);
            Assert.AreEqual(24000, meta.SampleRateHz);
            Assert.AreEqual(1, meta.Channels);
            Assert.AreEqual(8, meta.BitrateKbps);
        }

        // ---------- FLAC ----------

        private static byte[] BuildFlac(int sampleRate, int channels, int bits, long totalSamples)
        {
            var info = new byte[34];
            SetBits(info, 0, 16, 4096);
            SetBits(info, 16, 16, 4096);
            SetBits(info, 32, 24, 0);
            SetBits(info, 56, 24, 0);
            SetBits(info, 80, 20, sampleRate);
            SetBits(info, 100, 3, channels - 1);
            SetBits(info, 103, 5, bits - 1);
            SetBits(info, 108, 36, totalSamples);

            return Concat(
                Encoding.ASCII.GetBytes("fLaC"),
                new byte[] { 0x80, 0x00, 0x00, 0x22 }, // LAST | type 0, length 34
                info);
        }

        [TestMethod]
        public void Flac_ParsesStreamInfo()
        {
            byte[] data = BuildFlac(44100, 2, 16, 441_000); // 10 seconds

            var meta = MediaMetadataProbe.Probe(data);

            Assert.AreEqual("FLAC", meta.Codec);
            Assert.AreEqual(44100, meta.SampleRateHz);
            Assert.AreEqual(2, meta.Channels);
            Assert.AreEqual(16, meta.BitsPerSample);
            Assert.AreEqual(10_000, meta.DurationMs);
        }

        [TestMethod]
        public void Flac_ZeroTotalSamples_NoDuration()
        {
            byte[] data = BuildFlac(48000, 1, 24, 0);
            var meta = MediaMetadataProbe.Probe(data);

            Assert.AreEqual(48000, meta.SampleRateHz);
            Assert.IsNull(meta.DurationMs);
            Assert.AreEqual(1, meta.Channels);
        }

        // ---------- WAV ----------

        private static byte[] BuildWav(int format, int channels, int sampleRate, int byteRate, int bits, int dataSize)
        {
            return Concat(
                Encoding.ASCII.GetBytes("RIFF"), Le32(36 + dataSize), Encoding.ASCII.GetBytes("WAVE"),
                Encoding.ASCII.GetBytes("fmt "), Le32(16),
                Le16(format), Le16(channels), Le32(sampleRate), Le32(byteRate), Le16(4), Le16(bits),
                Encoding.ASCII.GetBytes("data"), Le32(dataSize),
                new byte[Math.Min(dataSize, 64)]);
        }

        [TestMethod]
        public void Wav_Pcm16_ParsesChannelsRateDurationBitrate()
        {
            // 44100 Hz, 2ch, 16bit -> 176400 bytes/s; 1 s of data
            byte[] data = BuildWav(1, 2, 44100, 176400, 16, 176400);
            var meta = MediaMetadataProbe.Probe(data);

            Assert.AreEqual("PCM", meta.Codec);
            Assert.AreEqual(2, meta.Channels);
            Assert.AreEqual(44100, meta.SampleRateHz);
            Assert.AreEqual(16, meta.BitsPerSample);
            Assert.AreEqual(1000, meta.DurationMs);
            Assert.AreEqual(1411, meta.BitrateKbps);
        }

        [TestMethod]
        public void Wav_FloatFormat_Reported()
        {
            byte[] data = BuildWav(3, 2, 48000, 384000, 32, 384000);
            var meta = MediaMetadataProbe.Probe(data);

            Assert.AreEqual("IEEE Float PCM", meta.Codec);
            Assert.AreEqual(48000, meta.SampleRateHz);
            Assert.AreEqual(1000, meta.DurationMs);
        }

        // ---------- MP4 ----------

        [TestMethod]
        public void Mp4_Video_ParsesCodecDimsDurationFps()
        {
            byte[] data = BuildVideoMp4(60_000, 640, 480, 1500);
            var meta = MediaMetadataProbe.Probe(data);

            Assert.AreEqual("H.264/AVC", meta.Codec);
            Assert.AreEqual(60_000, meta.DurationMs);
            Assert.AreEqual(640, meta.Width);
            Assert.AreEqual(480, meta.Height);
            Assert.AreEqual(25.0, meta.FrameRateFps.Value, 0.01);
            Assert.IsNotNull(meta.BitrateKbps);
            long expected = data.Length * 8L * 1000L / 60_000L / 1000L;
            Assert.AreEqual(expected, meta.BitrateKbps);
        }

        [TestMethod]
        public void Mp4_AudioOnly_ReportsAacAndNoDims()
        {
            byte[] data = BuildAudioMp4("mp4a");
            var meta = MediaMetadataProbe.Probe(data);

            Assert.AreEqual("AAC", meta.Codec);
            Assert.AreEqual(60_000, meta.DurationMs);
            Assert.IsNull(meta.Width);
            Assert.IsNull(meta.Height);
        }

        [TestMethod]
        public void Mp4_HevcCodec_Reported()
        {
            byte[] data = BuildAudioMp4("hvc1");
            // replace stsd fourcc with hvc1 by rebuilding as video-ish
            byte[] stbl = Box("stbl", Stsd("hvc1"));
            byte[] minf = Box("minf", stbl);
            byte[] mdia = Box("mdia", Mdhd(), Hdlr("vide"), minf);
            byte[] trak = Box("trak", Tkhd(1920, 1080, 60_000), mdia);
            byte[] moov = Box("moov", Mvhd(1000, 60_000), trak);
            byte[] full = Concat(Box("ftyp", Encoding.ASCII.GetBytes("isom"), Be32(0), Encoding.ASCII.GetBytes("isom")), moov);

            var meta = MediaMetadataProbe.Probe(full);

            Assert.AreEqual("H.265/HEVC", meta.Codec);
            Assert.AreEqual(1920, meta.Width);
            Assert.AreEqual(1080, meta.Height);
        }

        [TestMethod]
        public void Mp4_NonFaststart_TailFallbackInProbeFile()
        {
            // ftyp at start, then 2 MB mdat so the moov is outside the head window,
            // then the moov box at the end.
            byte[] ftyp = Box("ftyp", Encoding.ASCII.GetBytes("isom"), Be32(0), Encoding.ASCII.GetBytes("isom"));
            byte[] original = BuildVideoMp4(60_000, 640, 480, 1500);
            byte[] moov = original.Skip(ftyp.Length).ToArray();
            byte[] pad = new byte[2 * 1024 * 1024];
            byte[] mdat = Box("mdat", pad);

            string path = Path.Combine(Path.GetTempPath(), "xfiles-test-" + Guid.NewGuid().ToString("N") + ".mp4");
            using (var fs = File.Create(path))
            {
                fs.Write(ftyp, 0, ftyp.Length);
                fs.Write(mdat, 0, mdat.Length);
                fs.Write(moov, 0, moov.Length);
            }

            try
            {
                byte[] headOnly = new byte[512 * 1024];
                using (var fs = File.OpenRead(path))
                {
                    fs.Read(headOnly, 0, headOnly.Length);
                }
                var headMeta = MediaMetadataProbe.Probe(headOnly, new FileInfo(path).Length);
                Assert.IsNull(headMeta.DurationMs);

                var full = MediaMetadataProbe.ProbeFile(path);
                Assert.IsNotNull(full.DurationMs, "tail moov should give duration");
                Assert.IsNotNull(full.Codec);
            }
            finally
            {
                File.Delete(path);
            }
        }

        // ---------- defensive ----------

        [TestMethod]
        public void EmptyBuffer_NeverThrows()
        {
            var meta = MediaMetadataProbe.Probe(new byte[2]);
            Assert.IsTrue(meta.IsEmpty);
            Assert.IsNull(meta.Codec);
        }

        [TestMethod]
        public void TruncatedFlac_NeverThrows()
        {
            var meta = MediaMetadataProbe.Probe(Encoding.ASCII.GetBytes("fLaC"));
            Assert.AreEqual("FLAC", meta.Codec);
            Assert.IsNull(meta.SampleRateHz);
        }

        [TestMethod]
        public void JunkBytes_NeverThrows()
        {
            byte[] junk = new byte[512];
            new Random(7).NextBytes(junk);
            junk[4] = (byte)'f'; junk[5] = (byte)'t'; junk[6] = (byte)'y'; junk[7] = (byte)'p';

            var meta = MediaMetadataProbe.Probe(junk, 999);
            Assert.IsNotNull(meta);
        }

        [TestMethod]
        public void ProbeFile_MissingFile_ReturnsEmpty()
        {
            var meta = MediaMetadataProbe.ProbeFile("Z:\\definitely\\missing-" + Guid.NewGuid().ToString("N") + ".mp3");
            Assert.IsTrue(meta.IsEmpty);
        }

        // ---------- image dimensions ----------

        private static byte[] PngHeader(int width, int height)
        {
            byte[] b = new byte[24];
            b[0] = 0x89; b[1] = 0x50; b[2] = 0x4E; b[3] = 0x47;
            b[4] = 0x0D; b[5] = 0x0A; b[6] = 0x1A; b[7] = 0x0A;
            Buffer.BlockCopy(Be32(width), 0, b, 16, 4);
            Buffer.BlockCopy(Be32(height), 0, b, 20, 4);
            return b;
        }

        private static byte[] GifHeader(int width, int height)
        {
            byte[] b = new byte[13];
            Buffer.BlockCopy(Encoding.ASCII.GetBytes("GIF89a"), 0, b, 0, 6);
            Buffer.BlockCopy(Le16(width), 0, b, 6, 2);
            Buffer.BlockCopy(Le16(height), 0, b, 8, 2);
            return b;
        }

        private static byte[] BmpHeader(int width, int height)
        {
            byte[] b = new byte[26];
            b[0] = (byte)'B'; b[1] = (byte)'M';
            Buffer.BlockCopy(Le32(width), 0, b, 18, 4);
            Buffer.BlockCopy(Le32(height), 0, b, 22, 4);
            return b;
        }

        private static byte[] JpegHeader(int width, int height)
        {
            // SOI, APP0, SOF0 with height/width.
            byte[] b = new byte[]
            {
                0xFF, 0xD8,
                0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
                0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x01, 0x00
            };
            b[25] = (byte)((height >> 8) & 0xFF); b[26] = (byte)(height & 0xFF);
            b[27] = (byte)((width >> 8) & 0xFF); b[28] = (byte)(width & 0xFF);
            return b;
        }

        [TestMethod]
        public void Image_ProbePng()
        {
            var (w, h) = MediaMetadataProbe.ProbeImageDimensions(PngHeader(640, 480));
            Assert.AreEqual(640, w);
            Assert.AreEqual(480, h);
        }

        [TestMethod]
        public void Image_ProbeGif()
        {
            var (w, h) = MediaMetadataProbe.ProbeImageDimensions(GifHeader(320, 200));
            Assert.AreEqual(320, w);
            Assert.AreEqual(200, h);
        }

        [TestMethod]
        public void Image_ProbeBmp()
        {
            var (w, h) = MediaMetadataProbe.ProbeImageDimensions(BmpHeader(128, 64));
            Assert.AreEqual(128, w);
            Assert.AreEqual(64, h);
        }

        [TestMethod]
        public void Image_ProbeJpeg()
        {
            var (w, h) = MediaMetadataProbe.ProbeImageDimensions(JpegHeader(800, 600));
            Assert.AreEqual(800, w);
            Assert.AreEqual(600, h);
        }

        [TestMethod]
        public void Image_ProbeJunk()
        {
            var (w, h) = MediaMetadataProbe.ProbeImageDimensions(new byte[] { 1, 2, 3, 4, 5 });
            Assert.IsNull(w);
            Assert.IsNull(h);
        }

        // ---------- pdf page count ----------

        [TestMethod]
        public void Pdf_CountFromPagesTree()
        {
            string path = Path.Combine(Path.GetTempPath(), $"xftest-pdf-{Guid.NewGuid():N}.pdf");
            try
            {
                string frag1 = "| /Type /Catalog /Pages 2 0 R |";
                string frag2 = "| 2 0 obj << /Type /Pages /Count 4 >> |";
                string frag3 = "| /Type /Page | /Type /Page |";
                byte[] data = Encoding.ASCII.GetBytes(frag1 + new string(' ', 4096) + frag2 + new string(' ', 4096) + frag3);
                File.WriteAllBytes(path, data);

                int count = MediaMetadataProbe.ProbePdfPageCount(path);
                Assert.AreEqual(4, count);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [TestMethod]
        public void Pdf_CountFallsBackToPageObjects()
        {
            string path = Path.Combine(Path.GetTempPath(), $"xftest-pdf-{Guid.NewGuid():N}.pdf");
            try
            {
                string frag = "| /Type /Page | /Type /Pages | /Type /Page |";
                File.WriteAllBytes(path, Encoding.ASCII.GetBytes(frag));

                int count = MediaMetadataProbe.ProbePdfPageCount(path);
                Assert.AreEqual(2, count);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [TestMethod]
        public void Pdf_MissingFile_ReturnsZero()
        {
            Assert.AreEqual(0, MediaMetadataProbe.ProbePdfPageCount("Z:\\missing-" + Guid.NewGuid().ToString("N") + ".pdf"));
        }
    }
}