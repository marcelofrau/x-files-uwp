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
        private bool _isAudioMode;
        private string _currentFilePath;
        private Uri _currentSourceUri;
        private AudioLevelService _audioLevelService;
        private bool _isPlaying;
        private bool _isLoadingPlayback;
        private bool _hasEnded;
        private MetadataGuesser _metadataGuesser;
        private CancellationTokenSource _metadataCts;

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

        public MediaPlaybackItem CurrentPlaybackItem => _currentPlaybackItem;
        public List<SubtitleTrack> CurrentSubtitleTracks => _currentSubtitleTracks;
        public List<AudioTrackInfo> CurrentAudioTracks => _currentAudioTracks;
        public int CurrentSubtitleTrackIndex => _currentSubtitleIndex;
        public int CurrentAudioTrackIndex => _currentAudioIndex;

        public TimeSpan CurrentPosition
        {
            get
            {
                if (_isAudioMode && _audioLevelService != null)
                    return _audioLevelService.Position;
                return Session?.Position ?? TimeSpan.Zero;
            }
        }

        public MediaPreviewControl()
        {
            this.InitializeComponent();
            _audioLevelService = new AudioLevelService();
            _metadataGuesser = new MetadataGuesser();
#if AUDIO_ANALYSIS
            VuMeter.AttachService(_audioLevelService);
#endif
            _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _progressTimer.Tick += OnProgressTimerTick;
            _audioLevelService.MediaOpened += OnAudioMediaOpened;
            _audioLevelService.MediaEnded += OnAudioMediaEnded;
            _audioLevelService.MediaFailed += OnAudioMediaFailed;
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
            Log.Info("MediaPreviewControl: loading {Path}", filePath);

            _currentFilePath = filePath;
            string ext = Path.GetExtension(filePath);
            _isAudioMode = FilePreviewService.IsAudioFile(ext);
            Log.Dbg("MediaPreviewControl: ext={Ext} isAudio={IsAudio}", ext, _isAudioMode);

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

        public void LoadNextTrack(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            string ext = Path.GetExtension(filePath);
            bool newIsAudio = FilePreviewService.IsAudioFile(ext);

            Log.Dbg("MediaPreviewControl.LoadNextTrack: enter for {Path} isAudio={IsAudio} wasPlaying={WasPlaying}", filePath, newIsAudio, _isPlaying);

            if (_isAudioMode != newIsAudio)
            {
                Log.Info("MediaPreviewControl: mode switch, full load for {Path}", filePath);
                LoadFile(filePath);
                return;
            }

            Log.Info("MediaPreviewControl: swapping source for {Path}", filePath);
            _currentFilePath = filePath;
            _hasEnded = false;
            _isPlaying = false;

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
                _ = _audioLevelService.SwapSourceAsync(filePath);
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
            try
            {
#if AUDIO_ANALYSIS
                VuMeter.AttachService(_audioLevelService);
#endif
                await _audioLevelService.LoadAndPlay(filePath);
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
                _isLoadingPlayback = false;
            }
        }

		public void StopPlayer()
		{
			if (_isPlaying)
			{
				if (_isAudioMode)
					_audioLevelService.Pause();
				else
					Player.Pause();
				_isPlaying = false;
				_progressTimer.Stop();
				UpdatePlayPauseIcon();
				PlayerStateChanged?.Invoke(this, EventArgs.Empty);
			}
			if (_isAudioMode)
			{
				_audioLevelService.MediaOpened -= OnAudioMediaOpened;
				_audioLevelService.MediaEnded -= OnAudioMediaEnded;
				_audioLevelService.MediaFailed -= OnAudioMediaFailed;
				_audioLevelService.Dispose();
				_audioLevelService = null;
#if AUDIO_ANALYSIS
				VuMeter.DetachService();
#endif
			}
			_metadataCts?.Cancel();
			_metadataCts = null;
			_currentFilePath = null;
		}

		public void Stop()
		{
			_progressTimer.Stop();
			_metadataCts?.Cancel();
			_metadataCts = null;
			if (_isAudioMode)
			{
				if (_audioLevelService != null)
				{
					_audioLevelService.Stop();
#if AUDIO_ANALYSIS
					VuMeter.DetachService();
#endif
				}
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

		public void TogglePlayPause()
		{
			Log.Dbg("MediaPreviewControl.TogglePlayPause: audio={IsAudio} playing={IsPlaying} loading={IsLoading} fileLoaded={FileLoaded} file={File}",
				_isAudioMode, _isPlaying, _isLoadingPlayback, _audioLevelService?.IsFileLoaded ?? false, _currentFilePath ?? "(null)");
			if (_audioLevelService == null)
			{
				_audioLevelService = new AudioLevelService();
				_audioLevelService.MediaOpened += OnAudioMediaOpened;
				_audioLevelService.MediaEnded += OnAudioMediaEnded;
				_audioLevelService.MediaFailed += OnAudioMediaFailed;
			}
			if (_isAudioMode)
			{
                if (string.IsNullOrEmpty(_currentFilePath)) return;
                bool fileMismatch = _audioLevelService.IsFileLoaded
                    && !string.IsNullOrEmpty(_currentFilePath)
                    && _audioLevelService.CurrentFilePath != _currentFilePath;
                if (fileMismatch) _audioLevelService.Stop();
                if ((!_audioLevelService.IsFileLoaded || fileMismatch) && !_isLoadingPlayback)
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
                _audioLevelService.TogglePlayPause();
                _isPlaying = _audioLevelService.IsPlaying;
                if (!_isPlaying)
                {
                    Log.Dbg("MediaPreviewControl.TogglePlayPause: resume failed, forcing reload");
                    _audioLevelService.Stop();
                    _isLoadingPlayback = true;
                    _hasEnded = false;
                    _isPlaying = true;
                    UpdatePlayPauseIcon();
                    _progressTimer.Start();
                    PlayerStateChanged?.Invoke(this, EventArgs.Empty);
                    _ = StartAudioPlayback(_currentFilePath);
                    return;
                }
                _hasEnded = false;
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
                _audioLevelService.Stop();
#if AUDIO_ANALYSIS
                VuMeter.DetachService();
#endif
                _isPlaying = false;
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
                _audioLevelService.Stop();
#if AUDIO_ANALYSIS
                VuMeter.DetachService();
#endif
                _isPlaying = false;
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

            for (int i = 0; i < _currentPlaybackItem.TimedMetadataTracks.Count; i++)
            {
                var track = _currentPlaybackItem.TimedMetadataTracks[i];
                if (track.TimedMetadataKind == Windows.Media.Core.TimedMetadataKind.Subtitle)
                {
                    string lang = track.Language ?? "Unknown";
                    _currentSubtitleTracks.Add(new SubtitleTrack
                    {
                        Language = lang,
                        EmbeddedIndex = i,
                        IsExternal = false
                    });
                }
            }

            for (int i = 0; i < _currentPlaybackItem.AudioTracks.Count; i++)
            {
                var track = _currentPlaybackItem.AudioTracks[i];
                string lang = track.Language ?? "Unknown";
                _currentAudioTracks.Add(new AudioTrackInfo
                {
                    Language = lang,
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
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () => UpdateProgress());
        }

        private void UpdateProgress()
        {
            TimeSpan total;
            TimeSpan current;

            if (_isAudioMode && _audioLevelService != null && _audioLevelService.IsFileLoaded)
            {
                total = _audioLevelService.Duration;
                current = _audioLevelService.Position;
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
                        AudioTrackEnded?.Invoke(this, EventArgs.Empty);
                    else
                        VideoTrackEnded?.Invoke(this, EventArgs.Empty);
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
            if (_isAudioMode && _audioLevelService != null && _audioLevelService.IsFileLoaded)
            {
                var total = _audioLevelService.Duration;
                var newPos = _audioLevelService.Position + offset;
                if (newPos < TimeSpan.Zero) newPos = TimeSpan.Zero;
                if (total.TotalSeconds > 0 && newPos > total) newPos = total;
                _audioLevelService.Seek(newPos);
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
            if (_isAudioMode && _audioLevelService != null && _audioLevelService.IsFileLoaded)
            {
                var page = VisualTreeHelper.GetParent(this) as FrameworkElement;
                while (page != null && !(page is MillerColumnsPage))
                    page = VisualTreeHelper.GetParent(page) as FrameworkElement;
                if (page is MillerColumnsPage millerPage)
                {
                    var filePath = _currentFilePath;
                    var position = _audioLevelService.Position;
                    StopPlayer();
                    PlayerStateChanged?.Invoke(this, EventArgs.Empty);
                    await millerPage.OpenFullscreenForFile(filePath, position);
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
