using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using XFiles.Audio;
using XFiles.FileSystem;
using XFiles.Metadata;
using XFiles.Navigation;

namespace XFiles.Controls
{
    public sealed partial class MediaPreviewControl : UserControl
    {
        private DispatcherTimer _progressTimer;
        private readonly DispatchedHandler _progressUpdateHandler;
        private bool _isAudioMode;
        private bool _ownsAudioService;
        private string _currentFilePath;
        private Uri _currentSourceUri;
        private bool _isPlaying;
        private bool _isLoadingPlayback;
        private bool _hasEnded;
        private MetadataGuesser _metadataGuesser;
        private CancellationTokenSource _metadataCts;
        private readonly ArchiveBrowser _archiveBrowser = new ArchiveBrowser();

        // Chiptune subsong state: which track of which source is loaded.
        private string _chiptuneSource;
        private int _chiptuneTrack;
        private int _chiptuneTrackCount = 1;
        private string _chiptuneTitle;

        // Generation of the audio load currently attached to the AudioLevelService
        // MediaOpened/MediaEnded/MediaFailed events. Used to discard stale events
        // from a superseded load (prevents a dead player state).
        private int _ownedAudioGen = -1;


        private MediaPlaybackItem _currentPlaybackItem;
        private List<SubtitleTrack> _currentSubtitleTracks = new List<SubtitleTrack>();
        private List<AudioTrackInfo> _currentAudioTracks = new List<AudioTrackInfo>();
        private int _currentSubtitleIndex = -1;
        private int _currentAudioIndex = -1;

        private MediaPlayer Player => MediaPlayerElementControl.MediaPlayer;
        private MediaPlaybackSession Session => Player?.PlaybackSession;

        public bool IsAudioMode => _isAudioMode;
        public string CurrentFilePath => _currentFilePath;
        public bool IsFileLoaded(string filePath) => _currentFilePath == filePath;

        /// <summary>
        /// Chiptune subsong currently selected by the preview (used by the
        /// fullscreen player to continue track navigation).
        /// </summary>
        public string CurrentChiptuneSource => _chiptuneSource;
        public int CurrentChiptuneTrack => _chiptuneTrack;
        public int CurrentChiptuneTrackCount => _chiptuneTrackCount;
        public string CurrentChiptuneTitle => _chiptuneTitle ?? "";

        /// <summary>
        /// Select a specific chiptune subsong for the given source, so the next
        /// decode renders that track. Used when opening the fullscreen player from
        /// a drilled-in track list.
        /// </summary>
        public void SetChiptuneTrack(string source, int track)
        {
            _chiptuneSource = source;
            _chiptuneTrack = Math.Max(0, track);
        }

        public MediaPlaybackItem CurrentPlaybackItem => _currentPlaybackItem;
        public List<SubtitleTrack> CurrentSubtitleTracks => _currentSubtitleTracks;
        public List<AudioTrackInfo> CurrentAudioTracks => _currentAudioTracks;
        public int CurrentSubtitleTrackIndex => _currentSubtitleIndex;
        public int CurrentAudioTrackIndex => _currentAudioIndex;

        public TimeSpan CurrentPosition
        {
            get
            {
                if (_isAudioMode)
                    return AudioLevelService.Instance.Position;
                return Session?.Position ?? TimeSpan.Zero;
            }
        }

        public MediaPreviewControl()
        {
            this.InitializeComponent();
            this.Unloaded += OnUnloaded;
            _progressUpdateHandler = UpdateProgress;
            _metadataGuesser = new MetadataGuesser();
            _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _progressTimer.Tick += OnProgressTimerTick;
            Player.MediaOpened += OnMediaPlayerOpened;
            Player.MediaEnded += OnMediaPlayerEnded;
            Player.MediaFailed += OnMediaPlayerFailed;
        }

        public void LoadFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            Log.Dbg("MediaPreviewControl.LoadFile: enter for {Path} (wasPlaying={WasPlaying})", filePath, _isPlaying);
            Stop();
            _hasEnded = false;
            ResetProgressUi();
            Log.Info("MediaPreviewControl: loading {Path}", filePath);

            _currentFilePath = filePath;
            string ext = Path.GetExtension(filePath);
            _isAudioMode = FilePreviewService.IsAudioFile(ext) || FilePreviewService.IsChiptuneFile(ext);
            Log.Dbg("MediaPreviewControl: ext={Ext} isAudio={IsAudio}", ext, _isAudioMode);

