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
using XFiles.FileSystem;
using XFiles.Navigation;

namespace XFiles.Controls
{
    public sealed partial class MediaPreviewControl : UserControl
    {
        private bool _isPlaying;
        private DispatcherTimer _progressTimer;
        private bool _isAudioMode;
        private string _currentFilePath;
        private Uri _currentSourceUri;

        private MediaPlayer Player => MediaPlayerElementControl.MediaPlayer;
        private MediaPlaybackSession Session => Player.PlaybackSession;

        public bool IsAudioMode => _isAudioMode;
        public TimeSpan CurrentPosition => Session.Position;

        public MediaPreviewControl()
        {
            this.InitializeComponent();
            _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _progressTimer.Tick += OnProgressTimerTick;
            Player.MediaOpened += OnMediaOpened;
            Player.MediaEnded += OnMediaEnded;
            Player.MediaFailed += OnMediaFailed;
        }

        public void LoadFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            Stop();
            Log.Information("MediaPreviewControl: loading {Path}", filePath);

            _currentFilePath = filePath;
            string ext = Path.GetExtension(filePath);
            _isAudioMode = FilePreviewService.IsAudioFile(ext);

            if (_isAudioMode)
            {
                AudioInfoPanel.Visibility = Visibility.Visible;
                _ = LoadId3TagsAsync(filePath);
            }

            _currentSourceUri = new Uri(filePath);
            Player.Source = MediaSource.CreateFromUri(_currentSourceUri);
            _isPlaying = false;
            UpdatePlayPauseIcon();
            Visibility = Visibility.Visible;
        }

        public void StopPlayer()
        {
            if (_isPlaying)
            {
                Player.Pause();
                _isPlaying = false;
                _progressTimer.Stop();
                UpdatePlayPauseIcon();
                PlayerStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Stop()
        {
            _progressTimer.Stop();
            Player.Pause();
            Player.Source = null;
            _currentSourceUri = null;
            _isPlaying = false;
            _isAudioMode = false;
            UpdatePlayPauseIcon();
            ClearMetadata();
            PlayerStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler PlayerStateChanged;

        public bool IsPlayerActive => _isPlaying;

        public void TogglePlayPause()
        {
            if (_isPlaying)
            {
                Player.Pause();
                _isPlaying = false;
                _progressTimer.Stop();
            }
            else
            {
                Player.Play();
                _isPlaying = true;
                _progressTimer.Start();
            }
            UpdatePlayPauseIcon();
            PlayerStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdatePlayPauseIcon()
        {
            PlayPauseIcon.Glyph = _isPlaying ? "\uE769" : "\uE768";
        }

        private async Task LoadId3TagsAsync(string filePath)
        {
            try
            {
                var tag = await Task.Run(() => Id3Tag.ReadFromFile(filePath));

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

                Log.Information("ID3: title={Title} artist={Artist} album={Album} art={HasArt}",
                    tag?.Title, tag?.Artist, tag?.Album, hasArt);
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

        private async void OnMediaOpened(Windows.Media.Playback.MediaPlayer sender, object args)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                Log.Verbose("Media opened: {Duration}", Session.NaturalDuration);
                UpdateProgress();
            });
        }

        private async void OnMediaEnded(Windows.Media.Playback.MediaPlayer sender, object args)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                _isPlaying = false;
                UpdatePlayPauseIcon();
                _progressTimer.Stop();
                ProgressSlider.Value = 100;
            });
        }

        private async void OnMediaFailed(Windows.Media.Playback.MediaPlayer sender, Windows.Media.Playback.MediaPlayerFailedEventArgs args)
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
            var total = Session.NaturalDuration;
            if (total.TotalSeconds > 0)
            {
                var current = Session.Position;
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
            if (Player.Source != null)
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
            Player.Volume = Math.Max(0.0, Math.Min(1.0, volume));
        }

        public async Task OpenFullscreen()
        {
            if (_currentSourceUri == null) return;
            var page = VisualTreeHelper.GetParent(this) as FrameworkElement;
            while (page != null && !(page is MillerColumnsPage))
                page = VisualTreeHelper.GetParent(page) as FrameworkElement;
            if (page is MillerColumnsPage millerPage)
            {
                bool isVideo = !_isAudioMode && Session.NaturalVideoHeight > 0;
                var source = _currentSourceUri;
                var position = Session.Position;
                StopPlayer();
                PlayerStateChanged?.Invoke(this, EventArgs.Empty);
                await millerPage.ShowMediaFullscreenAsync(source, isVideo, position);
            }
        }
    }
}
