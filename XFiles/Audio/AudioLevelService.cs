using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Windows.Storage.Streams;

namespace XFiles.Audio
{
    public sealed class AudioLevelService : IDisposable
    {
        public static AudioLevelService Instance { get; } = new AudioLevelService();
        public const int BandCount = 26;
        public const int FftSize = 2048;
        public int QuantumSkipN { get; set; } = 1; // process every Nth quantum callback
        private const long NoGcRegionSize = 128 * 1024 * 1024L;

        private AudioGraph _graph;
        private AudioFileInputNode _fileInputNode;
        private MediaSourceAudioInputNode _mediaSourceNode;
        private AudioDeviceOutputNode _deviceOutputNode;
        private AudioFrameOutputNode _frameOutputNode;
        private int _channels;
        private int _sampleRate;

#if AUDIO_ANALYSIS
        private readonly float[] _fftReal = new float[FftSize];
        private readonly float[] _fftImag = new float[FftSize];
        private readonly float[] _windowedBuffer = new float[FftSize];
        private readonly float[] _magnitudes = new float[FftSize / 2];
        private readonly float[] _bandDb = new float[BandCount];
        private readonly float[] _bandLevels = new float[BandCount];
        private readonly float[] _bandPeaks = new float[BandCount];
        private readonly float[] _bandPeakHoldTimers = new float[BandCount];
        private readonly int[] _bandBinStart = new int[BandCount];
        private readonly int[] _bandBinEnd = new int[BandCount];

        // Waveform: time-domain samples for visualizers
        private readonly float[] _waveformBuffer = new float[FftSize];
        private int _waveformCount;

        // Beat detector
        private float _beat;
        private float _beatDecay = 0.92f;
        private float _energyHistory;
        private float _energyInstant;
        private const float BeatThreshold = 1.5f;
        private const float BeatEnergySmoothing = 0.05f;

        // Background FFT worker — keeps heavy work off the AudioGraph quantum thread
        private float[] _frameCopyBuffer = new float[FftSize];
        private int _frameCopyCount;
        private volatile bool _frameReady;
        private Thread _fftWorker;
        private ManualResetEventSlim _fftSignal = new ManualResetEventSlim(false);
#else
        private readonly float[] _bandLevels = new float[BandCount];
        private readonly float[] _bandPeaks = new float[BandCount];
#endif

        private bool _isAnalyzing;
        private int _isProcessing;
        private bool _gcRegionActive;
        private int _quantumSkipCount;

        private CancellationTokenSource _swapCts;
        private float _decayFactor = 0.85f;
        private float _peakHoldDuration = 1.5f;
        private float _peakDecayFactor = 0.92f;
        private int _quantumLogCounter;
        private bool _firstBandDataLogged;
        private bool _quantumTidLogged;
        private string _currentFilePath;

        private CancellationTokenSource _driftCts;
        private long _driftStartTicks;
        private TimeSpan _driftStartPos;
        private int _driftWarnCount;

        private int _silenceRunQuantums;
        private int _silenceRunStartQuantum;
        private long _silenceGapTotal;

#if AUDIO_ANALYSIS
        public float[] BandLevels => _bandLevels;
        public float[] BandPeaks => _bandPeaks;
        public float[] Magnitudes => _magnitudes;
        public float[] Waveform => _waveformBuffer;
        public int WaveformCount => _waveformCount;
        public float Beat => _beat;
        public bool IsAnalyzing => _isAnalyzing;
#else
        public float[] BandLevels => _bandLevels;
        public float[] BandPeaks => _bandPeaks;
        public float[] Magnitudes => System.Array.Empty<float>();
        public float[] Waveform => System.Array.Empty<float>();
        public int WaveformCount => 0;
        public float Beat => 0f;
        public bool IsAnalyzing => false;
#endif

        private bool _isGraphRunning;
        private bool _remoteStreamNode;
        public bool IsPlaying => _isGraphRunning;
        public string CurrentFilePath => _currentFilePath;
        public bool IsFileLoaded => _fileInputNode != null || _mediaSourceNode != null;
        public bool IsGraphLive => _graph != null;

        public TimeSpan Position
        {
            get
            {
                if (_mediaSourceNode != null)
                    return _mediaSourceNode.Position;
                return _fileInputNode?.Position ?? TimeSpan.Zero;
            }
        }

        public TimeSpan Duration
        {
            get
            {
                if (_mediaSourceNode != null)
                    return _mediaSourceNode.Duration;
                return _fileInputNode?.Duration ?? TimeSpan.Zero;
            }
        }

        public event EventHandler MediaOpened;
        public event EventHandler MediaEnded;
        public event EventHandler MediaFailed;

        private AudioLevelService()
        {
#if AUDIO_ANALYSIS
            InitBandMappings(48000);
#endif
        }

#if AUDIO_ANALYSIS
        private void InitBandMappings(int sampleRate)
        {
            double minFreq = 40.0;
            double maxFreq = 20000.0;
            double binWidth = (double)sampleRate / FftSize;

            for (int i = 0; i < BandCount; i++)
            {
                double t = (double)i / (BandCount - 1);
                double lowFreq = minFreq * Math.Pow(maxFreq / minFreq, t);
                double highFreq;
                if (i < BandCount - 1)
                {
                    double nextT = (double)(i + 1) / (BandCount - 1);
                    highFreq = minFreq * Math.Pow(maxFreq / minFreq, nextT);
                }
                else
                {
                    highFreq = maxFreq;
                }

                _bandBinStart[i] = Math.Max(1, (int)(lowFreq / binWidth));
                _bandBinEnd[i] = Math.Min(FftSize / 2 - 1, (int)(highFreq / binWidth));

                if (_bandBinEnd[i] < _bandBinStart[i])
                    _bandBinEnd[i] = _bandBinStart[i];
            }
        }
#endif

