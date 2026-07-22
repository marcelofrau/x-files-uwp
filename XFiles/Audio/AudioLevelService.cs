using System;
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
        public const int BandCount = 26;
        public const int FftSize = 2048;

        private AudioGraph _graph;
        private AudioFileInputNode _fileInputNode;
        private MediaSourceAudioInputNode _mediaSourceNode;
        private AudioDeviceOutputNode _deviceOutputNode;
        private AudioFrameOutputNode _frameOutputNode;
        private int _channels;
        private int _sampleRate;

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

        private bool _isAnalyzing;
        private int _isProcessing;
        private float _decayFactor = 0.85f;
        private float _peakHoldDuration = 1.5f;
        private float _peakDecayFactor = 0.92f;
        private int _quantumLogCounter;
        private string _currentFilePath;

        public float[] BandLevels => _bandLevels;
        public float[] BandPeaks => _bandPeaks;
        public float[] Magnitudes => _magnitudes;
        public float[] Waveform => _waveformBuffer;
        public int WaveformCount => _waveformCount;
        public float Beat => _beat;
        public bool IsAnalyzing => _isAnalyzing;

        private bool _isGraphRunning;
        public bool IsPlaying => _isGraphRunning;
        public bool IsFileLoaded => _fileInputNode != null || _mediaSourceNode != null;

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

        public AudioLevelService()
        {
            InitBandMappings(48000);
        }

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

        public async Task LoadAndPlay(string filePath)
        {
            await LoadInternal(filePath, createDeviceOutput: true);
        }

        public async Task StartAnalysis(string filePath)
        {
            await LoadInternal(filePath, createDeviceOutput: false);
        }

        private readonly SemaphoreSlim _loadLock = new SemaphoreSlim(1, 1);
        private string _pendingLoadPath;
        private bool _pendingCreateDeviceOutput;

        private async Task LoadInternal(string filePath, bool createDeviceOutput)
        {
            if (!await _loadLock.WaitAsync(0))
            {
                Log.Information("AudioLevelService: load already in progress, queuing {Path}", filePath);
                _pendingLoadPath = filePath;
                _pendingCreateDeviceOutput = createDeviceOutput;
                return;
            }

            try
            {
                await LoadInternalCore(filePath, createDeviceOutput);
            }
            finally
            {
                _loadLock.Release();

                if (_pendingLoadPath != null)
                {
                    var pending = _pendingLoadPath;
                    var pendingDevice = _pendingCreateDeviceOutput;
                    _pendingLoadPath = null;
                    _ = LoadInternal(pending, pendingDevice);
                }
            }
        }

        private async Task LoadInternalCore(string filePath, bool createDeviceOutput)
        {
            Stop();
            _currentFilePath = filePath;
            Log.Information("AudioLevelService: loading {Path}", filePath);

            StorageFile storageFile = null;

            try
            {
                var dir = Path.GetDirectoryName(filePath);
                var fileName = Path.GetFileName(filePath);
                var folder = await StorageFolder.GetFolderFromPathAsync(dir);
                storageFile = await folder.GetFileAsync(fileName);
                Log.Information("AudioLevelService: StorageFile acquired via folder+GetFileAsync");
            }
            catch (Exception ex)
            {
                Log.Warning("AudioLevelService: folder+GetFileAsync failed: {Error}", ex.Message);
            }

            if (storageFile == null)
            {
                try
                {
                    storageFile = await StorageFile.GetFileFromPathAsync(filePath);
                    Log.Information("AudioLevelService: StorageFile acquired via GetFileFromPathAsync");
                }
                catch (Exception ex)
                {
                    Log.Warning("AudioLevelService: GetFileFromPathAsync failed: {Error}", ex.Message);
                }
            }

            if (storageFile != null)
            {
                await LoadViaStorageFile(storageFile, createDeviceOutput);
            }
            else
            {
                Log.Information("AudioLevelService: no StorageFile — falling back to stream via MediaSourceAudioInputNode");
                await LoadViaStream(filePath, createDeviceOutput);
            }
        }

        private async Task CreateGraphCommon(bool createDeviceOutput)
        {
            var settings = new AudioGraphSettings(Windows.Media.Render.AudioRenderCategory.Media);
            var graphResult = await AudioGraph.CreateAsync(settings);
            if (graphResult.Status != AudioGraphCreationStatus.Success)
            {
                throw new Exception($"AudioGraph creation failed: {graphResult.Status}");
            }

            var localGraph = graphResult.Graph;
            _channels = (int)localGraph.EncodingProperties.ChannelCount;
            _sampleRate = (int)localGraph.EncodingProperties.SampleRate;
            InitBandMappings(_sampleRate);

            Log.Information("AudioLevelService: graph enc={Enc} rate={Rate} ch={Ch}",
                localGraph.EncodingProperties.Subtype, _sampleRate, _channels);

            var deviceResult = await localGraph.CreateDeviceOutputNodeAsync();
            if (deviceResult.Status != AudioDeviceNodeCreationStatus.Success)
            {
                localGraph.Dispose();
                throw new Exception($"Device output node failed: {deviceResult.Status}");
            }
            _deviceOutputNode = deviceResult.DeviceOutputNode;

            if (createDeviceOutput)
            {
                Log.Information("AudioLevelService: playback mode (device output connected)");
            }
            else
            {
                Log.Information("AudioLevelService: analysis mode (device output for clock only)");
            }

            _frameOutputNode = localGraph.CreateFrameOutputNode();
            localGraph.QuantumStarted += OnQuantumStarted;
            _graph = localGraph;
        }

        private async Task LoadViaStorageFile(StorageFile storageFile, bool createDeviceOutput)
        {
            try
            {
                await CreateGraphCommon(createDeviceOutput);

                var fileResult = await _graph.CreateFileInputNodeAsync(storageFile);
                if (fileResult.Status != AudioFileNodeCreationStatus.Success)
                {
                    Log.Warning("AudioLevelService: file node failed: {Status}", fileResult.Status);
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

                Log.Information("AudioLevelService: file loaded dur={Dur:F1}s — starting playback",
                    _fileInputNode.Duration.TotalSeconds);

                _fileInputNode.Start();
                _graph.Start();
                _isGraphRunning = true;
                _isAnalyzing = true;
                MediaOpened?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Log.Warning("AudioLevelService: LoadViaStorageFile failed: {Error}", ex.Message);
                MediaFailed?.Invoke(this, EventArgs.Empty);
                Stop();
            }
        }

        private async Task LoadViaStream(string filePath, bool createDeviceOutput)
        {
            try
            {
                var fileStream = new FileStream(filePath,
                    FileMode.Open, FileAccess.Read,
                    FileShare.Read | FileShare.Write | FileShare.Delete,
                    bufferSize: 65536, useAsync: true);

                var stream = fileStream.AsRandomAccessStream();
                var mediaSource = MediaSource.CreateFromStream(stream, "audio/mpeg");

                await CreateGraphCommon(createDeviceOutput);

                var nodeResult = await _graph.CreateMediaSourceAudioInputNodeAsync(mediaSource);
                if (nodeResult.Status != MediaSourceAudioInputNodeCreationStatus.Success)
                {
                    Log.Warning("AudioLevelService: MediaSourceAudioInputNode failed: {Status}", nodeResult.Status);
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

                Log.Information("AudioLevelService: stream loaded dur={Dur:F1}s — starting playback",
                    _mediaSourceNode.Duration.TotalSeconds);

                _mediaSourceNode.Start();
                _graph.Start();
                _isGraphRunning = true;
                _isAnalyzing = true;
                MediaOpened?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Log.Warning("AudioLevelService: LoadViaStream failed: {Error}", ex.Message);
                MediaFailed?.Invoke(this, EventArgs.Empty);
                Stop();
            }
        }

        private void OnFileCompleted(AudioFileInputNode sender, object args)
        {
            Log.Information("AudioLevelService: file completed");
            MediaEnded?.Invoke(this, EventArgs.Empty);
        }

        public void Pause()
        {
            if (_graph == null) return;
            try
            {
                _graph.Stop();
                _isGraphRunning = false;
                Log.Information("AudioLevelService: paused");
            }
            catch (Exception ex)
            {
                Log.Warning("AudioLevelService: pause failed: {Error}", ex.Message);
            }
        }

        public void Resume()
        {
            if (_graph == null) return;
            try
            {
                _graph.Start();
                _isGraphRunning = true;
                Log.Information("AudioLevelService: resumed");
            }
            catch (Exception ex)
            {
                Log.Warning("AudioLevelService: resume failed: {Error}", ex.Message);
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
            }
            catch (Exception ex)
            {
                Log.Warning("AudioLevelService: Seek failed: {Error}", ex.Message);
            }
        }

        public void Stop()
        {
            _isAnalyzing = false;
            _isGraphRunning = false;
            Interlocked.Exchange(ref _isProcessing, 0);

            if (_mediaSourceNode != null)
            {
                try { _mediaSourceNode.Dispose(); } catch { }
                _mediaSourceNode = null;
            }

            if (_graph != null)
            {
                try { _graph.Stop(); } catch { }
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

            _currentFilePath = null;

            for (int i = 0; i < BandCount; i++)
            {
                _bandLevels[i] = 0f;
                _bandPeaks[i] = 0f;
                _bandPeakHoldTimers[i] = 0f;
            }

            _beat = 0f;
            _energyHistory = 0f;
            _waveformCount = 0;

            Log.Information("AudioLevelService: stopped");
        }

        private unsafe void OnQuantumStarted(AudioGraph sender, object args)
        {
            if (!_isAnalyzing) return;
            if (Interlocked.CompareExchange(ref _isProcessing, 1, 0) != 0) return;

            var frameOutput = _frameOutputNode;
            if (frameOutput == null) { Interlocked.Exchange(ref _isProcessing, 0); return; }

            AudioFrame frame = null;
            try { frame = frameOutput.GetFrame(); }
            catch { Interlocked.Exchange(ref _isProcessing, 0); return; }

            if (frame == null) { Interlocked.Exchange(ref _isProcessing, 0); return; }

            try
            {
                ProcessFrame(frame);

                _quantumLogCounter++;
                if (_quantumLogCounter <= 5 || (_quantumLogCounter % 1000 == 0 && _quantumLogCounter <= 10000))
                {
                    float sum = 0f;
                    for (int i = 0; i < BandCount; i++) sum += _bandLevels[i];
                    Log.Information("AudioLevelService: quantum#{Cnt} rate={Rate} ch={Ch} bandsSum={Sum:F4} lvl0={L0:F4} lvl5={L5:F4}",
                        _quantumLogCounter, _sampleRate, _channels, sum, _bandLevels[0], _bandLevels[5]);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("AudioLevelService: ProcessFrame error: {Error}", ex.Message);
            }
            finally
            {
                try { frame.Dispose(); } catch { }
                Interlocked.Exchange(ref _isProcessing, 0);
            }
        }

        private unsafe void ProcessFrame(AudioFrame frame)
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

                if (fftSamples == 0) return;

                for (int i = 0; i < fftSamples; i++)
                {
                    float maxVal = 0f;
                    for (int ch = 0; ch < _channels; ch++)
                    {
                        float val = Math.Abs(((float*)dataByte)[i * _channels + ch]);
                        if (val > maxVal) maxVal = val;
                    }
                    _windowedBuffer[i] = maxVal;
                }

                for (int i = fftSamples; i < FftSize; i++)
                    _windowedBuffer[i] = 0f;

                // Save raw waveform before Hamming window (for visualizers)
                _waveformCount = fftSamples;
                for (int i = 0; i < fftSamples; i++)
                    _waveformBuffer[i] = _windowedBuffer[i];
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

                // Beat detection: compare instantaneous energy to moving average
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
        }

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
                Log.Warning("AudioLevelService: SetVolume failed: {Error}", ex.Message);
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
