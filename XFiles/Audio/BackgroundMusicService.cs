using System;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Media.Audio;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;
using XFiles.FileSystem;
using XFiles.Settings;

namespace XFiles.Audio
{
    /// <summary>
    /// Loop-playing background music, sourced from a copy inside LocalState\BGM so
    /// playback never touches an external drive. Owns a dedicated AudioGraph,
    /// separate from AudioLevelService (the media player), so both can run at once.
    /// </summary>
    /// <remarks>
    /// Loop model: AudioFileInputNode.LoopCount = 1 + FileCompleted handler that
    /// waits LoopGapMs and then seeks back to zero — produces the requested
    /// silence gap between iterations (LoopCount = null would loop seamlessly).
    /// Resume after a pause restarts the track from the beginning (AudioFileInputNode
    /// exposes no Position API).
    /// </remarks>
    public sealed class BackgroundMusicService
    {
        public static BackgroundMusicService Instance { get; } = new BackgroundMusicService();

        private const string FolderName = "BGM";
        private const int LoopGapMs = 2500;
        private const int ResumeCooldownMs = 10000;
        private const string DefaultFileName = "bgm.wav";
        private const string DefaultDisplayName = "17 Stickerbrush Symphony.spc";

        private AudioGraph _graph;
        private AudioFileInputNode _fileNode;
        private AudioDeviceOutputNode _deviceOut;
        private bool _graphRunning;

        private volatile bool _isEnabled;
        private volatile bool _isPaused;
        private float _volume = 0.5f;
        private string _bgmFileName = "";
        private string _bgmSourceName = "";
        private int _fadeGeneration;
        private StorageFolder _bgmFolder;

        // Generation counters cancel pending loop-gap restarts and pending
        // cooldown resumes when state changes underneath them.
        private int _gapGeneration;
        private int _resumeGeneration;

        private DispatcherQueue _uiQueue;

        // Boot chime completion task (set once at startup): the BGM waits for the
        // chime to finish, then 1s, before fading in. Null after the boot flow.
        private Task _chimeWait;

        private BackgroundMusicService() { }

        public bool IsEnabled => _isEnabled;
        public bool IsPlaying => _isEnabled && !_isPaused && _graphRunning;
        public string SourceName => string.IsNullOrEmpty(_bgmSourceName) ? _bgmFileName : _bgmSourceName;