        public async Task LoadAndPlay(string filePath, bool forceStream = false)
        {
            await LoadInternal(filePath, createDeviceOutput: true, forceStream: forceStream);
        }

        /// <summary>
        /// Plays a remote (network) stream directly through the graph — no local
        /// file is involved. The caller supplies a blocking IRandomAccessStream
        /// (e.g. RemoteStream over an SMB read) whose reads pull data on demand,
        /// so playback starts as soon as the first bytes arrive.
        /// </summary>
        public async Task PlayRemoteStreamAsync(Windows.Storage.Streams.IRandomAccessStream stream, string mimeType, bool autoPlay = true)
        {
            await _loadLock.WaitAsync();
            try
            {
                await LoadRemoteStreamCore(stream, mimeType, autoPlay);
            }
            finally
            {
                _loadLock.Release();
            }
        }

        private async Task LoadRemoteStreamCore(Windows.Storage.Streams.IRandomAccessStream stream, string mimeType, bool autoPlay)
        {
            if (_isGraphRunning)
                Stop();
            _currentFilePath = "(network stream)";
            Log.Info("AudioLevelService: playing remote stream mime={Mime}", mimeType);

            try
            {
                var mediaSource = MediaSource.CreateFromStream(stream, mimeType);
                await CreateGraphCommon(true);

                var nodeResult = await _graph.CreateMediaSourceAudioInputNodeAsync(mediaSource);
                if (nodeResult.Status != MediaSourceAudioInputNodeCreationStatus.Success)
                {
                    Log.Warn("AudioLevelService: remote MediaSourceAudioInputNode failed: {Status}", nodeResult.Status);
                    try { stream.Dispose(); } catch { }
                    MediaFailed?.Invoke(this, EventArgs.Empty);
                    Stop();
                    return;
                }

                _mediaSourceNode = nodeResult.Node;
                _mediaSourceNode.AddOutgoingConnection(_deviceOutputNode);
                _mediaSourceNode.AddOutgoingConnection(_frameOutputNode);

                _quantumLogCounter = 0;
                _remoteStreamNode = true;

                if (autoPlay)
                {
                    Log.Info("AudioLevelService: remote stream loaded dur={Dur:F1}s — starting playback",
                        _mediaSourceNode.Duration.TotalSeconds);

                    _mediaSourceNode.Start();
                    _isAnalyzing = true;
                    Log.Info("AudioLevelService: IsAnalyzing=true (remote stream)");

                    _graph.Start();
                    _isGraphRunning = true;
                    StartDriftMonitor();
                    MediaOpened?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    // Load-only (inline preview): the graph and source node are
                    // prepared but not started — playback begins on TogglePlayPause.
                    _isGraphRunning = false;
                    Log.Info("AudioLevelService: remote stream prepared (load-only) dur={Dur:F1}s",
                        _mediaSourceNode.Duration.TotalSeconds);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("AudioLevelService: PlayRemoteStream failed", ex);
                try { stream.Dispose(); } catch { }
                MediaFailed?.Invoke(this, EventArgs.Empty);
                Stop();
            }
        }

        public async Task StartAnalysis(string filePath)
        {
            await LoadInternal(filePath, createDeviceOutput: false);
        }

        private readonly SemaphoreSlim _loadLock = new SemaphoreSlim(1, 1);
        private string _pendingLoadPath;
        private bool _pendingCreateDeviceOutput;
        private bool _pendingForceStream;

        private async Task LoadInternal(string filePath, bool createDeviceOutput, bool forceStream = false)
        {
            if (!await _loadLock.WaitAsync(0))
            {
                Log.Info("AudioLevelService: load already in progress, queuing {Path}", filePath);
                _pendingLoadPath = filePath;
                _pendingCreateDeviceOutput = createDeviceOutput;
                _pendingForceStream = forceStream;
                return;
            }

            try
            {
                await LoadInternalCore(filePath, createDeviceOutput, forceStream);
            }
            finally
            {
                _loadLock.Release();

                if (_pendingLoadPath != null)
                {
                    var pending = _pendingLoadPath;
                    var pendingDevice = _pendingCreateDeviceOutput;
                    var pendingStream = _pendingForceStream;
                    _pendingLoadPath = null;
                    _ = LoadInternal(pending, pendingDevice, pendingStream);
                }
            }
        }

        private async Task LoadInternalCore(string filePath, bool createDeviceOutput, bool forceStream = false)
        {
            if (_isGraphRunning)
                Stop();
            _currentFilePath = filePath;
            Log.Info("AudioLevelService: loading {Path}", filePath);

            if (forceStream)
            {
                Log.Info("AudioLevelService: forceStream requested — using stream via MediaSourceAudioInputNode");
                await LoadViaStream(filePath, createDeviceOutput, mimeType: "audio/wav");
                return;
            }

            StorageFile storageFile = null;

            try
            {
                storageFile = await StorageFile.GetFileFromPathAsync(filePath);
                Log.Info("AudioLevelService: StorageFile acquired via GetFileFromPathAsync");
            }
            catch (Exception ex)
            {
                Log.Warn("AudioLevelService: GetFileFromPathAsync failed", ex);
            }

            if (storageFile == null)
            {
                try
                {
                    var dir = Path.GetDirectoryName(filePath);
                    var fileName = Path.GetFileName(filePath);
                    var folder = await StorageFolder.GetFolderFromPathAsync(dir);
                    storageFile = await folder.GetFileAsync(fileName);
                    Log.Info("AudioLevelService: StorageFile acquired via folder+GetFileAsync");
                }
                catch (Exception ex)
                {
                    Log.Warn("AudioLevelService: folder+GetFileAsync failed", ex);
                }
            }

            if (storageFile != null)
            {
                await LoadViaStorageFile(storageFile, createDeviceOutput);
            }
            else
            {
                Log.Info("AudioLevelService: no StorageFile — falling back to stream via MediaSourceAudioInputNode");
                await LoadViaStream(filePath, createDeviceOutput);
            }
        }

        private async Task CreateGraphCommon(bool createDeviceOutput)
        {
            var settings = new AudioGraphSettings(Windows.Media.Render.AudioRenderCategory.GameMedia);
            settings.DesiredSamplesPerQuantum = 4800;
            var graphResult = await AudioGraph.CreateAsync(settings);
            if (graphResult.Status != AudioGraphCreationStatus.Success)
            {
                throw new Exception($"AudioGraph creation failed: {graphResult.Status}");
            }

            var localGraph = graphResult.Graph;
            _channels = (int)localGraph.EncodingProperties.ChannelCount;
            _sampleRate = (int)localGraph.EncodingProperties.SampleRate;
#if AUDIO_ANALYSIS
            InitBandMappings(_sampleRate);
#endif

            Log.Info("AudioLevelService: graph enc={Enc} rate={Rate} ch={Ch} quantum={QuantumMs:F1}ms desiredSamples={Desired}",
                localGraph.EncodingProperties.Subtype, _sampleRate, _channels,
                4800.0 / _sampleRate * 1000.0, 4800);

            var deviceResult = await localGraph.CreateDeviceOutputNodeAsync();
            if (deviceResult.Status != AudioDeviceNodeCreationStatus.Success)
            {
                localGraph.Dispose();
                throw new Exception($"Device output node failed: {deviceResult.Status}");
            }
            _deviceOutputNode = deviceResult.DeviceOutputNode;

            if (createDeviceOutput)
            {
                Log.Info("AudioLevelService: playback mode (device output connected)");
            }
            else
            {
                Log.Info("AudioLevelService: analysis mode (device output for clock only)");
            }

            _frameOutputNode = localGraph.CreateFrameOutputNode();
            localGraph.QuantumStarted += OnQuantumStarted;
            _graph = localGraph;
#if AUDIO_ANALYSIS
            StartFftWorker();
#endif
        }

        private async Task LoadViaStorageFile(StorageFile storageFile, bool createDeviceOutput)
        {
            try
            {
                await CreateGraphCommon(createDeviceOutput);

                var fileResult = await _graph.CreateFileInputNodeAsync(storageFile);
                if (fileResult.Status != AudioFileNodeCreationStatus.Success)
                {
                    Log.Warn("AudioLevelService: file node failed: {Status}", fileResult.Status);
                    MediaFailed?.Invoke(this, EventArgs.Empty);
                    Stop();
                    return;
                }

                _fileInputNode = fileResult.FileInputNode;
                _fileInputNode.FileCompleted += OnFileCompleted;

                if (createDeviceOutput)
                    _fileInputNode.AddOutgoingConnection(_deviceOutputNode);
                _fileInputNode.AddOutgoingConnection(_frameOutputNode);

                _quantumLogCounter = 0;

                Log.Info("AudioLevelService: file loaded dur={Dur:F1}s — starting playback",
                    _fileInputNode.Duration.TotalSeconds);

                _fileInputNode.Start();

                _isAnalyzing = true;
                Log.Info("AudioLevelService: IsAnalyzing=true (LoadViaStorageFile)");
                long allocBefore = GC.GetAllocatedBytesForCurrentThread();
                try { if (GC.TryStartNoGCRegion(NoGcRegionSize)) _gcRegionActive = true; } catch { }
                if (_gcRegionActive)
                {
                    long netAlloc = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
                    int tid = Environment.CurrentManagedThreadId;
                    Log.Verb("AudioLevelService[TID={Tid}]: NoGCRegion started, size={Size}MB setupAlloc={Setup}KB totalMem={Total}KB",
                        tid, NoGcRegionSize / (1024 * 1024), netAlloc / 1024, GC.GetTotalMemory(false) / 1024);
                }
                _graph.Start();
                _isGraphRunning = true;
                StartDriftMonitor();
                MediaOpened?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Log.Warn("AudioLevelService: LoadViaStorageFile failed", ex);
                MediaFailed?.Invoke(this, EventArgs.Empty);
                Stop();
            }
        }

        private async Task LoadViaStream(string filePath, bool createDeviceOutput, string mimeType = "audio/mpeg")
        {
            try
            {
                // A streaming chiptune render may still be writing "{path}.tmp" — open
                // whichever exists (the renderer renames .tmp -> final on completion).
                filePath = XFiles.Audio.RetroAudioPlayer.ResolveChiptuneWavPath(filePath);
                var fileStream = new FileStream(filePath,
                    FileMode.Open, FileAccess.Read,
                    FileShare.Read | FileShare.Write | FileShare.Delete,
                    bufferSize: 1048576, useAsync: true);

                var stream = fileStream.AsRandomAccessStream();
                var mediaSource = MediaSource.CreateFromStream(stream, mimeType);

                Log.Info("AudioLevelService: stream path mime={Mime} size={Size}MB",
                    mimeType, new FileInfo(filePath).Length / (1024.0 * 1024.0));

                await CreateGraphCommon(createDeviceOutput);

                int wavRate = LogWavHeader(filePath);
                if (wavRate > 0 && wavRate != _sampleRate)
                {
                    Log.Warn("AudioLevelService: WAV rate {WavRate} != graph device rate {GraphRate} — in-graph resample (glitch risk on Xbox)",
                        wavRate, _sampleRate);
                }

                var nodeResult = await _graph.CreateMediaSourceAudioInputNodeAsync(mediaSource);
                if (nodeResult.Status != MediaSourceAudioInputNodeCreationStatus.Success)
                {
                    Log.Warn("AudioLevelService: MediaSourceAudioInputNode failed: {Status}", nodeResult.Status);
                    stream.Dispose();
                    fileStream.Dispose();
                    MediaFailed?.Invoke(this, EventArgs.Empty);
                    Stop();
                    return;
                }

                _mediaSourceNode = nodeResult.Node;

                if (createDeviceOutput)
                    _mediaSourceNode.AddOutgoingConnection(_deviceOutputNode);
                _mediaSourceNode.AddOutgoingConnection(_frameOutputNode);

                _quantumLogCounter = 0;

                Log.Info("AudioLevelService: stream loaded dur={Dur:F1}s — starting playback",
                    _mediaSourceNode.Duration.TotalSeconds);

                _mediaSourceNode.Start();

                _isAnalyzing = true;
                Log.Info("AudioLevelService: IsAnalyzing=true (LoadViaStream)");
                long allocBefore = GC.GetAllocatedBytesForCurrentThread();
                try { if (GC.TryStartNoGCRegion(NoGcRegionSize)) _gcRegionActive = true; } catch { }
                if (_gcRegionActive)
                {
                    long netAlloc = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
                    int tid = Environment.CurrentManagedThreadId;
                    Log.Verb("AudioLevelService[TID={Tid}]: NoGCRegion started, size={Size}MB setupAlloc={Setup}KB totalMem={Total}KB",
                        tid, NoGcRegionSize / (1024 * 1024), netAlloc / 1024, GC.GetTotalMemory(false) / 1024);
                }
                _graph.Start();
                _isGraphRunning = true;
                StartDriftMonitor();
                MediaOpened?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Log.Warn("AudioLevelService: LoadViaStream failed", ex);
                MediaFailed?.Invoke(this, EventArgs.Empty);
                Stop();
            }
        }

        private void OnFileCompleted(AudioFileInputNode sender, object args)
        {
            Log.Info("AudioLevelService: {File} — FileCompleted fired, invoking MediaEnded", _currentFilePath ?? "(null)");
            MediaEnded?.Invoke(this, EventArgs.Empty);
        }

        public void Pause()
        {
            if (_graph == null) return;
            try
            {
                if (_remoteStreamNode && _mediaSourceNode != null)
                {
                    try { _mediaSourceNode.Stop(); } catch { }
                }
                _graph.Stop();
                EndGcRegion();
                _isGraphRunning = false;
                _driftCts?.Cancel();
                Log.Info("AudioLevelService: paused");
            }
            catch (Exception ex)
            {
                Log.Warn("AudioLevelService: pause failed", ex);
            }
        }

        public void Resume()
        {
            if (_graph == null) return;
            try
            {
                long allocBefore = GC.GetAllocatedBytesForCurrentThread();
                try { if (GC.TryStartNoGCRegion(NoGcRegionSize)) _gcRegionActive = true; } catch { }
                if (_gcRegionActive)
                {
                    long netAlloc = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
                    int tid = Environment.CurrentManagedThreadId;
                    Log.Verb("AudioLevelService[TID={Tid}]: NoGCRegion restarted (Resume) size={Size}MB setupAlloc={Setup}KB totalMem={Total}KB",
                        tid, NoGcRegionSize / (1024 * 1024), netAlloc / 1024, GC.GetTotalMemory(false) / 1024);
                }
                if (_remoteStreamNode && _mediaSourceNode != null)
                {
                    try { _mediaSourceNode.Start(); } catch { }
                }
                _graph.Start();
                _isGraphRunning = true;
                StartDriftMonitor();
                _isAnalyzing = true;
                MediaOpened?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Log.Warn("AudioLevelService: resume failed", ex);
            }
        }

        public void TogglePlayPause()
        {
            if (_graph == null) return;
            if (_isGraphRunning)
                Pause();
            else
                Resume();
        }

        public void Seek(TimeSpan position)
        {
            try
            {
                if (_mediaSourceNode != null)
                {
                    _mediaSourceNode.Seek(position);
                }
                else if (_fileInputNode != null)
                {
                    _fileInputNode.Seek(position);
                }
                StartDriftMonitor();
            }
            catch (Exception ex)
            {
                Log.Warn("AudioLevelService: Seek failed", ex);
            }
        }

        public void Stop()
        {
            _isAnalyzing = false;
            _isGraphRunning = false;
            _remoteStreamNode = false;
            Interlocked.Exchange(ref _isProcessing, 0);

            if (_mediaSourceNode != null)
            {
                try { _mediaSourceNode.Dispose(); } catch { }
                _mediaSourceNode = null;
            }

            if (_graph != null)
            {
                try { _graph.Stop(); } catch { }
                EndGcRegion();
                try { _graph.QuantumStarted -= OnQuantumStarted; } catch { }

                if (_fileInputNode != null)
                {
                    try { _fileInputNode.FileCompleted -= OnFileCompleted; } catch { }
                }

                _fileInputNode = null;
                _deviceOutputNode = null;
                _frameOutputNode = null;
                _graph = null;
            }

            _swapCts?.Dispose();
            _swapCts = null;

            _driftCts?.Cancel();
            _driftCts?.Dispose();
            _driftCts = null;
            _driftWarnCount = 0;

            _currentFilePath = null;
            _firstBandDataLogged = false;

#if AUDIO_ANALYSIS
            _frameReady = false;
            _frameCopyCount = 0;

            for (int i = 0; i < BandCount; i++)
            {
                _bandLevels[i] = 0f;
                _bandPeaks[i] = 0f;
                _bandPeakHoldTimers[i] = 0f;
                _bandDb[i] = 0f;
            }

            _beat = 0f;
            _energyHistory = 0f;
            _waveformCount = 0;
            _silenceRunQuantums = 0;
            _silenceGapTotal = 0;
#endif

            Log.Info("AudioLevelService: stopped");
        }

        public async Task SwapSourceAsync(string filePath, bool forceStream = false)
        {
            if (string.IsNullOrEmpty(filePath))
                return;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            _swapCts?.Cancel();
            _swapCts?.Dispose();
            _swapCts = new CancellationTokenSource();

            if (_graph == null)
            {
                await LoadAndPlay(filePath, forceStream);
                return;
            }

            _currentFilePath = filePath;
            Log.Info("AudioLevelService: swapping source to {Path}", filePath);

            if (_isGraphRunning)
            {
                try { _graph.Stop(); } catch { }
                EndGcRegion();
                Interlocked.Exchange(ref _isProcessing, 0);
                _isGraphRunning = false;
            }

            if (_fileInputNode != null)
            {
                try
                {
                    _fileInputNode.FileCompleted -= OnFileCompleted;
                    _fileInputNode.RemoveOutgoingConnection(_deviceOutputNode);
                    _fileInputNode.RemoveOutgoingConnection(_frameOutputNode);
                    _fileInputNode.Stop();
                    _fileInputNode.Dispose();
                }
                catch { }
                _fileInputNode = null;
            }
            if (_mediaSourceNode != null)
            {
                try
                {
                    _mediaSourceNode.RemoveOutgoingConnection(_deviceOutputNode);
                    _mediaSourceNode.RemoveOutgoingConnection(_frameOutputNode);
                    _mediaSourceNode.Stop();
                    _mediaSourceNode.Dispose();
                }
                catch { }
                _mediaSourceNode = null;
            }

            try
            {
                // Primary path: raw FileStream → MediaSource. Skips the slow
                // StorageFile.GetFileFromPathAsync + CreateFileInputNodeAsync
                // round-trip (UWP path resolution is expensive for arbitrary
                // drive paths on Xbox). The stream path is already proven for
                // growing chiptune WAVs — MP3s read just as well from a seekable
                // stream, which is what makes next/prev swaps feel instant.
                // Chiptune WAVs (possibly at game AI rate, e.g. USF 22047 Hz) also
                // must go through the stream path — the file node can fail to open
                // them inside an already-running graph.
                filePath = XFiles.Audio.RetroAudioPlayer.ResolveChiptuneWavPath(filePath);
                var fileStream = new FileStream(filePath,
                    FileMode.Open, FileAccess.Read,
                    FileShare.Read | FileShare.Write | FileShare.Delete,
                    1048576, true);
                var mediaSource = MediaSource.CreateFromStream(
                    fileStream.AsRandomAccessStream(), forceStream ? "audio/wav" : "audio/mpeg");
                var nodeResult = await _graph.CreateMediaSourceAudioInputNodeAsync(mediaSource);
                if (nodeResult.Status == MediaSourceAudioInputNodeCreationStatus.Success)
                {
                    _mediaSourceNode = nodeResult.Node;
                    _mediaSourceNode.AddOutgoingConnection(_deviceOutputNode);
                    _mediaSourceNode.AddOutgoingConnection(_frameOutputNode);
                    _mediaSourceNode.Start();
                }
                else
                {
                    Log.Warn("AudioLevelService: SwapSource stream node failed ({Status}) — falling back to file node", nodeResult.Status);
                    fileStream.Dispose();

                    // Fallback: StorageFile + file input node (rare).
                    StorageFile storageFile = null;
                    try { storageFile = await StorageFile.GetFileFromPathAsync(filePath); }
                    catch { }

                    if (storageFile == null)
                    {
                        try
                        {
                            string dir = Path.GetDirectoryName(filePath);
                            string name = Path.GetFileName(filePath);
                            var folder = await StorageFolder.GetFolderFromPathAsync(dir);
                            storageFile = await folder.GetFileAsync(name);
                        }
                        catch { }
                    }

                    if (storageFile == null)
                    {
                        Log.Warn("AudioLevelService: SwapSource file fallback — could not resolve {Path}", filePath);
                        MediaFailed?.Invoke(this, EventArgs.Empty);
                        Stop();
                        return;
                    }

                    var fileResult = await _graph.CreateFileInputNodeAsync(storageFile);
                    if (fileResult.Status != AudioFileNodeCreationStatus.Success)
                    {
                        Log.Warn("AudioLevelService: SwapSource file node failed: {Status}", fileResult.Status);
                        MediaFailed?.Invoke(this, EventArgs.Empty);
                        Stop();
                        return;
                    }
                    _fileInputNode = fileResult.FileInputNode;
                    _fileInputNode.FileCompleted += OnFileCompleted;
                    _fileInputNode.AddOutgoingConnection(_deviceOutputNode);
                    _fileInputNode.AddOutgoingConnection(_frameOutputNode);
                    _fileInputNode.Start();
                }
            }
            catch (Exception ex)
            {
                Log.Warn("AudioLevelService: SwapSource failed", ex);
                MediaFailed?.Invoke(this, EventArgs.Empty);
                Stop();
                return;
            }

            try
            {
                long allocBefore = GC.GetAllocatedBytesForCurrentThread();
                try { if (GC.TryStartNoGCRegion(NoGcRegionSize)) _gcRegionActive = true; } catch { }
                if (_gcRegionActive)
                {
                    long netAlloc = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
                    int tid = Environment.CurrentManagedThreadId;
                    Log.Verb("AudioLevelService[TID={Tid}]: NoGCRegion restarted (SwapSource) size={Size}MB setupAlloc={Setup}KB totalMem={Total}KB",
                        tid, NoGcRegionSize / (1024 * 1024), netAlloc / 1024, GC.GetTotalMemory(false) / 1024);
                }
                _graph.Start();
                _isGraphRunning = true;
                StartDriftMonitor();
                MediaOpened?.Invoke(this, EventArgs.Empty);
                Log.Info("AudioLevelService: SwapSource done in {Elapsed}ms", sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                Log.Warn("AudioLevelService: SwapSource post-startup failed", ex);
                Stop();
            }
        }

        private unsafe void OnQuantumStarted(AudioGraph sender, object args)
        {
            if (!_isAnalyzing) return;
            if (Interlocked.CompareExchange(ref _isProcessing, 1, 0) != 0) return;
            if (++_quantumSkipCount % QuantumSkipN != 0) { Interlocked.Exchange(ref _isProcessing, 0); return; }

            var frameOutput = _frameOutputNode;
            if (frameOutput == null) { Interlocked.Exchange(ref _isProcessing, 0); return; }

            if (!_quantumTidLogged)
            {
                _quantumTidLogged = true;
                Log.Info("AudioLevelService: OnQuantumStarted first call TID={Tid}", Environment.CurrentManagedThreadId);
            }

            AudioFrame frame = null;
            try { frame = frameOutput.GetFrame(); }
            catch { Interlocked.Exchange(ref _isProcessing, 0); return; }

            if (frame == null) { Interlocked.Exchange(ref _isProcessing, 0); return; }

            try
            {
#if AUDIO_ANALYSIS
                CopyFrameToBuffer(frame);

                _quantumLogCounter++;

                float bandsSum = 0f;
                for (int i = 0; i < BandCount; i++) bandsSum += _bandLevels[i];

                if (!_firstBandDataLogged && bandsSum > 0.001f)
                {
                    _firstBandDataLogged = true;
                    Log.Info("AudioLevelService: FIRST non-zero band data quantum#{Cnt} rate={Rate} ch={Ch} sum={Sum:F4} lvl0={L0:F4} lvl5={L5:F4}",
                        _quantumLogCounter, _sampleRate, _channels, bandsSum, _bandLevels[0], _bandLevels[5]);
                }

                // Live silence-gap detector: consecutive near-silent quantums mid-playback
                // point to underrun (source starved) or gaps baked into the source.
                const float SilenceSum = 0.0005f;
                if (_quantumLogCounter > 20)
                {
                    if (bandsSum < SilenceSum)
                    {
                        if (_silenceRunQuantums == 0)
                            _silenceRunStartQuantum = _quantumLogCounter;
                        _silenceRunQuantums++;
                        if (_silenceRunQuantums == 3)
                        {
                            _silenceGapTotal++;
                            int qMs = (int)(4800.0 / _sampleRate * 1000.0);
                            Log.Warn("AudioLevelService: AUDIO SILENCE GAP quantum#{Start}-#{End} dur={Dur}ms sum={Sum:F5} gapsTotal={Total}",
                                _silenceRunStartQuantum, _quantumLogCounter,
                                _silenceRunQuantums * qMs, bandsSum, _silenceGapTotal);
                        }
                    }
                    else
                    {
                        _silenceRunQuantums = 0;
                    }
                }

#if AUDIO_LEVEL_DEBUG
                if (_quantumLogCounter <= 5 || (_quantumLogCounter % 1000 == 0 && _quantumLogCounter <= 10000))
                {
                    Log.Info("AudioLevelService: quantum#{Cnt} rate={Rate} ch={Ch} bandsSum={Sum:F4} lvl0={L0:F4} lvl5={L5:F4}",
                        _quantumLogCounter, _sampleRate, _channels, bandsSum, _bandLevels[0], _bandLevels[5]);
                }
#endif
#endif
            }
            catch (Exception ex)
            {
                Log.Warn("AudioLevelService: OnQuantumStarted error", ex);
            }
            finally
            {
                try { frame.Dispose(); } catch { }
                Interlocked.Exchange(ref _isProcessing, 0);
            }
        }

#if AUDIO_ANALYSIS
        private unsafe void CopyFrameToBuffer(AudioFrame frame)
        {
            using (var buffer = frame.LockBuffer(Windows.Media.AudioBufferAccessMode.Read))
            using (var reference = buffer.CreateReference())
            {
                var byteAccess = reference as IMemoryBufferByteAccess;
                if (byteAccess == null) return;

                byte* dataByte;
                uint capacity;
                byteAccess.GetBuffer(out dataByte, out capacity);

                int floatCount = (int)(capacity / sizeof(float));
                int totalSamples = floatCount / _channels;
                int fftSamples = Math.Min(FftSize, totalSamples);

                // Bounds check: ensure (fftSamples-1)*_channels + ch stays within floatCount
                int maxSafeSamples = (floatCount - _channels) / _channels;
                if (maxSafeSamples < 0) maxSafeSamples = 0;
                fftSamples = Math.Min(fftSamples, maxSafeSamples);
                if (fftSamples == 0) return;

#if AUDIO_LEVEL_DEBUG
                if (_quantumLogCounter <= 3)
                {
                    Log.Dbg("AudioLevelService.CopyFrameToBuffer: q#{Cnt} cap={Cap} flt={Flt} ch={Ch} total={Total} fft={Fft} maxSafe={Safe}",
                        _quantumLogCounter, capacity, floatCount, _channels, totalSamples, fftSamples, maxSafeSamples);
                }
#endif

                int channelsToAvg = Math.Min(2, _channels);
                for (int i = 0; i < fftSamples; i++)
                {
                    float sum = 0f;
                    for (int ch = 0; ch < channelsToAvg; ch++)
                        sum += ((float*)dataByte)[i * _channels + ch];
                    _frameCopyBuffer[i] = sum / channelsToAvg;
                }
                _frameCopyCount = fftSamples;
                _frameReady = true;
                _fftSignal.Set();
            }
        }

        private void ProcessFrameFromBuffer()
        {
            try
            {
            int fftSamples = _frameCopyCount;

            if (fftSamples <= 0 || fftSamples > FftSize)
            {
                Log.Warn("AudioLevelService: ProcessFrameFromBuffer invalid count {Count}", fftSamples);
                return;
            }

            Array.Copy(_frameCopyBuffer, _windowedBuffer, fftSamples);
            for (int i = fftSamples; i < FftSize; i++)
                _windowedBuffer[i] = 0f;

            _waveformCount = fftSamples;
            Array.Copy(_windowedBuffer, _waveformBuffer, fftSamples);
            for (int i = fftSamples; i < FftSize; i++)
                _waveformBuffer[i] = 0f;

            FftHelper.ApplyHammingWindow(_windowedBuffer, FftSize);

            for (int i = 0; i < FftSize; i++)
            {
                _fftReal[i] = _windowedBuffer[i];
                _fftImag[i] = 0f;
            }

            FftHelper.Compute(_fftReal, _fftImag, false);

            int binCount = FftSize / 2;
            float normFactor = FftSize / 2f;
            for (int i = 0; i < binCount; i++)
                _magnitudes[i] = (float)Math.Sqrt(_fftReal[i] * _fftReal[i] + _fftImag[i] * _fftImag[i]) / normFactor;

            for (int b = 0; b < BandCount; b++)
            {
                float maxMag = 0f;
                for (int k = _bandBinStart[b]; k <= _bandBinEnd[b] && k < binCount; k++)
                {
                    if (_magnitudes[k] > maxMag) maxMag = _magnitudes[k];
                }

                if (maxMag < 0.00001f) maxMag = 0.00001f;

                float db = 20f * (float)Math.Log10(maxMag);

                float trebleBoost = (b / (float)(BandCount - 1)) * 32f;
                db += trebleBoost;

                db = Math.Max(-60f, Math.Min(0f, db));
                float normalized = (db + 60f) / 60f;
                _bandDb[b] = Math.Min(1f, normalized * 2.0f);
            }

            float dt = (float)FftSize / _sampleRate;
            for (int b = 0; b < BandCount; b++)
            {
                float target = _bandDb[b];

                if (target > _bandLevels[b])
                    _bandLevels[b] = target;
                else
                    _bandLevels[b] *= _decayFactor;

                if (_bandLevels[b] > _bandPeaks[b])
                {
                    _bandPeaks[b] = _bandLevels[b];
                    _bandPeakHoldTimers[b] = _peakHoldDuration;
                }
                else
                {
                    _bandPeakHoldTimers[b] -= dt;
                    if (_bandPeakHoldTimers[b] <= 0f)
                    {
                        _bandPeaks[b] *= _peakDecayFactor;
                        if (_bandPeaks[b] < 0.01f) _bandPeaks[b] = 0f;
                    }
                }

                _bandLevels[b] = Math.Max(0f, Math.Min(1f, _bandLevels[b]));
                _bandPeaks[b] = Math.Max(0f, Math.Min(1f, _bandPeaks[b]));
            }

            float energy = 0f;
            for (int b = 0; b < BandCount; b++)
                energy += _bandLevels[b];
            energy /= BandCount;

            _energyHistory = _energyHistory * (1f - BeatEnergySmoothing) + energy * BeatEnergySmoothing;
            if (energy > _energyHistory * BeatThreshold)
                _beat = 1f;
            else
                _beat *= _beatDecay;
            if (_beat < 0.01f) _beat = 0f;
            }
            catch (Exception ex)
            {
                Log.Err("AudioLevelService: ProcessFrameFromBuffer error", ex);
            }
        }
#endif

        public void SetVolume(double volume)
        {
            var gain = Math.Max(0.0, Math.Min(1.0, volume));
            try
            {
                if (_deviceOutputNode != null)
                    _deviceOutputNode.OutgoingGain = gain;
            }
            catch (Exception ex)
            {
                Log.Warn("AudioLevelService: SetVolume failed", ex);
            }
        }

        private TimeSpan CurrentNodePosition()
        {
            if (_mediaSourceNode != null) return _mediaSourceNode.Position;
            if (_fileInputNode != null) return _fileInputNode.Position;
            return TimeSpan.Zero;
        }

        private TimeSpan CurrentNodeDuration()
        {
            if (_mediaSourceNode != null) return _mediaSourceNode.Duration;
            if (_fileInputNode != null) return _fileInputNode.Duration;
            return TimeSpan.Zero;
        }

        private void StartDriftMonitor()
        {
            _driftCts?.Cancel();
            _driftCts?.Dispose();
            _driftCts = new CancellationTokenSource();
            _driftStartTicks = Stopwatch.GetTimestamp();
            _driftStartPos = CurrentNodePosition();
            _ = DriftMonitorLoopAsync(_driftCts.Token);
        }

        private async Task DriftMonitorLoopAsync(CancellationToken ct)
        {
            int beat = 0;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(5000, ct).ConfigureAwait(false);
                    if (!_isGraphRunning) continue;
                    if (_mediaSourceNode == null && _fileInputNode == null) continue;

                    TimeSpan pos = CurrentNodePosition();
                    TimeSpan dur = CurrentNodeDuration();
                    TimeSpan expected = _driftStartPos +
                        TimeSpan.FromSeconds((double)(Stopwatch.GetTimestamp() - _driftStartTicks) / Stopwatch.Frequency);
                    double driftMs = (pos - expected).TotalMilliseconds;
                    beat++;

                    if (Math.Abs(driftMs) > 750.0)
                    {
                        _driftWarnCount++;
                        Log.Warn("AudioLevelService: PLAYBACK DRIFT pos={Pos:F1}s expected={Exp:F1}s dur={Dur:F1}s drift={Drift:+#0;-0;0}ms warn#{W} — underrun/stutter suspect",
                            pos.TotalSeconds, expected.TotalSeconds, dur.TotalSeconds, driftMs, _driftWarnCount);
                        _driftStartTicks = Stopwatch.GetTimestamp();
                        _driftStartPos = pos;
                    }
                    else if (beat % 3 == 0)
                    {
                        Log.Info("AudioLevelService: playback heartbeat pos={Pos:F1}s / {Dur:F1}s drift={Drift:+#0;-0;0}ms q={Q} gaps={Gaps}",
                            pos.TotalSeconds, dur.TotalSeconds, driftMs, _quantumLogCounter, _silenceGapTotal);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Warn("AudioLevelService: drift monitor error", ex);
            }
        }

        private static int LogWavHeader(string filePath)
        {
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    byte[] head = new byte[64];
                    int n = fs.Read(head, 0, head.Length);
                    if (n < 12 || head[0] != 'R' || head[1] != 'I' || head[2] != 'F' || head[3] != 'F')
                    {
                        Log.Info("AudioLevelService: {Path} not RIFF (n={N}) — skipping WAV header log", filePath, n);
                        return 0;
                    }

                    int rate = 0, ch = 0, bits = 0;
                    long dataBytes = -1;
                    int off = 12;
                    while (off + 8 <= n)
                    {
                        var id = System.Text.Encoding.ASCII.GetString(head, off, 4);
                        int size = head[off + 4] | (head[off + 5] << 8) | (head[off + 6] << 16) | (head[off + 7] << 24);
                        if (id == "fmt ")
                        {
                            if (off + 24 <= n)
                            {
                                ch = head[off + 10] | (head[off + 11] << 8);
                                rate = head[off + 12] | (head[off + 13] << 8) | (head[off + 14] << 16) | (head[off + 15] << 24);
                                bits = head[off + 22] | (head[off + 23] << 8);
                            }
                        }
                        else if (id == "data")
                        {
                            dataBytes = size;
                        }
                        off += 8 + size + (size & 1);
                    }

                    if (rate > 0)
                    {
                        double durSec = dataBytes > 0 ? dataBytes / (double)(rate * Math.Max(ch, 1) * Math.Max(bits / 8, 1)) : -1;
                        Log.Info("AudioLevelService: WAV header {Path}: rate={Rate} ch={Ch} bits={Bits} dataBytes={Data} dur={Dur:F1}s",
                            filePath, rate, ch, bits, dataBytes, durSec);
                    }
                    return rate;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("AudioLevelService: LogWavHeader failed for {Path}", ex);
                return 0;
            }
        }

#if AUDIO_ANALYSIS
        private void StartFftWorker()
        {
            if (_fftWorker != null) return;
            _fftWorker = new Thread(FftWorkerLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "AudioLevelService.FFT"
            };
            _fftWorker.Start();
        }

        private void StopFftWorker()
        {
            if (_fftWorker == null) return;
            _fftSignal.Set();
            _fftWorker.Join(500);
            _fftWorker = null;
            _fftSignal.Reset();
        }

        private void FftWorkerLoop()
        {
            Log.Dbg("AudioLevelService: FFT worker started");
            while (true)
            {
                if (_fftSignal.Wait(100))
                {
                    _fftSignal.Reset();
                }
                if (_isAnalyzing && _frameReady)
                {
                    _frameReady = false;
                    ProcessFrameFromBuffer();
                }
            }
        }
#endif

        private void EndGcRegion()
        {
            if (_gcRegionActive)
            {
                _gcRegionActive = false;
                long memBefore = GC.GetTotalMemory(false);
                try { GC.EndNoGCRegion(); } catch { }
                long totalFreed = memBefore - GC.GetTotalMemory(true);
                int tid = Environment.CurrentManagedThreadId;
                Log.Verb("AudioLevelService[TID={Tid}]: NoGCRegion ended, totalMem={Total}KB estimatedFreed={Freed}KB",
                    tid, GC.GetTotalMemory(false) / 1024, Math.Max(0, totalFreed) / 1024);
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
