using System;
using System.IO;

namespace XFiles.Metadata
{
    /// <summary>
    /// Rich media metadata read purely from container headers. Local files only —
    /// the property dialog passes the first bytes of the file. Never throws on
    /// malformed data: a parse that cannot produce a field just leaves it null so
    /// the caller falls back to basic info. Pure + linkable into the desktop test
    /// project (tests hand Byte[] buffers directly).
    ///
    /// Why not Windows.Storage FileProperties: StorageFile.GetFileFromPathAsync +
    /// Properties only works inside the app container on Xbox — arbitrary drive
    /// paths (external HDD, NAS mount) fail (project pitfall #1). Manual container
    /// parsing through the FromApp file APIs is the only path that works everywhere.
    /// </summary>
    public sealed class MediaMetadata
    {
        /// <summary>Human-codec name, e.g. "MPEG-1 Layer 3", "FLAC", "H.264/AVC".</summary>
        public string Codec;

        public int? SampleRateHz;
        public int? Channels;
        public int? BitsPerSample;
        public long? DurationMs;

        /// <summary>Average bitrate in kbps.</summary>
        public long? BitrateKbps;

        public int? Width;
        public int? Height;
        public double? FrameRateFps;

        public bool IsEmpty =>
            Codec == null && SampleRateHz == null && Channels == null && BitsPerSample == null &&
            DurationMs == null && BitrateKbps == null && Width == null && Height == null && FrameRateFps == null;
    }

    /// <summary>
    /// Container header parsers for the current media corpus: MP3 (ID3v2 skip +
    /// first MPEG audio frame), FLAC (STREAMINFO), WAV (RIFF fmt/data chunks),
    /// MP4/M4A (mvhd/tkhd/stsd/stsz). MKV/AVI/OGG are future v2 work.
    /// </summary>
    public static class MediaMetadataProbe
    {
        private const int HeadBytes = 512 * 1024;
        private const int TailBytes = 512 * 1024;