        /// <summary>
        /// One-time startup. Reads settings; if enabled and the LocalState copy
        /// still exists, starts looping playback. If enabled but no track is
        /// present (first run, or LocalState\BGM\ was deleted), installs the
        /// bundled default and plays it. Never blocks first paint — the caller
        /// fires this and forgets.
        /// </summary>
        public async Task InitializeAsync(Task chimeWait = null)
        {
            try
            {
                _chimeWait = chimeWait;

                _uiQueue = DispatcherQueue.GetForCurrentThread();
                if (_uiQueue == null)
                    _uiQueue = CoreApplication.MainView.DispatcherQueue;

                _volume = MusicFormatClassifier.PercentToGain(await XFilesSettings.GetBgmVolumeAsync());

                bool enabled = await XFilesSettings.GetBgmEnabledAsync();
                if (!enabled) { _isEnabled = false; return; }

                // The BGM folder doubles as the install state: when it (or the
                // track inside it) is gone, restore the initial state by
                // reinstalling the bundled default. Do NOT create the folder
                // before checking — creating it would mask the first-run signal.
                StorageFolder folder = null;
                try
                {
                    folder = await ApplicationData.Current.LocalFolder.GetFolderAsync(FolderName);
                }
                catch (FileNotFoundException)
                {
                    folder = null;
                }

                string fileName = await XFilesSettings.GetBgmFileNameAsync();
                StorageFile file = null;
                if (folder != null && !string.IsNullOrEmpty(fileName))
                {
                    try
                    {
                        file = await folder.GetFileAsync(fileName);
                    }
                    catch (FileNotFoundException)
                    {
                        file = null;
                    }
                }

                if (file == null)
                {
                    Log.Info("BackgroundMusic.Initialize: no BGM track present — installing bundled default");
                    await InstallDefaultAsync();
                    return;
                }

                _bgmFolder = folder;
                _bgmFileName = fileName;
                _bgmSourceName = await XFilesSettings.GetBgmSourceNameAsync();
                _isEnabled = true;
                Log.Info("BackgroundMusic.Initialize: starting '{Name}' at {Vol:F0}%", fileName, _volume * 100f);
                await PlayFileAsync(file.Path);
            }
            catch (Exception ex)
            {
                Log.Err("BackgroundMusic.Initialize failed", ex);
                _isEnabled = false;
            }
        }
        /// <summary>
        /// First-run / fresh-state install: streams the bundled default SPC into
        /// LocalState\BGM\bgm.wav via the growing-file render (same validated
        /// pipeline as the media player), starts playback as soon as enough audio
        /// exists, and lets the render finish in the background. Persists the
        /// display name so the settings menus show the track title.
        /// </summary>
        private async Task<bool> InstallDefaultAsync()
        {
            try
            {
                _bgmFolder = await ApplicationData.Current.LocalFolder
                    .CreateFolderAsync(FolderName, CreationCollisionOption.OpenIfExists);

                StorageFile asset = await StorageFile.GetFileFromApplicationUriAsync(
                    new Uri("ms-appx:///Assets/Audio/default-bgm.spc"));

                IBuffer raw = await FileIO.ReadBufferAsync(asset);
                byte[] data = new byte[raw.Length];
                using (DataReader reader = DataReader.FromBuffer(raw))
                    reader.ReadBytes(data);

                string targetPath = Path.Combine(_bgmFolder.Path, DefaultFileName);
                Log.Info("BackgroundMusic.InstallDefault: streaming bundled SPC to WAV");
                ChiptuneRenderHandle handle = RetroAudioPlayer.StartChiptuneStreamToFile(
                    asset.Path, data, ".spc", 0, targetPath);

                string playPath = await RetroAudioPlayer.WaitForStreamableWavAsync(handle, 8.0);
                if (string.IsNullOrEmpty(playPath))
                {
                    Log.Warn("BackgroundMusic.InstallDefault: render produced no streamable WAV");
                    _isEnabled = false;
                    return false;
                }

                _bgmFileName = DefaultFileName;
                _bgmSourceName = DefaultDisplayName;
                _isEnabled = true;
                _isPaused = false;
                await XFilesSettings.SetBgmFileNameAsync(DefaultFileName);
                await XFilesSettings.SetBgmSourceNameAsync(DefaultDisplayName);
                await XFilesSettings.SetBgmEnabledAsync(true);
                Log.Info("BackgroundMusic.InstallDefault: bundled default '{Name}' playing", DefaultDisplayName);
                await PlayFileAsync(targetPath);
                return true;
            }
            catch (Exception ex)
            {
                Log.Err("BackgroundMusic.InstallDefault failed", ex);
                _isEnabled = false;
                return false;
            }
        }

