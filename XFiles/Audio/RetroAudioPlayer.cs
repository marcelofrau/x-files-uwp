using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
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
    /// In-flight chiptune render that the caller may start playing before the
    /// full track is decoded. The renderer writes the WAV progressively (header
    /// pre-declares the full size, body pre-allocated) so the audio graph can
    /// stream it while <see cref="BytesWritten"/> grows toward the declared size.
    /// </summary>
    public sealed class ChiptuneRenderHandle
    {
        private long _bytesWritten;

        /// <summary>Final cache path of the render (exists once the render completes).</summary>
        public string WavPath { get; }

        /// <summary>Cache key (sourceKey+track) this handle reports progress for.</summary>
        internal string CacheKey { get; }

        /// <summary>Completes when the full render lands (path) or failed (null).</summary>
        public Task<string> RenderTask => _renderTask;

        /// <summary>Data bytes written so far (approximate, updated per render chunk).</summary>
        public long BytesWritten => Volatile.Read(ref _bytesWritten);

        internal void ReportProgress(long bytes)
        {
            Volatile.Write(ref _bytesWritten, bytes);
            if (!string.IsNullOrEmpty(CacheKey)) RetroAudioPlayer._renderProgress[CacheKey] = bytes;
        }

        internal void SetTask(Task<string> task) { _renderTask = task; }

        private Task<string> _renderTask;

        internal ChiptuneRenderHandle(string wavPath, string cacheKey, Task<string> renderTask)
        {
            WavPath = wavPath;
            CacheKey = cacheKey;
            _renderTask = renderTask;
        }
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

        private static readonly ConcurrentDictionary<string, Task<string>> _inflightRenders =
            new ConcurrentDictionary<string, Task<string>>();

        private static readonly ConcurrentDictionary<string, CancellationTokenSource> _inflightCancels =
            new ConcurrentDictionary<string, CancellationTokenSource>();

        internal static readonly ConcurrentDictionary<string, long> _renderProgress =
            new ConcurrentDictionary<string, long>();

        /// <summary>
        /// Render track to a cached WAV file. Returns the WAV path or null on failure.
        /// sourceKey is used for the cache key (file path or "archive|internal").
        /// Concurrent renders of the same source+track share a single task.
        /// </summary>
        public static Task<string> RenderToWavAsync(string sourceKey, byte[] data, string extension, int track)
        {
            string cacheKey = ComputeCacheKey(sourceKey, track);
            string wavPath = GetCachedWavPath(cacheKey);
            if (File.Exists(wavPath) && IsValidCachedWav(wavPath))
                return Task.FromResult(wavPath);
            return GetOrStartRenderTask(cacheKey, sourceKey, data, extension, track);
        }

        /// <summary>
        /// Start a streaming-capable render. Returns immediately with a handle that
        /// lets the caller poll <see cref="ChiptuneRenderHandle.BytesWritten"/> and
        /// start playback as soon as enough audio exists, while the render continues
        /// filling the cache file in the background.
        /// </summary>
        public static ChiptuneRenderHandle StartChiptuneStream(string sourceKey, byte[] data, string extension, int track)
        {
            string cacheKey = ComputeCacheKey(sourceKey, track);
            string wavPath = GetCachedWavPath(cacheKey);
            if (File.Exists(wavPath) && IsValidCachedWav(wavPath))
                return new ChiptuneRenderHandle(wavPath, cacheKey, Task.FromResult(wavPath));

            var handle = new ChiptuneRenderHandle(wavPath, cacheKey, null);
            Task<string> task = GetOrStartRenderTask(cacheKey, sourceKey, data, extension, track, handle.ReportProgress);
            handle.SetTask(task);
            return handle;
        }

        /// <summary>
        /// Start a streaming render that writes to an explicit target path (not
        /// the render cache) — the background-music default install uses this so
        /// the growing .tmp lives in LocalState\BGM\ and the final WAV is renamed
        /// to bgm.wav in place. The inflight key folds in the target so a media
        /// player render of the same source never shares (and corrupts) the BGM file.
        /// </summary>
        public static ChiptuneRenderHandle StartChiptuneStreamToFile(string sourceKey, byte[] data, string extension, int track, string targetWavPath)
        {
            string cacheKey = ComputeCacheKey(sourceKey + "\u0001" + targetWavPath, track);
            if (File.Exists(targetWavPath) && IsValidCachedWav(targetWavPath))
                return new ChiptuneRenderHandle(targetWavPath, cacheKey, Task.FromResult(targetWavPath));

            var handle = new ChiptuneRenderHandle(targetWavPath, cacheKey, null);
            Task<string> task = GetOrStartRenderTask(cacheKey, sourceKey, data, extension, track, handle.ReportProgress, targetWavPath);
            handle.SetTask(task);
            return handle;
        }

        /// <summary>
        /// Request cancellation of an in-flight render for a source+track. The render
        /// loop aborts at the next chunk boundary and the native session lock (held
        /// for the whole open→free session) is released, so a navigation does not
        /// wait for the orphaned render of the track being left. No-op when the
        /// render already completed or was never started.
        /// </summary>
        public static void CancelChiptuneRender(string sourceKey, int track)
        {
            if (string.IsNullOrEmpty(sourceKey)) return;
            string cacheKey = ComputeCacheKey(sourceKey, track);
            if (_inflightCancels.TryGetValue(cacheKey, out var cts))
            {
                Log.Dbg("RetroAudioPlayer: cancelling in-flight render '{Key}' track {Track}", sourceKey, track);
                cts.Cancel();
            }
        }

        /// <summary>
        /// Wait until a streaming render has enough audio to start playback (or the
        /// render completes/fails). Cached renders return immediately.
        /// </summary>
        public static async Task<string> WaitForStreamableWavAsync(ChiptuneRenderHandle handle, double minSeconds = 8.0)
        {
            if (handle.RenderTask.IsCompleted)
                return await handle.RenderTask;

            long minBytes = (long)(minSeconds * 44100 * 2 * 2); // conservative: 44100 Hz stereo 16-bit
            var sw = Stopwatch.StartNew();
            while (!handle.RenderTask.IsCompleted)
            {
                long shared = 0;
                if (!string.IsNullOrEmpty(handle.CacheKey)) _renderProgress.TryGetValue(handle.CacheKey, out shared);
                long written = Math.Max(handle.BytesWritten, shared);
                if (written >= minBytes)
                    return handle.WavPath;
                if (sw.ElapsedMilliseconds > 60000)
                {
                    Log.Warn("RetroAudioPlayer.WaitForStreamableWavAsync: timeout waiting for render of '{Path}'", handle.WavPath);
                    break;
                }
                await Task.Delay(100);
            }
            return await handle.RenderTask;
        }

        private static Task<string> GetOrStartRenderTask(string cacheKey, string sourceKey, byte[] data, string extension, int track, Action<long> onProgress = null, string targetWavPath = null)
        {
            if (_inflightRenders.TryGetValue(cacheKey, out var existing) &&
                (!_inflightCancels.TryGetValue(cacheKey, out var existingCts) || !existingCts.IsCancellationRequested))
            {
                return existing;
            }

            _inflightRenders.TryRemove(cacheKey, out _);
            _inflightCancels.TryRemove(cacheKey, out _);
            _renderProgress.TryRemove(cacheKey, out _);

            var cts = new CancellationTokenSource();
            _inflightCancels[cacheKey] = cts;
            Task<string> task = _inflightRenders.GetOrAdd(cacheKey, _ =>
                Task.Run(() => RenderToWavSync(sourceKey, data, extension, track, onProgress, cts.Token, targetWavPath)));
            _ = task.ContinueWith(completedTask =>
            {
                _inflightRenders.TryRemove(cacheKey, out _);
                _inflightCancels.TryRemove(cacheKey, out _);
                _renderProgress.TryRemove(cacheKey, out _);
            }, TaskContinuationOptions.ExecuteSynchronously);
            return task;
        }

        private static string RenderToWavSync(string sourceKey, byte[] data, string extension, int track, Action<long> onProgress = null, CancellationToken ct = default, string targetWavPath = null)
        {
            try
            {
                if (ct.IsCancellationRequested) return null;

                if (data == null)
                {
                    if (IsArchiveEntryPath(sourceKey)) return null;
                    data = ReadFileBytes(sourceKey);
                }

                string cacheKey = ComputeCacheKey(sourceKey, track);
                string wavPath = targetWavPath ?? GetCachedWavPath(cacheKey);
                if (File.Exists(wavPath))
                {
                    if (IsValidCachedWav(wavPath))
                    {
                        Log.Dbg("RetroAudioPlayer: reuse cached render {Path}", wavPath);
                        return wavPath;
                    }
                    Log.Warn("RetroAudioPlayer: cached render {Path} is corrupt ({Size} bytes) — re-rendering", wavPath, new FileInfo(wavPath).Length);
                    try { File.Delete(wavPath); }
                    catch (Exception delEx) { Log.Dbg("RetroAudioPlayer: failed to delete corrupt cache: {Error}", delEx.Message); }
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

                    string tmpPath = wavPath + ".tmp";
                    if (!WriteWav(handle, tmpPath, track, onProgress, ct))
                    {
                        if (ct.IsCancellationRequested)
                        {
                            // Leave the partial .tmp in place: the departing graph may
                            // still be reading it, and it is not a valid cache anyway
                            // (cleaned up or overwritten by the next render).
                            Log.Info("RetroAudioPlayer: render of '{Key}' cancelled — partial cache kept", sourceKey);
                            return null;
                        }
                        // Render failed mid-track: discard the partial file so it can't
                        // poison the cache (a pre-allocated, header-complete partial would
                        // otherwise play as silence and look like a hang).
                        Log.Warn("RetroAudioPlayer: render produced no audio for '{Key}' — discarding", sourceKey);
                        try { File.Delete(tmpPath); } catch (Exception delEx) { Log.Dbg("RetroAudioPlayer: failed to delete empty WAV: {Error}", delEx.Message); }
                        return null;
                    }

                    int scanRate = RA_GetSampleRate(handle);
                    if (scanRate <= 0) scanRate = 44100;
                    ScanWavSilenceRuns(tmpPath, scanRate);

                    RA_EndTrack(handle);

                    try { if (File.Exists(wavPath)) File.Delete(wavPath); } catch (Exception oldEx) { Log.Dbg("RetroAudioPlayer: failed to remove stale cache {Path}: {Error}", wavPath, oldEx.Message); }
                    File.Move(tmpPath, wavPath);

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

        /// <summary>
        /// Render the track into tmpPath as a WAV whose header pre-declares the full
        /// size and whose body is pre-allocated, so streaming readers see a
        /// complete-length file while the renderer fills it progressively (the
        /// renderer runs far faster than realtime, so playback never catches up).
        /// Truncates the pre-allocated tail and patches the header to the actual
        /// written size before returning. Returns false when the render fails or
        /// produces no audio (caller discards the partial file).
        /// </summary>
        private static bool WriteWav(IntPtr handle, string tmpPath, int track, Action<long> onProgress = null, CancellationToken ct = default)
        {
            int sampleRate = RA_GetSampleRate(handle);
            int channels = RA_GetChannels(handle);
            if (sampleRate <= 0) sampleRate = 44100;
            if (channels <= 0) channels = 2;

            int bytesPerFrame = channels * 2;
            long maxFrames = MaxSeconds * (long)sampleRate;

            double durSec = RA_GetDurationSec(handle, track);
            long declaredBytes = 0;
            if (durSec > 0 && durSec < MaxSeconds)
                declaredBytes = (long)(durSec * sampleRate) * bytesPerFrame;
            if (declaredBytes <= 0) declaredBytes = maxFrames * bytesPerFrame;

            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
            using (var bw = new BinaryWriter(fs))
            {
                // Header pre-patched to the declared size so the parser sees the full
                // duration and stream Length is full while the body is still being filled.
                WriteWavHeader(bw, declaredBytes, sampleRate, channels, bytesPerFrame);
                fs.SetLength(44 + declaredBytes);

                int chunkBytes = ChunkFrames * bytesPerFrame;
                IntPtr pcm = Marshal.AllocHGlobal(chunkBytes);
                try
                {
                    byte[] buffer = new byte[chunkBytes];
                    long dataBytes = 0;

                    while (dataBytes < declaredBytes)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            Log.Dbg("RetroAudioPlayer: render cancelled — stopping write of {Tmp}", tmpPath);
                            return false;
                        }

                        int written;
                        int ended;
                        int rrc = RA_RenderFrames(handle, pcm, ChunkFrames, out written, out ended);
                        if (rrc != 0 || written <= 0)
                        {
                            Log.Warn("RetroAudioPlayer: RA_RenderFrames rc={Rc} written={Written} — render aborted", rrc, written);
                            return false;
                        }

                        int byteCount = written * bytesPerFrame;
                        Marshal.Copy(pcm, buffer, 0, byteCount);
                        bw.Write(buffer, 0, byteCount);
                        dataBytes += byteCount;
                        onProgress?.Invoke(dataBytes);

                        if (ended != 0) break;

                        // Make written data visible to the streaming reader promptly.
                        // The audio graph opens this file while it is still growing, so
                        // every chunk must reach the OS — a 4096-byte FileStream buffer
                        // tail would let the graph read pre-allocated zeros instead of
                        // the just-rendered audio (audible as a gap/click on Xbox).
                        fs.Flush();
                    }

                    if (dataBytes <= 0) return false;

                    // Truncate the pre-allocated tail and patch the header to the actual size.
                    fs.SetLength(44 + dataBytes);
                    bw.Seek(4, SeekOrigin.Begin);
                    bw.Write((uint)(dataBytes + 36));
                    bw.Seek(40, SeekOrigin.Begin);
                    bw.Write((uint)dataBytes);
                    bw.Flush();
                    return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(pcm);
                }
            }
        }

        private static void WriteWavHeader(BinaryWriter bw, long dataBytes, int sampleRate, int channels, int bytesPerFrame)
        {
            bw.Write(Encoding.ASCII.GetBytes("RIFF"));
            bw.Write((uint)(dataBytes + 36));
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
            bw.Write((uint)dataBytes);
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

        /// <summary>
        /// A cached WAV is only reusable if it is a real, non-empty render. Corrupt
        /// entries (empty data chunk, truncated write, stale file from an older
        /// build) otherwise get played as silence and look like a hang.
        /// </summary>
        private static bool IsValidCachedWav(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fs.Length < 44) return false;
                    var hdr = new byte[44];
                    int read = 0;
                    while (read < 44)
                    {
                        int n = fs.Read(hdr, read, 44 - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    if (read < 44) return false;
                    if (!(hdr[0] == 'R' && hdr[1] == 'I' && hdr[2] == 'F' && hdr[3] == 'F')) return false;
                    if (!(hdr[8] == 'W' && hdr[9] == 'A' && hdr[10] == 'V' && hdr[11] == 'E')) return false;
                    if (!(hdr[36] == 'd' && hdr[37] == 'a' && hdr[38] == 't' && hdr[39] == 'a')) return false;
                    uint dataBytes = (uint)(hdr[40] | (hdr[41] << 8) | (hdr[42] << 16) | (hdr[43] << 24));
                    if (dataBytes <= 0) return false;
                    // The renderer pre-declares the full size in the header before the
                    // body is written; a valid cache entry must contain the declared
                    // data (a truncated write or a crashed streaming render leaves a
                    // header-complete but body-short file that would play as silence).
                    return fs.Length >= dataBytes + 44L;
                }
            }
            catch (Exception ex)
            {
                Log.Dbg("RetroAudioPlayer: cache validation failed for {Path}: {Error}", path, ex.Message);
                return false;
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

        /// <summary>
        /// Resolve the path that currently exists for a chiptune render. A streaming
        /// render writes to "{final}.tmp" until it completes, then renames it to the
        /// final path — the audio loader must open whichever exists at open time.
        /// </summary>
        public static string ResolveChiptuneWavPath(string finalPath)
        {
            if (string.IsNullOrEmpty(finalPath)) return finalPath;
            if (File.Exists(finalPath)) return finalPath;
            string tmp = finalPath + ".tmp";
            if (File.Exists(tmp)) return tmp;
            return finalPath;
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

                // Stale .tmp files only exist mid-render; a leftover after a crash is
                // garbage regardless of age and would confuse streaming loads.
                try
                {
                    foreach (string file in Directory.GetFiles(dir, "*.tmp"))
                    {
                        try { File.Delete(file); }
                        catch (Exception ex) { Log.Dbg("RetroAudioPlayer.CleanupOldCache: failed to delete stale tmp {Path}: {Error}", file, ex.Message); }
                    }
                }
                catch (Exception ex) { Log.Warn("RetroAudioPlayer.CleanupOldCache: tmp cleanup failed: {Error}", ex.Message); }

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
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sourceKey + "|" + track + "|render5"));
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