            if (_isAudioMode)
            {
                AudioInfoPanel.Visibility = Visibility.Visible;
                AlbumArtBorder.Visibility = Visibility.Collapsed;
                DefaultArtPanel.Visibility = Visibility.Visible;
                TitleText.Text = GetDisplayName(filePath);
                ArtistText.Text = "";
                ArtistText.Visibility = Visibility.Collapsed;
                AlbumText.Text = "";
                AlbumText.Visibility = Visibility.Collapsed;

                if (RetroAudioPlayer.IsChiptuneFile(filePath))
                {
                    _chiptuneSource = filePath;
                    _chiptuneTrack = 0;
                    _chiptuneTrackCount = 1;
                    _chiptuneTitle = null;
                }
                else
                {
                    _chiptuneSource = null;
                    _ = LoadMetadataAsync(filePath);
                }
            }
            else
            {
                _currentSourceUri = new Uri(filePath);
                var source = MediaSource.CreateFromUri(_currentSourceUri);
                _currentPlaybackItem = new MediaPlaybackItem(source);
                Player.Source = _currentPlaybackItem;
            }

            _isPlaying = false;
            UpdatePlayPauseIcon();
            Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Load a specific chiptune subsong from a source (file or archive entry).
        /// Playback begins on the next play action, like regular audio files.
        /// </summary>
        public void LoadChiptuneTrack(string source, int track)
        {
            if (string.IsNullOrEmpty(source)) return;
            Log.Dbg("MediaPreviewControl.LoadChiptuneTrack: enter for {Source} track={Track} (wasPlaying={WasPlaying})", source, track, _isPlaying);
            Stop();
            _hasEnded = false;

            _currentFilePath = source;
            _isAudioMode = true;
            _chiptuneSource = source;
            _chiptuneTrack = Math.Max(0, track);
            _chiptuneTitle = null;
            ResetProgressUi();

            AudioInfoPanel.Visibility = Visibility.Visible;
            AlbumArtBorder.Visibility = Visibility.Collapsed;
            DefaultArtPanel.Visibility = Visibility.Visible;
            TitleText.Text = GetDisplayName(source);
            ArtistText.Text = "";
            ArtistText.Visibility = Visibility.Collapsed;
            AlbumText.Text = "";
            AlbumText.Visibility = Visibility.Collapsed;

            _isPlaying = false;
            UpdatePlayPauseIcon();
            Visibility = Visibility.Visible;
        }

        private static string GetDisplayName(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return filePath ?? "";
            if (RetroAudioPlayer.IsArchiveEntryPath(filePath))
            {
                // "archivePath|internalPath" — show the entry name, not the archive.
                int pipe = filePath.LastIndexOf('|');
                return Path.GetFileNameWithoutExtension(filePath.Substring(pipe + 1));
            }
            return Path.GetFileNameWithoutExtension(filePath);
        }