        /// <summary>
        /// Pick a new music file. Standard audio is copied as-is; chiptune is
        /// rendered to WAV (one-time, may take seconds) and that WAV is copied.
        /// Enables BGM and starts playing on success.
        /// </summary>
        public async Task<bool> SetTrackAsync(string sourcePath)
        {
            try
            {
                string ext = Path.GetExtension(sourcePath);
                if (!MusicFormatClassifier.IsMusicFile(ext))
                {
                    Log.Warn("BackgroundMusic.SetTrack: unsupported extension '{Ext}'", ext);
                    return false;
                }

                if (_bgmFolder == null)
                    _bgmFolder = await ApplicationData.Current.LocalFolder
                        .CreateFolderAsync(FolderName, CreationCollisionOption.OpenIfExists);

                string destName;
                if (MusicFormatClassifier.IsStandardAudio(ext))
                {
                    destName = "bgm" + ext.ToLowerInvariant();
                    string destPath = Path.Combine(_bgmFolder.Path, destName);
                    Log.Info("BackgroundMusic.SetTrack: copying '{Source}' → '{Dest}'", sourcePath, destPath);
                    File.Copy(sourcePath, destPath, overwrite: true);
                }
                else
                {
                    byte[] data = File.ReadAllBytes(sourcePath);
                    Log.Info("BackgroundMusic.SetTrack: rendering chiptune '{Source}' to WAV", sourcePath);
                    string wavPath = await RetroAudioPlayer.RenderToWavAsync(sourcePath, data, ext, 0);
                    destName = "bgm.wav";
                    string destPath = Path.Combine(_bgmFolder.Path, destName);
                    File.Copy(wavPath, destPath, overwrite: true);
                }

                await CleanupStaleCopiesAsync(destName);

                _bgmFileName = destName;
                _bgmSourceName = Path.GetFileName(sourcePath);
                _isEnabled = true;
                _isPaused = false;
                await XFilesSettings.SetBgmFileNameAsync(destName);
                await XFilesSettings.SetBgmSourceNameAsync(_bgmSourceName);
                await XFilesSettings.SetBgmEnabledAsync(true);

                StorageFile file = await _bgmFolder.GetFileAsync(destName);
                await PlayFileAsync(file.Path);
                Log.Info("BackgroundMusic.SetTrack: playing '{Name}'", destName);
                return true;
            }
            catch (Exception ex)
            {
                Log.Err("BackgroundMusic.SetTrack failed for '{Path}'", ex, sourcePath);
                return false;
            }
        }

        /// <summary>Toggle on/off (keeps the chosen file).</summary>
        public async Task SetEnabledAsync(bool enabled)
        {
            try
            {
                if (enabled && _bgmFolder == null)
                    _bgmFolder = await ApplicationData.Current.LocalFolder
                        .CreateFolderAsync(FolderName, CreationCollisionOption.OpenIfExists);

                if (enabled)
                {
                    if (!string.IsNullOrEmpty(_bgmFileName))
                    {
                        StorageFile file = await _bgmFolder.GetFileAsync(_bgmFileName);
                        _isEnabled = true;
                        _isPaused = false;
                        await PlayFileAsync(file.Path);
                    }
                    else
                    {
                        Log.Info("BackgroundMusic.SetEnabled: enabled but no track — installing bundled default");
                        await InstallDefaultAsync();
                    }
                }
                else
                {
                    _gapGeneration++;
                    _resumeGeneration++;
                    _isEnabled = false;
                    StopGraph();
                }
                await XFilesSettings.SetBgmEnabledAsync(enabled);
            }
            catch (Exception ex)
            {
                Log.Err("BackgroundMusic.SetEnabled({Enabled}) failed", ex, enabled);
            }
        }

        /// <summary>Pause immediately (media player engaged). Cancels any pending gap restart.</summary>
        public void Pause()
        {
            if (!_isEnabled) return;
            _gapGeneration++;
            _fadeGeneration++;
            _resumeGeneration++;
            _isPaused = true;
            try
            {
                _fileNode?.Stop();
                _graph?.Stop();
                _graphRunning = false;
            }
            catch (Exception ex) { Log.Warn("BackgroundMusic.Pause failed", ex); }
            Log.Info("BackgroundMusic: paused");
        }

        /// <summary>
        /// Request resume after the media player releases. Starts a 10s cooldown;
        /// a new request (more media activity) re-arms the window. Restarts the
        /// track from the beginning.
        /// </summary>
        public void RequestResume()
        {
            if (!_isEnabled) return;
            _resumeGeneration++;
            int gen = _resumeGeneration;
            _ = Task.Delay(ResumeCooldownMs).ContinueWith(_ =>
            {
                if (gen != _resumeGeneration) return;
                if (_uiQueue == null) return;
                _uiQueue.TryEnqueue(() => Resume(gen));
            });
        }

