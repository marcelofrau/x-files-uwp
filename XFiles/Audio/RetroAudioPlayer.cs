using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using XFiles.FileSystem;

namespace XFiles.Audio
{
    /// <summary>
    /// Chiptune track information produced by RetroAudioPlayer.Probe.
    /// </summary>
    public sealed class ChiptuneTrackInfo
    {
        public int TrackCount { get; set; }
        public int SampleRate { get; set; }
        public int Channels { get; set; }
        public string[] Titles { get; set; }
        public double[] DurationsSec { get; set; }
    }

    /// <summary>
    /// P/Invoke bridge to RetroAudio.dll (game-music-emu + libopenmpt).
    /// Decodes chiptunes (NSF, MOD, SPC, VGM, ...) to a cached WAV file in the
    /// app's local folder so the existing AudioLevelService playback path can
    /// consume them. See XFiles/Native/build-native.ps1 and retroaudio.h.
    /// </summary>
    public static class RetroAudioPlayer
    {
        private const string DllName = "RetroAudio.dll";
        private const int MaxSeconds = 600;
        private const int ChunkFrames = 8192;

        // Formats handled by game-music-emu (console chiptunes).
        private static readonly string[] GmeExtensions =
        {
            ".spc", ".gbs", ".nsf", ".nsfe", ".vgm", ".vgz", ".gym",
            ".sid", ".hes", ".kss", ".ay", ".sap"
        };

        // Formats handled by libopenmpt (tracker music).
        private static readonly string[] OpenmptExtensions =
        {
            ".mod", ".xm", ".s3m", ".it", ".mtm", ".stm", ".669", ".med",
            ".far", ".mdl", ".ult", ".ptm", ".dbm", ".dsm", ".amf", ".okt",
            ".dmf", ".ams", ".mt2", ".pol", ".ppm", ".cba", ".psm", ".j2b",
            ".mpm", ".umx", ".mo3"
        };

        // Formats handled by aosdk engine_psf (PlayStation) and lazyusf (N64).
        private static readonly string[] PsfUsfExtensions =
        {
            ".psf", ".minipsf", ".usf", ".miniusf"
        };

        private static readonly HashSet<string> _extensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static string _cacheDir;

        static RetroAudioPlayer()
        {
            foreach (string ext in GmeExtensions) _extensions.Add(ext);
            foreach (string ext in OpenmptExtensions) _extensions.Add(ext);
            foreach (string ext in PsfUsfExtensions) _extensions.Add(ext);
        }

        public static IEnumerable<string> ChiptuneExtensions => _extensions;

        public static bool IsChiptuneExt(string extension)
        {
            return !string.IsNullOrEmpty(extension) && _extensions.Contains(extension);
        }

        public static bool IsChiptuneFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return IsChiptuneExt(Path.GetExtension(path));
        }

