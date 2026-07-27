using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
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
        private static string _highlightJs;
        private static string _highlightCss;
        private static string _fontBase64;



        private const int VK_LT = 0x7001;
        private const int VK_RT = 0x7002;

        private static readonly HashSet<string> PlainTextExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".txt", ".log", ".out", ".err",
            };

        public MillerColumnsPage()
        {
            Log.Dbg("MillerColumnsPage.ctor");
            this.InitializeComponent();
            this.KeyDown += OnKeyDown;
            this.PointerPressed += OnPointerPressed;
            this.Loaded += OnLoaded;

            _navigator.ColumnsChanged += OnColumnsChanged;
            _navigator.PreviewChanged += OnPreviewChanged;
            _navigator.LoadingChanged += OnLoadingChanged;
            _navigator.Error += OnError;

            _fullscreenProgressTimer.Tick += OnFullscreenProgressTick;
            _fsHideTimer.Tick += OnFsHideTimerTick;

            _mediaLoadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
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
        }

        private bool _isMediaPlayerActive;

        // Preview debounce — delays preview update during rapid scrolling
        private DispatcherTimer _previewDebounceTimer;
        private const int PreviewDebounceMs = 180;

        private long _overlayClosedTick;
        private readonly DisplayRequest _displayRequest = new DisplayRequest();
        private bool _displayActive;

        private void OnMediaPlayerStateChanged(object sender, EventArgs e)
        {
            _isMediaPlayerActive = MediaPreview.IsPlayerActive;
            UpdateMediaPlayerFocusUI();
            UpdateDisplayRequest();
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
                FooterViewLabel.Visibility = Visibility.Collapsed;
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
            await UpdateUIAsync();
        }

        private async void OnPreviewChanged()
        {
            await UpdatePreviewColumnAsync();
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
                if (_navigator.Current != null)
                {
                    BindCurrentList(_navigator.Current);
                    CurrentStatus.Text = $"{_navigator.Current.Entries.Count} items";
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

        private async Task UpdatePreviewColumnAsync()
        {
            HideAllPreviewPanels();

            // At root: QuickRefPanel is visible, skip preview update
            if (_navigator.Parent == null)
            {
                PreviewHeader.Text = "";
                PreviewStatus.Text = "";
                return;
            }

            if (_navigator.Preview == null)
            {
                PreviewHeader.Text = "";
                PreviewStatus.Text = "";
                Log.Verb("UpdatePreviewColumn: preview is null");
                return;
            }

            Log.Verb("UpdatePreviewColumn: label={Label}, isFile={IsFile}, type={Type}",
                _navigator.Preview.Label, _navigator.Preview.IsFilePreview, _navigator.Preview.PreviewType);

            PreviewHeader.Text = _navigator.Preview.Label ?? "";

            if (!_navigator.Preview.IsFilePreview)
            {
                BindList(PreviewList, _navigator.Preview);
                PreviewStatus.Text = $"{_navigator.Preview.Entries.Count} items";
                PreviewList.Visibility = Visibility.Visible;
            }
            else
            {
                switch (_navigator.Preview.PreviewType)
                {
                    case FilePreviewType.Text:
                        PreviewStatus.Text = _navigator.Preview.PreviewIsTruncated
                            ? $"{_navigator.Preview.PreviewFileType} (truncated)"
                            : _navigator.Preview.PreviewFileType;

                        string ext = Path.GetExtension(_navigator.Preview.Label ?? "");
                        bool isPlainText = PlainTextExtensions.Contains(ext);
                        Log.Dbg("UpdatePreviewColumn: ext={Ext} isPlainText={IsPlainText} contentLen={Len}",
                            ext, isPlainText, _navigator.Preview.PreviewTextContent?.Length ?? 0);

                        if (isPlainText)
                        {
                            PreviewTextBlock.Text = _navigator.Preview.PreviewTextContent ?? "";
                            PreviewTextScroll.Visibility = Visibility.Visible;
                        }
                        else if (FilePreviewService.IsSvgFile(ext))
                        {
                            string svgHtml = BuildSvgHtml(
                                _navigator.Preview.PreviewTextContent ?? "");
                            _ = LoadHighlightHtml(svgHtml);
                            PreviewCodeView.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            string html = await BuildHighlightHtmlAsync(
                                _navigator.Preview.PreviewTextContent ?? "", ext);
                            _ = LoadHighlightHtml(html);
                            PreviewCodeView.Visibility = Visibility.Visible;
                        }
                        break;

                    case FilePreviewType.Image:
                        PreviewImage.Source = _navigator.Preview.PreviewImageSource;
                        int pw = _navigator.Preview.PreviewPixelWidth;
                        int ph = _navigator.Preview.PreviewPixelHeight;
                        int smallThreshold = 256;
                        int maxScale = 4;
                        if (pw > 0 && ph > 0 && (pw < smallThreshold || ph < smallThreshold))
                        {
                            PreviewImage.MaxWidth = Math.Min(pw * maxScale, 1024);
                            PreviewImage.MaxHeight = Math.Min(ph * maxScale, 1024);
                        }
                        else
                        {
                            PreviewImage.MaxWidth = double.PositiveInfinity;
                            PreviewImage.MaxHeight = double.PositiveInfinity;
                        }
                        PreviewStatus.Text = _navigator.Preview.PreviewFileType;
                        PreviewImagePanel.Visibility = Visibility.Visible;
                        break;

                    case FilePreviewType.Pdf:
                        PreviewImage.Source = _navigator.Preview.PreviewImageSource;
                        int pdfPw = _navigator.Preview.PreviewPixelWidth;
                        int pdfPh = _navigator.Preview.PreviewPixelHeight;
                        if (pdfPw > 0 && pdfPh > 0)
                        {
                            PreviewImage.MaxWidth = double.PositiveInfinity;
                            PreviewImage.MaxHeight = double.PositiveInfinity;
                        }
                        int pageCount = _navigator.Preview.PreviewPdfPageCount;
                        PreviewStatus.Text = pageCount > 1
                            ? $"{_navigator.Preview.PreviewFileType} — 1/{pageCount} pages"
                            : _navigator.Preview.PreviewFileType;
                        PreviewImagePanel.Visibility = Visibility.Visible;
                        break;

                    case FilePreviewType.Audio:
                        string audioPath = _navigator.Preview.PreviewFilePath;
                        Log.Dbg("UpdatePreviewColumn: media type={Type} path={Path}", _navigator.Preview.PreviewType, audioPath);
                        PreviewStatus.Text = _navigator.Preview.PreviewFileType;
                        PreviewMediaPanel.Visibility = Visibility.Visible;
                        MediaPreview.ShowPlaceholder(audioPath);
                        _pendingMediaPath = audioPath;
                        _mediaLoadTimer.Stop();
                        _mediaLoadTimer.Start();
                        break;

                    case FilePreviewType.Video:
                        string videoPath = _navigator.Preview.PreviewFilePath;
                        Log.Dbg("UpdatePreviewColumn: media type={Type} path={Path}", _navigator.Preview.PreviewType, videoPath);
                        PreviewStatus.Text = _navigator.Preview.PreviewFileType;
                        PreviewMediaPanel.Visibility = Visibility.Visible;
                        _pendingMediaPath = videoPath;
                        _mediaLoadTimer.Stop();
                        _mediaLoadTimer.Start();
                        break;

                    case FilePreviewType.Error:
                        PreviewErrorText.Text = _navigator.Preview.PreviewErrorMessage ?? "Unknown error";
                        PreviewStatus.Text = "";
                        PreviewErrorPanel.Visibility = Visibility.Visible;
                        break;

                    case FilePreviewType.Unsupported:
                        {
                            string previewPath = _navigator.Preview.PreviewFilePath ?? "";
                            bool isInsideArchive = previewPath.Contains("|");
                            string fileExt = System.IO.Path.GetExtension(previewPath);
                            bool isMedia = FilePreviewService.IsAudioFile(fileExt) || FilePreviewService.IsVideoFile(fileExt);

                            if (isInsideArchive && isMedia)
                            {
                                PreviewArchiveMediaPanel.Visibility = Visibility.Visible;
                            }
                            else
                            {
                                // 96x96 icons for preview panel
                                var archiveExts = new[] { ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz", ".tgz", ".zst" };
                                bool isArchive = archiveExts.Contains(fileExt, StringComparer.OrdinalIgnoreCase);
                                var iconFile = isArchive ? "file-archive-96.png" : "file-generic-96.png";
                                PreviewUnsupportedIcon.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(
                                    new Uri($"ms-appx:///Assets/FileTypes/{iconFile}"));

                                PreviewUnsupportedType.Text = _navigator.Preview.PreviewFileType ?? "";
                                PreviewUnsupportedSize.Text = FormatSize(_navigator.Preview.PreviewFileSize);
                                PreviewStatus.Text = "";
                                PreviewUnsupportedPanel.Visibility = Visibility.Visible;
                            }
                        }
                        break;

                    default:
                        PreviewStatus.Text = _navigator.Preview.PreviewFileType ?? "";
                        break;
                }
            }
        }

        private void HideAllPreviewPanels()
        {
            PreviewList.Visibility = Visibility.Collapsed;
            PreviewTextScroll.Visibility = Visibility.Collapsed;
            PreviewCodeView.Visibility = Visibility.Collapsed;
            PreviewImagePanel.Visibility = Visibility.Collapsed;
            PreviewMediaPanel.Visibility = Visibility.Collapsed;
            _mediaLoadTimer.Stop();
            MediaPreview.Stop();
            PreviewErrorPanel.Visibility = Visibility.Collapsed;
            PreviewUnsupportedPanel.Visibility = Visibility.Collapsed;
            PreviewArchiveMediaPanel.Visibility = Visibility.Collapsed;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        private static async Task<string> BuildHighlightHtmlAsync(string code, string extension)
        {
            string lang = GetHighlightLang(extension);
            string escaped = HtmlEncode(code);

            await EnsureHighlightAssetsLoadedAsync();

            Log.Dbg("BuildHighlightHtmlAsync: ext={Ext} lang={Lang} cssLen={CssLen} codeLen={CodeLen} jsLen={JsLen}",
                extension, lang, _highlightCss?.Length ?? 0, code?.Length ?? 0, _highlightJs?.Length ?? 0);

            return $@"<!DOCTYPE html>
<html>
<head>
<meta charset=""utf-8"">
<style>
  @font-face {{
    font-family:'Inconsolata';
    src:url(data:font/truetype;base64,{_fontBase64}) format('truetype');
    font-weight:normal; font-style:normal;
  }}
  html, body {{ margin:0; padding:0; background:#0F1318; overflow-x:auto; }}
  pre {{ margin:0; padding:12px 8px; white-space:pre; overflow-x:auto;
         font-family:'Inconsolata','Consolas','Courier New',monospace;
         font-size:12px; color:#dcdcdc; line-height:1.4;
         display:inline-block; min-width:100%; }}
  code {{ font-family:inherit; }}
</style>
<style>{_highlightCss}</style>
</head>
<body>
<pre><code class=""{lang}"">{escaped}</code></pre>
<script>{_highlightJs}</script>
<script>hljs.highlightBlock(document.querySelector('code'));</script>
</body></html>";
        }

        private static string BuildSvgHtml(string svgContent)
        {
            string b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svgContent));
            return $@"<!DOCTYPE html>
<html>
<head>
<meta charset=""utf-8"">
<style>
  body {{ margin:0; padding:12px; background:#0F1318; display:flex;
         align-items:center; justify-content:center; min-height:100vh; }}
  img {{ max-width:100%; max-height:100%; object-fit:contain; }}
</style>
</head>
<body>
<img src=""data:image/svg+xml;base64,{b64}"" />
</body></html>";
        }

        private async Task LoadHighlightHtml(string html)
        {
            try
            {
                Log.Dbg("LoadHighlightHtml: NavigateToString ({Len} chars)", html.Length);
                PreviewCodeView.NavigateToString(html);
            }
            catch (Exception ex)
            {
                Log.Err("Failed NavigateToString", ex);
            }
        }

        private static async Task EnsureHighlightAssetsLoadedAsync()
        {
            if (_highlightJs != null && _highlightCss != null && _fontBase64 != null) return;

            try
            {
                Log.Dbg("EnsureHighlightAssetsLoadedAsync: loading JS...");
                var jsFile = await StorageFile.GetFileFromApplicationUriAsync(
                    new Uri("ms-appx:///Assets/highlight.min.js"));
                _highlightJs = await FileIO.ReadTextAsync(jsFile);
                Log.Dbg("EnsureHighlightAssetsLoadedAsync: JS loaded, {Len} chars", _highlightJs.Length);

                Log.Dbg("EnsureHighlightAssetsLoadedAsync: loading CSS...");
                var cssFile = await StorageFile.GetFileFromApplicationUriAsync(
                    new Uri("ms-appx:///Assets/highlight-aco.css"));
                _highlightCss = await FileIO.ReadTextAsync(cssFile);
                Log.Dbg("EnsureHighlightAssetsLoadedAsync: CSS loaded, {Len} chars", _highlightCss.Length);

                Log.Dbg("EnsureHighlightAssetsLoadedAsync: loading font...");
                var fontFile = await StorageFile.GetFileFromApplicationUriAsync(
                    new Uri("ms-appx:///Assets/Inconsolata-Regular.ttf"));
                var fontBytes = await Task.Run(() => System.IO.File.ReadAllBytes(fontFile.Path));
                _fontBase64 = Convert.ToBase64String(fontBytes);
                Log.Dbg("EnsureHighlightAssetsLoadedAsync: font loaded, {Len} bytes, b64={B64Len}",
                    fontBytes.Length, _fontBase64.Length);
            }
            catch (Exception ex)
            {
                Log.Err("Failed to load highlight.js assets", ex);
                _highlightJs = "";
                _highlightCss = "";
            }
        }

        private static string GetHighlightLang(string extension)
        {
            if (string.IsNullOrEmpty(extension)) return "";
            string ext = extension.TrimStart('.').ToLowerInvariant();

            switch (ext)
            {
                case "js": case "mjs": case "cjs": return "javascript";
                case "ts": case "tsx": return "typescript";
                case "jsx": return "javascript";
                case "cs": return "csharp";
                case "rb": return "ruby";
                case "kt": case "kts": return "kotlin";
                case "rs": return "rust";
                case "sh": case "bash": case "zsh": case "fish": return "bash";
                case "ps1": case "psm1": case "psd1": return "powershell";
                case "yml": return "yaml";
                case "md": case "markdown": return "markdown";
                case "html": case "htm": case "xhtml": return "html";
                case "py": case "pyw": case "pyi": return "python";
                case "sql": return "sql";
                case "go": return "go";
                case "java": return "java";
                case "lua": return "lua";
                case "pl": case "pm": return "perl";
                case "swift": return "swift";
                case "dart": return "dart";
                case "r": return "r";
                case "css": return "css";
                case "scss": return "scss";
                case "less": return "less";
                case "xml": return "xml";
                case "json": case "jsonc": case "json5": return "json";
                case "tex": case "latex": return "latex";
                case "dockerfile": return "dockerfile";
                case "ini": case "cfg": case "conf": return "ini";
                case "toml": return "toml";
                case "c": case "h": return "c";
                case "cpp": case "cc": case "cxx": case "hpp": case "hxx": return "cpp";
                case "fs": case "fsx": case "fsi": return "fsharp";
                case "vb": return "vbnet";
                case "proto": return "protobuf";
                case "graphql": case "gql": return "graphql";
                default: return "";
            }
        }

        private static string HtmlEncode(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }

        private void BindList(ListView listView, ColumnState state)
        {
            var vms = state.Entries.Select(e => new EntryViewModel
            {
                Name = e.Name,
                FullPath = e.FullPath,
                IsDirectory = e.IsDirectory,
                IsDrive = e.IsDrive,
                IsArchive = e.IsArchive,
                SizeBytes = e.SizeBytes,
                ArchiveRootPath = e.ArchiveRootPath,
                ArchiveInternalPath = e.ArchiveInternalPath
            }).ToList();

            listView.ItemsSource = vms;
        }

        private void BindParentList(ListView listView, ColumnState state, string highlightName)
        {
            var vms = state.Entries.Select(e => new EntryViewModel
            {
                Name = e.Name,
                FullPath = e.FullPath,
                IsDirectory = e.IsDirectory,
                IsDrive = e.IsDrive,
                IsArchive = e.IsArchive,
                SizeBytes = e.SizeBytes,
                ArchiveRootPath = e.ArchiveRootPath,
                ArchiveInternalPath = e.ArchiveInternalPath,
                IsHighlighted = (highlightName != null && e.Name == highlightName)
            }).ToList();

            listView.ItemsSource = vms;
        }

        private void BindCurrentList(ColumnState state)
        {
            var vms = state.Entries.Select(e => new EntryViewModel
            {
                Name = e.Name,
                FullPath = e.FullPath,
                IsDirectory = e.IsDirectory,
                IsDrive = e.IsDrive,
                IsArchive = e.IsArchive,
                SizeBytes = e.SizeBytes,
                ArchiveRootPath = e.ArchiveRootPath,
                ArchiveInternalPath = e.ArchiveInternalPath
            }).ToList();

            SlideColumn(_slideFromRight);

            CurrentList.ItemsSource = vms;

            Log.Dbg("BindCurrentList: state.SelectedIndex={StateIndex}, itemCount={Count}", state.SelectedIndex, vms.Count);
            if (state.SelectedIndex >= 0 && state.SelectedIndex < CurrentList.Items.Count)
                CurrentList.SelectedIndex = state.SelectedIndex;

            CurrentList.Focus(FocusState.Programmatic);
        }

        private void SlideColumn(bool fromRight)
        {
            double offset = 80;
            double startX = fromRight ? offset : -offset;
            ParentColumnSlide.X = startX;
            CurrentColumnSlide.X = startX;
            PreviewColumnSlide.X = startX;

            var sb = new Windows.UI.Xaml.Media.Animation.Storyboard();
            var dur = new Windows.UI.Xaml.Duration(TimeSpan.FromMilliseconds(180));
            var ease = new Windows.UI.Xaml.Media.Animation.CubicEase
            {
                EasingMode = Windows.UI.Xaml.Media.Animation.EasingMode.EaseOut
            };

            foreach (var target in new[] { ParentColumnSlide, CurrentColumnSlide, PreviewColumnSlide })
            {
                var anim = new Windows.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    To = 0,
                    Duration = dur,
                    EasingFunction = ease
                };
                Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(anim, target);
                Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(anim, "X");
                sb.Children.Add(anim);
            }

            sb.Begin();
        }

        private void CurrentList_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.GamepadLeftTrigger:
                case Windows.System.VirtualKey.GamepadRightTrigger:
                    e.Handled = true;
                    break;
            }
        }

        private void CurrentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var items = _navigator.Current?.Entries;
            string itemName = (items != null && CurrentList.SelectedIndex >= 0 && CurrentList.SelectedIndex < items.Count)
                ? items[CurrentList.SelectedIndex].Name : "(none)";
            Log.Dbg("SelectionChanged: index={Index} item=\"{Item}\" count={Count} updating={Updating}",
                CurrentList.SelectedIndex, itemName, items?.Count ?? 0, _updating);

            if (_updating) return;
            if (CurrentList.SelectedIndex >= 0 && _navigator.Current != null)
            {
                _navigator.Current.SelectedIndex = CurrentList.SelectedIndex;

                if (!_isMediaPlayerActive)
                {
                    var selected = CurrentList.SelectedItem as EntryViewModel;

                    // At root: keep debounce for HDD spin-up, but don't update visual elements
                    // (PreviewHeader/PreviewStatus would bleed through the semi-transparent QuickRefPanel)
                    if (_navigator.Parent != null)
                    {
                        // Instant loading feedback — clear stale preview immediately
                        HideAllPreviewPanels();
                        PreviewHeader.Text = selected?.Name ?? "";
                        PreviewStatus.Text = "Loading...";
                    }

                    // Debounce preview update — skip if scrolling rapidly
                    _previewDebounceTimer.Stop();
                    _previewDebounceTimer.Start();
                }
            }

            // Update footer count
            int totalCount = _navigator.Current?.Entries.Count ?? 0;
            int selectedIndex = CurrentList.SelectedIndex >= 0 ? CurrentList.SelectedIndex + 1 : 0;
            FooterItemCount.Text = totalCount > 0 ? $"{selectedIndex}/{totalCount}" : "";

            // Update A button label based on selected item type
            UpdateFooterALabelFromSelection();
        }

        private void OnPreviewDebounceTick(object sender, object e)
        {
            _previewDebounceTimer.Stop();
            _ = _navigator.OnSelectionChangedAsync();
        }

        private void UpdateFooterALabel(string label)
        {
            FooterALabel.Text = label;
        }

        private void UpdateFooterALabelFromSelection()
        {
            var selected = CurrentList.SelectedItem as EntryViewModel;
            if (selected == null || FileActionSheetControl.IsOpen)
            {
                UpdateFooterALabel("Enter");
                FooterXLabel.Text = "Refresh";
                return;
            }
            if (selected.IsDirectory || selected.IsArchive)
            {
                UpdateFooterALabel("Open");
                FooterXLabel.Text = "Refresh";
                return;
            }
            string ext = System.IO.Path.GetExtension(selected.Name);
            if (FilePreviewService.IsImageFile(ext) && !FilePreviewService.IsSvgFile(ext)
                || FilePreviewService.IsPdfFile(ext))
            {
                UpdateFooterALabel("Open");
                FooterXLabel.Text = "Refresh";
                return;
            }
            if (FilePreviewService.IsMediaFile(ext))
            {
                UpdateFooterALabel("Play");
                FooterXLabel.Text = "Fullscreen";
                return;
            }
            UpdateFooterALabel("Menu");
            FooterXLabel.Text = "Refresh";
        }

        private void UpdateClipboardIndicator()
        {
            if (ClipboardState.HasItems)
            {
                var count = ClipboardState.Count;
                var item = count == 1 ? "1 item" : $"{count} items";
                FooterClipboardIndicator.Text = $"\U0001F4CB Copied: {item}";
                FooterClipboardIndicator.Visibility = Visibility.Visible;
            }
            else
            {
                FooterClipboardIndicator.Text = "";
                FooterClipboardIndicator.Visibility = Visibility.Collapsed;
            }
            UpdateFooterXLabel();
        }

        private void UpdateFooterXLabel()
        {
            FooterXLabel.Text = ClipboardState.HasItems ? "Paste" : "Refresh";
        }

        // --- Input handling ---

        private void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            // GamepadInputService handles all gamepad input via polling.
            // Mark gamepad navigation keys as handled to suppress XAML's
            // built-in XY focus navigation sound (the "click" on DPad/A).
            switch (e.Key)
            {
                case Windows.System.VirtualKey.GamepadDPadUp:
                case Windows.System.VirtualKey.GamepadDPadDown:
                case Windows.System.VirtualKey.GamepadDPadLeft:
                case Windows.System.VirtualKey.GamepadDPadRight:
                case Windows.System.VirtualKey.GamepadA:
                    e.Handled = true;
                    break;
            }
        }

        private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var props = e.GetCurrentPoint(this).Properties;
            if (props.IsRightButtonPressed)
            {
                e.Handled = true;
                OnBack();
            }
        }

        private void OnPreviewNavigationStarting(WebView sender, WebViewNavigationStartingEventArgs args)
        {
            Log.Dbg("OnPreviewNavigationStarting: uri={Uri}", args.Uri?.ToString() ?? "(null)");
        }

        private void OnPreviewNavigationCompleted(WebView sender, WebViewNavigationCompletedEventArgs args)
        {
            Log.Dbg("OnPreviewNavigationCompleted: isSuccess={IsSuccess}", args.IsSuccess);
        }

        public bool IsMediaFullscreen => ImageFullScreen.IsOpen || PdfFullScreen.IsOpen
            || TextEditorOverlayControl.IsOpen
            || VideoFullScreenPanel.Visibility == Visibility.Visible
            || AudioFullScreenPanel.Visibility == Visibility.Visible;

        public bool IsMediaPlayerActive => _isMediaPlayerActive;

        // --- INavigable ---

        public void OnDPadUp(bool isRepeat = false)
        {
            if (TextEditorOverlayControl.IsOpen) { TextEditorOverlayControl.HandleDPadUp(); return; }
            if (VideoTrackMenuControl.IsOpen) { VideoTrackMenuControl.HandleButton(Windows.System.VirtualKey.GamepadDPadUp); return; }
            if (FolderBrowserDialogControl.IsOpen) { FolderBrowserDialogControl.HandleDPad(Windows.System.VirtualKey.GamepadDPadUp); return; }
            if (IsAnyFullscreen) { Log.Dbg("OnDPadUp: blocked by fullscreen (repeat={R})", isRepeat); return; }
            if (IsAnyOverlayVisible) { Log.Dbg("OnDPadUp: blocked by overlay (repeat={R})", isRepeat); return; }
            if (StartMenuControl.IsOpen) { StartMenuControl.ForwardDPad(Windows.System.VirtualKey.Up); return; }
            if (FileActionSheetControl.IsOpen) { FileActionSheetControl.ForwardDPad(Windows.System.VirtualKey.Up); return; }
            if (SettingsPageControl.IsVisible) { SettingsPageControl.HandleDPad(Windows.System.VirtualKey.Up); return; }
            if (LogsPageControl.IsVisible) { LogsPageControl.HandleDPad(Windows.System.VirtualKey.Up); return; }
            if (ShareDialogControl.IsVisible) { ShareDialogControl.HandleDPad(Windows.System.VirtualKey.Up); return; }

            if (_isMediaPlayerActive) { MediaPreview.StopPlayer(); UpdateMediaPlayerFocusUI(); }

            var before = CurrentList.SelectedIndex;
            var entries = _navigator.Current?.Entries;
            int count = entries?.Count ?? 0;
            string beforeName = (entries != null && before >= 0 && before < count) ? entries[before].Name : "(none)";

            if (count > 0 && CurrentList.SelectedIndex <= 0)
                CurrentList.SelectedIndex = count - 1;
            else if (count > 0)
                CurrentList.SelectedIndex--;

            CurrentList.ScrollIntoView(CurrentList.SelectedItem);
            string afterName = (entries != null && CurrentList.SelectedIndex >= 0 && CurrentList.SelectedIndex < count) ? entries[CurrentList.SelectedIndex].Name : "(none)";
            Log.Verb("OnDPadUp: {Before}→{After} \"{BeforeName}\"→\"{AfterName}\" repeat={R}", before, CurrentList.SelectedIndex, beforeName, afterName, isRepeat);
        }

        public void OnDPadDown(bool isRepeat = false)
        {
            if (TextEditorOverlayControl.IsOpen) { TextEditorOverlayControl.HandleDPadDown(); return; }
            if (VideoTrackMenuControl.IsOpen) { VideoTrackMenuControl.HandleButton(Windows.System.VirtualKey.GamepadDPadDown); return; }
            if (FolderBrowserDialogControl.IsOpen) { FolderBrowserDialogControl.HandleDPad(Windows.System.VirtualKey.GamepadDPadDown); return; }
            if (IsAnyFullscreen) { Log.Dbg("OnDPadDown: blocked by fullscreen (repeat={R})", isRepeat); return; }
            if (IsAnyOverlayVisible) { Log.Dbg("OnDPadDown: blocked by overlay (repeat={R})", isRepeat); return; }
            if (StartMenuControl.IsOpen) { StartMenuControl.ForwardDPad(Windows.System.VirtualKey.Down); return; }
            if (FileActionSheetControl.IsOpen) { FileActionSheetControl.ForwardDPad(Windows.System.VirtualKey.Down); return; }
            if (SettingsPageControl.IsVisible) { SettingsPageControl.HandleDPad(Windows.System.VirtualKey.Down); return; }
            if (LogsPageControl.IsVisible) { LogsPageControl.HandleDPad(Windows.System.VirtualKey.Down); return; }
            if (ShareDialogControl.IsVisible) { ShareDialogControl.HandleDPad(Windows.System.VirtualKey.Down); return; }

            if (_isMediaPlayerActive) { MediaPreview.StopPlayer(); UpdateMediaPlayerFocusUI(); }

            var before = CurrentList.SelectedIndex;
            var entries = _navigator.Current?.Entries;
            int count = entries?.Count ?? 0;
            string beforeName = (entries != null && before >= 0 && before < count) ? entries[before].Name : "(none)";

            if (count > 0 && CurrentList.SelectedIndex >= count - 1)
                CurrentList.SelectedIndex = 0;
            else if (count > 0)
                CurrentList.SelectedIndex++;

            CurrentList.ScrollIntoView(CurrentList.SelectedItem);
            string afterName = (entries != null && CurrentList.SelectedIndex >= 0 && CurrentList.SelectedIndex < count) ? entries[CurrentList.SelectedIndex].Name : "(none)";
            Log.Verb("OnDPadDown: {Before}→{After} \"{BeforeName}\"→\"{AfterName}\" repeat={R}", before, CurrentList.SelectedIndex, beforeName, afterName, isRepeat);
        }

        public void OnDPadLeft()
        {
            if (TextEditorOverlayControl.IsOpen) { TextEditorOverlayControl.HandleDPadLeft(); return; }
            if (VideoTrackMenuControl.IsOpen) { VideoTrackMenuControl.HandleButton(Windows.System.VirtualKey.GamepadDPadLeft); return; }
            if (ImageFullScreen.IsOpen) return;
            if (PdfFullScreen.IsOpen) return;
            if (AudioFullScreenPanel.Visibility == Visibility.Visible) return;
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { HandleContinuousSeek(-5); return; }
            if (IsAnyOverlayVisible) return;
            if (StartMenuControl.IsOpen) return;
            if (FileActionSheetControl.IsOpen) return;
            if (LogsPageControl.IsVisible) return;
            if (ShareDialogControl.IsVisible) return;
            if (_isMediaPlayerActive) return;
            _slideFromRight = false;
            _ = _navigator.DrillOutAsync();
        }

        public void OnDPadRight()
        {
            if (TextEditorOverlayControl.IsOpen) { TextEditorOverlayControl.HandleDPadRight(); return; }
            if (VideoTrackMenuControl.IsOpen) { VideoTrackMenuControl.HandleButton(Windows.System.VirtualKey.GamepadDPadRight); return; }
            if (ImageFullScreen.IsOpen) return;
            if (PdfFullScreen.IsOpen) return;
            if (AudioFullScreenPanel.Visibility == Visibility.Visible) return;
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { HandleContinuousSeek(5); return; }
            if (IsAnyOverlayVisible) return;
            if (StartMenuControl.IsOpen) return;
            if (FileActionSheetControl.IsOpen) return;
            if (LogsPageControl.IsVisible) return;
            if (ShareDialogControl.IsVisible) return;
            if (_isMediaPlayerActive) return;
            // ".." always means drill-out (go up)
            var sel = CurrentList.SelectedItem as EntryViewModel;
            if (sel != null && sel.Name == "..")
            {
                Log.Info("OnDPadRight: '..' selected → DrillOutAsync");
                _ = _navigator.DrillOutAsync();
                return;
            }
            _slideFromRight = true;
            _ = _navigator.DrillInAsync();
        }

        public void OnConfirm()
        {
            if (TextEditorOverlayControl.IsOpen) { TextEditorOverlayControl.HandleButton(Windows.System.VirtualKey.GamepadA); return; }
            if (ErrorOverlay.Visibility == Visibility.Visible) { Log.Info("OnConfirm: blocked by ErrorOverlay"); return; }
            if (InputDialogControl.Visibility == Visibility.Visible) { Log.Dbg("OnConfirm: → InputDialog"); InputDialogControl.HandleButton(Windows.System.VirtualKey.GamepadA); return; }
            if (AlertDialogControl.Visibility == Visibility.Visible) { Log.Dbg("OnConfirm: → ConfirmDialog"); AlertDialogControl.HandleButton(Windows.System.VirtualKey.GamepadA); return; }
            if (OverwriteDialogControl.IsDialogVisible) { Log.Dbg("OnConfirm: → OverwriteDialog"); OverwriteDialogControl.HandleButton(Windows.System.VirtualKey.GamepadA); return; }
            if (FileOperationConfirmDialogControl.IsDialogVisible) { Log.Dbg("OnConfirm: → FileOperationConfirmDialog"); FileOperationConfirmDialogControl.HandleButton(Windows.System.VirtualKey.GamepadA); return; }
            if (FolderBrowserDialogControl.IsOpen) { Log.Dbg("OnConfirm: → FolderBrowserDialog"); FolderBrowserDialogControl.HandleButton(Windows.System.VirtualKey.GamepadA); return; }
            if (IsAnyOverlayVisible) { Log.Dbg("OnConfirm: blocked by overlay"); return; }
            if (StartMenuControl.IsOpen) { Log.Dbg("OnConfirm: → StartMenu"); StartMenuControl.ForwardDPad(Windows.System.VirtualKey.GamepadA); return; }
            if (SettingsPageControl.IsVisible) { Log.Dbg("OnConfirm: → Settings"); SettingsPageControl.HandleDPad(Windows.System.VirtualKey.GamepadA); return; }
            if (ShareDialogControl.IsVisible) { Log.Dbg("OnConfirm: → ShareDialog"); ShareDialogControl.HandleDPad(Windows.System.VirtualKey.GamepadA); return; }
            if (LogsPageControl.IsVisible) { Log.Dbg("OnConfirm: → LogsPage close"); LogsPageControl.HandleDPad(Windows.System.VirtualKey.GamepadB); return; }
            if (ImageFullScreen.IsOpen) { Log.Info("OnConfirm: blocked by ImageFullScreen"); return; }
            if (PdfFullScreen.IsOpen) { Log.Info("OnConfirm: blocked by PdfFullScreen"); return; }
            if (VideoTrackMenuControl.IsOpen) { Log.Dbg("OnConfirm: → VideoTrackMenu"); VideoTrackMenuControl.HandleButton(Windows.System.VirtualKey.GamepadA); return; }
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { Log.Dbg("OnConfirm: → FsVideoInput"); OnFsVideoInput(); return; }
            if (AudioFullScreenPanel.Visibility == Visibility.Visible) { Log.Info("OnConfirm: → toggle audio play/pause"); ToggleAudioFullscreenPlayPause(); return; }
            if (FileActionSheetControl.IsOpen) { Log.Dbg("OnConfirm: → FileActionSheet"); FileActionSheetControl.ForwardDPad(Windows.System.VirtualKey.GamepadA); return; }
            if (_isMediaPlayerActive)
            {
                Log.Info("OnConfirm: → media player button");
                MediaPreview.HandleButton(Windows.System.VirtualKey.GamepadA);
                UpdateMediaPlayerFocusUI();
                return;
            }
            if (_navigator.Current == null) return;

            var selected = CurrentList.SelectedItem as EntryViewModel;
            if (selected == null)
            {
                _slideFromRight = true;
                _ = _navigator.DrillInAsync();
                return;
            }

            // ".." always means drill-out (go up)
            if (selected.Name == "..")
            {
                Log.Info("OnConfirm: '..' selected → DrillOutAsync");
                _ = _navigator.DrillOutAsync();
                return;
            }

            if (selected.IsDirectory || selected.IsArchive)
            {
                _slideFromRight = true;
                _ = _navigator.DrillInAsync();
            }
            else
            {
                string ext = System.IO.Path.GetExtension(selected.Name);
                if (FilePreviewService.IsImageFile(ext) && !FilePreviewService.IsSvgFile(ext))
                {
                    Log.Verb("OnConfirm: image selected — opening fullscreen");
                    ImageFullScreen.Show(_navigator.Preview?.PreviewImageSource);
                }
                else if (FilePreviewService.IsPdfFile(ext))
                {
                    Log.Verb("OnConfirm: PDF selected — opening fullscreen");
                    var preview = _navigator.Preview;
                    if (preview != null)
                    {
                        _ = PdfFullScreen.ShowAsync(
                            selected.FullPath, preview.PreviewPdfPageCount, 0);
                    }
                }
                else if (FilePreviewService.IsAudioFile(ext))
                {
                    if (selected.SizeBytes == 0)
                    {
                        Log.Warn("OnConfirm: empty audio file, blocking play");
                        _ = AlertDialogControl.ShowAsync($"\"{selected.Name}\" is empty (0 bytes).", AlertType.Error);
                        return;
                    }
                    Log.Verb("OnConfirm: audio file — toggling play/pause");
                    _mediaLoadTimer.Stop();
                    _pendingMediaPath = null;
                    if (_isMediaPlayerActive)
                    {
                        MediaPreview.TogglePlayPause();
                    }
                    else if (MediaPreview.IsFileLoaded(selected.FullPath))
                    {
                        MediaPreview.TogglePlayPause();
                        UpdateMediaPlayerFocusUI();
                    }
                    else
                    {
                        MediaPreview.LoadFile(selected.FullPath);
                        MediaPreview.TogglePlayPause();
                        UpdateMediaPlayerFocusUI();
                    }
                }
                else if (FilePreviewService.IsVideoFile(ext))
                {
                    if (selected.SizeBytes == 0)
                    {
                        Log.Warn("OnConfirm: empty video file, blocking play");
                        _ = AlertDialogControl.ShowAsync($"\"{selected.Name}\" is empty (0 bytes).", AlertType.Error);
                        return;
                    }
                    Log.Verb("OnConfirm: video file — toggling play/pause");
                    _mediaLoadTimer.Stop();
                    _pendingMediaPath = null;
                    if (_isMediaPlayerActive)
                    {
                        MediaPreview.TogglePlayPause();
                    }
                    else if (MediaPreview.IsFileLoaded(selected.FullPath))
                    {
                        MediaPreview.TogglePlayPause();
                        UpdateMediaPlayerFocusUI();
                    }
                    else
                    {
                        MediaPreview.LoadFile(selected.FullPath);
                        MediaPreview.TogglePlayPause();
                        UpdateMediaPlayerFocusUI();
                    }
                }
                else
                {
                    Log.Verb("OnConfirm: file selected — showing FileActionSheet");
                    _ = ShowFileActionSheetAsync();
                }
            }
        }

        public void OnBack()
        {
            // Skip if an overlay just closed this tick (XAML Escape closed dialog, same B press arrives here)
            if (Environment.TickCount - _overlayClosedTick < 100)
            {
                Log.Info("OnBack: skipped — overlay just closed");
                return;
            }

            if (TextEditorOverlayControl.IsOpen)
            {
                Log.Dbg("OnBack: → TextEditorOverlay");
                TextEditorOverlayControl.HandleButton(Windows.System.VirtualKey.GamepadB);
                return;
            }
            if (ErrorOverlay.Visibility == Visibility.Visible) { Log.Dbg("OnBack: → HideError"); HideError(); return; }
            if (AboutOverlay.Visibility == Visibility.Visible) { Log.Dbg("OnBack: → HideAbout"); HideAbout(); return; }
            if (PlaceholderOverlay.Visibility == Visibility.Visible) { Log.Dbg("OnBack: → HidePlaceholder"); HidePlaceholder(); return; }
            if (InputDialogControl.Visibility == Visibility.Visible) { Log.Dbg("OnBack: → InputDialog cancel"); InputDialogControl.HandleButton(Windows.System.VirtualKey.GamepadB); return; }
            if (AlertDialogControl.Visibility == Visibility.Visible) { Log.Dbg("OnBack: → ConfirmDialog cancel"); AlertDialogControl.HandleButton(Windows.System.VirtualKey.GamepadB); return; }
            if (OverwriteDialogControl.IsDialogVisible) { Log.Dbg("OnBack: → OverwriteDialog skip"); OverwriteDialogControl.HandleButton(Windows.System.VirtualKey.GamepadB); return; }
            if (FileOperationConfirmDialogControl.IsDialogVisible) { Log.Dbg("OnBack: → FileOperationConfirmDialog cancel"); FileOperationConfirmDialogControl.HandleButton(Windows.System.VirtualKey.GamepadB); return; }
            if (FolderBrowserDialogControl.IsOpen) { Log.Dbg("OnBack: → FolderBrowserDialog cancel"); FolderBrowserDialogControl.HandleButton(Windows.System.VirtualKey.GamepadB); return; }
            if (IsAnyOverlayVisible) { Log.Dbg("OnBack: blocked by overlay"); return; }
            if (StartMenuControl.IsOpen) { Log.Dbg("OnBack: → StartMenu"); StartMenuControl.ForwardDPad(Windows.System.VirtualKey.GamepadB); return; }
            if (SettingsPageControl.IsVisible) { Log.Dbg("OnBack: → Settings close"); SettingsPageControl.HandleDPad(Windows.System.VirtualKey.GamepadB); return; }
            if (ShareDialogControl.IsVisible) { Log.Dbg("OnBack: → ShareDialog close"); ShareDialogControl.HandleDPad(Windows.System.VirtualKey.GamepadB); return; }
            if (LogsPageControl.IsVisible) { Log.Dbg("OnBack: → LogsPage close"); LogsPageControl.HandleDPad(Windows.System.VirtualKey.GamepadB); return; }
            if (ImageFullScreen.IsOpen) { Log.Info("OnBack: → ImageFullScreen close"); ImageFullScreen.HandleButton(Windows.System.VirtualKey.GamepadB); UpdateFooterALabelFromSelection(); return; }
            if (PdfFullScreen.IsOpen) { Log.Info("OnBack: → PdfFullScreen close"); PdfFullScreen.HandleButton(Windows.System.VirtualKey.GamepadB); UpdateFooterALabelFromSelection(); return; }
            if (VideoTrackMenuControl.IsOpen) { Log.Info("OnBack: → VideoTrackMenu close"); VideoTrackMenuControl.HandleButton(Windows.System.VirtualKey.GamepadB); return; }
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { Log.Info("OnBack: → CloseVideoFullScreen"); CloseVideoFullScreen(); UpdateFooterALabelFromSelection(); return; }
            if (AudioFullScreenPanel.Visibility == Visibility.Visible) { Log.Info("OnBack: → CloseAudioFullscreen"); CloseAudioFullscreen(); UpdateMediaPlayerFocusUI(); return; }
            if (FileActionSheetControl.IsOpen) { Log.Dbg("OnBack: → FileActionSheet cancel"); FileActionSheetControl.ForwardDPad(Windows.System.VirtualKey.GamepadB); return; }
            if (OpProgressDialog.IsOpen) { Log.Dbg("OnBack: → OpProgressDialog cancel"); OpProgressDialog.Cancel(); return; }
            if (_isMediaPlayerActive)
            {
                Log.Info("OnBack: → StopPlayer");
                MediaPreview.StopPlayer();
                UpdateMediaPlayerFocusUI();
                return;
            }

            // B button → go to parent directory
            Log.Info("OnBack: → DrillOutAsync");
            _slideFromRight = false;
            _ = _navigator.DrillOutAsync();
        }

        public void OnContextMenu()
        {
            if (TextEditorOverlayControl.IsOpen) { TextEditorOverlayControl.HandleButton(Windows.System.VirtualKey.GamepadY); return; }
            if (IsAnyFullscreen) return;
            if (ErrorOverlay.Visibility == Visibility.Visible) return;
            if (IsAnyOverlayVisible) return;
            if (StartMenuControl.IsOpen) return;
            if (FileActionSheetControl.IsOpen) return;
            if (LogsPageControl.IsVisible) { LogsPageControl.HandleDPad(Windows.System.VirtualKey.GamepadY); return; }
            if (ShareDialogControl.IsVisible) return;
            if (_isMediaPlayerActive) return;
            Log.Verb("MillerColumnsPage.OnContextMenu — showing FileActionSheet");
            _ = ShowFileActionSheetAsync();
        }

        public void OnRefresh()
        {
            if (TextEditorOverlayControl.IsOpen) { TextEditorOverlayControl.HandleButton(Windows.System.VirtualKey.GamepadX); return; }
            if (IsAnyFullscreen) return;
            if (FileActionSheetControl.IsOpen) return;
            if (StartMenuControl.IsOpen) return;
            if (ErrorOverlay.Visibility == Visibility.Visible) return;
            if (IsAnyOverlayVisible) return;
            if (LogsPageControl.IsVisible) return;
            if (ShareDialogControl.IsVisible) return;

            var selected = CurrentList.SelectedItem as EntryViewModel;
            if (selected != null)
            {
                string ext = System.IO.Path.GetExtension(selected.Name);

                if (FilePreviewService.IsVideoFile(ext))
                {
                    Log.Info("OnRefresh: video file → fullscreen");
                    var pos = (_isMediaPlayerActive && !MediaPreview.IsAudioMode)
                        ? MediaPreview.CurrentPosition
                        : TimeSpan.Zero;
                    if (_isMediaPlayerActive) { MediaPreview.StopPlayer(); UpdateMediaPlayerFocusUI(); }
                    _ = ShowMediaFullscreenAsync(new Uri(selected.FullPath), true, pos);
                    return;
                }

                if (FilePreviewService.IsAudioFile(ext))
                {
                    Log.Info("OnRefresh: audio file → fullscreen");
                    var pos = (_isMediaPlayerActive && MediaPreview.IsAudioMode)
                        ? MediaPreview.CurrentPosition
                        : TimeSpan.Zero;
                    if (_isMediaPlayerActive) { MediaPreview.StopPlayer(); UpdateMediaPlayerFocusUI(); }
                    OpenAudioFullscreen(selected.FullPath, pos);
                    return;
                }
            }

            Log.Info("OnRefresh: refreshing current directory");
            FooterSpinner.IsActive = true;
            _ = _navigator.RefreshCurrentAsync().ContinueWith(t =>
            {
                Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                    () => FooterSpinner.IsActive = false);
            });
        }

        public void OnPaste()
        {
            if (TextEditorOverlayControl.IsOpen) { TextEditorOverlayControl.HandleButton(Windows.System.VirtualKey.GamepadX); return; }
            if (IsAnyFullscreen) return;
            if (FileActionSheetControl.IsOpen) return;
            if (StartMenuControl.IsOpen) return;
            if (IsAnyOverlayVisible) return;
            if (LogsPageControl.IsVisible) return;
            if (ShareDialogControl.IsVisible) return;
            if (!FileSystem.ClipboardState.HasItems) return;

            _ = HandlePasteAsync();
        }

        public void OnSettings()
        {
            if (TextEditorOverlayControl.IsOpen) return;
            if (ErrorOverlay.Visibility == Visibility.Visible) return;
            if (IsAnyOverlayVisible) return;
            if (StartMenuControl.IsOpen) { StartMenuControl.ForwardDPad(Windows.System.VirtualKey.GamepadA); return; }
            if (FileActionSheetControl.IsOpen) return;
            if (ImageFullScreen.IsOpen) return;
            if (PdfFullScreen.IsOpen) return;
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) return;
            if (_isMediaPlayerActive) return;
            _ = ShowStartMenuAsync();
        }

        private async System.Threading.Tasks.Task ShowStartMenuAsync()
        {
            Log.Info("OnSettings — showing start menu");
            var result = await StartMenuControl.ShowAsync();
            if (result == null) return;

            switch (result.Value)
            {
                case StartMenuItem.Settings:
                    Log.Dbg("Start menu: Settings selected");
                    var cacheCleared = await SettingsPageControl.ShowAsync();
                    if (cacheCleared)
                    {
                        Log.Dbg("Settings: cache cleared, navigating to root");
                        await _navigator.LoadRootAsync();
                        CurrentList.SelectedIndex = 0;
                        CurrentList.Focus(Windows.UI.Xaml.FocusState.Programmatic);
                    }
                    break;
                case StartMenuItem.About:
                    Log.Dbg("Start menu: About selected");
                    ShowAbout();
                    break;
                case StartMenuItem.ViewLogs:
                    Log.Dbg("Start menu: View Logs selected");
                    ShowLogs();
                    break;
                case StartMenuItem.CloseApplication:
                    Log.Info("Start menu: Close Application selected");
                    Windows.UI.Xaml.Application.Current.Exit();
                    break;
            }
        }

        private void ShowLogs()
        {
            Log.Info("ShowLogs: configuring callbacks and calling Show()");
            LogsPageControl.OnClosed = () =>
            {
                Log.Dbg("ShowLogs: LogsPage closed");
                _overlayClosedTick = Environment.TickCount;
            };
            LogsPageControl.OnShareRequested = (url) =>
            {
                Log.Dbg("ShowLogs: share requested, opening ShareDialog");
                ShareDialogControl.Show(url);
            };
            LogsPageControl.Show();
            Log.Info("ShowLogs: Show() returned, IsVisible={V}", LogsPageControl.IsVisible);
        }

        private void ShowPlaceholder(string title, string subtitle, string body)
        {
            PlaceholderTitleText.Text = title;
            PlaceholderSubtitleText.Text = subtitle;
            PlaceholderBodyText.Text = body;
            PlaceholderOverlay.Visibility = Visibility.Visible;
        }

        private void HidePlaceholder()
        {
            PlaceholderOverlay.Visibility = Visibility.Collapsed;
        }

        private void ShowAbout()
        {
            Log.Dbg("Showing About overlay");
            AboutOverlay.Visibility = Visibility.Visible;
        }

        private void HideAbout()
        {
            AboutOverlay.Visibility = Visibility.Collapsed;
        }

        private bool IsAnyOverlayVisible =>
            PlaceholderOverlay.Visibility == Visibility.Visible
            || AboutOverlay.Visibility == Visibility.Visible
            || InputDialogControl.Visibility == Visibility.Visible
            || AlertDialogControl.Visibility == Visibility.Visible
            || OverwriteDialogControl.IsDialogVisible
            || FileOperationConfirmDialogControl.IsDialogVisible
            || FolderBrowserDialogControl.IsOpen
            || OpProgressDialog.IsOpen;

        private bool IsAnyFullscreen =>
            ImageFullScreen.IsOpen || PdfFullScreen.IsOpen
            || VideoFullScreenPanel.Visibility == Visibility.Visible
            || AudioFullScreenPanel.Visibility == Visibility.Visible;

        public void OnPageUp()
        {
            if (ImageFullScreen.IsOpen) { ImageFullScreen.HandleButton((Windows.System.VirtualKey)VK_LT); return; }
            if (PdfFullScreen.IsOpen) return;
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { HandleContinuousSeek(-5); return; }
            if (_isMediaPlayerActive) { MediaPreview.Seek(TimeSpan.FromSeconds(-5)); return; }
            var before = CurrentList.SelectedIndex;
            if (CurrentList.SelectedIndex > 0)
                CurrentList.SelectedIndex = Math.Max(0, CurrentList.SelectedIndex - 8);
            CurrentList.ScrollIntoView(CurrentList.SelectedItem);
            Log.Dbg("OnPageUp: before={Before} after={After}", before, CurrentList.SelectedIndex);
        }

        public void OnPageDown()
        {
            if (ImageFullScreen.IsOpen) { ImageFullScreen.HandleButton((Windows.System.VirtualKey)VK_RT); return; }
            if (PdfFullScreen.IsOpen) return;
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { HandleContinuousSeek(5); return; }
            if (_isMediaPlayerActive) { MediaPreview.Seek(TimeSpan.FromSeconds(5)); return; }
            var before = CurrentList.SelectedIndex;
            if (_navigator.Current != null && CurrentList.Items.Count > 0)
                CurrentList.SelectedIndex = Math.Min(CurrentList.Items.Count - 1, CurrentList.SelectedIndex + 8);
            CurrentList.ScrollIntoView(CurrentList.SelectedItem);
            Log.Dbg("OnPageDown: before={Before} after={After}", before, CurrentList.SelectedIndex);
        }

        public void OnSeekBack()
        {
            if (ImageFullScreen.IsOpen) return;
            if (PdfFullScreen.IsOpen) { PdfFullScreen.HandleBumper(true); return; }
            if (AudioFullScreenPanel.Visibility == Visibility.Visible) { NavigateAudioTrack(-1); return; }
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { HandleContinuousSeek(-5); return; }
            if (_isMediaPlayerActive) { NavigatePreviewTrack(-1); return; }
            JumpByLetter(-1);
        }

        public void OnSeekForward()
        {
            if (ImageFullScreen.IsOpen) return;
            if (PdfFullScreen.IsOpen) { PdfFullScreen.HandleBumper(false); return; }
            if (AudioFullScreenPanel.Visibility == Visibility.Visible) { NavigateAudioTrack(1); return; }
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { HandleContinuousSeek(5); return; }
            if (_isMediaPlayerActive) { NavigatePreviewTrack(1); return; }
            JumpByLetter(1);
        }

        /// <summary>
        /// Jump to next (direction=1) or previous (direction=-1) entry
        /// whose first letter differs from the current selection.
        /// </summary>
        private void JumpByLetter(int direction)
        {
            if (_navigator.Current == null) return;
            var entries = _navigator.Current.Entries;
            int idx = CurrentList.SelectedIndex;
            if (idx < 0 || entries.Count == 0) return;

            char currentLetter = GetFirstLetter(entries[idx].Name);
            int i = idx;

            while (true)
            {
                i += direction;
                if (i < 0 || i >= entries.Count) break;
                char letter = GetFirstLetter(entries[i].Name);
                if (letter != currentLetter)
                {
                    CurrentList.SelectedIndex = i;
                    CurrentList.ScrollIntoView(CurrentList.SelectedItem);
                    return;
                }
            }

            // If no different letter found, clamp to boundary
            if (direction > 0 && idx < entries.Count - 1)
                CurrentList.SelectedIndex = entries.Count - 1;
            else if (direction < 0 && idx > 0)
                CurrentList.SelectedIndex = 0;
            CurrentList.ScrollIntoView(CurrentList.SelectedItem);
        }

        private static char GetFirstLetter(string name)
        {
            if (string.IsNullOrEmpty(name)) return '\0';
            char c = char.ToUpperInvariant(name[0]);
            // Skip ".." and non-alpha — treat directories/special as '\0'
            if (!char.IsLetterOrDigit(c)) return '\0';
            return c;
        }

        public void OnSeekRepeat(int seconds)
        {
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { HandleContinuousSeek(seconds); return; }
            if (AudioFullScreenPanel.Visibility == Visibility.Visible) { return; }
            if (_isMediaPlayerActive) { MediaPreview.Seek(TimeSpan.FromSeconds(seconds)); return; }
        }

        public void OnTriggerHeld(float leftTrigger, float rightTrigger)
        {
            if (ImageFullScreen.IsOpen)
            {
                ImageFullScreen.HandleTriggers(leftTrigger, rightTrigger);
                _ltWasDown = false;
                _rtWasDown = false;
                return;
            }
            if (PdfFullScreen.IsOpen)
            {
                PdfFullScreen.HandleTriggers(leftTrigger, rightTrigger);
                _ltWasDown = false;
                _rtWasDown = false;
                return;
            }
            bool isMedia = VideoFullScreenPanel.Visibility == Visibility.Visible || _isMediaPlayerActive || AudioFullScreenPanel.Visibility == Visibility.Visible;
            if (!isMedia) { _ltWasDown = false; _rtWasDown = false; return; }

            const float Threshold = 0.3f;

            bool ltDown = leftTrigger > Threshold;
            bool rtDown = rightTrigger > Threshold;

            if (ltDown)
            {
                if (!_ltWasDown) _ltHoldMs = 0;
                _ltHoldMs += 16;
                if (_seekCooldown <= 0)
                {
                    int seek = ComputeAcceleratedSeek(_ltHoldMs);
                    HandleContinuousSeek(-seek);
                    _seekCooldown = 60;
                }
            }
            else
            {
                _ltWasDown = false;
                _ltHoldMs = 0;
            }

            if (rtDown)
            {
                if (!_rtWasDown) _rtHoldMs = 0;
                _rtHoldMs += 16;
                if (_seekCooldown <= 0)
                {
                    int seek = ComputeAcceleratedSeek(_rtHoldMs);
                    HandleContinuousSeek(seek);
                    _seekCooldown = 60;
                }
            }
            else
            {
                _rtWasDown = false;
                _rtHoldMs = 0;
            }

            _ltWasDown = ltDown;
            _rtWasDown = rtDown;
            if (_seekCooldown > 0) _seekCooldown -= 16;
        }

        private static int ComputeAcceleratedSeek(double holdMs)
        {
            double t = holdMs / 1000.0;
            return Math.Max(1, (int)(Math.Pow(t, 1.8) * 8));
        }

        private void HandleContinuousSeek(int seconds)
        {
            if (VideoFullScreenPanel.Visibility == Visibility.Visible)
            {
                ShowFsControls();

                var pos = FsVideoSession.Position + TimeSpan.FromSeconds(seconds);
                if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
                var total = FsVideoSession.NaturalDuration;
                if (total.TotalSeconds > 0 && pos > total) pos = total;

                FsVideoSession.Position = pos;

                if (total.TotalSeconds > 0)
                {
                    FSProgress.Value = (pos.TotalSeconds / total.TotalSeconds) * 100;
                    FSTimeText.Text = $"{FormatFsTime(pos)} / {FormatFsTime(total)}";
                }

                string dir = seconds > 0 ? "\u25B6\u25B6" : "\u25C0\u25C0";
                ShowFsOsd($"{dir}  {(seconds > 0 ? "+" : "")}{seconds}s", null, 800);
            }
            else if (AudioFullScreenPanel.Visibility == Visibility.Visible && _fsAudioLevelService != null && _fsAudioLevelService.IsFileLoaded)
            {
                var pos = _fsAudioLevelService.Position + TimeSpan.FromSeconds(seconds);
                if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
                var total = _fsAudioLevelService.Duration;
                if (total.TotalSeconds > 0 && pos > total) pos = total;

                _fsAudioLevelService.Seek(pos);

                string dir = seconds > 0 ? "\u25B6\u25B6" : "\u25C0\u25C0";
                ShowFsOsd($"{dir}  {(seconds > 0 ? "+" : "")}{seconds}s", null, 800);
            }
            else if (_isMediaPlayerActive)
            {
                MediaPreview.Seek(TimeSpan.FromSeconds(seconds));
                ShowFsOsd($"{(seconds > 0 ? "+" : "")}{seconds}s", null, 800);
            }
        }

        public void OnLeftStickMove(float x, float y)
        {
            if (TextEditorOverlayControl.IsOpen) { TextEditorOverlayControl.HandleLeftStick(x, y); return; }
            if (LogsPageControl.IsVisible) { LogsPageControl.HandleLeftStick(x, y); return; }
            if (ShareDialogControl.IsVisible) return;
            if (FolderBrowserDialogControl.IsOpen)
            {
                FolderBrowserDialogControl.HandleStick(y);
                return;
            }
            if (FileOperationConfirmDialogControl.IsDialogVisible)
            {
                FileOperationConfirmDialogControl.HandleStick(x, y);
                return;
            }
            if (OpProgressDialog.IsOpen)
            {
                return;
            }
            if (ImageFullScreen.IsOpen)
            {
                ImageFullScreen.HandleRightStick(x, y);
                return;
            }
            if (PdfFullScreen.IsOpen)
            {
                PdfFullScreen.HandleRightStick(x, y);
                return;
            }
            if (AudioFullScreenPanel.Visibility == Visibility.Visible)
            {
                UpdateFsVolume(y);
            }
            else if (VideoFullScreenPanel.Visibility == Visibility.Visible)
            {
                UpdateFsVolume(y);
            }
            else if (_isMediaPlayerActive)
            {
                UpdateFsVolume(y);
            }
        }

        public void OnRightStickMove(float x, float y)
        {
            if (TextEditorOverlayControl.IsOpen) { TextEditorOverlayControl.HandleStick(x, y); return; }
            if (LogsPageControl.IsVisible) { LogsPageControl.HandleRightStick(x, y); return; }
            if (ShareDialogControl.IsVisible) return;
            if (FolderBrowserDialogControl.IsOpen)
            {
                FolderBrowserDialogControl.HandleStick(y);
                return;
            }
            if (FileOperationConfirmDialogControl.IsDialogVisible)
            {
                FileOperationConfirmDialogControl.HandleStick(x, y);
                return;
            }
            if (OpProgressDialog.IsOpen)
            {
                return;
            }
            if (ImageFullScreen.IsOpen)
            {
                ImageFullScreen.HandleRightStick(x, y);
                return;
            }
            if (PdfFullScreen.IsOpen)
            {
                PdfFullScreen.HandleRightStick(x, y);
                return;
            }
            if (AudioFullScreenPanel.Visibility == Visibility.Visible)
            {
                UpdateFsVolume(y);
            }
            else if (VideoFullScreenPanel.Visibility == Visibility.Visible)
            {
                UpdateFsVolume(y);
            }
            else if (_isMediaPlayerActive)
            {
                UpdateFsVolume(y);
            }
        }

        public void OnHome()
        {
            if (IsAnyFullscreen) return;
            var before = CurrentList.SelectedIndex;
            if (CurrentList.Items.Count > 0)
                CurrentList.SelectedIndex = 0;
            CurrentList.ScrollIntoView(CurrentList.SelectedItem);
            Log.Dbg("OnHome: before={Before} after={After}", before, CurrentList.SelectedIndex);
        }

        public void OnEnd()
        {
            if (IsAnyFullscreen) return;
            var before = CurrentList.SelectedIndex;
            if (_navigator.Current != null && CurrentList.Items.Count > 0)
                CurrentList.SelectedIndex = CurrentList.Items.Count - 1;
            CurrentList.ScrollIntoView(CurrentList.SelectedItem);
            Log.Dbg("OnEnd: before={Before} after={After}", before, CurrentList.SelectedIndex);
        }

        public void OnScrollVertical(double delta)
        {
            if (ImageFullScreen.IsOpen) return;
            if (PdfFullScreen.IsOpen) return;
            try
            {
                if (PreviewTextScroll.Visibility == Visibility.Visible)
                {
                    double newOffset = PreviewTextScroll.VerticalOffset + delta;
                    PreviewTextScroll.ScrollToVerticalOffset(Math.Max(0, newOffset));
                }
                else if (PreviewCodeView.Visibility == Visibility.Visible)
                {
                    string js = $"window.scrollBy(0, {delta:F1});";
                    _ = PreviewCodeView.InvokeScriptAsync("eval", new[] { js });
                }
            }
            catch (Exception ex)
            {
                Log.Warn("OnScrollVertical failed", ex);
            }
        }

        public void OnScrollHorizontal(double delta)
        {
            if (ImageFullScreen.IsOpen) return;
            if (PdfFullScreen.IsOpen) return;
            try
            {
                if (PreviewTextScroll.Visibility == Visibility.Visible)
                {
                    double newOffset = PreviewTextScroll.HorizontalOffset + delta;
                    PreviewTextScroll.ScrollToHorizontalOffset(Math.Max(0, newOffset));
                }
                else if (PreviewCodeView.Visibility == Visibility.Visible)
                {
                    string js = $"window.scrollBy({delta:F1}, 0);";
                    _ = PreviewCodeView.InvokeScriptAsync("eval", new[] { js });
                }
            }
            catch (Exception ex)
            {
                Log.Warn("OnScrollHorizontal failed", ex);
            }
        }

        public void OnSelectVisualizer()
        {
            if (VideoFullScreenPanel.Visibility == Visibility.Visible)
            {
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    if (VideoTrackMenuControl.IsOpen)
                    {
                        VideoTrackMenuControl.HandleButton(Windows.System.VirtualKey.GamepadA);
                    }
                    else
                    {
                        OpenVideoTrackMenu();
                    }
                });
            }
            else if (_isMediaPlayerActive && !MediaPreview.IsAudioMode)
            {
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    if (VideoTrackMenuControl.IsOpen)
                    {
                        VideoTrackMenuControl.HandleButton(Windows.System.VirtualKey.GamepadA);
                    }
                    else if (MediaPreview.CurrentSubtitleTracks.Count > 0 || MediaPreview.CurrentAudioTracks.Count > 1)
                    {
                        VideoTrackMenuControl.Show(
                            MediaPreview.CurrentSubtitleTracks,
                            MediaPreview.CurrentAudioTracks,
                            MediaPreview.CurrentSubtitleTrackIndex,
                            MediaPreview.CurrentAudioTrackIndex);
                    }
                });
            }
            else if (_isAudioFullscreen)
            {
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    CycleAudioVisualizer();
                });
            }
        }

        // --- Fullscreen Video ---

        public async Task OpenFullscreenForFile(string filePath, TimeSpan position)
        {
            OpenAudioFullscreen(filePath, position);
            await System.Threading.Tasks.Task.CompletedTask;
        }

        public async Task ShowMediaFullscreenAsync(Uri source, bool isVideo, TimeSpan position)
        {
            if (!isVideo) return;

            // Always stop preview before fullscreen — idempotent, safe if already stopped
            if (_isMediaPlayerActive) { MediaPreview.StopPlayer(); UpdateMediaPlayerFocusUI(); }

            _fsVideoPath = source.LocalPath;

            // Detect external subtitles (VLC-style same-name matching)
            _fsSubtitles = SubtitleDetector.FindExternalSubtitles(_fsVideoPath);
            _fsSelectedSubtitleIndex = -1;
            _fsSelectedAudioIndex = 0;
            _fsAudioTracks = new List<AudioTrackInfo>();
            _fsSuppressTrackEvent = false;

            // Build MediaSource with external subtitle tracks
            var mediaSource = Windows.Media.Core.MediaSource.CreateFromUri(source);
            foreach (var sub in _fsSubtitles)
            {
                try
                {
                    var tts = TimedTextSource.CreateFromUri(new Uri(sub.FilePath));
                    tts.Resolved += OnExternalSubtitleResolved;
                    mediaSource.ExternalTimedTextSources.Add(tts);
                }
                catch (Exception ex)
                {
                    Log.Warn("ShowMediaFullscreenAsync: failed to add subtitle '{Path}'", sub.FilePath, ex);
                }
            }

            // Create MediaPlaybackItem so we can access TimedMetadataTracks and AudioTracks later
            _fsPlaybackItem = new Windows.Media.Playback.MediaPlaybackItem(mediaSource);
            FsVideoPlayer.Source = _fsPlaybackItem;

            // Subscribe to MediaOpened to enumerate tracks after source is ready
            FsVideoPlayer.MediaOpened += OnFsVideoMediaOpened;
            FsVideoPlayer.MediaEnded += OnFsVideoMediaEnded;

            FsVideoSession.Position = position;
            FsVideoPlayer.Volume = _fsVolume;
            FsVideoPlayer.Play();
            _fsVideoPlaying = true;
            FSPlayPauseIcon.Glyph = "\uE769";
            FSVolumeText.Text = $"Vol {(int)(_fsVolume * 100)}%";
            _fullscreenProgressTimer.Start();
            VideoFullScreenPanel.Visibility = Visibility.Visible;
            ShowFsControls();
            UpdateDisplayRequest();
            ShowFsOsd("PLAY", "ms-appx:///Assets/Views/MillerColumnsPage/osd/osd-play-48.png");
            Log.Info("ShowMediaFullscreenAsync: started fullscreen video at {Position}, {SubCount} external subs", position, _fsSubtitles.Count);
            await System.Threading.Tasks.Task.CompletedTask;
        }

        private void OnEmbeddedSubtitleCueEntered(TimedMetadataTrack sender, MediaCueEventArgs args)
        {
            if (args.Cue is TimedTextCue ttCue && ttCue.CueStyle == null)
            {
                ttCue.CueStyle = new TimedTextStyle
                {
                    FontFamily = "Arial",
                    FontSize = new TimedTextDouble { Unit = TimedTextUnit.Percentage, Value = 100 },
                    Foreground = Windows.UI.Colors.White,
                    OutlineColor = Windows.UI.Colors.Black,
                    OutlineThickness = new TimedTextDouble { Unit = TimedTextUnit.Percentage, Value = 4 },
                    OutlineRadius = new TimedTextDouble { Unit = TimedTextUnit.Percentage, Value = 2 }
                };
            }
        }

        private void OnExternalSubtitleResolved(TimedTextSource sender, TimedTextSourceResolveResultEventArgs args)
        {
            Log.Verb("OnExternalSubtitleResolved: external subtitle track resolved");
            if (args.Tracks == null) return;
            foreach (var track in args.Tracks)
            {
                if (track.TimedMetadataKind != TimedMetadataKind.Subtitle) continue;
                foreach (var cue in track.Cues)
                {
                    if (cue is TimedTextCue ttCue)
                    {
                        ttCue.CueStyle = new TimedTextStyle
                        {
                            FontFamily = "Arial",
                            FontSize = new TimedTextDouble { Unit = TimedTextUnit.Percentage, Value = 100 },
                            Foreground = Windows.UI.Colors.White,
                            OutlineColor = Windows.UI.Colors.Black,
                            OutlineThickness = new TimedTextDouble { Unit = TimedTextUnit.Percentage, Value = 4 },
                            OutlineRadius = new TimedTextDouble { Unit = TimedTextUnit.Percentage, Value = 2 }
                        };
                    }
                }
            }
        }

        private void OnFsVideoMediaOpened(MediaPlayer sender, object args)
        {
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                FsVideoPlayer.MediaOpened -= OnFsVideoMediaOpened;
                EnumerateAllTracks();
            });
        }

        private async void OnFsVideoMediaEnded(MediaPlayer sender, object args)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                Log.Info("FsVideo: media ended — auto-advancing");
                NavigateFullscreenVideo(1);
            });
        }

        private void NavigateFullscreenVideo(int direction)
        {
            if (string.IsNullOrEmpty(_fsVideoPath) || _navigator.Current == null) return;

            var videoFiles = _navigator.Current.Entries
                .Where(e => !e.IsDirectory && FilePreviewService.IsVideoFile(System.IO.Path.GetExtension(e.Name)))
                .ToList();

            if (videoFiles.Count == 0) { CloseVideoFullScreen(); return; }

            int currentIdx = videoFiles.FindIndex(e =>
                string.Equals(e.FullPath, _fsVideoPath, StringComparison.OrdinalIgnoreCase));

            int nextIdx = currentIdx + direction;
            if (nextIdx < 0) nextIdx = videoFiles.Count - 1;
            if (nextIdx >= videoFiles.Count) nextIdx = 0;

            var nextFile = videoFiles[nextIdx];
            Log.Info("NavigateFullscreenVideo: {Direction} to {Path}", direction > 0 ? "next" : "prev", nextFile.FullPath);

            // Update selection in main list
            int mainIdx = _navigator.Current.Entries.IndexOf(nextFile);
            if (mainIdx >= 0)
            {
                _updating = true;
                CurrentList.SelectedIndex = mainIdx;
                _updating = false;
            }

            // Play next video in fullscreen
            _ = ShowMediaFullscreenAsync(new Uri(nextFile.FullPath), true, TimeSpan.Zero);
        }

        private void EnumerateAllTracks()
        {
            try
            {
                if (_fsPlaybackItem == null) return;

                _fsSuppressTrackEvent = true;

                // Enumerate embedded subtitle tracks
                int embeddedSubCount = 0;
                for (int i = 0; i < _fsPlaybackItem.TimedMetadataTracks.Count; i++)
                {
                    var track = _fsPlaybackItem.TimedMetadataTracks[i];
                    if (track.TimedMetadataKind == Windows.Media.Core.TimedMetadataKind.Subtitle)
                    {
                        string lang = track.Language ?? "Unknown";
                        string title = track.Label ?? lang;
                        _fsSubtitles.Add(new SubtitleTrack
                        {
                            Language = lang,
                            Title = title,
                            IsExternal = false,
                            EmbeddedIndex = embeddedSubCount,
                            FilePath = null
                        });
                        track.CueEntered += OnEmbeddedSubtitleCueEntered;
                        embeddedSubCount++;
                    }
                }

                Log.Info("EnumerateAllTracks: found {EmbeddedSubs} embedded subtitle tracks", embeddedSubCount);

                // Enumerate audio tracks
                _fsAudioTracks.Clear();
                for (int i = 0; i < _fsPlaybackItem.AudioTracks.Count; i++)
                {
                    var track = _fsPlaybackItem.AudioTracks[i];
                    _fsAudioTracks.Add(new AudioTrackInfo
                    {
                        Language = track.Language ?? "Unknown",
                        Title = track.Label ?? track.Language ?? "Unknown",
                        Index = i
                    });
                }
                _fsSelectedAudioIndex = (int)_fsPlaybackItem.AudioTracks.SelectedIndex;

                Log.Info("EnumerateAllTracks: found {AudioCount} audio tracks", _fsAudioTracks.Count);
            }
            catch (Exception ex)
            {
                Log.Warn("EnumerateAllTracks failed", ex);
            }
            finally
            {
                _fsSuppressTrackEvent = false;
            }
        }

        private void OpenVideoTrackMenu()
        {
            if (VideoFullScreenPanel.Visibility != Visibility.Visible) return;
            if (VideoTrackMenuControl.IsOpen) return;

            // If tracks haven't been enumerated yet (source still opening), try now
            if (_fsAudioTracks.Count == 0 && _fsSubtitles.Count == 1 && _fsSubtitles[0].IsExternal)
            {
                EnumerateAllTracks();
            }

            VideoTrackMenuControl.Show(_fsSubtitles, _fsAudioTracks, _fsSelectedSubtitleIndex, _fsSelectedAudioIndex);
            _fsHideTimer.Stop();
        }

        private void OnVideoSubtitleSelected(object sender, SubtitleTrack track)
        {
            var playbackItem = _fsPlaybackItem ?? MediaPreview.CurrentPlaybackItem;
            if (playbackItem == null) return;

            try
            {
                for (uint i = 0; i < playbackItem.TimedMetadataTracks.Count; i++)
                {
                    playbackItem.TimedMetadataTracks.SetPresentationMode(i,
                        TimedMetadataTrackPresentationMode.Disabled);
                }

                if (track.IsExternal && _fsPlaybackItem != null)
                {
                    for (uint i = 0; i < _fsPlaybackItem.TimedMetadataTracks.Count; i++)
                    {
                        var ttTrack = _fsPlaybackItem.TimedMetadataTracks[(int)i];
                        if (ttTrack.TimedMetadataKind == TimedMetadataKind.Subtitle)
                        {
                            int externalIndex = _fsSubtitles.FindIndex(s => s.IsExternal && s.FilePath == track.FilePath);
                            int trackIndex = externalIndex >= 0 ? externalIndex : (int)i;

                            if (trackIndex == (int)i)
                            {
                                _fsPlaybackItem.TimedMetadataTracks.SetPresentationMode(i,
                                    TimedMetadataTrackPresentationMode.PlatformPresented);
                                _fsSelectedSubtitleIndex = _fsSubtitles.IndexOf(track);
                                ShowFsOsd($"Sub: {track.GetDisplayName()}");
                                Log.Info("OnVideoSubtitleSelected: enabled external subtitle '{Name}'", track.GetDisplayName());
                                return;
                            }
                        }
                    }
                }
                else if (track.EmbeddedIndex >= 0)
                {
                    int count = 0;
                    for (uint i = 0; i < playbackItem.TimedMetadataTracks.Count; i++)
                    {
                        var ttTrack = playbackItem.TimedMetadataTracks[(int)i];
                        if (ttTrack.TimedMetadataKind == TimedMetadataKind.Subtitle)
                        {
                            if (count == track.EmbeddedIndex)
                            {
                                playbackItem.TimedMetadataTracks.SetPresentationMode(i,
                                    TimedMetadataTrackPresentationMode.PlatformPresented);
                                if (_fsPlaybackItem != null)
                                    _fsSelectedSubtitleIndex = _fsSubtitles.IndexOf(track);
                                Log.Info("OnVideoSubtitleSelected: enabled embedded subtitle '{Name}'", track.GetDisplayName());
                                return;
                            }
                            count++;
                        }
                    }
                }
                else
                {
                    if (_fsPlaybackItem != null)
                        _fsSelectedSubtitleIndex = -1;
                    Log.Info("OnVideoSubtitleSelected: subtitles disabled");
                }
            }
            catch (Exception ex)
            {
                Log.Warn("OnVideoSubtitleSelected failed", ex);
            }
        }

        private void OnVideoAudioTrackSelected(object sender, int trackIndex)
        {
            try
            {
                var playbackItem = _fsPlaybackItem ?? MediaPreview.CurrentPlaybackItem;
                if (playbackItem == null) return;

                if (trackIndex >= 0 && trackIndex < playbackItem.AudioTracks.Count)
                {
                    playbackItem.AudioTracks.SelectedIndex = trackIndex;

                    if (_fsPlaybackItem != null)
                    {
                        _fsSelectedAudioIndex = trackIndex;
                        string name = trackIndex < _fsAudioTracks.Count
                            ? _fsAudioTracks[trackIndex].DisplayName
                            : $"Track {trackIndex + 1}";
                        ShowFsOsd($"Audio: {name}");
                    }

                    Log.Info("OnVideoAudioTrackSelected: selected audio track {Index}", trackIndex);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("OnVideoAudioTrackSelected failed", ex);
            }
        }

        private void CloseVideoFullScreen()
        {
            _fullscreenProgressTimer.Stop();
            _fsHideTimer.Stop();
            _fsOsdHideTimer.Stop();
            FsVideoPlayer.Pause();
            FsVideoPlayer.MediaEnded -= OnFsVideoMediaEnded;
            FsVideoPlayer.Source = null;
            _fsPlaybackItem = null;
            _fsVideoPlaying = false;
            _fsSubtitles?.Clear();
            _fsAudioTracks?.Clear();
            _fsSelectedSubtitleIndex = -1;
            _fsSelectedAudioIndex = -1;
            VideoTrackMenuControl.Close();
            VideoFullScreenPanel.Visibility = Visibility.Collapsed;
            Log.Info("CloseVideoFullScreen: stopped, track state cleared");
            UpdateDisplayRequest();
        }

        private async System.Threading.Tasks.Task HandleEditAsync(FileEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.FullPath))
            {
                Log.Warn("HandleEditAsync: null/empty entry");
                return;
            }
            Log.Info("HandleEditAsync: opening {Path} (ext={Ext})", entry.Name, System.IO.Path.GetExtension(entry.FullPath));
            TextEditorOverlayControl.Show(entry.FullPath);
            Log.Dbg("HandleEditAsync: Show() returned, overlay visible={Vis}", TextEditorOverlayControl.IsOpen);
            await System.Threading.Tasks.Task.CompletedTask;
        }

        private async System.Threading.Tasks.Task HandleShareAsync(FileEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.FullPath))
            {
                Log.Warn("HandleShareAsync: null/empty entry");
                return;
            }
            Log.Info("HandleShareAsync: {File}", entry.FullPath);

            var cts = new System.Threading.CancellationTokenSource();

            OpProgressDialog.Show("Sharing", entry.Name, "");

            try
            {
                string url = await XFiles.Services.FileShareService.ShareAsync(
                    entry.FullPath,
                    statusText =>
                    {
                        _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            if (OpProgressDialog.IsOpen)
                            {
                                OpProgressDialog.UpdateProgress(new FileOperations.OperationProgress
                                {
                                    FileName = statusText,
                                    PercentComplete = -1
                                });
                            }
                        });
                    },
                    (bytesUploaded, totalBytes) =>
                    {
                        _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            if (OpProgressDialog.IsOpen)
                            {
                                OpProgressDialog.UpdateProgress(new FileOperations.OperationProgress
                                {
                                    FileName = $"Uploading {FormatBytes(bytesUploaded)} / {FormatBytes(totalBytes)}",
                                    PercentComplete = totalBytes > 0 ? (double)bytesUploaded / totalBytes * 100 : -1,
                                    BytesCopied = bytesUploaded,
                                    TotalBytes = totalBytes
                                });
                            }
                        });
                    },
                    cts.Token);

                OpProgressDialog.Complete();
                await System.Threading.Tasks.Task.Delay(400);
                OpProgressDialog.Close();

                if (!string.IsNullOrEmpty(url))
                {
                    ShareDialogControl.Show(url);
                }
                else
                {
                    Log.Warn("HandleShareAsync: upload returned null URL");
                    _ = AlertDialogControl.ShowAsync("Share failed: upload returned no URL.", AlertType.Error);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("HandleShareAsync: exception: {Error}", ex.Message);
                OpProgressDialog.Close();
                _ = AlertDialogControl.ShowAsync($"Share failed: {ex.Message}", AlertType.Error);
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        private void OnFullscreenProgressTick(object sender, object e)
        {
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
            {
                // Fullscreen video progress
                if (VideoFullScreenPanel.Visibility == Visibility.Visible)
                {
                    var total = FsVideoSession.NaturalDuration;
                    if (total.TotalSeconds > 0)
                    {
                        var current = FsVideoSession.Position;
                        FSProgress.Value = Math.Max(0, Math.Min(100, (current.TotalSeconds / total.TotalSeconds) * 100));
                        FSTimeText.Text = $"{FormatFsTime(current)} / {FormatFsTime(total)}";
                    }
                }
                // Fullscreen audio progress
                else if (AudioFullScreenPanel.Visibility == Visibility.Visible && _fsAudioLevelService != null && _fsAudioLevelService.IsFileLoaded)
                {
                    var total = _fsAudioLevelService.Duration;
                    if (total.TotalSeconds > 0)
                    {
                        var current = _fsAudioLevelService.Position;
                        FsAudioProgress.Value = Math.Max(0, Math.Min(100, (current.TotalSeconds / total.TotalSeconds) * 100));
                        FsCurrentTimeText.Text = FormatFsTime(current);
                        FsTotalTimeText.Text = FormatFsTime(total);
                    }
                }
            });
        }

        private static string FormatFsTime(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            return $"{ts.Minutes}:{ts.Seconds:D2}";
        }

        private void OnVideoFullScreenTapped(object sender, TappedRoutedEventArgs e)
        {
            if (FSControlsBar.Opacity > 0)
                CloseVideoFullScreen();
            else
                ShowFsControls();
        }

        private void ShowFsControls()
        {
            var sb = new Storyboard();
            var dur = new Duration(TimeSpan.FromMilliseconds(250));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            var animBar = new DoubleAnimation { To = 1.0, Duration = dur, EasingFunction = ease };
            Storyboard.SetTarget(animBar, FSControlsBar);
            Storyboard.SetTargetProperty(animBar, "Opacity");
            sb.Children.Add(animBar);

            var animLeg = new DoubleAnimation { To = 1.0, Duration = dur, EasingFunction = ease };
            Storyboard.SetTarget(animLeg, FSLegendText);
            Storyboard.SetTargetProperty(animLeg, "Opacity");
            sb.Children.Add(animLeg);

            sb.Begin();
            _fsHideTimer.Stop();
            _fsHideTimer.Start();
        }

        private void HideFsControls()
        {
            var sb = new Storyboard();
            var dur = new Duration(TimeSpan.FromMilliseconds(400));
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

            var animBar = new DoubleAnimation { To = 0.0, Duration = dur, EasingFunction = ease };
            Storyboard.SetTarget(animBar, FSControlsBar);
            Storyboard.SetTargetProperty(animBar, "Opacity");
            sb.Children.Add(animBar);

            var animLeg = new DoubleAnimation { To = 0.0, Duration = dur, EasingFunction = ease };
            Storyboard.SetTarget(animLeg, FSLegendText);
            Storyboard.SetTargetProperty(animLeg, "Opacity");
            sb.Children.Add(animLeg);

            sb.Begin();
        }

        private void ShowFsOsd(string text, string iconSource = null, double hideDelayMs = 1500)
        {
            FsOsdText.Text = text;
            if (iconSource != null)
            {
                FsOsdIcon.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(iconSource));
                FsOsdIcon.Visibility = Visibility.Visible;
            }
            else
            {
                FsOsdIcon.Visibility = Visibility.Collapsed;
            }
            FsOsdBorder.Visibility = Visibility.Visible;
            var fadeIn = new Storyboard();
            var dur = new Duration(TimeSpan.FromMilliseconds(150));
            var anim = new DoubleAnimation { To = 1.0, Duration = dur, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(anim, FsOsdBorder);
            Storyboard.SetTargetProperty(anim, "Opacity");
            fadeIn.Children.Add(anim);
            fadeIn.Begin();
            _fsOsdHideTimer.Stop();
            _fsOsdHideTimer.Interval = TimeSpan.FromMilliseconds(hideDelayMs);
            _fsOsdHideTimer.Tick -= OnFsOsdHideTick;
            _fsOsdHideTimer.Tick += OnFsOsdHideTick;
            _fsOsdHideTimer.Start();
        }

        private void HideFsOsd()
        {
            var fadeOut = new Storyboard();
            var dur = new Duration(TimeSpan.FromMilliseconds(300));
            var anim = new DoubleAnimation { To = 0.0, Duration = dur, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            Storyboard.SetTarget(anim, FsOsdBorder);
            Storyboard.SetTargetProperty(anim, "Opacity");
            fadeOut.Children.Add(anim);
            fadeOut.Completed += (s, e) => FsOsdBorder.Visibility = Visibility.Collapsed;
            fadeOut.Begin();
        }

        private void OnFsOsdHideTick(object sender, object e)
        {
            _fsOsdHideTimer.Stop();
            HideFsOsd();
        }

        private DispatcherTimer _fsAudioOsdHideTimer = new DispatcherTimer();

        private void ShowAudioOsd(string text, string iconSource = null, double hideDelayMs = 1500)
        {
            FsAudioOsdText.Text = text;
            if (iconSource != null)
            {
                FsAudioOsdIcon.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(iconSource));
                FsAudioOsdIcon.Visibility = Visibility.Visible;
            }
            else
            {
                FsAudioOsdIcon.Visibility = Visibility.Collapsed;
            }
            FsAudioOsdBorder.Visibility = Visibility.Visible;
            var fadeIn = new Storyboard();
            var dur = new Duration(TimeSpan.FromMilliseconds(150));
            var anim = new DoubleAnimation { To = 1.0, Duration = dur, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(anim, FsAudioOsdBorder);
            Storyboard.SetTargetProperty(anim, "Opacity");
            fadeIn.Children.Add(anim);
            fadeIn.Begin();
            _fsAudioOsdHideTimer.Stop();
            _fsAudioOsdHideTimer.Interval = TimeSpan.FromMilliseconds(hideDelayMs);
            _fsAudioOsdHideTimer.Tick -= OnFsAudioOsdHideTick;
            _fsAudioOsdHideTimer.Tick += OnFsAudioOsdHideTick;
            _fsAudioOsdHideTimer.Start();
        }

        private void HideAudioOsd()
        {
            var fadeOut = new Storyboard();
            var dur = new Duration(TimeSpan.FromMilliseconds(300));
            var anim = new DoubleAnimation { To = 0.0, Duration = dur, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            Storyboard.SetTarget(anim, FsAudioOsdBorder);
            Storyboard.SetTargetProperty(anim, "Opacity");
            fadeOut.Children.Add(anim);
            fadeOut.Completed += (s, e) => FsAudioOsdBorder.Visibility = Visibility.Collapsed;
            fadeOut.Begin();
        }

        private void OnFsAudioOsdHideTick(object sender, object e)
        {
            _fsAudioOsdHideTimer.Stop();
            HideAudioOsd();
        }

        private static readonly (AudioFullscreenMode Mode, string Label)[] _fsModeOrder = new[]
        {
            (AudioFullscreenMode.Default, "Default"),
            (AudioFullscreenMode.RadialSpectrum, "Radial Spectrum"),
            (AudioFullscreenMode.Waveform, "Waveform"),
            (AudioFullscreenMode.Plasma, "Plasma"),
            (AudioFullscreenMode.Starfield, "Starfield"),
            (AudioFullscreenMode.SpiralSpectrum, "Spiral Spectrum"),
            (AudioFullscreenMode.MirrorTunnel, "Mirror Tunnel"),
            (AudioFullscreenMode.FireParticles, "Fire Particles"),
            (AudioFullscreenMode.Lissajous, "Lissajous"),
            (AudioFullscreenMode.TerrainGenerator, "Terrain Generator"),
            (AudioFullscreenMode.OrbitingCircles, "Orbiting Circles"),
            (AudioFullscreenMode.IsometricEqualizer, "Isometric Equalizer"),
            (AudioFullscreenMode.NeonGlare, "Neon Glare"),
            (AudioFullscreenMode.Kaleidoscope, "Kaleidoscope"),
            (AudioFullscreenMode.ParticleBurst, "Particle Burst"),
            (AudioFullscreenMode.RipplePulse, "Ripple Pulse"),
            (AudioFullscreenMode.FeedbackTrail, "Feedback Trail"),
            (AudioFullscreenMode.VoxelMatrix, "Voxel Matrix"),
            (AudioFullscreenMode.AnalogVUMeter, "Analog VU Meter"),
            (AudioFullscreenMode.CircularRadialSpectrum, "Circular Radial Spectrum"),
            (AudioFullscreenMode.RetroOscilloscope, "Retro Oscilloscope"),
            (AudioFullscreenMode.InfernoCore, "Inferno Core"),
            (AudioFullscreenMode.WaveformTunnel, "Waveform Tunnel"),
            (AudioFullscreenMode.InfernoCore2, "Inferno Core 2")
        };

        private DispatcherTimer _fsModeOsdTimer;

        public void CycleAudioVisualizer()
        {
            int count = Enum.GetValues(typeof(AudioFullscreenMode)).Length;
            int next = (int)_fsVisualizerMode;
            for (int i = 0; i < count; i++)
            {
                next = (next + 1) % count;
                var candidate = (AudioFullscreenMode)next;
                if (candidate == AudioFullscreenMode.Default || VisualizerRegistry.Resolve(candidate) != null)
                {
                    _fsVisualizerMode = candidate;
                    ApplyAudioVisualizerMode();
                    ShowModeOsd(_fsModeOrder.First(m => m.Mode == candidate).Label);
                    if (candidate == AudioFullscreenMode.Default)
                        FsTrackInfoBorder.Visibility = Visibility.Collapsed;
                    return;
                }
            }
        }

        private void ApplyAudioVisualizerMode()
        {
            bool showDefault = _fsVisualizerMode == AudioFullscreenMode.Default;
            FsAlbumArtBorder.Visibility = showDefault && _fsHasAlbumArt
                ? Visibility.Visible : Visibility.Collapsed;
            FsDefaultArtPanel.Visibility = showDefault && !_fsHasAlbumArt
                ? Visibility.Visible : Visibility.Collapsed;
            FsTitleText.Visibility = showDefault ? Visibility.Visible : Visibility.Collapsed;
            FsArtistText.Visibility = showDefault ? Visibility.Visible : Visibility.Collapsed;
            FsAlbumText.Visibility = showDefault ? Visibility.Visible : Visibility.Collapsed;
            FsVuMeter.Visibility = showDefault ? Visibility.Visible : Visibility.Collapsed;

            if (showDefault)
            {
                FsVisualizerCanvas.Deactivate();
                FsVisualizerCanvas.DetachService();
                FsVisualizerCanvas.Visibility = Visibility.Collapsed;
            }
            else
            {
                var viz = VisualizerRegistry.Resolve(_fsVisualizerMode);
                if (viz != null)
                {
                    FsVisualizerCanvas.AttachService(_fsAudioLevelService);
                    FsVisualizerCanvas.Activate(viz);
                    FsVisualizerCanvas.Visibility = Visibility.Visible;
                }
            }
        }

        private void ShowModeOsd(string label)
        {
            FsModeText.Text = label;
            FsModeText.Visibility = Visibility.Visible;
            FsModeText.Opacity = 1.0;

            if (_fsModeOsdTimer == null)
            {
                _fsModeOsdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                _fsModeOsdTimer.Tick += (s, e) =>
                {
                    _fsModeOsdTimer.Stop();
                    var fade = new Storyboard();
                    var dur = new Duration(TimeSpan.FromMilliseconds(300));
                    var anim = new DoubleAnimation { To = 0.0, Duration = dur, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                    Storyboard.SetTarget(anim, FsModeText);
                    Storyboard.SetTargetProperty(anim, "Opacity");
                    fade.Children.Add(anim);
                    fade.Completed += (s2, e2) => FsModeText.Visibility = Visibility.Collapsed;
                    fade.Begin();
                };
            }
            _fsModeOsdTimer.Stop();
            _fsModeOsdTimer.Start();
        }

        private DispatcherTimer _fsTrackInfoTimer;

        private void ShowTrackInfoOsd()
        {
            if (_fsVisualizerMode == AudioFullscreenMode.Default) return;

            FsTrackInfoTitle.Text = FsTitleText.Text;
            FsTrackInfoArtist.Text = FsArtistText.Text;
            FsTrackInfoAlbum.Text = FsAlbumText.Text;
            FsTrackInfoArtist.Visibility = FsArtistText.Visibility;
            FsTrackInfoAlbum.Visibility = FsAlbumText.Visibility;

            if (_fsHasAlbumArt && _fsAlbumArtBitmap != null)
            {
                FsTrackInfoCoverArt.Source = _fsAlbumArtBitmap;
                FsTrackInfoCoverArt.Visibility = Visibility.Visible;
            }
            else
            {
                FsTrackInfoCoverArt.Visibility = Visibility.Collapsed;
            }

            FsTrackInfoBorder.Visibility = Visibility.Visible;
            FsTrackInfoBorder.Opacity = 1.0;

            if (_fsTrackInfoTimer == null)
            {
                _fsTrackInfoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
                _fsTrackInfoTimer.Tick += (s, e) =>
                {
                    _fsTrackInfoTimer.Stop();
                    var fade = new Storyboard();
                    var dur = new Duration(TimeSpan.FromMilliseconds(500));
                    var anim = new DoubleAnimation { To = 0.0, Duration = dur, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                    Storyboard.SetTarget(anim, FsTrackInfoBorder);
                    Storyboard.SetTargetProperty(anim, "Opacity");
                    fade.Children.Add(anim);
                    fade.Completed += (s2, e2) => FsTrackInfoBorder.Visibility = Visibility.Collapsed;
                    fade.Begin();
                };
            }
            _fsTrackInfoTimer.Stop();
            _fsTrackInfoTimer.Start();
        }

        private void OnFsHideTimerTick(object sender, object e)
        {
            _fsHideTimer.Stop();
            HideFsControls();
        }

        private void OnFsControlsAnyInput()
        {
            if (VideoFullScreenPanel.Visibility == Visibility.Visible)
                ShowFsControls();
        }

        private void OnFsVideoInput()
        {
            if (VideoFullScreenPanel.Visibility != Visibility.Visible) return;
            ShowFsControls();
            if (_fsVideoPlaying)
            {
                FsVideoPlayer.Pause();
                FSPlayPauseIcon.Glyph = "\uE768";
                _fsVideoPlaying = false;
                ShowFsOsd("PAUSE", "ms-appx:///Assets/Views/MillerColumnsPage/osd/osd-pause-48.png");
            }
            else
            {
                FsVideoPlayer.Play();
                FSPlayPauseIcon.Glyph = "\uE769";
                _fsVideoPlaying = true;
                ShowFsOsd("PLAY", "ms-appx:///Assets/Views/MillerColumnsPage/osd/osd-play-48.png");
            }
        }

        private void UpdateFsVolume(float stickY)
        {
            const double Deadzone = 0.12;
            if (Math.Abs(stickY) < Deadzone) return;

            double magnitude = Math.Abs(stickY);
            double curved = magnitude * magnitude;
            double direction = stickY > 0 ? 1.0 : -1.0;
            double delta = direction * curved * 0.02;
            if (AudioFullScreenPanel.Visibility == Visibility.Visible)
            {
                _audioVolume = Math.Max(0.0, Math.Min(1.0, _audioVolume + delta));
                _fsAudioLevelService?.SetVolume(_audioVolume);
                FsVolumeText.Text = $"Vol {(int)(_audioVolume * 100)}%";
                ShowAudioOsd($"Vol {(int)(_audioVolume * 100)}%", null, 1200);
            }
            else if (VideoFullScreenPanel.Visibility == Visibility.Visible)
            {
                _fsVolume = Math.Max(0.0, Math.Min(1.0, _fsVolume + delta));
                ShowFsControls();
                FsVideoPlayer.Volume = _fsVolume;
                FSVolumeText.Text = $"Vol {(int)(_fsVolume * 100)}%";
                ShowFsOsd($"Vol {(int)(_fsVolume * 100)}%", null, 1200);
            }
            else if (_isMediaPlayerActive)
            {
                _fsVolume = Math.Max(0.0, Math.Min(1.0, _fsVolume + delta));
                MediaPreview.SetVolume(_fsVolume);
            }
        }

        private bool _fsVideoPlaying = false;
        private double _fsVolume = 0.75;
        private string _fsVideoPath;
        private List<SubtitleTrack> _fsSubtitles;
        private List<AudioTrackInfo> _fsAudioTracks;
        private int _fsSelectedSubtitleIndex = -1;
        private int _fsSelectedAudioIndex = 0;
        private bool _fsSuppressTrackEvent;
        private Windows.Media.Playback.MediaPlaybackItem _fsPlaybackItem;

        private double _seekCooldown;
        private double _ltHoldMs;
        private double _rtHoldMs;
        private bool _ltWasDown;
        private bool _rtWasDown;

        // Single shared timer for all fullscreen progress updates (video + audio)
        private DispatcherTimer _fullscreenProgressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };

        private DispatcherTimer _fsHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };

        private DispatcherTimer _fsOsdHideTimer = new DispatcherTimer();

        // Media load debounce — avoids loading video/audio on every scroll tick
        private DispatcherTimer _mediaLoadTimer;
        private string _pendingMediaPath;

        public void StopAllTimers()
        {
            _fullscreenProgressTimer.Stop();
            _fsHideTimer.Stop();
            _fsOsdHideTimer.Stop();
            _mediaLoadTimer.Stop();
            ImageFullScreen?.Close();
            MediaPreview?.StopPlayer();
            if (_isAudioFullscreen) CloseAudioFullscreen();
            StopFsAudioAnalysis();
        }

        private void OnMediaLoadTimerTick(object sender, object e)
        {
            _mediaLoadTimer.Stop();
            if (!string.IsNullOrEmpty(_pendingMediaPath))
            {
                if (!MediaPreview.IsFileLoaded(_pendingMediaPath) && !MediaPreview.IsPlayerActive)
                {
                    Log.Info("OnMediaLoadTimerTick: loading {Path}", _pendingMediaPath);
                    MediaPreview.LoadFile(_pendingMediaPath);
                }
                _pendingMediaPath = null;
            }
        }

        // --- Fullscreen Audio ---

        private bool _isAudioFullscreen;
        private string _audioFullscreenPath;
        private double _audioVolume = 0.75;
        private AudioLevelService _fsAudioLevelService;
        private AudioFullscreenMode _fsVisualizerMode;
        private DispatcherTimer _fsVisualizerTimer = new DispatcherTimer();
        private bool _fsHasAlbumArt;
        private Windows.UI.Xaml.Media.Imaging.BitmapImage _fsAlbumArtBitmap;
        private MetadataGuesser _fsMetadataGuesser = new MetadataGuesser();
        private int _fsGeneration;

        // MediaPlayer/Session helpers for fullscreen video + audio (migrated from MediaElement)
        private Windows.Media.Playback.MediaPlayer FsVideoPlayer => VideoFullScreenPlayer.MediaPlayer;
        private Windows.Media.Playback.MediaPlaybackSession FsVideoSession => FsVideoPlayer.PlaybackSession;
        private Windows.Media.Playback.MediaPlayer FsAudioPlayer2 => FsAudioPlayer.MediaPlayer;
        private Windows.Media.Playback.MediaPlaybackSession FsAudioSession => FsAudioPlayer2.PlaybackSession;

        public async void OpenAudioFullscreen(string filePath, TimeSpan position)
        {
            Log.Info("OpenAudioFullscreen: {Path}", filePath);
            int gen = ++_fsGeneration;
            bool wasAlreadyFullscreen = _isAudioFullscreen;
            _audioFullscreenPath = filePath;
            _isAudioFullscreen = true;

            MediaPreview.Stop();

            StopFsAudioAnalysis();
            if (!wasAlreadyFullscreen)
                _fsVisualizerMode = AudioFullscreenMode.Default;
            _fsAudioLevelService = new AudioLevelService();
            _fsAudioLevelService.MediaOpened += OnFsAudioOpened;
            _fsAudioLevelService.MediaEnded += OnFsAudioEnded;
            _fsAudioLevelService.MediaFailed += OnFsAudioFailed;
#if AUDIO_ANALYSIS
            FsVuMeter.AttachService(_fsAudioLevelService);
#endif
            await _fsAudioLevelService.LoadAndPlay(filePath);

            if (gen != _fsGeneration)
            {
                Log.Dbg("OpenAudioFullscreen: stale generation, aborting");
                return;
            }

            if (position > TimeSpan.Zero)
                _fsAudioLevelService.Seek(position);

            FsPlayPauseIcon.Glyph = "\uE769";
            FsVolumeText.Text = $"Vol {(int)(_audioVolume * 100)}%";

            FsTitleText.Text = System.IO.Path.GetFileNameWithoutExtension(filePath);
            FsArtistText.Text = "";
            FsArtistText.Visibility = Visibility.Collapsed;
            FsAlbumText.Text = "";
            FsAlbumText.Visibility = Visibility.Collapsed;
            FsAlbumArtBorder.Visibility = Visibility.Collapsed;
            FsDefaultArtPanel.Visibility = Visibility.Visible;
            _fsHasAlbumArt = false;

            AudioFullScreenPanel.Visibility = Visibility.Visible;
            FsVuMeter.EnsureInitialized();
            UpdateMediaPlayerFocusUI();
            UpdateDisplayRequest();

            if (_fullscreenProgressTimer.IsEnabled == false)
                _fullscreenProgressTimer.Start();

            _ = LoadAudioFullscreenMetadataAsync(filePath);

            if (_fsVisualizerMode != AudioFullscreenMode.Default)
                ApplyAudioVisualizerMode();
        }

        private async Task LoadAudioFullscreenMetadataAsync(string filePath)
        {
            int gen = _fsGeneration;
            try
            {
                Log.Dbg("FsMetadata: starting async load for {Path}", filePath);
                _fsMetadataGuesser.SetInternetAvailable(true);
                var match = await _fsMetadataGuesser.ResolveAsync(filePath);
                var tag = match?.Metadata;

                Log.Info("FsMetadata: source={Source} score={Score:F2} title='{Title}' artist='{Artist}' album='{Album}' art={HasArt}",
                    match?.Source, match?.Confidence, tag?.Title, tag?.Artist, tag?.Album, tag?.HasAlbumArt);

                if (gen != _fsGeneration || _audioFullscreenPath != filePath)
                {
                    Log.Dbg("FsMetadata: stale result for {Path}, discarding", filePath);
                    return;
                }

                bool hasArt = tag?.HasAlbumArt == true;
                _fsHasAlbumArt = hasArt;

                if (hasArt)
                {
                    var bitmap = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                    using (var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream())
                    {
                        await stream.WriteAsync(tag.AlbumArt.AsBuffer());
                        stream.Seek(0);
                        await bitmap.SetSourceAsync(stream);
                    }
                    _fsAlbumArtBitmap = bitmap;
                    FsAlbumArtBorder.Visibility = Visibility.Visible;
                    FsDefaultArtPanel.Visibility = Visibility.Collapsed;
                    FsAlbumArtImage.Source = bitmap;
                }

                if (tag?.HasTitle == true)
                    FsTitleText.Text = tag.Title;
                if (tag?.HasArtist == true)
                {
                    FsArtistText.Text = tag.Artist;
                    FsArtistText.Visibility = Visibility.Visible;
                }
                if (tag?.HasAlbum == true)
                {
                    FsAlbumText.Text = tag.Album;
                    FsAlbumText.Visibility = Visibility.Visible;
                }

                Log.Info("FsMetadata: applied title={Title} artist={Artist} album={Album} art={HasArt}",
                    tag?.Title, tag?.Artist, tag?.Album, hasArt);

                ApplyAudioVisualizerMode();
                ShowTrackInfoOsd();
            }
            catch (Exception ex)
            {
                Log.Warn("FsMetadata: failed for {Path}", filePath, ex);
            }
        }

        public void CloseAudioFullscreen()
        {
            Log.Info("CloseAudioFullscreen");
            StopFsAudioAnalysis();
            FsVisualizerCanvas.Deactivate();
            FsVisualizerCanvas.DetachService();
            FsVisualizerCanvas.Visibility = Visibility.Collapsed;
            FsTrackInfoBorder.Visibility = Visibility.Collapsed;
            _fsVisualizerMode = AudioFullscreenMode.Default;
            _isAudioFullscreen = false;
            _audioFullscreenPath = null;
            AudioFullScreenPanel.Visibility = Visibility.Collapsed;
            // Stop shared progress timer only if no video fullscreen is active
            if (VideoFullScreenPanel.Visibility != Visibility.Visible)
                _fullscreenProgressTimer.Stop();
            UpdateDisplayRequest();
        }

        public void ToggleAudioFullscreenPlayPause()
        {
            if (_fsAudioLevelService == null || !_fsAudioLevelService.IsFileLoaded) return;

            _fsAudioLevelService.TogglePlayPause();

            if (_fsAudioLevelService.IsPlaying)
            {
                FsPlayPauseIcon.Glyph = "\uE769";
                ShowAudioOsd("Play", "ms-appx:///Assets/Views/MillerColumnsPage/osd/osd-play-48.png", 1200);
            }
            else
            {
                FsPlayPauseIcon.Glyph = "\uE768";
                ShowAudioOsd("Pause", "ms-appx:///Assets/Views/MillerColumnsPage/osd/osd-pause-48.png", 1200);
            }
        }

        private void NavigatePreviewTrack(int direction)
        {
            if (string.IsNullOrEmpty(MediaPreview.CurrentFilePath) || _navigator.Current == null)
            {
                Log.Warn("NavigatePreviewTrack: early exit — filePath={FilePath} current={Current}", MediaPreview.CurrentFilePath ?? "(null)", _navigator.Current != null);
                return;
            }

            var audioFiles = _navigator.Current.Entries
                .Where(e => !e.IsDirectory && FilePreviewService.IsAudioFile(System.IO.Path.GetExtension(e.Name)))
                .ToList();

            if (audioFiles.Count == 0)
            {
                Log.Warn("NavigatePreviewTrack: no audio files in current directory ({Total} entries total)", _navigator.Current.Entries.Count);
                return;
            }

            int currentIdx = audioFiles.FindIndex(e =>
                string.Equals(e.FullPath, MediaPreview.CurrentFilePath, StringComparison.OrdinalIgnoreCase));

            Log.Info("NavigatePreviewTrack: {Count} audio files, currentIdx={Idx}, direction={Dir}", audioFiles.Count, currentIdx, direction > 0 ? "next" : "prev");

            int nextIdx = currentIdx + direction;
            if (nextIdx < 0) nextIdx = audioFiles.Count - 1;
            if (nextIdx >= audioFiles.Count) nextIdx = 0;

            var nextFile = audioFiles[nextIdx];
            Log.Info("NavigatePreviewTrack: {Direction} to {Path}", direction > 0 ? "next" : "prev", nextFile.FullPath);

            int mainIdx = _navigator.Current.Entries.IndexOf(nextFile);
            if (mainIdx >= 0)
            {
                _updating = true;
                CurrentList.SelectedIndex = mainIdx;
                _updating = false;

                if (_navigator.Preview != null)
                    _navigator.Preview.PreviewFilePath = nextFile.FullPath;

                int totalCount = _navigator.Current?.Entries.Count ?? 0;
                FooterItemCount.Text = totalCount > 0 ? $"{mainIdx + 1}/{totalCount}" : "";
            }

            MediaPreview.Stop();
            MediaPreview.LoadFile(nextFile.FullPath);
            MediaPreview.TogglePlayPause();
        }

        private void NavigatePreviewVideoTrack(int direction)
        {
            if (string.IsNullOrEmpty(MediaPreview.CurrentFilePath) || _navigator.Current == null)
            {
                Log.Warn("NavigatePreviewVideoTrack: early exit — filePath={FilePath} current={Current}", MediaPreview.CurrentFilePath ?? "(null)", _navigator.Current != null);
                return;
            }

            var videoFiles = _navigator.Current.Entries
                .Where(e => !e.IsDirectory && FilePreviewService.IsVideoFile(System.IO.Path.GetExtension(e.Name)))
                .ToList();

            if (videoFiles.Count == 0)
            {
                Log.Warn("NavigatePreviewVideoTrack: no video files in current directory ({Total} entries total)", _navigator.Current.Entries.Count);
                return;
            }

            int currentIdx = videoFiles.FindIndex(e =>
                string.Equals(e.FullPath, MediaPreview.CurrentFilePath, StringComparison.OrdinalIgnoreCase));

            Log.Info("NavigatePreviewVideoTrack: {Count} video files, currentIdx={Idx}, direction={Dir}", videoFiles.Count, currentIdx, direction > 0 ? "next" : "prev");

            int nextIdx = currentIdx + direction;
            if (nextIdx < 0) nextIdx = videoFiles.Count - 1;
            if (nextIdx >= videoFiles.Count) nextIdx = 0;

            var nextFile = videoFiles[nextIdx];
            Log.Info("NavigatePreviewVideoTrack: {Direction} to {Path}", direction > 0 ? "next" : "prev", nextFile.FullPath);

            int mainIdx = _navigator.Current.Entries.IndexOf(nextFile);
            if (mainIdx >= 0)
            {
                _updating = true;
                CurrentList.SelectedIndex = mainIdx;
                _updating = false;

                if (_navigator.Preview != null)
                    _navigator.Preview.PreviewFilePath = nextFile.FullPath;

                int totalCount = _navigator.Current?.Entries.Count ?? 0;
                FooterItemCount.Text = totalCount > 0 ? $"{mainIdx + 1}/{totalCount}" : "";
            }

            MediaPreview.Stop();
            MediaPreview.LoadFile(nextFile.FullPath);
            MediaPreview.TogglePlayPause();
        }

        public void NavigateAudioTrack(int direction)
        {
            if (string.IsNullOrEmpty(_audioFullscreenPath) || _navigator.Current == null) return;

            var audioFiles = _navigator.Current.Entries
                .Where(e => !e.IsDirectory && FilePreviewService.IsAudioFile(System.IO.Path.GetExtension(e.Name)))
                .ToList();

            if (audioFiles.Count == 0) return;

            int currentIdx = audioFiles.FindIndex(e =>
                string.Equals(e.FullPath, _audioFullscreenPath, StringComparison.OrdinalIgnoreCase));

            int nextIdx = currentIdx + direction;
            if (nextIdx < 0) nextIdx = audioFiles.Count - 1;
            if (nextIdx >= audioFiles.Count) nextIdx = 0;

            var nextFile = audioFiles[nextIdx];
            _audioFullscreenPath = nextFile.FullPath;

            // Show placeholder immediately
            FsTitleText.Text = System.IO.Path.GetFileNameWithoutExtension(nextFile.FullPath);
            FsArtistText.Text = "";
            FsArtistText.Visibility = Visibility.Collapsed;
            FsAlbumText.Text = "";
            FsAlbumText.Visibility = Visibility.Collapsed;
            FsAlbumArtBorder.Visibility = Visibility.Collapsed;
            FsDefaultArtPanel.Visibility = Visibility.Visible;
            _fsHasAlbumArt = false;

            // Load next track via AudioGraph (playback + VU meter)
            StopFsAudioAnalysis();
            _fsAudioLevelService = new AudioLevelService();
            _fsAudioLevelService.MediaOpened += OnFsAudioOpened;
            _fsAudioLevelService.MediaEnded += OnFsAudioEnded;
            _fsAudioLevelService.MediaFailed += OnFsAudioFailed;
#if AUDIO_ANALYSIS
            FsVuMeter.AttachService(_fsAudioLevelService);
#endif
            _ = _fsAudioLevelService.LoadAndPlay(nextFile.FullPath);

            // Re-apply current visualizer mode with new audio service
            if (_fsVisualizerMode != AudioFullscreenMode.Default)
                ApplyAudioVisualizerMode();

            FsPlayPauseIcon.Glyph = "\uE769";
            ShowAudioOsd(direction > 0 ? "Next" : "Prev", direction > 0 ? "ms-appx:///Assets/Views/MillerColumnsPage/osd/osd-next-48.png" : "ms-appx:///Assets/Views/MillerColumnsPage/osd/osd-prev-48.png", 1200);

            _ = LoadAudioFullscreenMetadataAsync(nextFile.FullPath);

            // Update selection in main list
            int mainIdx = _navigator.Current.Entries.IndexOf(nextFile);
            if (mainIdx >= 0)
            {
                _updating = true;
                CurrentList.SelectedIndex = mainIdx;
                _updating = false;
            }
        }

        private void StopFsAudioAnalysis()
        {
#if AUDIO_ANALYSIS
            FsVuMeter.DetachService();
#endif
            if (_fsAudioLevelService != null)
            {
                _fsAudioLevelService.MediaOpened -= OnFsAudioOpened;
                _fsAudioLevelService.MediaEnded -= OnFsAudioEnded;
                _fsAudioLevelService.MediaFailed -= OnFsAudioFailed;
                _fsAudioLevelService.Dispose();
                _fsAudioLevelService = null;
            }
        }

        private async void OnFsAudioOpened(object sender, EventArgs e)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                FsPlayPauseIcon.Glyph = "\uE769";
                Log.Info("FsAudio: opened");
            });
        }

        private async void OnFsAudioEnded(object sender, EventArgs e)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                Log.Info("FsAudio: ended — auto-advancing");
                NavigateAudioTrack(1);
            });
        }

        private async void OnFsAudioFailed(object sender, EventArgs e)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                Log.Warn("FsAudio: failed");
                FsPlayPauseIcon.Glyph = "\uE768";
            });
        }

        // --- File Action Sheet ---

        private async Task ShowFileActionSheetAsync()
        {
            var selected = CurrentList.SelectedItem as EntryViewModel;

            FileEntry entry;
            if (selected != null)
            {
                entry = new FileEntry
                {
                    Name = selected.Name,
                    FullPath = selected.FullPath,
                    IsDirectory = selected.IsDirectory,
                    IsDrive = selected.IsDrive,
                    IsArchive = selected.IsArchive,
                    SizeBytes = selected.SizeBytes,
                    ArchiveRootPath = selected.ArchiveRootPath,
                    ArchiveInternalPath = selected.ArchiveInternalPath
                };
            }
            else
            {
                var currentPath = _navigator.Current?.Path ?? "";
                entry = new FileEntry
                {
                    Name = System.IO.Path.GetFileName(currentPath) ?? currentPath,
                    FullPath = currentPath,
                    IsDirectory = true
                };
            }

            Log.Info("ShowFileActionSheetAsync: file={File}, isDir={IsDir}, isArchive={IsArchive}",
                entry.Name, entry.IsDirectory, entry.IsArchive);

            UpdateFooterALabel("Select");
            var action = await FileActionSheetControl.ShowAsync(entry);
            UpdateFooterALabelFromSelection();
            if (action == null)
            {
                Log.Verb("ShowFileActionSheetAsync: cancelled");
                return;
            }

            Log.Info("ShowFileActionSheetAsync: action={Action}", action);

            switch (action)
            {
                case FileAction.Copy:
                    await HandleCopyAsync(entry);
                    break;
                case FileAction.Paste:
                    await HandlePasteAsync();
                    break;
                case FileAction.Move:
                    await HandleMoveAsync(entry);
                    break;
                case FileAction.Rename:
                    await HandleRenameAsync(entry);
                    break;
                case FileAction.Delete:
                    await HandleDeleteAsync(entry);
                    break;
                case FileAction.Extract:
                    await HandleExtractAsync(entry);
                    break;
                case FileAction.ExtractFile:
                    await HandleExtractFileAsync(entry);
                    break;
                case FileAction.CreateFolder:
                    await HandleCreateFolderAsync(entry);
                    break;
                case FileAction.CreateZip:
                    await HandleCreateZipAsync(entry);
                    break;
                case FileAction.Refresh:
                    OnRefresh();
                    break;
                case FileAction.Edit:
                    await HandleEditAsync(entry);
                    break;
                case FileAction.Share:
                    await HandleShareAsync(entry);
                    break;
            }
        }

        private async Task HandleCopyAsync(FileEntry entry)
        {
            Log.Info("HandleCopyAsync: {File} → clipboard", entry.FullPath);
            ClipboardState.Copy(new[] { entry });
            UpdateClipboardIndicator();
            await Task.CompletedTask;
        }

        private async Task HandlePasteAsync()
        {
            if (!ClipboardState.HasItems) return;

            var destDir = _navigator.Current?.Path;
            if (string.IsNullOrEmpty(destDir))
            {
                Log.Warn("HandlePasteAsync: no current directory");
                await Task.CompletedTask;
                return;
            }

            var entries = ClipboardState.Entries;
            Log.Info("HandlePasteAsync: {Count} items → {Dest}",
                entries.Count, destDir);

            int fileIndex = 0;
            foreach (var entry in entries)
            {
                fileIndex++;
                bool sameDir = string.Equals(
                    System.IO.Path.GetDirectoryName(entry.FullPath)?.TrimEnd('\\'),
                    destDir.TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);

                var progress = new Progress<FileOperations.OperationProgress>(p =>
                {
                    p.FileIndex = fileIndex;
                    p.FileTotal = entries.Count;
                    Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        OpProgressDialog.UpdateProgress(p));
                });

                OpProgressDialog.Show("Copying", entry.Name, destDir,
                    fileIndex, entries.Count);

                var result = await FileOperations.CopyAsync(
                    entry.FullPath, destDir, progress, sameDir, OpProgressDialog.CancelToken);

                if (result == FileOperations.OperationResult.Cancelled)
                {
                        Log.Dbg("HandlePasteAsync: cancelled at file {Index}/{Total}", fileIndex, entries.Count);
                    OpProgressDialog.Cancel();
                    await Task.Delay(1500);
                    OpProgressDialog.Close();
                    break;
                }

                OpProgressDialog.TrackCompleted(entry.Name);
                OpProgressDialog.Complete();
                await Task.Delay(400);
                OpProgressDialog.Close();

                if (result != FileOperations.OperationResult.Success)
                {
                    Log.Warn("HandlePasteAsync: {File} failed", entry.Name);
                    _ = AlertDialogControl.ShowAsync($"Copy failed: \"{entry.Name}\".", AlertType.Error);
                }
            }

            UpdateClipboardIndicator();
            await _navigator.RefreshCurrentAsync();
        }

        private async Task HandleMoveAsync(FileEntry entry)
        {
            Log.Info("HandleMoveAsync: {File}", entry.FullPath);

            // 1. Choose destination folder
            UpdateFooterALabel("Select");
            var destDir = await FolderBrowserDialogControl.ShowAsync(_navigator.Current?.Path ?? null);
            UpdateFooterALabelFromSelection();

            if (string.IsNullOrEmpty(destDir))
            {
                Log.Verb("HandleMoveAsync: cancelled at folder browser");
                return;
            }

            // Don't move to same directory
            if (string.Equals(
                System.IO.Path.GetDirectoryName(entry.FullPath)?.TrimEnd('\\'),
                destDir.TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase))
            {
                        Log.Dbg("HandleMoveAsync: same directory, skipping");
                _ = AlertDialogControl.ShowAsync("Source and destination are the same folder.", AlertType.Error);
                return;
            }

            // 2. Build file list for confirmation
            var (files, folderCount) = await FileOperations.ListRecursiveAsync(entry.FullPath);
            UpdateFooterALabel("Confirm");
            bool confirmed = await FileOperationConfirmDialogControl.ShowMoveAsync(entry.Name, destDir, files, folderCount);
            UpdateFooterALabelFromSelection();

            if (!confirmed)
            {
                Log.Verb("HandleMoveAsync: confirmation cancelled");
                return;
            }

            // 3. Execute move with progress
            var progress = new Progress<FileOperations.OperationProgress>(p =>
            {
                Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    OpProgressDialog.UpdateProgress(p));
            });

            OpProgressDialog.Show("Moving", entry.Name, destDir);
            var result = await FileOperations.MoveAsync(entry.FullPath, destDir, progress, OpProgressDialog.CancelToken);

            if (result == FileOperations.OperationResult.Cancelled)
            {
                    Log.Dbg("HandleMoveAsync: cancelled");
                OpProgressDialog.Cancel();
                await Task.Delay(1500);
                OpProgressDialog.Close();
                return;
            }

            OpProgressDialog.Complete();
            await Task.Delay(400);
            OpProgressDialog.Close();

            if (result == FileOperations.OperationResult.Success)
            {
                Log.Info("HandleMoveAsync: success");
                await _navigator.RefreshCurrentAsync();
            }
            else
            {
                Log.Warn("HandleMoveAsync: failed");
                _ = AlertDialogControl.ShowAsync($"Failed to move \"{entry.Name}\".", AlertType.Error);
            }
        }

        private async Task HandleRenameAsync(FileEntry entry)
        {
            Log.Info("HandleRenameAsync: {File}", entry.FullPath);
            var newName = await InputDialogControl.ShowAsync("Rename", entry.Name);
            if (string.IsNullOrEmpty(newName) || newName == entry.Name)
            {
                Log.Verb("HandleRenameAsync: cancelled or unchanged");
                return;
            }

            var invalidChars = new char[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };
            if (newName.IndexOfAny(invalidChars) >= 0)
            {
                Log.Warn("HandleRenameAsync: invalid characters in name");
                CurrentStatus.Text = "Invalid characters in name";
                return;
            }

            var reservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "CON", "PRN", "AUX", "NUL",
                "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
            };
            var nameNoExt = Path.GetFileNameWithoutExtension(newName);
            if (reservedNames.Contains(nameNoExt))
            {
                Log.Warn("HandleRenameAsync: reserved name");
                CurrentStatus.Text = "Reserved name";
                return;
            }

                var confirmed = await AlertDialogControl.ShowConfirmAsync($"Rename '{entry.Name}' to '{newName}'?");
            if (!confirmed)
            {
                Log.Verb("HandleRenameAsync: confirmation cancelled");
                return;
            }

            var result = await FileOperations.RenameAsync(entry.FullPath, newName);
            if (result == FileOperations.OperationResult.Success)
            {
                Log.Info("HandleRenameAsync: success — refreshing");
                await _navigator.RefreshCurrentAsync(newName);
            }
            else
            {
                Log.Warn("HandleRenameAsync: failed");
                _ = AlertDialogControl.ShowAsync($"Failed to rename \"{entry.Name}\".", AlertType.Error);
            }
        }

        private async Task HandleDeleteAsync(FileEntry entry)
        {
            Log.Info("HandleDeleteAsync: {File}", entry.FullPath);

            // Build file list for confirmation dialog
            var (files, folderCount) = await FileOperations.ListRecursiveAsync(entry.FullPath);
            bool confirmed = await FileOperationConfirmDialogControl.ShowAsync(
                entry.Name, entry.IsDirectory, files, folderCount);
            if (!confirmed)
            {
                Log.Verb("HandleDeleteAsync: confirmation cancelled");
                return;
            }

            FileOperations.OperationResult result;
            if (entry.IsDirectory)
            {
                result = await FileOperations.DeleteDirectoryAsync(entry.FullPath);
            }
            else
            {
                result = await FileOperations.DeleteAsync(entry.FullPath);
            }

            if (result == FileOperations.OperationResult.Success)
            {
                Log.Info("HandleDeleteAsync: success — refreshing");
                await _navigator.RefreshCurrentAsync();
            }
            else
            {
                Log.Warn("HandleDeleteAsync: failed");
                _ = AlertDialogControl.ShowAsync($"Failed to delete \"{entry.Name}\".", AlertType.Error);
            }
        }

        private async Task HandleExtractAsync(FileEntry entry)
        {
            Log.Info("HandleExtractAsync: {File}", entry.FullPath);
            var currentPath = _navigator.Current?.Path;
            if (string.IsNullOrEmpty(currentPath)) return;

            var archiveName = System.IO.Path.GetFileNameWithoutExtension(entry.Name);

            // Smart unzip: if archive has a single root folder, extract here directly
            string rootFolder = await Task.Run(() => FileOperations.GetSingleRootFolder(entry.FullPath));
            bool singleRoot = rootFolder != null;
            string selectAfter = null;

            if (singleRoot)
            {
                Log.Info("HandleExtractAsync: single root folder '{Folder}' — extracting here directly", rootFolder);
                selectAfter = rootFolder;
            }
            else
            {
                var choice = await FileActionSheetControl.ShowExtractChoiceAsync(archiveName);
                if (choice == null)
                {
                    Log.Verb("HandleExtractAsync: choice cancelled");
                    return;
                }

                if (choice == FileAction.ExtractToFolder)
                    selectAfter = archiveName;
            }

            var destDir = singleRoot
                ? currentPath
                : System.IO.Path.Combine(currentPath, archiveName);

            var progress = new Progress<FileOperations.OperationProgress>(p =>
            {
                Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    OpProgressDialog.UpdateProgress(p));
            });

            // Conflict callback: shows OverwriteDialog on UI thread, returns 0=skip/1=overwrite/2=all
            var conflictCallback = new Func<string, Task<int>>(conflictFileName =>
            {
                var tcs = new TaskCompletionSource<int>();
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                {
                    try
                    {
                        int decision = await OverwriteDialogControl.ShowAsync(conflictFileName);
                        tcs.TrySetResult(decision);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("OverwriteDialog error", ex);
                        tcs.TrySetResult(0); // Skip on error
                    }
                });
                return tcs.Task;
            });

            OpProgressDialog.Show("Extracting", entry.Name, destDir);
            var result = await FileOperations.ExtractAsync(entry.FullPath, destDir, progress, conflictCallback, OpProgressDialog.CancelToken);

            if (result == FileOperations.OperationResult.Cancelled)
            {
                Log.Info("HandleExtractAsync: cancelled");
                OpProgressDialog.Cancel();
                await Task.Delay(1500);
                OpProgressDialog.Close();
                return;
            }

            OpProgressDialog.Complete();
            await Task.Delay(400);
            OpProgressDialog.Close();

            if (result == FileOperations.OperationResult.Success)
            {
                Log.Info("HandleExtractAsync: success — selecting {Select}", selectAfter ?? "(none)");
                await _navigator.RefreshCurrentAsync(selectAfter);
            }
            else
            {
                Log.Warn("HandleExtractAsync: failed");
                _ = AlertDialogControl.ShowAsync($"Failed to extract \"{entry.Name}\".", AlertType.Error);
            }
        }

        private async Task HandleExtractFileAsync(FileEntry entry)
        {
            Log.Info("HandleExtractFileAsync: {Archive}|{Internal}",
                entry.ArchiveRootPath, entry.ArchiveInternalPath);

            if (string.IsNullOrEmpty(entry.ArchiveRootPath) || string.IsNullOrEmpty(entry.ArchiveInternalPath))
            {
                Log.Warn("HandleExtractFileAsync: missing archive path info");
                return;
            }

            var destDir = System.IO.Path.GetDirectoryName(entry.ArchiveRootPath);
            if (string.IsNullOrEmpty(destDir)) return;

            var fileName = System.IO.Path.GetFileName(entry.ArchiveInternalPath);

            // Conflict callback: shows OverwriteDialog on UI thread, returns 0=skip/1=overwrite/2=all
            var conflictCallback = new Func<string, Task<int>>(conflictFileName =>
            {
                var tcs = new TaskCompletionSource<int>();
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                {
                    try
                    {
                        int decision = await OverwriteDialogControl.ShowAsync(conflictFileName);
                        tcs.TrySetResult(decision);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("OverwriteDialog error", ex);
                        tcs.TrySetResult(0);
                    }
                });
                return tcs.Task;
            });

            OpProgressDialog.Show("Extracting", fileName, destDir);
            var result = await FileOperations.ExtractFileAsync(
                entry.ArchiveRootPath, entry.ArchiveInternalPath, destDir, conflictCallback, OpProgressDialog.CancelToken);

            if (result == FileOperations.OperationResult.Cancelled)
            {
                Log.Info("HandleExtractFileAsync: cancelled");
                OpProgressDialog.Cancel();
                await Task.Delay(1500);
                OpProgressDialog.Close();
                return;
            }

            OpProgressDialog.Complete();
            await Task.Delay(400);
            OpProgressDialog.Close();

            if (result == FileOperations.OperationResult.Success)
            {
                Log.Info("HandleExtractFileAsync: success — selecting {File}", fileName);
                await _navigator.RefreshCurrentAsync(selectName: fileName);
            }
            else
            {
                Log.Warn("HandleExtractFileAsync: failed");
                _ = AlertDialogControl.ShowAsync($"Failed to extract \"{fileName}\".", AlertType.Error);
            }
        }

        private async Task HandleCreateFolderAsync(FileEntry entry)
        {
            Log.Info("HandleCreateFolderAsync: {File}", entry?.Name ?? "(none)");

            var targetDir = _navigator.Current?.Path;
            if (string.IsNullOrEmpty(targetDir))
            {
                Log.Warn("HandleCreateFolderAsync: no target directory");
                return;
            }

            // Debounce: suggest unique name if "New Folder" already exists
            var entries = _navigator.Current?.Entries;
            string defaultName = "New Folder";
            if (entries != null)
            {
                int counter = 1;
                while (entries.Any(e => string.Equals(e.Name, defaultName, StringComparison.OrdinalIgnoreCase)))
                {
                    defaultName = $"New Folder ({counter})";
                    counter++;
                }
            }

            var folderName = await InputDialogControl.ShowAsync("New Folder", defaultName);
            if (string.IsNullOrEmpty(folderName))
            {
                Log.Verb("HandleCreateFolderAsync: name cancelled");
                return;
            }

            var fullPath = System.IO.Path.Combine(targetDir, folderName);
            var result = await FileOperations.CreateFolderAsync(fullPath);
            if (result == FileOperations.OperationResult.Success)
            {
                Log.Info("HandleCreateFolderAsync: success — refreshing and selecting '{Name}'", folderName);
                await _navigator.RefreshCurrentAsync(selectName: folderName);
            }
            else
            {
                Log.Warn("HandleCreateFolderAsync: failed");
                _ = AlertDialogControl.ShowAsync($"Failed to create folder \"{folderName}\".", AlertType.Error);
            }
        }

        private async Task HandleCreateZipAsync(FileEntry entry)
        {
            Log.Info("HandleCreateZipAsync: {File}", entry.FullPath);
            var zipName = await InputDialogControl.ShowAsync("Create ZIP", entry.Name + ".zip");
            if (string.IsNullOrEmpty(zipName))
            {
                Log.Verb("HandleCreateZipAsync: cancelled");
                return;
            }

            var currentPath = _navigator.Current?.Path;
            if (string.IsNullOrEmpty(currentPath)) return;

            var zipPath = System.IO.Path.Combine(currentPath, zipName);

            OpProgressDialog.Show("Creating ZIP", entry.Name, zipPath);
            var result = await FileOperations.CreateZipAsync(entry.FullPath, zipPath, null, OpProgressDialog.CancelToken);

            if (result == FileOperations.OperationResult.Cancelled)
            {
                Log.Info("HandleCreateZipAsync: cancelled");
                OpProgressDialog.Cancel();
                await Task.Delay(1500);
                OpProgressDialog.Close();
                return;
            }

            OpProgressDialog.Complete();
            await Task.Delay(400);
            OpProgressDialog.Close();

            if (result == FileOperations.OperationResult.Success)
            {
                Log.Info("HandleCreateZipAsync: success — selecting '{Name}'", zipName);
                await _navigator.RefreshCurrentAsync(selectName: zipName);
            }
            else
            {
                Log.Warn("HandleCreateZipAsync: failed");
                _ = AlertDialogControl.ShowAsync($"Failed to create ZIP \"{zipName}\".", AlertType.Error);
            }
        }

        private string _lastErrorText = "";

        public void ShowError(string title, string description, string details)
        {
            ErrorTitleText.Text = title;
            ErrorDescriptionText.Text = description;
            ErrorDetailsText.Text = details;
            ErrorOverlay.Visibility = Visibility.Visible;
            ErrorOverlay.Opacity = 0;

            var fadeIn = new DoubleAnimation { To = 1.0, Duration = new Duration(TimeSpan.FromMilliseconds(200)) };
            Storyboard.SetTarget(fadeIn, ErrorOverlay);
            Storyboard.SetTargetProperty(fadeIn, "Opacity");
            var sb = new Storyboard();
            sb.Children.Add(fadeIn);
            sb.Begin();

            _lastErrorText = $"[{title}] {description}\n\n{details}";
            Log.Warn("Error overlay shown: {Title} — {Description}", title, description);
        }

        private void HideError()
        {
            ErrorOverlay.Visibility = Visibility.Collapsed;
        }

        private async void OnErrorCloseClick(object sender, RoutedEventArgs e)
        {
            HideError();
        }

        private async void OnErrorCopyClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dp.SetText(_lastErrorText);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
                Log.Info("Error details copied to clipboard");
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to copy error to clipboard", ex);
            }
        }

        private async void OnErrorReportClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var uri = new Uri("https://github.com/marcelofrau/x-files-uwp/issues/new?template=bug_report.md&title=" +
                    Uri.EscapeDataString("Error: " + ErrorTitleText.Text));
                await Windows.System.Launcher.LaunchUriAsync(uri);
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to open GitHub", ex);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class BooleanToBrushConverter : IValueConverter
    {
        public Brush TrueBrush { get; set; }
        public Brush FalseBrush { get; set; }

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return (value is bool b && b) ? TrueBrush : FalseBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