        private void Resume(int gen)
        {
            try
            {
                if (gen != _resumeGeneration) return;
                if (!_isEnabled) return;
                if (_fileNode == null || _graph == null) return;
                _isPaused = false;
                _gapGeneration++;
                _fileNode.Seek(TimeSpan.Zero);
                _fileNode.OutgoingGain = 0f;
                _fileNode.Start();
                _graph.Start();
                _graphRunning = true;
                Log.Info("BackgroundMusic: resumed after {Cooldown}s cooldown", ResumeCooldownMs / 1000);
                _ = FadeInAsync(_volume);
            }
            catch (Exception ex)
            {
                Log.Warn("BackgroundMusic.Resume failed", ex);
            }
        }

        /// <summary>Apply a new volume level immediately (0-1) and persist it.</summary>
        public async Task SetVolumeAsync(float gain)
        {
            _volume = Math.Max(0f, Math.Min(1f, gain));
            try { _fileNode.OutgoingGain = _volume; } catch (Exception ex) { Log.Warn("BackgroundMusic.SetVolume: apply failed", ex); }
            try { await XFilesSettings.SetBgmVolumeAsync((int)Math.Round(_volume * 100f)); } catch (Exception ex) { Log.Warn("BackgroundMusic.SetVolume: persist failed", ex); }
            Log.Info("BackgroundMusic: volume set to {Vol:F0}%", _volume * 100f);
        }

        public async Task<float> GetVolumeAsync()
        {
            if (_volume != 0.5f || _bgmFolder != null) return _volume;
            _volume = MusicFormatClassifier.PercentToGain(await XFilesSettings.GetBgmVolumeAsync());
            return _volume;
        }

        /// <summary>
        /// Start playback of the track at <paramref name="path"/>. The 2s delay
        /// lets the boot chime finish before the music fades in. A streaming
        /// install renders to path.tmp then renames to path — the actual file is
        /// resolved after the delay and re-resolved once if the rename lands
        /// between the resolve and the node open (TOCTOU fix for the BGM install).
        /// </summary>
        private async Task PlayFileAsync(string path)
        {
            StopGraph();
            try
            {
                // Boot path: wait for the boot chime to finish, then 1s of silence,
                // then start with a fade-in. Non-boot paths (track pick, re-enable)
                // carry no chime and start immediately.
                Task chime = _chimeWait;
                _chimeWait = null;
                if (chime != null)
                {
                    // Bail out if the chime never signals (silent failure) so the
                    // BGM still comes up.
                    await Task.WhenAny(chime, Task.Delay(8000));
                    await Task.Delay(1000);
                }
                if (_isPaused || !_isEnabled) return;

                var settings = new AudioGraphSettings(Windows.Media.Render.AudioRenderCategory.GameMedia);
                settings.DesiredSamplesPerQuantum = 4800;
                var graphResult = await AudioGraph.CreateAsync(settings);
                if (graphResult.Status != AudioGraphCreationStatus.Success)
                {
                    Log.Warn("BackgroundMusic: graph creation failed: {Status}", graphResult.Status);
                    _isEnabled = false;
                    return;
                }
                var localGraph = graphResult.Graph;

                var deviceResult = await localGraph.CreateDeviceOutputNodeAsync();
                if (deviceResult.Status != AudioDeviceNodeCreationStatus.Success)
                {
                    Log.Warn("BackgroundMusic: device output node failed: {Status}", deviceResult.Status);
                    localGraph.Dispose();
                    _isEnabled = false;
                    return;
                }

                bool nodeOk = false;
                for (int attempt = 0; attempt < 2 && !nodeOk; attempt++)
                {
                    string actual = File.Exists(path + ".tmp") ? path + ".tmp" : path;
                    if (!File.Exists(actual))
                    {
                        Log.Warn("BackgroundMusic: playable file missing: {Path}", actual);
                        break;
                    }
                    StorageFile file = await StorageFile.GetFileFromPathAsync(actual);
                    var result = await localGraph.CreateFileInputNodeAsync(file);
                    nodeOk = result.Status == AudioFileNodeCreationStatus.Success;
                    if (nodeOk) _fileNode = result.FileInputNode;
                    else
                    {
                        Log.Warn("BackgroundMusic: file node attempt {Attempt} failed: {Status} ({Path})",
                            attempt + 1, result.Status, actual);
                        // Streaming render renamed .tmp -> path mid-open: re-resolve once.
                    }
                }
                if (!nodeOk)
                {
                    Log.Warn("BackgroundMusic: file node failed ({Path})", path);
                    localGraph.Dispose();
                    _isEnabled = false;
                    return;
                }

                _graph = localGraph;
                _deviceOut = deviceResult.DeviceOutputNode;
                _fileNode.LoopCount = 1;
                _fileNode.FileCompleted += OnFileCompleted;
                _fileNode.AddOutgoingConnection(_deviceOut);
                _fileNode.OutgoingGain = 0f;   // start silent — FadeInAsync ramps to volume

                _graph.Start();
                _graphRunning = true;
                Log.Info("BackgroundMusic: graph started, dur={Dur:F1}s, rate={Rate}",
                    _fileNode.Duration.TotalSeconds,
                    localGraph.EncodingProperties.SampleRate);
                _ = FadeInAsync(_volume);
            }
            catch (Exception ex)
            {
                Log.Err("BackgroundMusic.PlayFile failed", ex);
                StopGraph();
            }
        }