        public void LoadNextTrack(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            // Multi-track chiptune: advance to the next subsong of the same source.
            if (_chiptuneSource != null && _chiptuneTrackCount > 1 &&
                string.Equals(_chiptuneSource, filePath, StringComparison.OrdinalIgnoreCase))
            {
                int next = (_chiptuneTrack + 1) % _chiptuneTrackCount;
                Log.Info("MediaPreviewControl: chiptune next track {Next}/{Count} of {Path}", next + 1, _chiptuneTrackCount, filePath);
                LoadChiptuneTrack(_chiptuneSource, next);
                return;
            }

            string ext = Path.GetExtension(filePath);
            bool newIsAudio = FilePreviewService.IsAudioFile(ext) || FilePreviewService.IsChiptuneFile(ext);

            Log.Dbg("MediaPreviewControl.LoadNextTrack: enter for {Path} isAudio={IsAudio} wasPlaying={WasPlaying}", filePath, newIsAudio, _isPlaying);

            if (_isAudioMode != newIsAudio)
            {
                Log.Info("MediaPreviewControl: mode switch, full load for {Path}", filePath);
                LoadFile(filePath);
                return;
            }

            Log.Info("MediaPreviewControl: swapping source for {Path}", filePath);
            _loadGeneration++;
            _currentFilePath = filePath;
            _hasEnded = false;
            _isPlaying = false;
            ResetProgressUi();

            if (_isAudioMode)
            {
                AudioInfoPanel.Visibility = Visibility.Visible;
                AlbumArtBorder.Visibility = Visibility.Collapsed;
                DefaultArtPanel.Visibility = Visibility.Visible;
                TitleText.Text = Path.GetFileNameWithoutExtension(filePath);
                ArtistText.Text = "";
                ArtistText.Visibility = Visibility.Collapsed;
                AlbumText.Text = "";
                AlbumText.Visibility = Visibility.Collapsed;
                _ = LoadMetadataAsync(filePath);
                _isLoadingPlayback = true;
                _ = AudioLevelService.Instance.SwapSourceAsync(filePath);
            }
            else
            {
                _currentSourceUri = new Uri(filePath);
                var source = MediaSource.CreateFromUri(_currentSourceUri);
                _currentPlaybackItem = new MediaPlaybackItem(source);
                Player.Pause();
                Player.Source = _currentPlaybackItem;
            }

            UpdatePlayPauseIcon();
            Visibility = Visibility.Visible;
            PlayerStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ShowPlaceholder(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            Stop();
            ResetProgressUi();
            Log.Dbg("MediaPreviewControl: showing placeholder for {Path}", filePath);

            _isAudioMode = true;

            AudioInfoPanel.Visibility = Visibility.Visible;
            AlbumArtBorder.Visibility = Visibility.Collapsed;
            DefaultArtPanel.Visibility = Visibility.Visible;
            TitleText.Text = Path.GetFileNameWithoutExtension(filePath);
            ArtistText.Text = "";
            ArtistText.Visibility = Visibility.Collapsed;
            AlbumText.Text = "";
            AlbumText.Visibility = Visibility.Collapsed;

            _isPlaying = false;
            UpdatePlayPauseIcon();
            Visibility = Visibility.Visible;
        }

        private async Task StartAudioPlayback(string filePath)
        {
            Log.Info("MediaPreviewControl.StartAudioPlayback: starting for {Path}", filePath ?? "(null)");
            int gen = ++_loadGeneration;
            ResetProgressUi();
            try
            {
                string playPath = filePath;
                bool chiptune = false;
                if (RetroAudioPlayer.IsChiptuneFile(filePath))
                {
                    chiptune = true;
                    playPath = await PrepareChiptuneAsync(filePath);
                    if (gen != _loadGeneration)
                    {
                        Log.Dbg("MediaPreviewControl.StartAudioPlayback: stale chiptune load (gen {Gen} != {Current}) — aborting", gen, _loadGeneration);
                        _isLoadingPlayback = false;
                        return;
                    }
                    if (playPath == null)
                    {
                        Log.Warn("MediaPreviewControl: chiptune decode failed for {Path}", filePath);
                        _isLoadingPlayback = false;
                        _isPlaying = false;
                        _progressTimer.Stop();
                        UpdatePlayPauseIcon();
                        PlayerStateChanged?.Invoke(this, EventArgs.Empty);
                        return;
                    }
                }

                _ownedAudioGen = gen;
                AudioLevelService.Instance.MediaOpened -= OnAudioMediaOpened;
                AudioLevelService.Instance.MediaOpened += OnAudioMediaOpened;
                AudioLevelService.Instance.MediaEnded -= OnAudioMediaEnded;
                AudioLevelService.Instance.MediaEnded += OnAudioMediaEnded;
                AudioLevelService.Instance.MediaFailed -= OnAudioMediaFailed;
                AudioLevelService.Instance.MediaFailed += OnAudioMediaFailed;
#if AUDIO_ANALYSIS
                VuMeter.AttachService(AudioLevelService.Instance);
#endif
                _ownsAudioService = true;
                await AudioLevelService.Instance.LoadAndPlay(playPath, forceStream: chiptune);
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to start audio playback", ex);
                _isPlaying = false;
                _progressTimer.Stop();
                UpdatePlayPauseIcon();
                PlayerStateChanged?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                if (gen == _loadGeneration)
                    _isLoadingPlayback = false;
            }
        }

        /// <summary>
        /// Decode a chiptune source to a cached WAV and return its path.
        /// Handles plain files and archive-entry addresses ("archive|internal").
        /// </summary>
        private async Task<string> PrepareChiptuneAsync(string source)
        {
            try
            {
                string ext = Path.GetExtension(source);
                byte[] data = await ReadChiptuneSourceAsync(source);

                var probe = RetroAudioPlayer.Probe(source, data, ext);
                if (probe != null)
                {
                    _chiptuneTrackCount = probe.TrackCount;
                    _chiptuneTitle = null;

                    if (_chiptuneSource != null && _chiptuneTrack >= 0 && _chiptuneTrack < probe.TrackCount &&
                        !string.IsNullOrEmpty(probe.Titles[_chiptuneTrack]))
                    {
                        _chiptuneTitle = probe.Titles[_chiptuneTrack];
                        TitleText.Text = _chiptuneTitle;
                    }
                }

                string wav = await RetroAudioPlayer.RenderToWavAsync(source, data, ext, _chiptuneTrack);
                return wav;
            }
            catch (Exception ex)
            {
                Log.Warn("MediaPreviewControl.PrepareChiptuneAsync failed for '{Path}': {Error}", source, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Track count for a chiptune source — used to decide play-vs-drill-in on
        /// the confirm button. Returns 1 when the source cannot be probed.
        /// </summary>
        public async Task<int> GetChiptuneTrackCountAsync(string source)
        {
            try
            {
                if (string.IsNullOrEmpty(source) || !RetroAudioPlayer.IsChiptuneFile(source)) return 1;
                string ext = Path.GetExtension(source);
                byte[] data = await ReadChiptuneSourceAsync(source);
                var info = RetroAudioPlayer.Probe(source, data, ext);
                return info?.TrackCount ?? 1;
            }
            catch (Exception ex)
            {
                Log.Warn("MediaPreviewControl.GetChiptuneTrackCountAsync failed for '{Path}': {Error}", source, ex.Message);
                return 1;
            }
        }

        /// <summary>
        /// Decode a chiptune source to its cached WAV (for fullscreen playback).
        /// Uses the currently selected subsong when the source matches, else track 0.
        /// </summary>
        public async Task<string> GetChiptuneWavPathAsync(string source)
        {
            if (_chiptuneSource == null || !string.Equals(_chiptuneSource, source, StringComparison.OrdinalIgnoreCase))
            {
                _chiptuneSource = source;
                _chiptuneTrack = 0;
            }
            return await PrepareChiptuneAsync(source);
        }

        /// <summary>
        /// Decode a specific chiptune subsong to its cached WAV. Selects the track
        /// first so the probe/decoder render the right subsong.
        /// </summary>
        public async Task<string> GetChiptuneWavPathAsync(string source, int track)
        {
            _chiptuneSource = source;
            _chiptuneTrack = Math.Max(0, track);
            return await PrepareChiptuneAsync(source);
        }

        private async Task<byte[]> ReadChiptuneSourceAsync(string source)
        {
            if (RetroAudioPlayer.IsArchiveEntryPath(source))
            {
                int pipe = source.IndexOf('|');
                string archivePath = source.Substring(0, pipe);
                string internalPath = source.Substring(pipe + 1);

                using (var stream = _archiveBrowser.OpenEntryStream(archivePath, internalPath))
                {
                    if (stream == null)
                    {
                        Log.Warn("MediaPreviewControl: cannot open archive entry '{Archive}|{Internal}'", archivePath, internalPath);
                        return null;
                    }
                    using (var ms = new MemoryStream())
                    {
                        await stream.CopyToAsync(ms);
                        return ms.ToArray();
                    }
                }
            }
            return null;
        }

        private int _loadGeneration;

        public void StopPlayer()
        {
            _loadGeneration++;
            _isLoadingPlayback = false;
            _ownedAudioGen = -1;
            if (_isPlaying)
            {
                if (_isAudioMode)
                    AudioLevelService.Instance.Pause();
                else
                    Player.Pause();
                _isPlaying = false;
                _progressTimer.Stop();
                UpdatePlayPauseIcon();
                PlayerStateChanged?.Invoke(this, EventArgs.Empty);
            }
            if (_isAudioMode)
            {
                AudioLevelService.Instance.MediaOpened -= OnAudioMediaOpened;
                AudioLevelService.Instance.MediaEnded -= OnAudioMediaEnded;
                AudioLevelService.Instance.MediaFailed -= OnAudioMediaFailed;
                if (_ownsAudioService)
                {
                    AudioLevelService.Instance.Stop();
                    _ownsAudioService = false;
                }
#if AUDIO_ANALYSIS
                VuMeter.DetachService();
#endif
            }
            else
            {
                Player.Pause();
                Player.Source = null;
            }
            _metadataCts?.Cancel();
            _metadataCts = null;
            _currentFilePath = null;
        }

        public void Stop()
		{
			_loadGeneration++;
			_isLoadingPlayback = false;
			_ownedAudioGen = -1;
			_progressTimer.Stop();
			_metadataCts?.Cancel();
			_metadataCts = null;
			if (_isAudioMode)
			{
				AudioLevelService.Instance.MediaOpened -= OnAudioMediaOpened;
				AudioLevelService.Instance.MediaEnded -= OnAudioMediaEnded;
				AudioLevelService.Instance.MediaFailed -= OnAudioMediaFailed;
				if (_ownsAudioService)
				{
					AudioLevelService.Instance.Stop();
					_ownsAudioService = false;
				}
#if AUDIO_ANALYSIS
				VuMeter.DetachService();
#endif
			}
			else
			{
				Player.Pause();
				Player.Source = null;
			}
            _currentSourceUri = null;
            _currentFilePath = null;
            _isPlaying = false;
            _isAudioMode = false;
            UpdatePlayPauseIcon();
            ClearMetadata();
            Visibility = Visibility.Collapsed;
            PlayerStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler PlayerStateChanged;
        public event EventHandler AudioTrackEnded;
        public event EventHandler VideoTrackEnded;

        public bool IsPlayerActive => _isPlaying;

        public void SeekTo(TimeSpan position)
        {
            if (Session != null)
                Session.Position = position;
        }

        public void SelectSubtitleTrack(int index)
        {
            _currentSubtitleIndex = index;
        }

        public void SwitchAudioTrack(int trackIndex)
        {
            if (_isAudioMode) return;
            if (_currentPlaybackItem == null) return;
            if (trackIndex < 0 || trackIndex >= _currentPlaybackItem.AudioTracks.Count)
            {
                Log.Warn("MediaPreviewControl.SwitchAudioTrack: trackIndex {Index} out of range (count={Count})",
                    trackIndex, _currentPlaybackItem.AudioTracks.Count);
                return;
            }

            Log.Info("MediaPreviewControl.SwitchAudioTrack: switching to track {Index}", trackIndex);
            _currentPlaybackItem.AudioTracks.SelectedIndex = trackIndex;
            _currentAudioIndex = trackIndex;

            var pos = Session.Position;
            Player.Pause();
            Session.Position = pos;
            if (_isPlaying)
                Player.Play();
        }

        public void TogglePlayPause()
        {
            if (_isAudioMode)
            {
                if (string.IsNullOrEmpty(_currentFilePath)) return;
                if (!AudioLevelService.Instance.IsFileLoaded && !_isLoadingPlayback)
                {
                    _isLoadingPlayback = true;
                    _hasEnded = false;
                    _isPlaying = true;
                    UpdatePlayPauseIcon();
                    _progressTimer.Start();
                    PlayerStateChanged?.Invoke(this, EventArgs.Empty);
                    _ = StartAudioPlayback(_currentFilePath);
                    return;
                }
                if (_isLoadingPlayback) return;
                AudioLevelService.Instance.TogglePlayPause();
                _isPlaying = AudioLevelService.Instance.IsPlaying;
                if (_isPlaying) _hasEnded = false;
            }
            else
            {
                if (Player.Source == null) return;
                if (_isPlaying)
                {
                    Player.Pause();
                    _isPlaying = false;
                }
                else
                {
                    Player.Play();
                    _hasEnded = false;
                    _isPlaying = true;
                }
            }

            if (_isPlaying)
                _progressTimer.Start();
            else
                _progressTimer.Stop();

            UpdatePlayPauseIcon();
            PlayerStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdatePlayPauseIcon()
        {
            PlayPauseIcon.Glyph = _isPlaying ? "\uE769" : "\uE768";
        }

        private async Task LoadMetadataAsync(string filePath)
        {
            Log.Dbg("Metadata: starting async load for {Path}", filePath);
            _metadataCts?.Cancel();
            var cts = new CancellationTokenSource();
            _metadataCts = cts;
            try
            {
                _metadataGuesser.SetInternetAvailable(true);
                var match = await _metadataGuesser.ResolveAsync(filePath, cts.Token);
                var tag = match?.Metadata;
                Log.Dbg("Metadata: source={Source} score={Score:F2} title={Title} artist={Artist} album={Album}",
                    match?.Source, match?.Confidence, tag?.Title, tag?.Artist, tag?.Album);

                if (cts.IsCancellationRequested || _currentFilePath != filePath)
                {
                    Log.Info("Metadata: stale/cancelled result for {Path}, discarding", filePath);
                    return;
                }

                bool hasArt = tag?.HasAlbumArt == true;
                if (hasArt)
                {
                    AlbumArtBorder.Visibility = Visibility.Visible;
                    DefaultArtPanel.Visibility = Visibility.Collapsed;
                    await LoadAlbumArtAsync(tag.AlbumArt);
                }

                if (tag?.HasTitle == true)
                    TitleText.Text = tag.Title;
                if (tag?.HasArtist == true)
                {
                    ArtistText.Text = tag.Artist;
                    ArtistText.Visibility = Visibility.Visible;
                }
                if (tag?.HasAlbum == true)
                {
                    AlbumText.Text = tag.Album;
                    AlbumText.Visibility = Visibility.Visible;
                }

                Log.Dbg("Metadata: applied title={Title} artist={Artist} album={Album} art={HasArt}",
                    tag?.Title, tag?.Artist, tag?.Album, hasArt);
            }
            catch (Exception ex)
            {
                Log.Warn("Metadata: failed to load for {Path}", filePath, ex);
            }
        }

        private async Task LoadAlbumArtAsync(byte[] imageData)
        {
            try
            {
                var bitmap = new BitmapImage();
                using (var stream = new InMemoryRandomAccessStream())
                {
                    await stream.WriteAsync(imageData.AsBuffer());
                    stream.Seek(0);
                    await bitmap.SetSourceAsync(stream);
                }
                AlbumArtImage.Source = bitmap;
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to load album art", ex);
                AlbumArtBorder.Visibility = Visibility.Collapsed;
                DefaultArtPanel.Visibility = Visibility.Visible;
            }
        }

        private void ClearMetadata()
        {
            AudioInfoPanel.Visibility = Visibility.Collapsed;
            AlbumArtImage.Source = null;
            AlbumArtBorder.Visibility = Visibility.Collapsed;
            DefaultArtPanel.Visibility = Visibility.Collapsed;
            TitleText.Text = "";
            ArtistText.Text = "";
            AlbumText.Text = "";
        }

        private async void OnAudioMediaOpened(object sender, EventArgs args)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (_ownedAudioGen != _loadGeneration)
                {
                    Log.Dbg("MediaPreviewControl: stale media-opened event (gen {Gen} != {Current}) — ignoring", _ownedAudioGen, _loadGeneration);
                    return;
                }
                Log.Info("AudioLevelService: media opened — starting playback state");
                _isLoadingPlayback = false;
                _isPlaying = true;
                UpdatePlayPauseIcon();
                _progressTimer.Start();
                UpdateProgress();
                PlayerStateChanged?.Invoke(this, EventArgs.Empty);
            });
        }

        private async void OnAudioMediaEnded(object sender, EventArgs args)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                Log.Info("MediaPreview: {File} — audio ended, cleaning up, firing AudioTrackEnded", _currentFilePath ?? "(null)");
                AudioLevelService.Instance.Stop();
#if AUDIO_ANALYSIS
                VuMeter.DetachService();
#endif
                _isPlaying = false;
                _isLoadingPlayback = false;
                UpdatePlayPauseIcon();
                _progressTimer.Stop();
                ProgressSlider.Value = 100;
                PlayerStateChanged?.Invoke(this, EventArgs.Empty);
                AudioTrackEnded?.Invoke(this, EventArgs.Empty);
            });
        }