        /// <summary>
        /// True for the archive-entry address format "archivePath|internalPath"
        /// produced by ArchiveBrowser for entries inside an archive.
        /// </summary>
        public static bool IsArchiveEntryPath(string path)
        {
            return !string.IsNullOrEmpty(path) && path.IndexOf('|') >= 0;
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr RA_GetVersion();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int RA_IsSupportedExt([MarshalAs(UnmanagedType.LPStr)] string ext);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int RA_Open(byte[] data, IntPtr size, [MarshalAs(UnmanagedType.LPStr)] string ext, [MarshalAs(UnmanagedType.LPStr)] string baseDir, out IntPtr handle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void RA_Free(IntPtr handle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int RA_GetSampleRate(IntPtr handle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int RA_GetChannels(IntPtr handle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int RA_GetTrackCount(IntPtr handle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern double RA_GetDurationSec(IntPtr handle, int track);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int RA_GetTrackTitle(IntPtr handle, int track, StringBuilder title, IntPtr outSize);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int RA_BeginTrack(IntPtr handle, int track);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int RA_RenderFrames(IntPtr handle, IntPtr pcm, int capacityFrames, out int framesWritten, out int trackEnded);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void RA_EndTrack(IntPtr handle);

        /// <summary>
        /// Probe a chiptune source for track info. data may be null for a plain
        /// file source (then bytes are read from sourceKey). Returns null on error.
        /// </summary>
        public static ChiptuneTrackInfo Probe(string sourceKey, byte[] data, string extension)
        {
            try
            {
                if (data == null)
                {
                    if (IsArchiveEntryPath(sourceKey)) return null;
                    data = ReadFileBytes(sourceKey);
                }

                IntPtr handle;
                int rc = RA_Open(data, new IntPtr(data.Length), NormalizeExt(extension), GetBaseDir(sourceKey), out handle);
                if (rc != 0)
                {
                    Log.Warn("RetroAudioPlayer.Probe: RA_Open failed rc={Rc} ext={Ext}", rc, extension);
                    return null;
                }

                try
                {
                    int count = RA_GetTrackCount(handle);
                    if (count < 1) count = 1;

                    var info = new ChiptuneTrackInfo
                    {
                        TrackCount = count,
                        SampleRate = RA_GetSampleRate(handle),
                        Channels = RA_GetChannels(handle),
                        Titles = new string[count],
                        DurationsSec = new double[count]
                    };

                    for (int i = 0; i < count; i++)
                    {
                        info.DurationsSec[i] = RA_GetDurationSec(handle, i);
                        info.Titles[i] = GetTrackTitle(handle, i);
                    }

                    return info;
                }
                finally
                {
                    RA_Free(handle);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("RetroAudioPlayer.Probe failed for '{Key}': {Error}", sourceKey, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Render track to a cached WAV file. Returns the WAV path or null on failure.
        /// sourceKey is used for the cache key (file path or "archive|internal").
        /// </summary>
        public static Task<string> RenderToWavAsync(string sourceKey, byte[] data, string extension, int track)
        {
            return Task.Run(() => RenderToWavSync(sourceKey, data, extension, track));
        }

        private static string RenderToWavSync(string sourceKey, byte[] data, string extension, int track)
        {
            try
            {
                if (data == null)
                {
                    if (IsArchiveEntryPath(sourceKey)) return null;
                    data = ReadFileBytes(sourceKey);
                }

                string cacheKey = ComputeCacheKey(sourceKey, track);
                string wavPath = GetCachedWavPath(cacheKey);
                if (File.Exists(wavPath))
                {
                    Log.Dbg("RetroAudioPlayer: reuse cached render {Path}", wavPath);
                    return wavPath;
                }

                IntPtr handle;
                int rc = RA_Open(data, new IntPtr(data.Length), NormalizeExt(extension), GetBaseDir(sourceKey), out handle);
                if (rc != 0)
                {
                    Log.Warn("RetroAudioPlayer: RA_Open failed rc={Rc} ext={Ext}", rc, extension);
                    return null;
                }

                try
                {
                    int trackCount = RA_GetTrackCount(handle);
                    if (track < 0 || track >= trackCount)
                    {
                        Log.Warn("RetroAudioPlayer: track {Track} out of range (count={Count})", track, trackCount);
                        return null;
                    }

                    rc = RA_BeginTrack(handle, track);
                    if (rc != 0)
                    {
                        Log.Warn("RetroAudioPlayer: RA_BeginTrack failed rc={Rc}", rc);
                        return null;
                    }

                    Directory.CreateDirectory(_cacheDir ?? (_cacheDir = CacheDir()));
                CleanupOldCache();
                    WriteWav(handle, wavPath, track);
                    if (new FileInfo(wavPath).Length <= 44)
                    {
                        // Header-only WAV: render produced no audio (decode failed or
                        // track dead). Don't cache a broken file — the next play would
                        // load a 0-second track and look like a hang.
                        Log.Warn("RetroAudioPlayer: render produced no audio for '{Key}' — discarding", sourceKey);
                        try { File.Delete(wavPath); } catch (Exception delEx) { Log.Dbg("RetroAudioPlayer: failed to delete empty WAV: {Error}", delEx.Message); }
                        return null;
                    }
                    int scanRate = RA_GetSampleRate(handle);
                    if (scanRate <= 0) scanRate = 44100;
                    ScanWavSilenceRuns(wavPath, scanRate);

                    RA_EndTrack(handle);
                    Log.Info("RetroAudioPlayer: rendered track {Track} of '{Key}' -> {Wav}", track, sourceKey, wavPath);
                    return wavPath;
                }
                finally
                {
                    RA_Free(handle);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("RetroAudioPlayer render failed for '{Key}': {Error}", sourceKey, ex.Message);
                return null;
            }
        }

        private static void WriteWav(IntPtr handle, string wavPath, int track)
        {
            int sampleRate = RA_GetSampleRate(handle);
            int channels = RA_GetChannels(handle);
            if (sampleRate <= 0) sampleRate = 44100;
            if (channels <= 0) channels = 2;

            int bytesPerFrame = channels * 2;
            long maxFrames = MaxSeconds * (long)sampleRate;

            using (var fs = new FileStream(wavPath, FileMode.Create, FileAccess.ReadWrite))
            using (var bw = new BinaryWriter(fs))
            {
                // Header placeholders — patched after render.
                bw.Write(Encoding.ASCII.GetBytes("RIFF"));
                bw.Write(0u);
                bw.Write(Encoding.ASCII.GetBytes("WAVE"));
                bw.Write(Encoding.ASCII.GetBytes("fmt "));
                bw.Write(16u);
                bw.Write((ushort)1);
                bw.Write((ushort)channels);
                bw.Write(sampleRate);
                bw.Write(sampleRate * bytesPerFrame);
                bw.Write((ushort)bytesPerFrame);
                bw.Write((ushort)16);
                bw.Write(Encoding.ASCII.GetBytes("data"));
                bw.Write(0u);

                int chunkBytes = ChunkFrames * bytesPerFrame;
                IntPtr pcm = Marshal.AllocHGlobal(chunkBytes);
                try
                {
                    byte[] buffer = new byte[chunkBytes];
                    long totalBytes = 0;
                    long dataBytes = 0;

                    while (totalBytes < maxFrames * bytesPerFrame)
                    {
                        int written;
                        int ended;
                        int rrc = RA_RenderFrames(handle, pcm, ChunkFrames, out written, out ended);
                        if (rrc != 0 || written <= 0) break;

                        int byteCount = written * bytesPerFrame;
                        Marshal.Copy(pcm, buffer, 0, byteCount);
                        bw.Write(buffer, 0, byteCount);
                        dataBytes += byteCount;
                        totalBytes += byteCount;

                        if (ended != 0) break;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(pcm);
                }

                long fileLength = fs.Length;
                bw.Seek(4, SeekOrigin.Begin);
                bw.Write((uint)(fileLength - 8));
                bw.Seek(40, SeekOrigin.Begin);
                bw.Write((uint)(fileLength - 44));
                bw.Flush();
            }
        }

        private static string GetTrackTitle(IntPtr handle, int track)
        {
            try
            {
                var sb = new StringBuilder(512);
                RA_GetTrackTitle(handle, track, sb, new IntPtr(sb.Capacity));
                return sb.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static void ScanWavSilenceRuns(string wavPath, int sampleRate)
        {
            try
            {
                const int threshold = 40;             // |16-bit sample| below = near-silence
                const int maxScanSec = 30;            // scan first 30s only (diagnostic, cheap)
                const int channels = 2;               // our writer always emits 16-bit stereo PCM
                const long minGapMs = 60;
                int bytesPerFrame = channels * 2;
                long maxScanFrames = maxScanSec * (long)sampleRate;

                using (var fs = new FileStream(wavPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    fs.Seek(44, SeekOrigin.Begin);
                    byte[] buf = new byte[65536];
                    long runFrames = 0;
                    int gaps = 0;
                    long longestFrames = 0;
                    long longestAt = -1;
                    long frame = 0;

                    while (frame < maxScanFrames)
                    {
                        int read = fs.Read(buf, 0, buf.Length);
                        if (read <= 0) break;
                        int frames = read / bytesPerFrame;
                        for (int i = 0; i < frames && frame < maxScanFrames; i++)
                        {
                            bool silent = true;
                            for (int c = 0; c < channels; c++)
                            {
                                int off = (i * channels + c) * 2;
                                short s = (short)(buf[off] | (buf[off + 1] << 8));
                                if (Math.Abs(s) > threshold) { silent = false; break; }
                            }

                            if (silent)
                            {
                                runFrames++;
                            }
                            else if (runFrames > 0)
                            {
                                if (runFrames * 1000L / sampleRate >= minGapMs)
                                {
                                    gaps++;
                                    if (runFrames > longestFrames)
                                    {
                                        longestFrames = runFrames;
                                        longestAt = (frame - runFrames) * 1000L / sampleRate;
                                    }
                                }
                                runFrames = 0;
                            }
                            frame++;
                        }
                    }

                    if (runFrames > 0 && runFrames * 1000L / sampleRate >= minGapMs)
                    {
                        gaps++;
                        if (runFrames > longestFrames)
                        {
                            longestFrames = runFrames;
                            longestAt = (frame - runFrames) * 1000L / sampleRate;
                        }
                    }

                    Log.Info("RetroAudioPlayer: WAV silence scan {Path}: gaps(>=60ms)={Gaps} longest={Longest}ms at={At}ms scanned={ScanSec}s",
                        wavPath, gaps, longestFrames * 1000L / sampleRate,
                        longestAt < 0 ? -1 : longestAt, maxScanSec);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("RetroAudioPlayer: WAV silence scan failed for {Path}", ex);
            }
        }

        private static byte[] ReadFileBytes(string filePath)
        {
            using (var stream = Win32FileStream.OpenRead(filePath))
            {
                var bytes = new byte[stream.Length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read <= 0) break;
                    offset += read;
                }
                return bytes;
            }
        }

        private static string NormalizeExt(string extension)
        {
            string ext = extension ?? "";
            ext = ext.TrimStart('.').ToLowerInvariant();
            return ext;
        }

        /// <summary>
        /// Directory that contains the source file, used by the PSF/USF backends to
        /// resolve sibling library files (.psflib/.usflib). Archive-entry sources
        /// have no real directory, so pass an empty string — library lookups fail
        /// gracefully and the main file still renders.
        /// </summary>
        private static string GetBaseDir(string sourceKey)
        {
            if (string.IsNullOrEmpty(sourceKey) || IsArchiveEntryPath(sourceKey))
                return "";
            string dir = Path.GetDirectoryName(sourceKey);
            return dir ?? "";
        }

        private static string CacheDir()
        {
            string baseDir = ApplicationData.Current.LocalFolder.Path;
            return Path.Combine(baseDir, "ChiptuneCache");
        }

        private static string GetCachedWavPath(string cacheKey)
        {
            return Path.Combine(_cacheDir ?? (_cacheDir = CacheDir()), cacheKey + ".wav");
        }

        private const int CacheRetentionHours = 24;
        private static DateTime _lastCacheCleanupUtc = DateTime.MinValue;

        /// <summary>
        /// Delete cached WAV renders older than CacheRetentionHours to keep the
        /// ChiptuneCache folder bounded. Runs at most once per day.
        /// </summary>
        public static void CleanupOldCache()
        {
            try
            {
                DateTime now = DateTime.UtcNow;
                if ((now - _lastCacheCleanupUtc).TotalHours < 24) return;
                _lastCacheCleanupUtc = now;

                string dir = CacheDir();
                if (!Directory.Exists(dir)) return;

                var cutoff = now - TimeSpan.FromHours(CacheRetentionHours);
                foreach (string file in Directory.GetFiles(dir, "*.wav"))
                {
                    try
                    {
                        DateTime lastWrite = File.GetLastWriteTimeUtc(file);
                        if (lastWrite < cutoff)
                        {
                            File.Delete(file);
                            Log.Dbg("RetroAudioPlayer.CleanupOldCache: deleted {Path} (age {AgeHours:F1}h)", file,
                                (now - lastWrite).TotalHours);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("RetroAudioPlayer.CleanupOldCache: failed for {Path}: {Error}", file, ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("RetroAudioPlayer.CleanupOldCache failed: {Error}", ex.Message);
            }
        }

        private static string ComputeCacheKey(string sourceKey, int track)
        {
            using (var sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sourceKey + "|" + track + "|render3"));
                var sb = new StringBuilder(16);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString().Substring(0, 16);
            }
        }

        /// <summary>
        /// Log the native decoder version string (once).
        /// </summary>
        public static void LogVersion()
        {
            try
            {
                IntPtr ptr = RA_GetVersion();
                if (ptr != IntPtr.Zero)
                    Log.Info("RetroAudioPlayer: {Version}", Marshal.PtrToStringAnsi(ptr));
            }
            catch (Exception ex)
            {
                Log.Warn("RetroAudioPlayer.LogVersion failed: {Error}", ex.Message);
            }
        }
    }
}