        private async void OnFileCompleted(AudioFileInputNode sender, object args)
        {
            int gen = ++_gapGeneration;
            Log.Dbg("BackgroundMusic: track completed — gap {Gap}ms before loop restart", LoopGapMs);
            try { await Task.Delay(LoopGapMs); }
            catch { return; }
            if (gen != _gapGeneration || !_isEnabled || _isPaused) return;
            try
            {
                if (_fileNode != null)
                {
                    _fileNode.Seek(TimeSpan.Zero);
                    _fileNode.OutgoingGain = 0f;
                    _fileNode.Start();
                    _graph?.Start();
                    _graphRunning = true;
                    _ = FadeInAsync(_volume);
                }
            }
            catch (Exception ex) { Log.Warn("BackgroundMusic: loop restart failed", ex); }
        }

        /// <summary>Ramp the file node gain from 0 to target over ~1s. Aborts if the
        /// graph is stopped or a newer fade/restart supersedes it.</summary>
        private async Task FadeInAsync(float target)
        {
            int gen = ++_fadeGeneration;
            const int steps = 20;
            const int stepMs = 50;
            for (int i = 1; i <= steps; i++)
            {
                await Task.Delay(stepMs);
                if (_fileNode == null || gen != _fadeGeneration) return;
                _fileNode.OutgoingGain = target * i / steps;
            }
        }

        private void StopGraph()
        {
            _gapGeneration++;
            _fadeGeneration++;
            _resumeGeneration++;
            try
            {
                if (_fileNode != null) _fileNode.FileCompleted -= OnFileCompleted;
                if (_graph != null && _graphRunning) _graph.Stop();
                _fileNode = null;
                _deviceOut = null;
                _graph?.Dispose();
                _graph = null;
                _graphRunning = false;
            }
            catch (Exception ex) { Log.Warn("BackgroundMusic.StopGraph failed", ex); }
        }

        /// <summary>Delete every other copy in the BGM folder so only the active track remains.</summary>
        private async Task CleanupStaleCopiesAsync(string keepName)
        {
            try
            {
                foreach (StorageFile file in await _bgmFolder.GetFilesAsync())
                {
                    if (!string.Equals(file.Name, keepName, StringComparison.OrdinalIgnoreCase))
                        await file.DeleteAsync();
                }
            }
            catch (Exception ex) { Log.Warn("BackgroundMusic.CleanupStaleCopies failed", ex); }
        }
    }
}