        /// <summary>
        /// Reads the head of a local file (header probing, image dimensions). Null
        /// on failure — never throws. Public so the Properties dialog can reuse the
        /// same file-range reader for image dimension sniffing.
        /// </summary>
        public static byte[] ReadFileHead(string path, int maxBytes = 512 * 1024)
        {
            try
            {
                long length = new FileInfo(path).Length;
                if (length <= 0) return null;
                return ReadFileRange(path, 0, (int)Math.Min(length, (long)Math.Max(1, maxBytes)));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Probes a local file: reads the header region, dispatches by magic
        /// bytes, parses. For MP4 without moov at the start (non-faststart files)
        /// the tail is read too. Returns an empty metadata object (never null,
        /// never throws).
        /// </summary>
        public static MediaMetadata ProbeFile(string path)
        {
            try
            {
                long length = new FileInfo(path).Length;
                byte[] head = ReadFileRange(path, 0, (int)Math.Min(length, (long)HeadBytes));
                if (head == null || head.Length == 0) return new MediaMetadata();

                var meta = Probe(head, length);
                if (IsMp4Like(head) && meta.DurationMs == null)
                {
                    // Non-faststart MP4 stores moov at the end — sniff the tail and
                    // parse the moov box found inside the fragment.
                    byte[] tail = ReadFileRange(path, Math.Max(0, length - TailBytes), TailBytes);
                    if (tail != null && TryFindMoovInFragment(tail, out int moovOffset))
                    {
                        var tailMeta = new MediaMetadata();
                        ParseMp4At(tail, moovOffset, length, tailMeta);
                        if (!tailMeta.IsEmpty) return tailMeta;
                    }
                }
                return meta;
            }
            catch
            {
                return new MediaMetadata();
            }
        }

        /// <summary>Probes an in-memory buffer (tests, small stream snapshots).</summary>
        public static MediaMetadata Probe(byte[] data, long totalSize = -1)
        {
            var meta = new MediaMetadata();
            if (data == null || data.Length < 4) return meta;
            long fileSize = totalSize < 0 ? data.Length : totalSize;

            try
            {
                if (data[0] == 'f' && data[1] == 'L' && data[2] == 'a' && data[3] == 'C')
                    ParseFlac(data, fileSize, meta);
                else if (LooksLikeMp4(data, out _))
                    ParseMp4(data, fileSize, meta);
                else if (data.Length >= 12 && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F'
                    && data[8] == 'W' && data[9] == 'A' && data[10] == 'V' && data[11] == 'E')
                    ParseWav(data, meta);
                else
                    ParseMp3(data, fileSize, meta);
            }
            catch
            {
                // defensive: partial info already on meta is fine
            }

            return meta;
        }

        // ---------- MP3 ----------

        private static void ParseMp3(byte[] data, long fileSize, MediaMetadata meta)
        {
            // Skip ID3v2 tag if present.
            int p = SkipId3v2(data);

            // MPEG audio frame sync: 11 set bits (0xFF followed by 0xEx).
            int frameIndex = -1;
            for (int i = p; i + 4 <= data.Length; i++)
            {
                if (data[i] == 0xFF && (data[i + 1] & 0xE0) == 0xE0)
                {
                    if (TryParseMpegHeader(data, i, out int ver, out int layer, out int brIdx, out int srIdx, out int mode))
                    {
                        frameIndex = i;
                        meta.Codec = DescribeMpeg(ver, layer);
                        meta.SampleRateHz = MpegSampleRate(ver, srIdx);
                        meta.Channels = mode == 3 ? 1 : 2;
                        meta.BitrateKbps = MpegBitrateKbps(ver, layer, brIdx);
                        if (meta.BitrateKbps is long br && br > 0 && fileSize > 0)
                        {
                            meta.DurationMs = fileSize * 8L * 1000L / (br * 1000L);
                        }
                        break;
                    }
                }
            }

            if (frameIndex < 0)
            {
                // Only own the codec label when the buffer actually starts as
                // MP3 (ID3 tag or plausible frame). Random bytes probed by the
                // tail-sniff must stay empty so the caller falls back correctly.
                bool plausibleMp3 =
                    (data.Length >= 3 && data[0] == 'I' && data[1] == 'D' && data[2] == '3') ||
                    (data.Length >= 2 && data[0] == 0xFF && (data[1] & 0xE0) == 0xE0);
                if (plausibleMp3) meta.Codec = "MPEG Audio";
            }
        }

        private static int SkipId3v2(byte[] data)
        {
            if (data.Length < 10 || data[0] != 'I' || data[1] != 'D' || data[2] != '3') return 0;
            if ((data[3] & 0xEF) > 0x04) return 0; // future major version — bail
            int size = Synchsafe(data, 6);
            int offset = 10 + size;
            if ((data[5] & 0x40) != 0 && offset + 4 <= data.Length)
            {
                offset += Synchsafe(data, offset); // extended header size (22 total incl 2 sb)
            }
            return offset;
        }

        private static int Synchsafe(byte[] b, int o)
        {
            if (o + 4 > b.Length) return 0;
            return (b[o] << 21) | (b[o + 1] << 14) | (b[o + 2] << 7) | b[o + 3];
        }

        private static bool TryParseMpegHeader(byte[] data, int o, out int ver, out int layer, out int brIdx, out int srIdx, out int mode)
        {
            ver = layer = brIdx = srIdx = mode = -1;
            if (o + 4 > data.Length) return false;
            // 0xFF Ex
            if (data[o] != 0xFF || (data[o + 1] & 0xE0) != 0xE0) return false;
            // bits shared: version 2 bits, layer 2 bits, protection 1 bit
            int b1 = data[o + 1];
            int b2 = data[o + 2];
            int b3 = data[o + 3];

            ver = (b1 >> 3) & 0x3;      // 3=MPEG1, 2=MPEG2, 0=2.5
            layer = (b1 >> 1) & 0x3;    // 1=Layer III, 2=Layer II, 3=Layer I
            brIdx = (b2 >> 4) & 0xF;
            srIdx = (b2 >> 2) & 0x3;
            mode = (b3 >> 6) & 0x3;

            if (ver == 1) return false;      // reserved
            if (layer == 0) return false;    // reserved
            if (brIdx == 0 || brIdx == 15) return false;
            if (srIdx == 3) return false;
            return true;
        }

        private static string DescribeMpeg(int ver, int layer)
        {
            string v = ver == 3 ? "MPEG-1" : ver == 2 ? "MPEG-2" : "MPEG-2.5";
            string l = layer == 1 ? "Layer III" : layer == 2 ? "Layer II" : "Layer I";
            return v + " " + l;
        }

        private static int MpegSampleRate(int ver, int idx)
        {
            if (idx < 0 || idx > 2) return -1;
            int[] v1 = { 44100, 48000, 32000 };
            int[] v2 = { 22050, 24000, 16000 };
            int[] v25 = { 11025, 12000, 8000 };
            return ver == 3 ? v1[idx] : ver == 2 ? v2[idx] : v25[idx];
        }

        private static long MpegBitrateKbps(int ver, int layer, int idx)
        {
            if (idx < 0 || idx > 15) return 0;
            bool mpeg1 = ver == 3;
            // layer here is the raw header value: 1=Layer III, 2=Layer II, 3=Layer I
            int[] l3v1 = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0 };
            int[] l3v2 = { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0 };
            int[] l2v1 = { 0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384, 0 };
            int[] l1v1 = { 0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448, 0 };
            if (layer == 1) return mpeg1 ? l3v1[idx] : l3v2[idx];
            if (layer == 2) return mpeg1 ? l2v1[idx] : l3v2[idx];
            return mpeg1 ? l1v1[idx] : l3v2[idx];
        }

        // ---------- FLAC ----------

        private static void ParseFlac(byte[] data, long fileSize, MediaMetadata meta)
        {
            meta.Codec = "FLAC";
            int p = 4;
            while (p + 4 <= data.Length)
            {
                int header = Be32(data, p);
                int type = (header >> 24) & 0x7F;
                int length = header & 0xFFFFFF;
                p += 4;
                if (type == 0)
                {
                    if (p + 34 > data.Length) return;
                    // STREAMINFO bit layout (big-endian, MSB first):
                    //   min block(16) max block(16) min frame(24) max frame(24)
                    //   sampleRate(20) channels-1(3) bitsPerSample-1(5)
                    //   totalSamples(36) md5(128)
                    long startBit = p * 8L;
                    long sampleRate = ReadBits(data, startBit + 80, 20);
                    long channels = ReadBits(data, startBit + 100, 3) + 1;
                    long bits = ReadBits(data, startBit + 103, 5) + 1;
                    long totalSamples = ReadBits(data, startBit + 108, 36);

                    if (sampleRate <= 0) return;
                    meta.SampleRateHz = (int)sampleRate;
                    meta.Channels = (int)channels;
                    meta.BitsPerSample = (int)bits;
                    if (totalSamples > 0)
                    {
                        meta.DurationMs = totalSamples * 1000L / sampleRate;
                        if (fileSize > 0)
                            meta.BitrateKbps = fileSize * 8L * 1000L / meta.DurationMs.Value / 1000L;
                    }
                    return;
                }
                p += length;
            }
        }

        // ---------- WAV ----------

        private static void ParseWav(byte[] data, MediaMetadata meta)
        {
            meta.Codec = "PCM";
            int p = 12;
            long? dataSize = null;
            long byteRate = 0;
            int format = 1;

            while (p + 8 <= data.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(data, p, 4);
                int size = Le32(data, p + 4);
                int body = p + 8;

                if (id == "data")
                {
                    // The declared size usually exceeds the probe window (real large
                    // files) — only the header matters for duration/bitrate.
                    if (size < 0) break;
                    dataSize = size;
                    break;
                }

                if (body + size > data.Length) break;

                if (id == "fmt ")
                {
                    if (size < 16) break;
                    format = (int)data[body] | ((int)data[body + 1] << 8);
                    short ch = (short)(data[body + 2] | (data[body + 3] << 8));
                    int rate = Le32(data, body + 4);
                    byteRate = (uint)Le32(data, body + 8);
                    short bits = (short)(data[body + 14] | (data[body + 15] << 8));
                    meta.Channels = ch;
                    meta.SampleRateHz = rate;
                    meta.BitsPerSample = bits;
                    // WAVE_FORMAT_EXTENSIBLE: subformat GUID first 2 bytes = real format
                    if (format == 0xFFFE && body + 26 <= data.Length)
                    {
                        format = data[body + 24] | (data[body + 25] << 8);
                    }
                }
                else if (id == "data")
                {
                    dataSize = size;
                    break;
                }
                p = body + size;
            }

            if (format == 3) meta.Codec = "IEEE Float PCM";
            else if (format == 0xFFFE) meta.Codec = "Extensible PCM";

            if (dataSize is long ds && byteRate > 0)
            {
                meta.DurationMs = ds * 1000L / byteRate;
                meta.BitrateKbps = byteRate * 8L / 1000L;
            }
        }

        // ---------- MP4 / M4A ----------

        private static bool LooksLikeMp4(byte[] data, out int moovOffset)
        {
            moovOffset = 0;
            // ftyp at offset 4
            if (data.Length < 8) return false;
            if (data[4] != 'f' || data[5] != 't' || data[6] != 'y' || data[7] != 'p')
                return false;
            // Confirm a moov exists somewhere in the buffer (set moovOffset).
            FindBox(data, "moov", out moovOffset);
            return true;
        }

        private static bool IsMp4Like(byte[] head)
        {
            return head.Length >= 8 && head[4] == 'f' && head[5] == 't' && head[6] == 'y' && head[7] == 'p'
                || (head.Length >= 12 && head[0] == 'm' && head[1] == 'o' && head[2] == 'o' && head[3] == 'v');
        }

        /// <summary>Finds the first occurrence of a top-level box type. Returns true + offset.</summary>
        internal static bool FindBox(byte[] data, string type, out int offset)
        {
            offset = 0;
            int p = 0;
            while (p + 8 <= data.Length)
            {
                long size = Be32(data, p);
                string id = System.Text.Encoding.ASCII.GetString(data, p + 4, 4);
                if (id == type)
                {
                    offset = p;
                    return true;
                }
                if (size == 1)
                {
                    // 64-bit largesize
                    size = (long)Be64(data, p + 8);
                }
                else if (size < 8)
                {
                    return false; // size-to-end (usually the last box); could still match above
                }
                if (size <= 0) return false;
                p += (int)Math.Min(size, int.MaxValue);
            }
            return false;
        }

        private static void ParseMp4(byte[] data, long fileSize, MediaMetadata meta)
        {
            if (!FindBox(data, "moov", out int moov)) return;
            ParseMp4At(data, moov, fileSize, meta);
        }

        /// <summary>Parses the moov box starting at <paramref name="moovOffset"/>.</summary>
        private static void ParseMp4At(byte[] data, int moovOffset, long fileSize, MediaMetadata meta)
        {
            int moovEnd = ParseBoxLength(data, moovOffset);
            if (moovEnd > data.Length) moovEnd = data.Length;

            bool hasVideo = false;
            bool hasAudio = false;
            string videoFourcc = null;
            string audioFourcc = null;

            // Movie-level duration must be known before ParseStsz, which derives
            // the frame rate from duration + sample count.
            foreach (var mvhd in Children(data, moovOffset, moovEnd, "mvhd"))
            {
                ParseMvhd(data, mvhd, meta);
                break;
            }

            foreach (var trak in Children(data, moovOffset, moovEnd, "trak"))
            {
                int trakEnd = ParseBoxLength(data, trak);
                if (trakEnd > data.Length) trakEnd = data.Length;

                ParseTkhd(data, trak, meta);

                bool video = false, audio = false;
                string vfc = null, afc = null;
                ParseStsd(data, trak, trakEnd, ref vfc, ref afc, ref video, ref audio);
                if (video) { hasVideo = true; videoFourcc = videoFourcc ?? vfc; }
                if (audio) { hasAudio = true; audioFourcc = audioFourcc ?? afc; }

                // fps comes only from the VIDEO track's sample count.
                if (video)
                {
                    ParseStsz(data, trak, trakEnd, meta);
                }
            }

            if (videoFourcc != null) meta.Codec = DescribeVideoCodec(videoFourcc);
            else if (audioFourcc != null) meta.Codec = DescribeAudioCodec(audioFourcc);
            else if (hasVideo) meta.Codec = "MPEG-4 Video";
            else if (hasAudio) meta.Codec = "MP4 Audio";
            else meta.Codec = "MP4";

            // Average bitrate over the whole file (matches player "average bitrate").
            if (meta.DurationMs is long durMs && durMs > 0 && fileSize > 0)
            {
                meta.BitrateKbps = fileSize * 8L * 1000L / durMs / 1000L;
            }
        }

        private static int ParseBoxLength(byte[] data, int offset)
        {
            if (offset + 8 > data.Length) return data.Length;
            long size = Be32(data, offset);
            if (size == 1)
            {
                size = (long)Be64(data, offset + 8);
            }
            if (size < 8) return data.Length;
            return offset + (int)Math.Min(size, int.MaxValue);
        }

        /// <summary>Returns the offsets of immediate children of a given box type.</summary>
        private static System.Collections.Generic.IEnumerable<int> Children(byte[] data, int boxOffset, int boxEnd, string type)
        {
            int p = boxOffset + 8;
            long size = Be32(data, boxOffset);
            if (size == 1) p = boxOffset + 16;
            while (p + 8 <= boxEnd)
            {
                string id = System.Text.Encoding.ASCII.GetString(data, p + 4, 4);
                int childEnd = ParseBoxLength(data, p);
                if (id == type) yield return p;
                if (childEnd <= p) break;
                p = childEnd;
            }
        }

        // mvhd layout (data starts after size/type/version+flags):
        //   v0: creation(4) modification(4) timescale(4) duration(4)
        //   v1: creation(8) modification(8) timescale(4) duration(8)
        // timescale relative to data start: 8 for v0, 16 for v1;
        // duration relative to data start: 12 for v0, 20 for v1.
        private static void ParseMvhd(byte[] data, int boxOffset, MediaMetadata meta)
        {
            if (boxOffset + 32 > data.Length) return;
            int version = data[boxOffset + 8];
            int d = boxOffset + 12; // start of data after version+flags

            if (version == 1)
            {
                if (d + 20 > data.Length) return;
                long timescale = Be32(data, d + 16);
                long duration = (long)Be64(data, d + 20);
                if (timescale > 0 && duration > 0)
                    meta.DurationMs = duration * 1000L / timescale;
            }
            else
            {
                if (d + 16 > data.Length) return;
                long timescale = Be32(data, d + 8);
                long duration = Be32(data, d + 12);
                if (timescale > 0 && duration > 0)
                    meta.DurationMs = duration * 1000L / timescale;
            }
        }

        // tkhd layout (box header at boxOffset; d = box+12 = data start after version+flags):
        //   v0: creation(4) modification(4) trackId(4) reserved(4) duration(4)
        //       reserved(8) layer(2) altGroup(2) volume(2) reserved(2) matrix(36) width(4) height(4)
        //       -> 28 bytes of fields + 8 + matrix(36) = width at d+72
        //   v1: creation(8) modification(8) trackId(4) reserved(4) duration(8)
        //       reserved(8) layer(2) altGroup(2) volume(2) reserved(2) matrix(36) width(4) height(4)
        //       -> 32 + 8 + 36 = width at d+76
        private static void ParseTkhd(byte[] data, int trakOffset, MediaMetadata meta)
        {
            foreach (var tkhd in Children(data, trakOffset, int.MaxValue, "tkhd"))
            {
                if (tkhd + 100 > data.Length) return;
                int version = data[tkhd + 8];
                int d = tkhd + 12;
                int widthOff = version == 1 ? d + 76 : d + 72;
                if (widthOff + 8 > data.Length) return;
                int w = Be32(data, widthOff) >> 16;
                int h = Be32(data, widthOff + 4) >> 16;
                // Audio tracks carry width/height 0 or 1 — ignore those.
                if (w > 1 && h > 1)
                {
                    meta.Width = w;
                    meta.Height = h;
                }
                return;
            }
        }

        private static void ParseStsd(byte[] data, int trakOffset, int trakEnd,
            ref string videoFourcc, ref string audioFourcc, ref bool hasVideo, ref bool hasAudio)
        {
            bool hv = false, ha = false;
            string vf = null, af = null;
            WalkBoxes(data, trakOffset, trakEnd, stsd =>
            {
                if (stsd + 16 > data.Length) return;
                int entryCount = Be32(data, stsd + 12);
                int p = stsd + 16;
                for (int e = 0; e < entryCount && p + 8 <= trakEnd && p + 8 <= data.Length; e++)
                {
                    string fourcc = System.Text.Encoding.ASCII.GetString(data, p + 4, 4);
                    if (IsVideoFourcc(fourcc)) { hv = true; vf = fourcc; }
                    else if (IsAudioFourcc(fourcc)) { ha = true; af = fourcc; }
                    int entrySize = Be32(data, p);
                    if (entrySize < 8) break;
                    p += entrySize;
                }
            });
            hasVideo = hv;
            hasAudio = ha;
            videoFourcc = vf;
            audioFourcc = af;
        }

        private static bool IsVideoFourcc(string f) =>
            f == "avc1" || f == "hvc1" || f == "hev1" || f == "vp09" || f == "av01" ||
            f == "mp4v" || f == "s263" || f == "h263" || f == "dvh1" || f == "hevc";

        private static bool IsAudioFourcc(string f) =>
            f == "mp4a" || f == "alac" || f == "ac-3" || f == "ec-3" || f == "opus" ||
            f == "sowt" || f == "lpcm" || f == "fLaC" || f == "samr";

        // stsz lives at trak -> mdia -> minf -> stbl -> stsz.
        private static void ParseStsz(byte[] data, int trakOffset, int trakEnd, MediaMetadata meta)
        {
            WalkBoxes(data, trakOffset, trakEnd, stsz =>
            {
                if (stsz + 20 > data.Length) return;
                uint sampleCount = (uint)Be32(data, stsz + 16);
                if (meta.DurationMs is long dur && dur > 0 && sampleCount > 1)
                {
                    double seconds = dur / 1000.0;
                    double fps = sampleCount / seconds;
                    if (fps > 0 && fps < 1200) meta.FrameRateFps = Math.Round(fps, 2);
                }
            });
        }

        /// <summary>Visits a box and every descendant.</summary>
        private static void WalkBoxes(byte[] data, int boxOffset, int boxEnd, Action<int> visit)
        {
            if (boxOffset + 8 > data.Length) return;
            if (boxOffset >= boxEnd) return;

            visit(boxOffset);

            int p = boxOffset + 8;
            long size = Be32(data, boxOffset);
            if (size == 1) p = boxOffset + 16;
            while (p + 8 <= boxEnd && p + 8 <= data.Length)
            {
                int childEnd = ParseBoxLength(data, p);
                if (childEnd <= p) break;
                if (childEnd > data.Length) break;
                WalkBoxes(data, p, childEnd, visit);
                p = childEnd;
            }
        }

        /// <summary>
        /// Scans a fragment buffer (e.g. a tail slice of a non-faststart file) for
        /// a moov box header: 4 bytes "moov" preceded by a plausible box size.
        /// </summary>
        internal static bool TryFindMoovInFragment(byte[] data, out int moovOffset)
        {
            moovOffset = -1;
            if (data.Length < 12) return false;
            for (int i = 4; i + 4 <= data.Length; i++)
            {
                if (data[i] != 'm' || data[i + 1] != 'o' || data[i + 2] != 'o' || data[i + 3] != 'v')
                    continue;
                int size = Be32(data, i - 4);
                if (size < 8) continue;
                long end = (long)i - 4 + size;
                if (end > data.Length) continue;      // extends past the fragment
                moovOffset = i - 4;
                return true;
            }
            return false;
        }

        private static string DescribeVideoCodec(string fourcc)
        {
            if (fourcc == "avc1") return "H.264/AVC";
            if (fourcc == "hvc1" || fourcc == "hev1" || fourcc == "hevc") return "H.265/HEVC";
            if (fourcc == "vp09") return "VP9";
            if (fourcc == "av01") return "AV1";
            if (fourcc == "mp4v") return "MPEG-4 Visual";
            if (fourcc == "s263" || fourcc == "h263") return "H.263";
            if (fourcc == "dvh1") return "AVC-Intra";
            return fourcc;
        }

        private static string DescribeAudioCodec(string fourcc)
        {
            if (fourcc == "mp4a") return "AAC";
            if (fourcc == "alac") return "ALAC";
            if (fourcc == "ac-3") return "AC-3 (Dolby Digital)";
            if (fourcc == "ec-3") return "E-AC-3 (Dolby Digital Plus)";
            if (fourcc == "opus") return "Opus";
            if (fourcc == "sowt" || fourcc == "lpcm") return "PCM";
            if (fourcc == "fLaC") return "FLAC";
            return fourcc;
        }

        // ---------- helpers ----------

        private static byte[] ReadFileRange(string path, long offset, int count)
        {
            var buf = new byte[count];
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (offset > fs.Length) return new byte[0];
                fs.Seek(offset, SeekOrigin.Begin);
                int read = 0;
                while (read < count)
                {
                    int n = fs.Read(buf, read, count - read);
                    if (n <= 0) break;
                    read += n;
                }
                if (read < count) Array.Resize(ref buf, read);
            }
            return buf;
        }

