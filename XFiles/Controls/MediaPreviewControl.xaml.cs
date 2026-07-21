using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
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

        private MediaPlayer Player => MediaPlayerElementControl.MediaPlayer;
        private MediaPlaybackSession Session => Player?.PlaybackSession;

        public bool IsAudioMode => _isAudioMode;
        public string CurrentFilePath => _currentFilePath;
        public bool IsFileLoaded(string filePath) => _currentFilePath == filePath;

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
            VuMeter.AttachService(_audioLevelService);
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
            Stop();
            Log.Information("MediaPreviewControl: loading {Path}", filePath);

            _currentFilePath = filePath;
            string ext = Path.GetExtension(filePath);
            _isAudioMode = FilePreviewService.IsAudioFile(ext);
            Log.Information("MediaPreviewControl: ext={Ext} isAudio={IsAudio}", ext, _isAudioMode);

            if (_isAudioMode)
            {
                AudioInfoPanel.Visibility = Visibility.Visible;
                Log.Information("MediaPreviewControl: AudioInfoPanel set to Visible, starting ID3 load");
                _ = LoadId3TagsAsync(filePath);
            }
            else
            {
                _currentSourceUri = new Uri(filePath);
                Player.Source = MediaSource.CreateFromUri(_currentSourceUri);
            }

            _isPlaying = false;
            UpdatePlayPauseIcon();
            Visibility = Visibility.Visible;
        }

        private async Task StartAudioPlayback(string filePath)
        {
            try
            {
                VuMeter.AttachService(_audioLevelService);
                await _audioLevelService.LoadAndPlay(filePath);
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to start audio playback: {Error}", ex.Message);
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
                _audioLevelService.Stop();
                VuMeter.DetachService();
            }
            _currentFilePath = null;
        }

        public void Stop()
        {
            _progressTimer.Stop();
            if (_isAudioMode)
            {
                _audioLevelService.Stop();
                VuMeter.DetachService();
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

        public bool IsPlayerActive => _isPlaying;

        public void TogglePlayPause()
        {
            if (_isAudioMode)
            {
                if (string.IsNullOrEmpty(_currentFilePath)) return;
                if (!_audioLevelService.IsFileLoaded)
                {
                    _isPlaying = true;
                    UpdatePlayPauseIcon();
                    _progressTimer.Start();
                    PlayerStateChanged?.Invoke(this, EventArgs.Empty);
                    _ = StartAudioPlayback(_currentFilePath);
                    return;
                }
                _audioLevelService.TogglePlayPause();
                _isPlaying = _audioLevelService.IsPlaying;
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

        private async Task LoadId3TagsAsync(string filePath)
        {
            Log.Information("ID3: starting load for {Path}", filePath);
            try
            {
                var tag = await Task.Run(() => Id3Tag.ReadFromFile(filePath));
                Log.Information("ID3: tag result null={IsNull}", tag == null);

                bool hasArt = tag?.AlbumArt != null && tag.AlbumArt.Length > 0;
                if (hasArt)
                {
                    AlbumArtBorder.Visibility = Visibility.Visible;
                    DefaultArtPanel.Visibility = Visibility.Collapsed;
                    await LoadAlbumArtAsync(tag.AlbumArt);
                }
                else
                {
                    AlbumArtBorder.Visibility = Visibility.Collapsed;
                    DefaultArtPanel.Visibility = Visibility.Visible;
                }

                TitleText.Text = tag?.Title ?? Path.GetFileNameWithoutExtension(filePath);
                ArtistText.Text = tag?.Artist ?? "";
                ArtistText.Visibility = string.IsNullOrEmpty(tag?.Artist) ? Visibility.Collapsed : Visibility.Visible;
                AlbumText.Text = tag?.Album ?? "";
                AlbumText.Visibility = string.IsNullOrEmpty(tag?.Album) ? Visibility.Collapsed : Visibility.Visible;

                Log.Information("ID3: title={Title} artist={Artist} album={Album} art={HasArt} panel={PanelVis}",
                    tag?.Title, tag?.Artist, tag?.Album, hasArt, AudioInfoPanel.Visibility);
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to read ID3 tags: {Error}", ex.Message);
                AlbumArtBorder.Visibility = Visibility.Collapsed;
                DefaultArtPanel.Visibility = Visibility.Visible;
                TitleText.Text = Path.GetFileNameWithoutExtension(filePath);
                ArtistText.Visibility = Visibility.Collapsed;
                AlbumText.Visibility = Visibility.Collapsed;
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
                Log.Warning("Failed to load album art: {Error}", ex.Message);
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
                Log.Information("AudioLevelService: media opened — starting playback state");
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
                _isPlaying = false;
                UpdatePlayPauseIcon();
                _progressTimer.Stop();
                ProgressSlider.Value = 100;
            });
        }

        private async void OnAudioMediaFailed(object sender, EventArgs args)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                Log.Information("AudioLevelService media failed");
                _isPlaying = false;
                _progressTimer.Stop();
                UpdatePlayPauseIcon();
            });
        }

        private async void OnMediaPlayerOpened(Windows.Media.Playback.MediaPlayer sender, object args)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                Log.Verbose("Media opened: {Duration}", Session.NaturalDuration);
                _isPlaying = true;
                UpdatePlayPauseIcon();
                _progressTimer.Start();
                UpdateProgress();
                PlayerStateChanged?.Invoke(this, EventArgs.Empty);
            });
        }

        private async void OnMediaPlayerEnded(Windows.Media.Playback.MediaPlayer sender, object args)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                _isPlaying = false;
                UpdatePlayPauseIcon();
                _progressTimer.Stop();
                ProgressSlider.Value = 100;
            });
        }

        private async void OnMediaPlayerFailed(Windows.Media.Playback.MediaPlayer sender, Windows.Media.Playback.MediaPlayerFailedEventArgs args)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                Log.Information("Media preview failed: {Error} {HResult}", args.Error.ToString(), args.ExtendedErrorCode);
                _isPlaying = false;
                _progressTimer.Stop();
                UpdatePlayPauseIcon();
            });
        }

        private void OnProgressTimerTick(object sender, object e)
        {
            UpdateProgress();
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
                ProgressSlider.Value = (current.TotalSeconds / total.TotalSeconds) * 100;
                TimeText.Text = $"{FormatTime(current)} / {FormatTime(total)}";
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
