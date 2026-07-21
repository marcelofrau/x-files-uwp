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
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using XFiles.Audio;
using XFiles.FileSystem;
using XFiles.Navigation;

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
            Log.Information("MillerColumnsPage.ctor");
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

            PreviewCodeView.NavigationStarting += OnPreviewNavigationStarting;
            PreviewCodeView.NavigationCompleted += OnPreviewNavigationCompleted;

            MediaPreview.PlayerStateChanged += OnMediaPlayerStateChanged;

            var v = Package.Current.Id.Version;
            var version = $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
            VersionText.Text = $"v{version}";
            AboutVersionText.Text = $"v{version}";

            _ = _navigator.LoadRootAsync();
        }

        private bool _isMediaPlayerActive;
        private int _lastValidSelectedIndex = -1;

        private void OnMediaPlayerStateChanged(object sender, EventArgs e)
        {
            _isMediaPlayerActive = MediaPreview.IsPlayerActive;
            UpdateMediaPlayerFocusUI();
        }

        private void UpdateMediaPlayerFocusUI()
        {
            if (_isMediaPlayerActive)
            {
                ParentColumn.Opacity = 0.3;
                CurrentColumn.Opacity = 0.6;
                ParentColumn.IsHitTestVisible = false;
                CurrentColumn.IsHitTestVisible = false;

                FooterALabel.Text = "Pause";
                FooterBLabel.Text = "Stop";
                FooterXLabel.Text = "Fullscreen";
                FooterLTLabel.Text = "-5s";
                FooterRTLabel.Text = "+5s";
                FooterLBLabel.Text = "-5s";
                FooterRBLabel.Text = "+5s";
                FooterLTRT.Visibility = Visibility.Visible;
                FooterLBRB.Visibility = Visibility.Visible;
            }
            else
            {
                ParentColumn.Opacity = 1.0;
                CurrentColumn.Opacity = 1.0;
                ParentColumn.IsHitTestVisible = true;
                CurrentColumn.IsHitTestVisible = true;

                FooterLTRT.Visibility = Visibility.Collapsed;
                FooterLBRB.Visibility = Visibility.Collapsed;
                UpdateFooterALabelFromSelection();
                FooterBLabel.Text = "Back";
                FooterXLabel.Text = "Refresh";
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Log.Verbose("MillerColumnsPage loaded — setting focus");
            CurrentList.Focus(FocusState.Programmatic);
            if (App.GamepadInput != null)
            {
                App.GamepadInput.ActiveNavigable = this;
                Log.Information("MillerColumnsPage: set as ActiveNavigable");
            }
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
            Log.Verbose("Loading state: {IsLoading}", isLoading);
            CurrentLoading.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            CurrentList.Opacity = isLoading ? 0.4 : 1.0;
        }

        private void OnError(string message)
        {
            Log.Error("MillerColumnsPage error: {Message}", args: message);
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

                // Parent column
                if (!atRoot)
                {
                    ParentHeader.Text = _navigator.Parent.Label ?? "";
                    BindList(ParentList, _navigator.Parent);
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
                Log.Verbose("UpdatePreviewColumn: preview is null");
                return;
            }

            Log.Verbose("UpdatePreviewColumn: label={Label}, isFile={IsFile}, type={Type}",
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
                        Log.Information("UpdatePreviewColumn: ext={Ext} isPlainText={IsPlainText} contentLen={Len}",
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

                    case FilePreviewType.Video:
                    case FilePreviewType.Audio:
                        string mediaPath = _navigator.Preview.PreviewFilePath;
                        Log.Information("UpdatePreviewColumn: media type={Type} path={Path}", _navigator.Preview.PreviewType, mediaPath);
                        PreviewStatus.Text = _navigator.Preview.PreviewFileType;
                        PreviewMediaPanel.Visibility = Visibility.Visible;
                        _pendingMediaPath = mediaPath;
                        _mediaLoadTimer.Stop();
                        _mediaLoadTimer.Start();
                        break;

                    case FilePreviewType.Error:
                        PreviewErrorText.Text = _navigator.Preview.PreviewErrorMessage ?? "Unknown error";
                        PreviewStatus.Text = "";
                        PreviewErrorPanel.Visibility = Visibility.Visible;
                        break;

                    case FilePreviewType.Unsupported:
                        PreviewUnsupportedType.Text = $"No preview available ({_navigator.Preview.PreviewFileType})";
                        PreviewUnsupportedSize.Text = FormatSize(_navigator.Preview.PreviewFileSize);
                        PreviewStatus.Text = "";
                        PreviewUnsupportedPanel.Visibility = Visibility.Visible;
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

            Log.Information("BuildHighlightHtmlAsync: ext={Ext} lang={Lang} cssLen={CssLen} codeLen={CodeLen} jsLen={JsLen}",
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
                Log.Information("LoadHighlightHtml: NavigateToString ({Len} chars)", html.Length);
                PreviewCodeView.NavigateToString(html);
            }
            catch (Exception ex)
            {
                Log.Error("Failed NavigateToString", ex);
            }
        }

        private static async Task EnsureHighlightAssetsLoadedAsync()
        {
            if (_highlightJs != null && _highlightCss != null && _fontBase64 != null) return;

            try
            {
                Log.Information("EnsureHighlightAssetsLoadedAsync: loading JS...");
                var jsFile = await StorageFile.GetFileFromApplicationUriAsync(
                    new Uri("ms-appx:///Assets/highlight.min.js"));
                _highlightJs = await FileIO.ReadTextAsync(jsFile);
                Log.Information("EnsureHighlightAssetsLoadedAsync: JS loaded, {Len} chars", _highlightJs.Length);

                Log.Information("EnsureHighlightAssetsLoadedAsync: loading CSS...");
                var cssFile = await StorageFile.GetFileFromApplicationUriAsync(
                    new Uri("ms-appx:///Assets/highlight-aco.css"));
                _highlightCss = await FileIO.ReadTextAsync(cssFile);
                Log.Information("EnsureHighlightAssetsLoadedAsync: CSS loaded, {Len} chars", _highlightCss.Length);

                Log.Information("EnsureHighlightAssetsLoadedAsync: loading font...");
                var fontFile = await StorageFile.GetFileFromApplicationUriAsync(
                    new Uri("ms-appx:///Assets/Inconsolata-Regular.ttf"));
                var fontBytes = await Task.Run(() => System.IO.File.ReadAllBytes(fontFile.Path));
                _fontBase64 = Convert.ToBase64String(fontBytes);
                Log.Information("EnsureHighlightAssetsLoadedAsync: font loaded, {Len} bytes, b64={B64Len}",
                    fontBytes.Length, _fontBase64.Length);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to load highlight.js assets", ex);
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
                ArchiveRootPath = e.ArchiveRootPath
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
                ArchiveRootPath = e.ArchiveRootPath
            }).ToList();

            SlideColumn(_slideFromRight);

            CurrentList.ItemsSource = vms;

            Log.Information("BindCurrentList: state.SelectedIndex={StateIndex}, itemCount={Count}", state.SelectedIndex, vms.Count);
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
            Log.Information("SelectionChanged: index={Index}, updating={Updating}, mediaActive={MediaActive}",
                CurrentList.SelectedIndex, _updating, _isMediaPlayerActive);

            // stop marquee on previous selection
            StopMarqueeOnIndex(_lastValidSelectedIndex);

            if (_updating) return;
            if (_isMediaPlayerActive)
            {
                // Revert selection — ListView's built-in keyboard nav changed it before we could block
                if (_lastValidSelectedIndex >= 0 && _lastValidSelectedIndex < CurrentList.Items.Count)
                    CurrentList.SelectedIndex = _lastValidSelectedIndex;
                return;
            }
            _lastValidSelectedIndex = CurrentList.SelectedIndex;
            if (CurrentList.SelectedIndex >= 0 && _navigator.Current != null)
            {
                _navigator.Current.SelectedIndex = CurrentList.SelectedIndex;

                // start marquee on new selection
                StartMarqueeOnIndex(CurrentList.SelectedIndex);

                // Instant loading feedback — clear stale preview immediately
                HideAllPreviewPanels();
                var selected = CurrentList.SelectedItem as EntryViewModel;
                PreviewHeader.Text = selected?.Name ?? "";
                PreviewStatus.Text = "Loading...";

                _ = _navigator.OnSelectionChangedAsync();
            }

            // Update footer count
            int totalCount = _navigator.Current?.Entries.Count ?? 0;
            int selectedIndex = CurrentList.SelectedIndex >= 0 ? CurrentList.SelectedIndex + 1 : 0;
            FooterItemCount.Text = totalCount > 0 ? $"{selectedIndex}/{totalCount}" : "";

            // Update A button label based on selected item type
            UpdateFooterALabelFromSelection();
        }

        private void StartMarqueeOnIndex(int index)
        {
            if (index < 0) return;
            var container = CurrentList.ContainerFromIndex(index) as ContentPresenter;
            if (container == null) return;
            var marquee = FindMarqueeTextBlock(container);
            if (marquee != null) marquee.Marquee = true;
        }

        private void StopMarqueeOnIndex(int index)
        {
            if (index < 0) return;
            var container = CurrentList.ContainerFromIndex(index) as ContentPresenter;
            if (container == null) return;
            var marquee = FindMarqueeTextBlock(container);
            if (marquee != null)
            {
                marquee.Marquee = false;
                marquee.StopMarquee();
            }
        }

        private static MarqueeTextBlock FindMarqueeTextBlock(DependencyObject parent)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is MarqueeTextBlock m)
                    return m;
                var result = FindMarqueeTextBlock(child);
                if (result != null) return result;
            }
            return null;
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
                return;
            }
            if (selected.IsDirectory || selected.IsArchive)
            {
                UpdateFooterALabel("Open");
                return;
            }
            string ext = System.IO.Path.GetExtension(selected.Name);
            if (FilePreviewService.IsImageFile(ext) && !FilePreviewService.IsSvgFile(ext)
                || FilePreviewService.IsPdfFile(ext))
            {
                UpdateFooterALabel("Open");
                return;
            }
            if (FilePreviewService.IsMediaFile(ext))
            {
                UpdateFooterALabel("Play");
                return;
            }
            UpdateFooterALabel("Menu");
        }

        private void UpdateClipboardIndicator()
        {
            if (ClipboardState.HasItems)
            {
                var label = ClipboardState.IsCut ? "Cut" : "Copied";
                var count = ClipboardState.Count;
                var item = count == 1 ? "1 item" : $"{count} items";
                FooterClipboardIndicator.Text = $"📋 {label}: {item}";
                FooterClipboardIndicator.Visibility = Visibility.Visible;
            }
            else
            {
                FooterClipboardIndicator.Text = "";
                FooterClipboardIndicator.Visibility = Visibility.Collapsed;
            }
        }

        // --- Input handling ---

        private void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.Enter:
                    e.Handled = true;
                    OnConfirm();
                    break;
                case Windows.System.VirtualKey.Back:
                    e.Handled = true;
                    OnBack();
                    break;
                case Windows.System.VirtualKey.Left:
                    e.Handled = true;
                    OnBack();
                    break;
                case Windows.System.VirtualKey.Right:
                    e.Handled = true;
                    OnConfirm();
                    break;
                case Windows.System.VirtualKey.Up:
                    e.Handled = true;
                    OnDPadUp();
                    break;
                case Windows.System.VirtualKey.Down:
                    e.Handled = true;
                    OnDPadDown();
                    break;
                case Windows.System.VirtualKey.PageUp:
                    e.Handled = true;
                    OnPageUp();
                    break;
                case Windows.System.VirtualKey.PageDown:
                    e.Handled = true;
                    OnPageDown();
                    break;
                case Windows.System.VirtualKey.Home:
                    e.Handled = true;
                    OnHome();
                    break;
                case Windows.System.VirtualKey.End:
                    e.Handled = true;
                    OnEnd();
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
            Log.Information("OnPreviewNavigationStarting: uri={Uri}", args.Uri?.ToString() ?? "(null)");
        }

        private void OnPreviewNavigationCompleted(WebView sender, WebViewNavigationCompletedEventArgs args)
        {
            Log.Information("OnPreviewNavigationCompleted: isSuccess={IsSuccess}", args.IsSuccess);
        }

        public bool IsMediaFullscreen => ImageFullScreen.IsOpen || PdfFullScreen.IsOpen
            || VideoFullScreenPanel.Visibility == Visibility.Visible
            || AudioFullScreenPanel.Visibility == Visibility.Visible;

        public bool IsMediaPlayerActive => _isMediaPlayerActive;

        // --- INavigable ---

        public void OnDPadUp()
        {
            if (IsAnyFullscreen) return;
            if (IsAnyOverlayVisible) return;
            if (StartMenuControl.IsOpen) { StartMenuControl.ForwardDPad(Windows.System.VirtualKey.Up); return; }
            if (FileActionSheetControl.IsOpen) { FileActionSheetControl.ForwardDPad(Windows.System.VirtualKey.Up); return; }
            if (_isMediaPlayerActive) return;
            var before = CurrentList.SelectedIndex;
            if (CurrentList.SelectedIndex > 0)
                CurrentList.SelectedIndex--;
            Log.Information("OnDPadUp: before={Before} after={After}", before, CurrentList.SelectedIndex);
        }

        public void OnDPadDown()
        {
            if (IsAnyFullscreen) return;
            if (IsAnyOverlayVisible) return;
            if (StartMenuControl.IsOpen) { StartMenuControl.ForwardDPad(Windows.System.VirtualKey.Down); return; }
            if (FileActionSheetControl.IsOpen) { FileActionSheetControl.ForwardDPad(Windows.System.VirtualKey.Down); return; }
            if (_isMediaPlayerActive) return;
            var before = CurrentList.SelectedIndex;
            if (CurrentList.SelectedIndex < _navigator.Current?.Entries.Count - 1)
                CurrentList.SelectedIndex++;
            Log.Information("OnDPadDown: before={Before} after={After}", before, CurrentList.SelectedIndex);
        }

        public void OnDPadLeft()
        {
            if (ImageFullScreen.IsOpen) return;
            if (PdfFullScreen.IsOpen) return;
            if (AudioFullScreenPanel.Visibility == Visibility.Visible) return;
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { HandleContinuousSeek(-5); return; }
            if (IsAnyOverlayVisible) return;
            if (StartMenuControl.IsOpen) return;
            if (FileActionSheetControl.IsOpen) return;
            if (_isMediaPlayerActive) return;
            _slideFromRight = false;
            _ = _navigator.DrillOutAsync();
        }

        public void OnDPadRight()
        {
            if (ImageFullScreen.IsOpen) return;
            if (PdfFullScreen.IsOpen) return;
            if (AudioFullScreenPanel.Visibility == Visibility.Visible) return;
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { HandleContinuousSeek(5); return; }
            if (IsAnyOverlayVisible) return;
            if (StartMenuControl.IsOpen) return;
            if (FileActionSheetControl.IsOpen) return;
            if (_isMediaPlayerActive) return;
            _slideFromRight = true;
            _ = _navigator.DrillInAsync();
        }

        public void OnConfirm()
        {
            if (ErrorOverlay.Visibility == Visibility.Visible) return;
            if (IsAnyOverlayVisible) return;
            if (StartMenuControl.IsOpen) { StartMenuControl.ForwardDPad(Windows.System.VirtualKey.GamepadA); return; }
            if (ImageFullScreen.IsOpen) return;
            if (PdfFullScreen.IsOpen) return;
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { OnFsVideoInput(); return; }
            if (AudioFullScreenPanel.Visibility == Visibility.Visible) { ToggleAudioFullscreenPlayPause(); return; }
            if (FileActionSheetControl.IsOpen) { FileActionSheetControl.ForwardDPad(Windows.System.VirtualKey.GamepadA); return; }
            if (_isMediaPlayerActive)
            {
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
                    Log.Verbose("OnConfirm: image selected — opening fullscreen");
                    ImageFullScreen.Show(_navigator.Preview?.PreviewImageSource);
                }
                else if (FilePreviewService.IsPdfFile(ext))
                {
                    Log.Verbose("OnConfirm: PDF selected — opening fullscreen");
                    var preview = _navigator.Preview;
                    if (preview != null)
                    {
                        _ = PdfFullScreen.ShowAsync(
                            selected.FullPath, preview.PreviewPdfPageCount, 0);
                    }
                }
                else if (FilePreviewService.IsAudioFile(ext))
                {
                    Log.Verbose("OnConfirm: audio file — toggling play/pause");
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
                    Log.Verbose("OnConfirm: video file — toggling play/pause");
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
                    Log.Verbose("OnConfirm: file selected — showing FileActionSheet");
                    _ = ShowFileActionSheetAsync();
                }
            }
        }

        public void OnBack()
        {
            if (ErrorOverlay.Visibility == Visibility.Visible) { HideError(); return; }
            if (AboutOverlay.Visibility == Visibility.Visible) { HideAbout(); return; }
            if (PlaceholderOverlay.Visibility == Visibility.Visible) { HidePlaceholder(); return; }
            if (StartMenuControl.IsOpen) { StartMenuControl.ForwardDPad(Windows.System.VirtualKey.GamepadB); return; }
            if (ImageFullScreen.IsOpen) { ImageFullScreen.HandleButton(Windows.System.VirtualKey.GamepadB); UpdateFooterALabelFromSelection(); return; }
            if (PdfFullScreen.IsOpen) { PdfFullScreen.HandleButton(Windows.System.VirtualKey.GamepadB); UpdateFooterALabelFromSelection(); return; }
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { CloseVideoFullScreen(); UpdateFooterALabelFromSelection(); return; }
            if (AudioFullScreenPanel.Visibility == Visibility.Visible) { CloseAudioFullscreen(); UpdateMediaPlayerFocusUI(); return; }
            if (FileActionSheetControl.IsOpen) { FileActionSheetControl.ForwardDPad(Windows.System.VirtualKey.GamepadB); return; }
            if (OpProgressDialog.IsOpen) { OpProgressDialog.Close(); return; }
            if (_isMediaPlayerActive)
            {
                MediaPreview.StopPlayer();
                UpdateMediaPlayerFocusUI();
                return;
            }

            // B button → go to parent directory
            _slideFromRight = false;
            _ = _navigator.DrillOutAsync();
        }

        public void OnContextMenu()
        {
            if (IsAnyFullscreen) return;
            if (ErrorOverlay.Visibility == Visibility.Visible) return;
            if (IsAnyOverlayVisible) return;
            if (StartMenuControl.IsOpen) return;
            if (FileActionSheetControl.IsOpen) return;
            if (_isMediaPlayerActive) return;
            Log.Verbose("MillerColumnsPage.OnContextMenu — showing FileActionSheet");
            _ = ShowFileActionSheetAsync();
        }

        public void OnRefresh()
        {
            if (IsAnyFullscreen) return;
            if (FileActionSheetControl.IsOpen) return;
            if (StartMenuControl.IsOpen) return;
            if (ErrorOverlay.Visibility == Visibility.Visible) return;
            if (IsAnyOverlayVisible) return;

            // If media player is active, X goes fullscreen
            if (_isMediaPlayerActive)
            {
                if (MediaPreview.IsAudioMode)
                {
                    var pos = MediaPreview.CurrentPosition;
                    MediaPreview.StopPlayer();
                    UpdateMediaPlayerFocusUI();
                    OpenAudioFullscreen(_navigator.Preview?.PreviewFilePath ?? "", pos);
                }
                else
                {
                    MediaPreview.OpenFullscreen();
                }
                return;
            }

            // If a media file is selected (not playing yet), X opens fullscreen directly
            var selected = CurrentList.SelectedItem as EntryViewModel;
            if (selected != null)
            {
                string ext = System.IO.Path.GetExtension(selected.Name);
                if (FilePreviewService.IsAudioFile(ext))
                {
                    Log.Verbose("OnRefresh: media file selected — opening audio fullscreen");
                    MediaPreview.LoadFile(selected.FullPath);
                    OpenAudioFullscreen(selected.FullPath, TimeSpan.Zero);
                    return;
                }
                if (FilePreviewService.IsVideoFile(ext))
                {
                    Log.Verbose("OnRefresh: video file selected — opening video fullscreen");
                    _ = MediaPreviewControl.OpenFullscreenForFile(selected.FullPath);
                    return;
                }
            }

            Log.Information("OnRefresh: refreshing current directory");
            FooterSpinner.IsActive = true;
            _ = _navigator.RefreshCurrentAsync().ContinueWith(t =>
            {
                Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                    () => FooterSpinner.IsActive = false);
            });
        }

        public void OnSettings()
        {
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
            Log.Information("OnSettings — showing start menu");
            var result = await StartMenuControl.ShowAsync();
            if (result == null) return;

            switch (result.Value)
            {
                case StartMenuItem.Settings:
                    Log.Information("Start menu: Settings selected");
                    ShowPlaceholder("Settings",
                        "Configure X-Files preferences and behavior.",
                        "Settings will allow you to customize sorting, display modes, theme colors, file associations, and controller mappings. This screen is under development.");
                    break;
                case StartMenuItem.StartupPreferences:
                    Log.Information("Start menu: Startup and Preferences selected");
                    ShowPlaceholder("Startup and Preferences",
                        "Control what happens when X-Files launches.",
                        "Choose startup directory, enable or disable boot chime, set default view mode (columns/list), and configure auto-refresh intervals. Coming soon.");
                    break;
                case StartMenuItem.About:
                    Log.Information("Start menu: About selected");
                    ShowAbout();
                    break;
                case StartMenuItem.CloseApplication:
                    Log.Information("Start menu: Close Application selected");
                    Windows.UI.Xaml.Application.Current.Exit();
                    break;
            }
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
            Log.Information("Showing About overlay");
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
            || ConfirmDialogControl.Visibility == Visibility.Visible
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
            var before = CurrentList.SelectedIndex;
            if (CurrentList.SelectedIndex > 0)
                CurrentList.SelectedIndex = Math.Max(0, CurrentList.SelectedIndex - 8);
            Log.Information("OnPageUp: before={Before} after={After}", before, CurrentList.SelectedIndex);
        }

        public void OnPageDown()
        {
            if (ImageFullScreen.IsOpen) { ImageFullScreen.HandleButton((Windows.System.VirtualKey)VK_RT); return; }
            if (PdfFullScreen.IsOpen) return;
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { HandleContinuousSeek(5); return; }
            var before = CurrentList.SelectedIndex;
            if (_navigator.Current != null && CurrentList.Items.Count > 0)
                CurrentList.SelectedIndex = Math.Min(CurrentList.Items.Count - 1, CurrentList.SelectedIndex + 8);
            Log.Information("OnPageDown: before={Before} after={After}", before, CurrentList.SelectedIndex);
        }

        public void OnSeekBack()
        {
            if (ImageFullScreen.IsOpen) return;
            if (PdfFullScreen.IsOpen) { PdfFullScreen.HandleBumper(true); return; }
            if (AudioFullScreenPanel.Visibility == Visibility.Visible) { NavigateAudioTrack(-1); return; }
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { HandleContinuousSeek(-5); return; }
            if (_isMediaPlayerActive) { MediaPreview.Seek(TimeSpan.FromSeconds(-5)); return; }
            JumpByLetter(-1);
        }

        public void OnSeekForward()
        {
            if (ImageFullScreen.IsOpen) return;
            if (PdfFullScreen.IsOpen) { PdfFullScreen.HandleBumper(false); return; }
            if (AudioFullScreenPanel.Visibility == Visibility.Visible) { NavigateAudioTrack(1); return; }
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { HandleContinuousSeek(5); return; }
            if (_isMediaPlayerActive) { MediaPreview.Seek(TimeSpan.FromSeconds(5)); return; }
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
                    return;
                }
            }

            // If no different letter found, clamp to boundary
            if (direction > 0 && idx < entries.Count - 1)
                CurrentList.SelectedIndex = entries.Count - 1;
            else if (direction < 0 && idx > 0)
                CurrentList.SelectedIndex = 0;
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
            bool isMedia = VideoFullScreenPanel.Visibility == Visibility.Visible || _isMediaPlayerActive;
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
            else if (_isMediaPlayerActive)
            {
                MediaPreview.Seek(TimeSpan.FromSeconds(seconds));
                ShowFsOsd($"{(seconds > 0 ? "+" : "")}{seconds}s", null, 800);
            }
        }

        public void OnLeftStickMove(float x, float y)
        {
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
            Log.Information("OnHome: before={Before} after={After}", before, CurrentList.SelectedIndex);
        }

        public void OnEnd()
        {
            if (IsAnyFullscreen) return;
            var before = CurrentList.SelectedIndex;
            if (_navigator.Current != null && CurrentList.Items.Count > 0)
                CurrentList.SelectedIndex = CurrentList.Items.Count - 1;
            Log.Information("OnEnd: before={Before} after={After}", before, CurrentList.SelectedIndex);
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
                Log.Warning("OnScrollVertical failed: {Error}", ex.Message);
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
                Log.Warning("OnScrollHorizontal failed: {Error}", ex.Message);
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
            FsVideoPlayer.Source = Windows.Media.Core.MediaSource.CreateFromUri(source);
            FsVideoSession.Position = position;
            FsVideoPlayer.Volume = _fsVolume;
            FsVideoPlayer.Play();
            _fsVideoPlaying = true;
            FSPlayPauseIcon.Glyph = "\uE769";
            FSVolumeText.Text = $"Vol {(int)(_fsVolume * 100)}%";
            _fullscreenProgressTimer.Start();
            VideoFullScreenPanel.Visibility = Visibility.Visible;
            ShowFsControls();
            ShowFsOsd("PLAY", "ms-appx:///Assets/Views/MillerColumnsPage/osd/osd-play-48.png");
            Log.Information("ShowMediaFullscreenAsync: started fullscreen video at {Position}", position);
            await System.Threading.Tasks.Task.CompletedTask;
        }

        private void CloseVideoFullScreen()
        {
            _fullscreenProgressTimer.Stop();
            _fsHideTimer.Stop();
            _fsOsdHideTimer.Stop();
            FsVideoPlayer.Pause();
            FsVideoPlayer.Source = null;
            _fsVideoPlaying = false;
            VideoFullScreenPanel.Visibility = Visibility.Collapsed;
            Log.Information("CloseVideoFullScreen: stopped");
        }

        private void OnFullscreenProgressTick(object sender, object e)
        {
            // Fullscreen video progress
            if (VideoFullScreenPanel.Visibility == Visibility.Visible)
            {
                var total = FsVideoSession.NaturalDuration;
                if (total.TotalSeconds > 0)
                {
                    var current = FsVideoSession.Position;
                    FSProgress.Value = (current.TotalSeconds / total.TotalSeconds) * 100;
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
                    FsAudioProgress.Value = (current.TotalSeconds / total.TotalSeconds) * 100;
                    FsCurrentTimeText.Text = FormatFsTime(current);
                    FsTotalTimeText.Text = FormatFsTime(total);
                }
            }
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
                if (!MediaPreview.IsFileLoaded(_pendingMediaPath))
                {
                    Log.Information("OnMediaLoadTimerTick: loading {Path}", _pendingMediaPath);
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

        // MediaPlayer/Session helpers for fullscreen video + audio (migrated from MediaElement)
        private Windows.Media.Playback.MediaPlayer FsVideoPlayer => VideoFullScreenPlayer.MediaPlayer;
        private Windows.Media.Playback.MediaPlaybackSession FsVideoSession => FsVideoPlayer.PlaybackSession;
        private Windows.Media.Playback.MediaPlayer FsAudioPlayer2 => FsAudioPlayer.MediaPlayer;
        private Windows.Media.Playback.MediaPlaybackSession FsAudioSession => FsAudioPlayer2.PlaybackSession;

        public async void OpenAudioFullscreen(string filePath, TimeSpan position)
        {
            Log.Information("OpenAudioFullscreen: {Path}", filePath);
            _audioFullscreenPath = filePath;
            _isAudioFullscreen = true;

            MediaPreview.Stop();

            // Use AudioGraph for both playback + VU meter (no duplicate MediaPlayer)
            StopFsAudioAnalysis();
            _fsAudioLevelService = new AudioLevelService();
            _fsAudioLevelService.MediaOpened += OnFsAudioOpened;
            _fsAudioLevelService.MediaEnded += OnFsAudioEnded;
            _fsAudioLevelService.MediaFailed += OnFsAudioFailed;
            FsVuMeter.AttachService(_fsAudioLevelService);
            await _fsAudioLevelService.LoadAndPlay(filePath);

            // Seek to position after load
            if (position > TimeSpan.Zero)
                _fsAudioLevelService.Seek(position);

            FsPlayPauseIcon.Glyph = "\uE769";
            FsVolumeText.Text = $"Vol {(int)(_audioVolume * 100)}%";

            AudioFullScreenPanel.Visibility = Visibility.Visible;
            UpdateMediaPlayerFocusUI();

            // Start shared fullscreen progress timer (if not already running)
            if (_fullscreenProgressTimer.IsEnabled == false)
                _fullscreenProgressTimer.Start();

            // Load metadata async — don't block UI thread
            await LoadAudioFullscreenMetadataAsync(filePath);
        }

        private async Task LoadAudioFullscreenMetadataAsync(string filePath)
        {
            try
            {
                var tag = await Task.Run(() => Id3Tag.ReadFromFile(filePath));
                bool hasArt = tag?.AlbumArt != null && tag.AlbumArt.Length > 0;

                if (hasArt)
                {
                    var bitmap = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                    using (var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream())
                    {
                        await stream.WriteAsync(tag.AlbumArt.AsBuffer());
                        stream.Seek(0);
                        await bitmap.SetSourceAsync(stream);
                    }
                    FsAlbumArtBorder.Visibility = Visibility.Visible;
                    FsDefaultArtPanel.Visibility = Visibility.Collapsed;
                    FsAlbumArtImage.Source = bitmap;
                }
                else
                {
                    FsAlbumArtBorder.Visibility = Visibility.Collapsed;
                    FsDefaultArtPanel.Visibility = Visibility.Visible;
                }

                FsTitleText.Text = tag?.Title ?? System.IO.Path.GetFileNameWithoutExtension(filePath);
                FsArtistText.Text = tag?.Artist ?? "";
                FsArtistText.Visibility = string.IsNullOrEmpty(tag?.Artist) ? Visibility.Collapsed : Visibility.Visible;
                FsAlbumText.Text = tag?.Album ?? "";
                FsAlbumText.Visibility = string.IsNullOrEmpty(tag?.Album) ? Visibility.Collapsed : Visibility.Visible;
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to load audio metadata: {Error}", ex.Message);
                FsAlbumArtBorder.Visibility = Visibility.Collapsed;
                FsDefaultArtPanel.Visibility = Visibility.Visible;
                FsTitleText.Text = System.IO.Path.GetFileNameWithoutExtension(filePath);
                FsArtistText.Visibility = Visibility.Collapsed;
                FsAlbumText.Visibility = Visibility.Collapsed;
            }
        }

        public void CloseAudioFullscreen()
        {
            Log.Information("CloseAudioFullscreen");
            StopFsAudioAnalysis();
            _isAudioFullscreen = false;
            _audioFullscreenPath = null;
            AudioFullScreenPanel.Visibility = Visibility.Collapsed;
            // Stop shared progress timer only if no video fullscreen is active
            if (VideoFullScreenPanel.Visibility != Visibility.Visible)
                _fullscreenProgressTimer.Stop();
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

            // Load next track via AudioGraph (playback + VU meter)
            StopFsAudioAnalysis();
            _fsAudioLevelService = new AudioLevelService();
            _fsAudioLevelService.MediaOpened += OnFsAudioOpened;
            _fsAudioLevelService.MediaEnded += OnFsAudioEnded;
            _fsAudioLevelService.MediaFailed += OnFsAudioFailed;
            FsVuMeter.AttachService(_fsAudioLevelService);
            _ = _fsAudioLevelService.LoadAndPlay(nextFile.FullPath);

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
            FsVuMeter.DetachService();
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
                Log.Information("FsAudio: opened");
            });
        }

        private async void OnFsAudioEnded(object sender, EventArgs e)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                FsPlayPauseIcon.Glyph = "\uE768";
                FsAudioProgress.Value = 100;
                Log.Information("FsAudio: ended");
            });
        }

        private async void OnFsAudioFailed(object sender, EventArgs e)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                Log.Warning("FsAudio: failed");
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
                    ArchiveRootPath = selected.ArchiveRootPath
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

            Log.Information("ShowFileActionSheetAsync: file={File}, isDir={IsDir}, isArchive={IsArchive}",
                entry.Name, entry.IsDirectory, entry.IsArchive);

            UpdateFooterALabel("Select");
            var action = await FileActionSheetControl.ShowAsync(entry);
            UpdateFooterALabelFromSelection();
            if (action == null)
            {
                Log.Verbose("ShowFileActionSheetAsync: cancelled");
                return;
            }

            Log.Information("ShowFileActionSheetAsync: action={Action}", action);

            switch (action)
            {
                case FileAction.Copy:
                    await HandleCopyAsync(entry);
                    break;
                case FileAction.Cut:
                    await HandleCutAsync(entry);
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
                case FileAction.CreateFolder:
                    await HandleCreateFolderAsync(entry);
                    break;
                case FileAction.CreateZip:
                    await HandleCreateZipAsync(entry);
                    break;
                case FileAction.Refresh:
                    OnRefresh();
                    break;
            }
        }

        private async Task HandleCopyAsync(FileEntry entry)
        {
            Log.Information("HandleCopyAsync: {File} → clipboard", entry.FullPath);
            ClipboardState.Copy(new[] { entry });
            UpdateClipboardIndicator();
            await Task.CompletedTask;
        }

        private async Task HandleCutAsync(FileEntry entry)
        {
            Log.Information("HandleCutAsync: {File} → clipboard", entry.FullPath);
            ClipboardState.Cut(new[] { entry });
            UpdateClipboardIndicator();
            await Task.CompletedTask;
        }

        private async Task HandlePasteAsync()
        {
            if (!ClipboardState.HasItems) return;

            var destDir = _navigator.Current?.Path;
            if (string.IsNullOrEmpty(destDir))
            {
                Log.Warning("HandlePasteAsync: no current directory");
                await Task.CompletedTask;
                return;
            }

            var entries = ClipboardState.Entries;
            Log.Information("HandlePasteAsync: {Count} items → {Dest}, isCut={IsCut}",
                entries.Count, destDir, ClipboardState.IsCut);

            foreach (var entry in entries)
            {
                var progress = new Progress<FileOperations.OperationProgress>(p =>
                {
                    Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        OpProgressDialog.UpdateProgress(p));
                });

                var opTitle = ClipboardState.IsCut ? "Moving" : "Copying";
                OpProgressDialog.Show(opTitle, entry.Name, destDir);

                FileOperations.OperationResult result;
                if (ClipboardState.IsCut)
                {
                    result = await FileOperations.MoveAsync(entry.FullPath, destDir, progress);
                }
                else
                {
                    result = await FileOperations.CopyAsync(entry.FullPath, destDir, progress);
                }

                OpProgressDialog.Complete();
                await Task.Delay(400);
                OpProgressDialog.Close();

                if (result != FileOperations.OperationResult.Success)
                {
                    Log.Warning("HandlePasteAsync: {File} failed", entry.Name);
                    CurrentStatus.Text = $"{opTitle} failed: {entry.Name}";
                }
            }

            if (ClipboardState.IsCut)
                ClipboardState.Clear();

            UpdateClipboardIndicator();
            await _navigator.RefreshCurrentAsync();
        }

        private async Task HandleMoveAsync(FileEntry entry)
        {
            Log.Information("HandleMoveAsync: {File}", entry.FullPath);
            var destDir = await InputDialogControl.ShowAsync("Move to (full path)", _navigator.Current?.Path ?? "");
            if (string.IsNullOrEmpty(destDir)) return;

            var progress = new Progress<FileOperations.OperationProgress>(p =>
            {
                Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    OpProgressDialog.UpdateProgress(p));
            });

            OpProgressDialog.Show("Moving", entry.Name, destDir);
            var result = await FileOperations.MoveAsync(entry.FullPath, destDir, progress);
            OpProgressDialog.Complete();
            await Task.Delay(400);
            OpProgressDialog.Close();

            if (result == FileOperations.OperationResult.Success)
            {
                Log.Information("HandleMoveAsync: success");
                await _navigator.RefreshCurrentAsync();
            }
            else
            {
                Log.Warning("HandleMoveAsync: failed");
                CurrentStatus.Text = $"Move failed: {entry.Name}";
            }
        }

        private async Task HandleRenameAsync(FileEntry entry)
        {
            Log.Information("HandleRenameAsync: {File}", entry.FullPath);
            var newName = await InputDialogControl.ShowAsync("Rename", entry.Name);
            if (string.IsNullOrEmpty(newName) || newName == entry.Name)
            {
                Log.Verbose("HandleRenameAsync: cancelled or unchanged");
                return;
            }

            var invalidChars = new char[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };
            if (newName.IndexOfAny(invalidChars) >= 0)
            {
                Log.Warning("HandleRenameAsync: invalid characters in name");
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
                Log.Warning("HandleRenameAsync: reserved name");
                CurrentStatus.Text = "Reserved name";
                return;
            }

            var confirmed = await ConfirmDialogControl.ShowAsync($"Rename '{entry.Name}' to '{newName}'?");
            if (!confirmed)
            {
                Log.Verbose("HandleRenameAsync: confirmation cancelled");
                return;
            }

            var result = await FileOperations.RenameAsync(entry.FullPath, newName);
            if (result == FileOperations.OperationResult.Success)
            {
                Log.Information("HandleRenameAsync: success — refreshing");
                await _navigator.RefreshCurrentAsync(newName);
            }
            else
            {
                Log.Warning("HandleRenameAsync: failed");
                CurrentStatus.Text = $"Rename failed: {entry.Name}";
            }
        }

        private async Task HandleDeleteAsync(FileEntry entry)
        {
            Log.Information("HandleDeleteAsync: {File}", entry.FullPath);
            var confirmed = await ConfirmDialogControl.ShowAsync($"Delete '{entry.Name}'?");
            if (!confirmed)
            {
                Log.Verbose("HandleDeleteAsync: confirmation cancelled");
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
                Log.Information("HandleDeleteAsync: success — refreshing");
                await _navigator.RefreshCurrentAsync();
            }
            else
            {
                Log.Warning("HandleDeleteAsync: failed");
                CurrentStatus.Text = $"Delete failed: {entry.Name}";
            }
        }

        private async Task HandleExtractAsync(FileEntry entry)
        {
            Log.Information("HandleExtractAsync: {File}", entry.FullPath);
            var currentPath = _navigator.Current?.Path;
            if (string.IsNullOrEmpty(currentPath)) return;

            var destDir = System.IO.Path.Combine(currentPath, System.IO.Path.GetFileNameWithoutExtension(entry.Name));

            var progress = new Progress<FileOperations.OperationProgress>(p =>
            {
                Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    OpProgressDialog.UpdateProgress(p));
            });

            OpProgressDialog.Show("Extracting", entry.Name, destDir);
            var result = await FileOperations.ExtractAsync(entry.FullPath, destDir, progress);
            OpProgressDialog.Complete();
            await Task.Delay(400);
            OpProgressDialog.Close();

            if (result == FileOperations.OperationResult.Success)
            {
                Log.Information("HandleExtractAsync: success");
                await _navigator.RefreshCurrentAsync();
            }
            else
            {
                Log.Warning("HandleExtractAsync: failed");
                CurrentStatus.Text = $"Extract failed: {entry.Name}";
            }
        }

        private async Task HandleCreateFolderAsync(FileEntry entry)
        {
            Log.Information("HandleCreateFolderAsync: {File}", entry?.Name ?? "(none)");

            string targetDir;
            if (entry != null && entry.IsDirectory && !entry.IsDrive)
            {
                var choice = await FileActionSheetControl.ShowLocationChoiceAsync(entry.Name);
                if (choice == null)
                {
                    Log.Verbose("HandleCreateFolderAsync: location choice cancelled");
                    return;
                }
                targetDir = choice == FileAction.CreateInside
                    ? entry.FullPath
                    : System.IO.Path.GetDirectoryName(entry.FullPath);
            }
            else
            {
                targetDir = _navigator.Current?.Path;
            }

            if (string.IsNullOrEmpty(targetDir))
            {
                Log.Warning("HandleCreateFolderAsync: no target directory");
                return;
            }

            var folderName = await InputDialogControl.ShowAsync("New Folder", "New Folder");
            if (string.IsNullOrEmpty(folderName))
            {
                Log.Verbose("HandleCreateFolderAsync: name cancelled");
                return;
            }

            var fullPath = System.IO.Path.Combine(targetDir, folderName);
            var result = await FileOperations.CreateFolderAsync(fullPath);
            if (result == FileOperations.OperationResult.Success)
            {
                Log.Information("HandleCreateFolderAsync: success — refreshing");
                if (targetDir == _navigator.Current?.Path)
                    await _navigator.RefreshCurrentAsync();
                else
                    await _navigator.RefreshCurrentAsync();
            }
            else
            {
                Log.Warning("HandleCreateFolderAsync: failed");
                CurrentStatus.Text = $"Create folder failed: {folderName}";
            }
        }

        private async Task HandleCreateZipAsync(FileEntry entry)
        {
            Log.Information("HandleCreateZipAsync: {File}", entry.FullPath);
            var zipName = await InputDialogControl.ShowAsync("Create ZIP", entry.Name + ".zip");
            if (string.IsNullOrEmpty(zipName))
            {
                Log.Verbose("HandleCreateZipAsync: cancelled");
                return;
            }

            var currentPath = _navigator.Current?.Path;
            if (string.IsNullOrEmpty(currentPath)) return;

            var zipPath = System.IO.Path.Combine(currentPath, zipName);

            OpProgressDialog.Show("Creating ZIP", entry.Name, zipPath);
            var result = await FileOperations.CreateZipAsync(entry.FullPath, zipPath);
            OpProgressDialog.Complete();
            await Task.Delay(400);
            OpProgressDialog.Close();

            if (result == FileOperations.OperationResult.Success)
            {
                Log.Information("HandleCreateZipAsync: success");
                await _navigator.RefreshCurrentAsync();
            }
            else
            {
                Log.Warning("HandleCreateZipAsync: failed");
                CurrentStatus.Text = $"Create ZIP failed: {zipName}";
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
            Log.Warning("Error overlay shown: {Title} — {Description}", title, description);
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
                Log.Information("Error details copied to clipboard");
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to copy error to clipboard: {Error}", ex.Message);
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
                Log.Warning("Failed to open GitHub: {Error}", ex.Message);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