        private static int Be32(byte[] b, int o)
        {
            return (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
        }

        private static int Le32(byte[] b, int o)
        {
            return b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);
        }

        /// <summary>
        /// Image pixel dimensions sniffed from container headers alone (no UWP
        /// decoder needed — StorageFile.GetFileFromPathAsync fails on arbitrary
        /// Xbox drive paths, project pitfall #1). PNG (IHDR), JPEG (SOF marker),
        /// GIF (logical screen) and BMP (DIB header). Nulls on anything unrecognized.
        /// </summary>
        public static (int? Width, int? Height) ProbeImageDimensions(byte[] data)
        {
            if (data == null || data.Length < 8) return (null, null);

            try
            {
                // PNG: 8-byte signature, then IHDR chunk (length 13) — width/height BE at 16/20.
                if (data.Length >= 24
                    && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
                {
                    return (Be32(data, 16), Be32(data, 20));
                }

                // GIF: signature then logical screen width/height LE at 6/8.
                if (data.Length >= 10
                    && data[0] == 'G' && data[1] == 'I' && data[2] == 'F' && data[3] == '8')
                {
                    return (Le16(data, 6), Le16(data, 8));
                }

                // BMP: 'BM' magic, DIB header width/height LE at 18/22.
                if (data.Length >= 26 && data[0] == 'B' && data[1] == 'M')
                {
                    return (Le32(data, 18), Le32(data, 22));
                }

                // JPEG: walk markers; SOF0..15 (baseline/progressive/etc.) carry W/H.
                if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
                {
                    int i = 2;
                    while (i + 3 < data.Length)
                    {
                        if (data[i] != 0xFF) { i++; continue; }
                        byte marker = data[i + 1];
                        if (marker == 0xD8 || marker == 0xD9) { i += 2; continue; }
                        if ((marker & 0xF0) == 0xE0 || marker == 0x01)
                        {
                            // APPn / TEM: skip length-prefixed
                            int len = (data[i + 2] << 8) | data[i + 3];
                            i += 2 + len;
                            continue;
                        }
                        if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                        {
                            int h = (data[i + 5] << 8) | data[i + 6];
                            int w = (data[i + 7] << 8) | data[i + 8];
                            if (w > 0 && h > 0) return (w, h);
                            return (null, null);
                        }
                        // Other marker: skip length
                        int seg = (data[i + 2] << 8) | data[i + 3];
                        i += 2 + seg;
                    }
                }
            }
            catch
            {
                // defensive
            }
            return (null, null);
        }

        /// <summary>
        /// Heuristic PDF page count read from head+tail fragments (no Windows.Data.Pdf
        /// dependency). Takes the largest "/Count N" found in a Pages tree, falling
        /// back to counting "/Type /Page" objects (excluding "/Type /Pages"). Best
        /// effort for the Properties dialog; returns 0 when undeterminable.
        /// </summary>
        public static int ProbePdfPageCount(string path)
        {
            try
            {
                long length = new FileInfo(path).Length;
                byte[] head = ReadFileRange(path, 0, (int)Math.Min(length, (long)TailBytes));
                byte[] tail = length > TailBytes ? ReadFileRange(path, length - TailBytes, TailBytes) : null;

                int count = 0;
                // Largest /Count in any Pages tree node.
                var headText = System.Text.Encoding.ASCII.GetString(head);
                int fromCount = MaxCountValue(headText);
                if (tail != null)
                {
                    fromCount = Math.Max(fromCount, MaxCountValue(System.Text.Encoding.ASCII.GetString(tail)));
                }

                if (fromCount > 0) return fromCount;

                // Fallback: count distinct "/Type /Page" objects (both fragments).
                count += CountPageObjects(headText);
                if (tail != null) count += CountPageObjects(System.Text.Encoding.ASCII.GetString(tail));
                return count;
            }
            catch
            {
                return 0;
            }
        }

        private static int MaxCountValue(string s)
        {
            int best = 0;
            int idx = 0;
            while ((idx = s.IndexOf("/Count", idx, StringComparison.Ordinal)) >= 0)
            {
                idx += "/Count".Length;
                while (idx < s.Length && char.IsWhiteSpace(s[idx])) idx++;
                int end = idx;
                while (end < s.Length && char.IsDigit(s[end])) end++;
                if (end > idx && int.TryParse(s.Substring(idx, end - idx), out int v))
                    if (v > best) best = v;
            }
            return best;
        }

        private static int CountPageObjects(string s)
        {
            const string pattern = "/Type /Page";
            int count = 0;
            int idx = 0;
            while ((idx = s.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
            {
                // Reject object-directive "/Type /Pages".
                int after = idx + pattern.Length;
                if (after < s.Length && s[after] == 's') { idx = after; continue; }
                if (after < s.Length && !char.IsWhiteSpace(s[after]) && s[after] != '<' && s[after] != '>')
                {
                    idx = after;
                    continue;
                }
                count++;
                idx = after;
            }
            return count;
        }

        private static int Le16(byte[] b, int o)
        {
            return b[o] | (b[o + 1] << 8);
        }

        private static long Be64(byte[] b, int o)
        {
            return ((long)Be32(b, o) << 32) | (uint)Be32(b, o + 4);
        }

        private static long ReadBits(byte[] b, long bitPos, int count)
        {
            long value = 0;
            for (int i = 0; i < count; i++)
            {
                long p = bitPos + i;
                int byteIdx = (int)(p >> 3);
                if (byteIdx >= b.Length) break;
                int bitIdx = 7 - (int)(p & 7);
                value = (value << 1) | (((b[byteIdx] & 0xFF) >> bitIdx) & 1);
            }
            return value;
        }
    }
}