        private async void OnAudioMediaFailed(object sender, EventArgs args)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                Log.Info("AudioLevelService media failed — cleaning up");
                AudioLevelService.Instance.Stop();
#if AUDIO_ANALYSIS
                VuMeter.DetachService();
#endif
                _isPlaying = false;
                _isLoadingPlayback = false;
                _progressTimer.Stop();
                UpdatePlayPauseIcon();
                PlayerStateChanged?.Invoke(this, EventArgs.Empty);
            });
        }

        private async void OnMediaPlayerOpened(Windows.Media.Playback.MediaPlayer sender, object args)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                Log.Verb("Media opened: {Duration}", Session.NaturalDuration);

                _isPlaying = Session.PlaybackState == Windows.Media.Playback.MediaPlaybackState.Playing;

                UpdatePlayPauseIcon();
                if (_isPlaying)
                    _progressTimer.Start();
                else
                    _progressTimer.Stop();
                UpdateProgress();
                EnumeratePreviewTracks();

                PlayerStateChanged?.Invoke(this, EventArgs.Empty);
            });
        }

        private void EnumeratePreviewTracks()
        {
            _currentSubtitleTracks.Clear();
            _currentAudioTracks.Clear();
            _currentSubtitleIndex = -1;
            _currentAudioIndex = -1;

            if (_currentPlaybackItem == null) return;

            int subCount = 0;
            for (int i = 0; i < _currentPlaybackItem.TimedMetadataTracks.Count; i++)
            {
                var track = _currentPlaybackItem.TimedMetadataTracks[i];
                if (track.TimedMetadataKind == Windows.Media.Core.TimedMetadataKind.Subtitle)
                {
                    string lang = track.Language ?? "Unknown";
                    _currentSubtitleTracks.Add(new SubtitleTrack
                    {
                        Language = lang,
                        Title = track.Label ?? lang,
                        EmbeddedIndex = subCount,
                        IsExternal = false
                    });
                    subCount++;
                }
            }

            for (int i = 0; i < _currentPlaybackItem.AudioTracks.Count; i++)
            {
                var track = _currentPlaybackItem.AudioTracks[i];
                string lang = track.Language ?? "Unknown";
                _currentAudioTracks.Add(new AudioTrackInfo
                {
                    Language = lang,
                    Title = track.Label ?? lang,
                    Index = i
                });
            }

            _currentAudioIndex = (int)_currentPlaybackItem.AudioTracks.SelectedIndex;

            Log.Dbg("EnumeratePreviewTracks: {SubCount} subtitle, {AudioCount} audio",
                _currentSubtitleTracks.Count, _currentAudioTracks.Count);
        }

        private async void OnMediaPlayerEnded(Windows.Media.Playback.MediaPlayer sender, object args)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                Log.Info("MediaPreview: {File} — video ended, firing VideoTrackEnded", _currentFilePath ?? "(null)");
                _isPlaying = false;
                UpdatePlayPauseIcon();
                _progressTimer.Stop();
                ProgressSlider.Value = 100;
                VideoTrackEnded?.Invoke(this, EventArgs.Empty);
            });
        }

        private async void OnMediaPlayerFailed(Windows.Media.Playback.MediaPlayer sender, Windows.Media.Playback.MediaPlayerFailedEventArgs args)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                Log.Info("Media preview failed: {Error} {HResult}", args.Error.ToString(), args.ExtendedErrorCode);
                _isPlaying = false;
                _progressTimer.Stop();
                UpdatePlayPauseIcon();
            });
        }

        private void OnProgressTimerTick(object sender, object e)
        {
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, _progressUpdateHandler);
        }

        /// <summary>
        /// Zero the progress display when a new track starts loading. Without this,
        /// the bar keeps the previous track's position during the chiptune decode
        /// window (AudioLevelService still reports the old node), so the player
        /// looks like it resumed mid-song.
        /// </summary>
        private void ResetProgressUi()
        {
            ProgressSlider.Value = 0;
            TimeText.Text = "0:00 / 0:00";
        }

        private void UpdateProgress()
        {
            TimeSpan total;
            TimeSpan current;

            if (_isAudioMode && AudioLevelService.Instance.IsFileLoaded)
            {
                total = AudioLevelService.Instance.Duration;
                current = AudioLevelService.Instance.Position;
            }
            else if (Session != null)
            {
                total = Session.NaturalDuration;
                current = Session.Position;
            }
            else
            {
                return;
            }

            if (total.TotalSeconds > 0)
            {
                double pct = Math.Max(0, Math.Min(100, (current.TotalSeconds / total.TotalSeconds) * 100));
                ProgressSlider.Value = pct;
                TimeText.Text = $"{FormatTime(current)} / {FormatTime(total)}";

                // End-of-playback detection: fires once when position reaches end
                if (_isPlaying && !_hasEnded && current >= total - TimeSpan.FromSeconds(0.5))
                {
                    _hasEnded = true;
                    Log.Info("MediaPreview: {File} — position reached end ({Current}/{Total}), firing ended event", _currentFilePath ?? "(null)", current, total);
                    _isPlaying = false;
                    UpdatePlayPauseIcon();
                    _progressTimer.Stop();
                    ProgressSlider.Value = 100;
                    PlayerStateChanged?.Invoke(this, EventArgs.Empty);
                if (_isAudioMode)
                {
                    AudioLevelService.Instance.Stop();
#if AUDIO_ANALYSIS
                    VuMeter.DetachService();
#endif
                    AudioTrackEnded?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    VideoTrackEnded?.Invoke(this, EventArgs.Empty);
                }
                }
            }
        }

        private void OnVideoPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                TogglePlayPause();
        }

        private static string FormatTime(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            return $"{ts.Minutes}:{ts.Seconds:D2}";
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _progressTimer.Stop();
            _progressTimer.Tick -= OnProgressTimerTick;
            Player.MediaOpened -= OnMediaPlayerOpened;
            Player.MediaEnded -= OnMediaPlayerEnded;
            Player.MediaFailed -= OnMediaPlayerFailed;
            _currentPlaybackItem?.Source?.Dispose();
            _currentPlaybackItem = null;
            _metadataCts?.Cancel();
            _metadataCts?.Dispose();
            _metadataCts = null;
            AudioLevelService.Instance.MediaOpened -= OnAudioMediaOpened;
            AudioLevelService.Instance.MediaEnded -= OnAudioMediaEnded;
            AudioLevelService.Instance.MediaFailed -= OnAudioMediaFailed;
        }

        public void HandleButton(VirtualKey key)
        {
            switch (key)
            {
                case VirtualKey.GamepadA:
                case VirtualKey.Space:
                    TogglePlayPause();
                    break;
            }
        }

        public void Seek(TimeSpan offset)
        {
            if (_isAudioMode && AudioLevelService.Instance.IsFileLoaded)
            {
                var total = AudioLevelService.Instance.Duration;
                var newPos = AudioLevelService.Instance.Position + offset;
                if (newPos < TimeSpan.Zero) newPos = TimeSpan.Zero;
                if (total.TotalSeconds > 0 && newPos > total) newPos = total;
                AudioLevelService.Instance.Seek(newPos);
            }
            else if (Session != null && Player.Source != null)
            {
                var total = Session.NaturalDuration;
                var newPos = Session.Position + offset;
                if (newPos < TimeSpan.Zero) newPos = TimeSpan.Zero;
                if (total.TotalSeconds > 0 && newPos > total) newPos = total;
                Session.Position = newPos;
            }
            UpdateProgress();
        }

        public void SetVolume(double volume)
        {
            var clamped = Math.Max(0.0, Math.Min(1.0, volume));
            if (_isAudioMode)
            {
                // AudioGraph volume control via device output node not directly exposed
                // Volume is controlled by system audio
            }
            else
            {
                Player.Volume = clamped;
            }
        }

        public async Task OpenFullscreen()
        {
            if (_isAudioMode && AudioLevelService.Instance.IsFileLoaded)
            {
                var page = VisualTreeHelper.GetParent(this) as FrameworkElement;
                while (page != null && !(page is MillerColumnsPage))
                    page = VisualTreeHelper.GetParent(page) as FrameworkElement;
                if (page is MillerColumnsPage millerPage)
                {
                    var filePath = _currentFilePath;
                    var position = AudioLevelService.Instance.Position;
                    int chipTrack = RetroAudioPlayer.IsChiptuneFile(filePath)
                        ? _chiptuneTrack
                        : -1;
                    StopPlayer();
                    PlayerStateChanged?.Invoke(this, EventArgs.Empty);
                    await millerPage.OpenFullscreenForFile(filePath, position, chipTrack);
                }
            }
            else if (_currentSourceUri != null)
            {
                var page = VisualTreeHelper.GetParent(this) as FrameworkElement;
                while (page != null && !(page is MillerColumnsPage))
                    page = VisualTreeHelper.GetParent(page) as FrameworkElement;
                if (page is MillerColumnsPage millerPage)
                {
                    bool isVideo = Session.NaturalVideoHeight > 0;
                    var source = _currentSourceUri;
                    var position = Session.Position;
                    StopPlayer();
                    PlayerStateChanged?.Invoke(this, EventArgs.Empty);
                    await millerPage.ShowMediaFullscreenAsync(source, isVideo, position);
                }
            }
        }

        public static async Task OpenFullscreenForFile(string filePath)
        {
            var page = Window.Current?.Content as FrameworkElement;
            while (page != null && !(page is MillerColumnsPage))
                page = VisualTreeHelper.GetParent(page) as FrameworkElement;
            if (page is MillerColumnsPage millerPage)
            {
                bool isVideo = FilePreviewService.IsVideoFile(Path.GetExtension(filePath));
                if (isVideo)
                    await millerPage.ShowMediaFullscreenAsync(new Uri(filePath), isVideo, TimeSpan.Zero);
                else
                    await millerPage.OpenFullscreenForFile(filePath, TimeSpan.Zero);
            }
        }
    }
}
