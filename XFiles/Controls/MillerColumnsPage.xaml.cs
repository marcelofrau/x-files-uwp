using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Data.Json;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.System;
using Windows.System.Display;
using XFiles.Audio;
using XFiles.FileSystem;
using XFiles.Metadata;
using XFiles.Navigation;
using XFiles.Services;
using XFiles.Visualizers;

namespace XFiles.Controls
{

    public sealed partial class MillerColumnsPage : Page, INavigable, INotifyPropertyChanged
    {
        private readonly ColumnNavigator _navigator = new ColumnNavigator();
        private bool _updating;
        private bool _slideFromRight;
        private bool _isBatchMode;
        public bool IsBatchMode
        {
            get => _isBatchMode;
            set { _isBatchMode = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBatchMode))); }
        }
        private readonly HashSet<string> _batchSelectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static string _highlightJs;
        private static string _highlightCss;

        private InputRouter _router;

        private const int VK_LT = 0x7001;
        private const int VK_RT = 0x7002;

        private static readonly HashSet<string> PlainTextExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".txt", ".log", ".out", ".err",
                ".nfo", ".diz", ".sfv",
                ".md5", ".sha1", ".sha256", ".sha512",
                ".asc", ".hash", ".crc",
            };

        public MillerColumnsPage()
        {
            this.InitializeComponent();
            _fullscreenProgressHandler = () =>
            {
                if (VideoFullScreenPanel.Visibility == Visibility.Visible)
                {
                    var total = FsVideoSession.NaturalDuration;
                    if (total.TotalSeconds > 0)
                    {
                        var current = FsVideoSession.Position;
                        FSProgress.Value = Math.Max(0, Math.Min(100, (current.TotalSeconds / total.TotalSeconds) * 100));
                        FSTimeText.Text = $"{Formatting.FormatFsTime(current)} / {Formatting.FormatFsTime(total)}";
                    }
                }
                else if (AudioFullScreenPanel.Visibility == Visibility.Visible && AudioLevelService.Instance.IsFileLoaded)
                {
                    var total = AudioLevelService.Instance.Duration;
                    if (total.TotalSeconds > 0)
                    {
                        var current = AudioLevelService.Instance.Position;
                        FsAudioProgress.Value = Math.Max(0, Math.Min(100, (current.TotalSeconds / total.TotalSeconds) * 100));
                        FsCurrentTimeText.Text = Formatting.FormatFsTime(current);
                        FsTotalTimeText.Text = Formatting.FormatFsTime(total);

                        if (!_fsAudioEnded && current >= total - TimeSpan.FromSeconds(0.5))
                        {
                            _fsAudioEnded = true;
                            Log.Info("FsAudio: position reached end — auto-advancing");
                            NavigateAudioTrack(1);
                        }
                    }
                }
            };
            this.Unloaded += OnUnloaded;
            this.KeyDown += OnKeyDown;
            this.PointerPressed += OnPointerPressed;
            this.Loaded += OnLoaded;

            _navigator.ColumnsChanged += OnColumnsChanged;
            _navigator.PreviewChanged += OnPreviewChanged;
            _navigator.LoadingChanged += OnLoadingChanged;
            _navigator.Error += OnError;
            _navigator.PortalSetupRequired += OnPortalSetupRequired;
            DevicePortalService.CredentialsRequired += OnPortalCredentialsRequired;

            _fullscreenProgressTimer.Tick += OnFullscreenProgressTick;
            _fsHideTimer.Tick += OnFsHideTimerTick;

            _mediaLoadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _mediaLoadTimer.Tick += OnMediaLoadTimerTick;

            _previewDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PreviewDebounceMs) };
            _previewDebounceTimer.Tick += OnPreviewDebounceTick;

            PreviewCodeView.NavigationStarting += OnPreviewNavigationStarting;
            PreviewCodeView.NavigationCompleted += OnPreviewNavigationCompleted;

            MediaPreview.PlayerStateChanged += OnMediaPlayerStateChanged;
            MediaPreview.AudioTrackEnded += OnMediaPreviewAudioEnded;
            MediaPreview.VideoTrackEnded += OnMediaPreviewVideoEnded;

            VideoTrackMenuControl.SubtitleSelected += OnVideoSubtitleSelected;
            VideoTrackMenuControl.AudioTrackSelected += OnVideoAudioTrackSelected;
            var v = Package.Current.Id.Version;
            var version = $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
            VersionText.Text = $"v{version}";
            AboutVersionText.Text = $"v{version}";

            _ = _navigator.LoadRootAsync();
            RegisterInputHandlers();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Log.Dbg("MillerColumnsPage.OnUnloaded");
            _navigator.ColumnsChanged -= OnColumnsChanged;
            _navigator.PreviewChanged -= OnPreviewChanged;
            _navigator.LoadingChanged -= OnLoadingChanged;
            _navigator.Error -= OnError;
            _navigator.PortalSetupRequired -= OnPortalSetupRequired;
            DevicePortalService.CredentialsRequired -= OnPortalCredentialsRequired;
            _fullscreenProgressTimer.Tick -= OnFullscreenProgressTick;
            _fsHideTimer.Tick -= OnFsHideTimerTick;
            _mediaLoadTimer.Tick -= OnMediaLoadTimerTick;
            _previewDebounceTimer.Tick -= OnPreviewDebounceTick;
            PreviewCodeView.NavigationStarting -= OnPreviewNavigationStarting;
            PreviewCodeView.NavigationCompleted -= OnPreviewNavigationCompleted;
            MediaPreview.PlayerStateChanged -= OnMediaPlayerStateChanged;
            MediaPreview.AudioTrackEnded -= OnMediaPreviewAudioEnded;
            MediaPreview.VideoTrackEnded -= OnMediaPreviewVideoEnded;
            VideoTrackMenuControl.SubtitleSelected -= OnVideoSubtitleSelected;
            VideoTrackMenuControl.AudioTrackSelected -= OnVideoAudioTrackSelected;
            _fsOsdHideTimer.Tick -= OnFsOsdHideTick;
            _fsAudioOsdHideTimer.Tick -= OnFsAudioOsdHideTick;
            _coverArtCts?.Cancel();
            _coverArtCts?.Dispose();
            _coverArtCts = null;
            StopAllTimers();
        }

        private bool _isMediaPlayerActive;

        // Preview debounce — delays preview update during rapid scrolling
        private DispatcherTimer _previewDebounceTimer;
        private const int PreviewDebounceMs = 90;

        private long _overlayClosedTick;
        private readonly DisplayRequest _displayRequest = new DisplayRequest();
        private bool _displayActive;

        private void OnMediaPlayerStateChanged(object sender, EventArgs e)
        {
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                _isMediaPlayerActive = MediaPreview.IsPlayerActive;
                UpdateMediaPlayerFocusUI();
                UpdateDisplayRequest();
            });
        }

        private void UpdateDisplayRequest()
        {
            bool shouldKeepAlive = _isMediaPlayerActive
                || AudioFullScreenPanel.Visibility == Visibility.Visible
                || VideoFullScreenPanel.Visibility == Visibility.Visible;

            if (shouldKeepAlive && !_displayActive)
            {
                try { _displayRequest.RequestActive(); _displayActive = true; }
                catch (Exception ex) { Log.Warn("DisplayRequest failed", ex); }
            }
            else if (!shouldKeepAlive && _displayActive)
            {
                try { _displayRequest.RequestRelease(); _displayActive = false; }
                catch (Exception ex) { Log.Warn("DisplayRequest release failed", ex); }
            }
        }

        private void OnMediaPreviewAudioEnded(object sender, EventArgs e)
        {
            Log.Info("MillerColumns: {File} — audio ended event received, calling NavigatePreviewTrack(1)", MediaPreview.CurrentFilePath ?? "(null)");
            NavigatePreviewTrack(1);
        }

        private void OnMediaPreviewVideoEnded(object sender, EventArgs e)
        {
            Log.Info("MillerColumns: {File} — video ended event received, calling NavigatePreviewVideoTrack(1)", MediaPreview.CurrentFilePath ?? "(null)");
            NavigatePreviewVideoTrack(1);
        }

        private void UpdateMediaPlayerFocusUI()
        {
            bool isFullscreen = AudioFullScreenPanel.Visibility == Visibility.Visible
                             || VideoFullScreenPanel.Visibility == Visibility.Visible;

            if (_isMediaPlayerActive)
            {
                if (!isFullscreen)
                {
                    ParentColumn.Opacity = 0.3;
                    CurrentColumn.Opacity = 0.6;
                }
                ParentColumn.IsHitTestVisible = false;
                CurrentColumn.IsHitTestVisible = false;

                FooterALabel.Text = "Pause";
                FooterBLabel.Text = "Stop";
                FooterXLabel.Text = "Fullscreen";
                FooterLTLabel.Text = "-5s";
                FooterRTLabel.Text = "+5s";
                FooterLBLabel.Text = "Prev";
                FooterRBLabel.Text = "Next";
                FooterLTRT.Visibility = Visibility.Visible;
                FooterLBRB.Visibility = Visibility.Visible;
                FooterViewLabel.Visibility = MediaPreview.IsAudioMode ? Visibility.Collapsed : Visibility.Visible;
                FooterViewLabelText.Text = "Tracks";
            }
            else
            {
                ParentColumn.Opacity = 1.0;
                CurrentColumn.Opacity = 1.0;
                ParentColumn.IsHitTestVisible = true;
                CurrentColumn.IsHitTestVisible = true;

                FooterLTRT.Visibility = Visibility.Collapsed;
                FooterLBRB.Visibility = Visibility.Collapsed;
                FooterViewLabel.Visibility = Visibility.Visible;
                FooterViewLabelText.Text = "Batch";
                UpdateFooterALabelFromSelection();
                FooterBLabel.Text = "Back";
                FooterXLabel.Text = "Refresh";
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Log.Verb("MillerColumnsPage loaded — setting focus");
            CurrentList.Focus(FocusState.Programmatic);
            if (App.GamepadInput != null)
            {
                App.GamepadInput.ActiveNavigable = this;
                Log.Dbg("MillerColumnsPage: set as ActiveNavigable");
            }
            Action markOverlayClosed = () => _overlayClosedTick = Environment.TickCount;
            InputDialogControl.OnClosed = markOverlayClosed;
            PortalSetupDialogControl.OnClosed = markOverlayClosed;
            PortalCredentialsDialogControl.OnClosed = markOverlayClosed;
            AlertDialogControl.OnClosed = markOverlayClosed;
            FileActionSheetControl.OnClosed = markOverlayClosed;
            StartMenuControl.OnClosed = markOverlayClosed;
            SettingsPageControl.OnClosed = markOverlayClosed;
            ImageFullScreen.OnClosed = markOverlayClosed;
            PdfFullScreen.OnClosed = markOverlayClosed;
            VideoTrackMenuControl.OnClosed = markOverlayClosed;
            OpProgressDialog.OnClosed = markOverlayClosed;

            UpdateClipboardIndicator();
        }

        private async void OnColumnsChanged()
        {
            try { await UpdateUIAsync(); }
            catch (Exception ex) { Log.Err("OnColumnsChanged: {Ex}", ex); }
        }

        private async void OnPreviewChanged()
        {
            try { await UpdatePreviewColumnAsync(); }
            catch (Exception ex) { Log.Err("OnPreviewChanged: {Ex}", ex); }
        }

        private void OnLoadingChanged(bool isLoading)
        {
            Log.Verb("Loading state: {IsLoading}", isLoading);
            CurrentLoading.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            CurrentList.Opacity = isLoading ? 0.4 : 1.0;
        }

        private void OnError(string message)
        {
            Log.Err("MillerColumnsPage error: {Message}", args: message);
            CurrentStatus.Text = $"ERROR: {message}";
        }

        private void OnPortalSetupRequired()
        {
            Log.Info("Portal setup required — showing setup dialog");
            string status = DevicePortalService.ProbeStatus;
            if (status == "not run" && DevicePortalService.HasCredentials)
                status = "Portal not reached — check loopback exemption and re-probe.";
            PortalSetupDialogControl.Show(status);
        }

        private void OnPortalCredentialsRequired()
        {
            Log.Info("Portal credentials required (401) — showing credentials dialog");
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                () => ShowPortalCredentialsAsync("Portal credentials rejected (401) — enter again"));
        }

        private async Task ShowPortalCredentialsAsync(string title)
        {
            string prefilled = await Settings.XFilesSettings.GetPortalUserAsync();
            var result = await PortalCredentialsDialogControl.ShowAsync(title, prefilled);
            if (result == null)
            {
                Log.Info("Portal credentials dialog cancelled");
                return;
            }

            Log.Info("Portal credentials entered — saving and re-probing");
            DevicePortalService.SetCredentials(result.User, result.Password);
            try
            {
                await Settings.XFilesSettings.SetPortalCredentialsAsync(result.User, result.Password);
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to persist portal credentials: {Error}", ex.Message);
            }
            DevicePortalService.ProbeAsync(force: true);
        }

        private async Task UpdateUIAsync()
        {
            _updating = true;
            try
            {
                bool atRoot = _navigator.Parent == null;

                // Welcome panel (left) — shown at root
                WelcomePanel.Visibility = atRoot ? Visibility.Visible : Visibility.Collapsed;
                ParentHeader.Visibility = atRoot ? Visibility.Collapsed : Visibility.Visible;
                ParentList.Visibility = atRoot ? Visibility.Collapsed : Visibility.Visible;
                ParentStatus.Visibility = atRoot ? Visibility.Collapsed : Visibility.Visible;

                // Quick reference panel (right) — shown at root
                QuickRefPanel.Visibility = atRoot ? Visibility.Visible : Visibility.Collapsed;

                // Navigation breadcrumb path in header — truncate middle if too long
                string breadcrumb = _navigator.GetBreadcrumbPath();
                const int MaxPathChars = 150;
                if (breadcrumb.Length > MaxPathChars)
                {
                    // Keep drive + first 2 folders as head: "E:\tests\Music"
                    var parts = breadcrumb.Split('\\');
                    int headEnd = Math.Min(parts.Length, 3); // drive letter + 2 folders
                    string head = string.Join("\\", parts, 0, headEnd);
                    // Tail: as much of the end as fits
                    int tailLen = MaxPathChars - head.Length - 3; // 3 for "..."
                    if (tailLen > 20)
                        breadcrumb = head + "..." + breadcrumb.Substring(breadcrumb.Length - tailLen);
                    // else: too short to truncate meaningfully, leave as-is
                }
                PathText.Text = breadcrumb;

                // Parent column
                if (!atRoot)
                {
                    ParentHeader.Text = _navigator.Parent.Label ?? "";
                    BindParentList(ParentList, _navigator.Parent, _navigator.Current?.Label);
                    ParentStatus.Text = $"{_navigator.Parent.Entries.Count} items";
                }
                else
                {
                    ParentHeader.Text = "";
                    ParentList.ItemsSource = null;
                    ParentStatus.Text = "";
                }

                // Current column
                CurrentHeader.Text = _navigator.Current?.Label ?? "(Drives)";
                PortalBanner.Visibility = (_navigator.Current != null && _navigator.Current.IsPortal)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                if (_navigator.Current != null)
                {
                    BindCurrentList(_navigator.Current);
                    CurrentStatus.Text = Formatting.FormatCount(_navigator.Current.Entries);
                }

                // Footer count
                int totalCount = _navigator.Current?.Entries.Count ?? 0;
                int selectedIndex = CurrentList.SelectedIndex >= 0 ? CurrentList.SelectedIndex + 1 : 0;
                FooterItemCount.Text = totalCount > 0 ? $"{selectedIndex}/{totalCount}" : "";

                // Preview column
                await UpdatePreviewColumnAsync();
            }
            finally
            {
                _updating = false;
            }
        }







        public event PropertyChangedEventHandler PropertyChanged;
    }

}
