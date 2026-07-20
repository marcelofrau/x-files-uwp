using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
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

            _fsProgressTimer.Tick += OnFSProgressTimerTick;
            _fsHideTimer.Tick += OnFsHideTimerTick;

            PreviewCodeView.NavigationStarting += OnPreviewNavigationStarting;
            PreviewCodeView.NavigationCompleted += OnPreviewNavigationCompleted;

            MediaPreview.PlayerStateChanged += OnMediaPlayerStateChanged;

            var v = Package.Current.Id.Version;
            VersionText.Text = $"v{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";

            _ = _navigator.LoadRootAsync();
        }

        private bool _isMediaPlayerActive;

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
                // Parent column
                if (_navigator.Parent != null)
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

                    case FilePreviewType.Video:
                    case FilePreviewType.Audio:
                        string mediaPath = _navigator.Preview.PreviewFilePath;
                        PreviewStatus.Text = _navigator.Preview.PreviewFileType;
                        PreviewMediaPanel.Visibility = Visibility.Visible;
                        MediaPreview.LoadFile(mediaPath);
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
  pre {{ margin:0; padding:12px; white-space:pre; overflow-x:auto;
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
            Log.Information("SelectionChanged: index={Index}, updating={Updating}", CurrentList.SelectedIndex, _updating);
            if (_updating) return;
            if (CurrentList.SelectedIndex >= 0 && _navigator.Current != null)
            {
                _navigator.Current.SelectedIndex = CurrentList.SelectedIndex;

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
            if (FilePreviewService.IsMediaFile(ext) || FilePreviewService.IsImageFile(ext))
            {
                UpdateFooterALabel("Play");
                return;
            }
            UpdateFooterALabel("Menu");
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

        public bool IsMediaFullscreen => VideoFullScreenPanel.Visibility == Visibility.Visible;

        // --- INavigable ---

        public void OnDPadUp()
        {
            if (ImageFullScreen.IsOpen) return;
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { OnFsVideoInput(); return; }
            if (PlaceholderOverlay.Visibility == Visibility.Visible) return;
            if (StartMenuControl.IsOpen) { StartMenuControl.ForwardDPad(Windows.System.VirtualKey.Up); return; }
            if (FileActionSheetControl.IsOpen) { FileActionSheetControl.ForwardDPad(Windows.System.VirtualKey.Up); return; }
            var before = CurrentList.SelectedIndex;
            if (CurrentList.SelectedIndex > 0)
                CurrentList.SelectedIndex--;
            Log.Information("OnDPadUp: before={Before} after={After}", before, CurrentList.SelectedIndex);
        }

        public void OnDPadDown()
        {
            if (ImageFullScreen.IsOpen) return;
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { OnFsVideoInput(); return; }
            if (PlaceholderOverlay.Visibility == Visibility.Visible) return;
            if (StartMenuControl.IsOpen) { StartMenuControl.ForwardDPad(Windows.System.VirtualKey.Down); return; }
            if (FileActionSheetControl.IsOpen) { FileActionSheetControl.ForwardDPad(Windows.System.VirtualKey.Down); return; }
            var before = CurrentList.SelectedIndex;
            if (CurrentList.SelectedIndex < _navigator.Current?.Entries.Count - 1)
                CurrentList.SelectedIndex++;
            Log.Information("OnDPadDown: before={Before} after={After}", before, CurrentList.SelectedIndex);
        }

        public void OnDPadLeft()
        {
            if (ImageFullScreen.IsOpen) return;
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { HandleContinuousSeek(-5); return; }
            if (PlaceholderOverlay.Visibility == Visibility.Visible) return;
            if (StartMenuControl.IsOpen) return;
            if (FileActionSheetControl.IsOpen) return;
            _slideFromRight = false;
            _ = _navigator.DrillOutAsync();
        }

        public void OnDPadRight()
        {
            if (ImageFullScreen.IsOpen) return;
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { HandleContinuousSeek(5); return; }
            if (PlaceholderOverlay.Visibility == Visibility.Visible) return;
            if (StartMenuControl.IsOpen) return;
            if (FileActionSheetControl.IsOpen) return;
            _slideFromRight = true;
            _ = _navigator.DrillInAsync();
        }

        public void OnConfirm()
        {
            if (ErrorOverlay.Visibility == Visibility.Visible) return;
            if (PlaceholderOverlay.Visibility == Visibility.Visible) return;
            if (StartMenuControl.IsOpen) { StartMenuControl.ForwardDPad(Windows.System.VirtualKey.GamepadA); return; }
            if (ImageFullScreen.IsOpen) return;
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { OnFsControlsAnyInput(); return; }
            if (FileActionSheetControl.IsOpen) { FileActionSheetControl.ForwardDPad(Windows.System.VirtualKey.GamepadA); return; }
            if (_navigator.Current == null) return;

            if (_isMediaPlayerActive)
            {
                MediaPreview.HandleButton(Windows.System.VirtualKey.GamepadA);
                UpdateMediaPlayerFocusUI();
                return;
            }

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
                else if (FilePreviewService.IsMediaFile(ext))
                {
                    Log.Verbose("OnConfirm: media file — toggling play/pause");
                    if (MediaPreview != null && MediaPreview.Visibility == Visibility.Visible)
                    {
                        MediaPreview.TogglePlayPause();
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
            if (PlaceholderOverlay.Visibility == Visibility.Visible) { HidePlaceholder(); return; }
            if (StartMenuControl.IsOpen) { StartMenuControl.ForwardDPad(Windows.System.VirtualKey.GamepadB); return; }
            if (ImageFullScreen.IsOpen) { ImageFullScreen.HandleButton(Windows.System.VirtualKey.GamepadB); UpdateFooterALabelFromSelection(); return; }
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { CloseVideoFullScreen(); UpdateFooterALabelFromSelection(); return; }
            if (FileActionSheetControl.IsOpen) { FileActionSheetControl.ForwardDPad(Windows.System.VirtualKey.GamepadB); return; }
            if (_isMediaPlayerActive)
            {
                MediaPreview.StopPlayer();
                UpdateMediaPlayerFocusUI();
                return;
            }
            _slideFromRight = false;
            _ = _navigator.DrillOutAsync();
        }

        public void OnContextMenu()
        {
            if (ErrorOverlay.Visibility == Visibility.Visible) return;
            if (PlaceholderOverlay.Visibility == Visibility.Visible) return;
            if (StartMenuControl.IsOpen) return;
            if (ImageFullScreen.IsOpen) return;
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { OnFsControlsAnyInput(); return; }
            if (FileActionSheetControl.IsOpen) return;
            Log.Verbose("MillerColumnsPage.OnContextMenu — showing FileActionSheet");
            _ = ShowFileActionSheetAsync();
        }

        public void OnRefresh()
        {
            if (ImageFullScreen.IsOpen) return;
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { OnFsControlsAnyInput(); return; }
            if (FileActionSheetControl.IsOpen) return;
            if (_isMediaPlayerActive)
            {
                _ = MediaPreview.OpenFullscreen();
                return;
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
            if (PlaceholderOverlay.Visibility == Visibility.Visible) return;
            if (StartMenuControl.IsOpen) { StartMenuControl.ForwardDPad(Windows.System.VirtualKey.GamepadA); return; }
            if (FileActionSheetControl.IsOpen) return;
            if (ImageFullScreen.IsOpen) return;
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
                    ShowPlaceholder("About X-Files",
                        "Gamepad-first file explorer for Xbox",
                        "Version 0.1.0\n\n" +
                        "A retro-styled Miller-column file browser designed for Xbox, built with C# and UWP XAML.\n\n" +
                        "Inspired by yazi's keyboard-driven UX, adapted for gamepad control.\n\n" +
                        "github.com/MarceloLins76/x-files-uwp");
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

        private bool IsAnyFullscreen =>
            ImageFullScreen.IsOpen || VideoFullScreenPanel.Visibility == Visibility.Visible;

        public void OnPageUp()
        {
            if (ImageFullScreen.IsOpen) { ImageFullScreen.HandleButton((Windows.System.VirtualKey)VK_LT); return; }
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { HandleContinuousSeek(-5); return; }
            var before = CurrentList.SelectedIndex;
            if (CurrentList.SelectedIndex > 0)
                CurrentList.SelectedIndex = Math.Max(0, CurrentList.SelectedIndex - 8);
            Log.Information("OnPageUp: before={Before} after={After}", before, CurrentList.SelectedIndex);
        }

        public void OnPageDown()
        {
            if (ImageFullScreen.IsOpen) { ImageFullScreen.HandleButton((Windows.System.VirtualKey)VK_RT); return; }
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { HandleContinuousSeek(5); return; }
            var before = CurrentList.SelectedIndex;
            if (_navigator.Current != null && CurrentList.Items.Count > 0)
                CurrentList.SelectedIndex = Math.Min(CurrentList.Items.Count - 1, CurrentList.SelectedIndex + 8);
            Log.Information("OnPageDown: before={Before} after={After}", before, CurrentList.SelectedIndex);
        }

        public void OnSeekBack()
        {
            if (ImageFullScreen.IsOpen) return;
            if (VideoFullScreenPanel.Visibility == Visibility.Visible) { HandleContinuousSeek(-5); return; }
            if (_isMediaPlayerActive) { MediaPreview.Seek(TimeSpan.FromSeconds(-5)); return; }
            JumpByLetter(-1);
        }

        public void OnSeekForward()
        {
            if (ImageFullScreen.IsOpen) return;
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
            HandleContinuousSeek(seconds);
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
            bool isMedia = VideoFullScreenPanel.Visibility == Visibility.Visible || _isMediaPlayerActive;
            if (!isMedia) { _ltWasDown = false; _rtWasDown = false; return; }

            const float Threshold = 0.3f;
            const float Release = 0.15f;

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
            else if (leftTrigger < Release)
            {
                if (_ltWasDown) CommitPendingSeek();
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
            else if (rightTrigger < Release)
            {
                if (_rtWasDown) CommitPendingSeek();
                _rtWasDown = false;
                _rtHoldMs = 0;
            }

            _ltWasDown = ltDown;
            _rtWasDown = rtDown;
            if (_seekCooldown > 0) _seekCooldown -= 16;
            if (_seekActualCooldown > 0) _seekActualCooldown -= 16;
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

                // Calculate clamped target position
                var pos = VideoFullScreenPlayer.Position + TimeSpan.FromSeconds(seconds);
                if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
                if (VideoFullScreenPlayer.NaturalDuration.HasTimeSpan)
                {
                    var total = VideoFullScreenPlayer.NaturalDuration.TimeSpan;
                    if (pos > total) pos = total;
                }

                _seekPendingPosition = pos;

                // Always update visual bar immediately (smooth feedback, no decoder reload)
                UpdateSeekVisual(pos);

                // Throttle actual video seek — decoder reload is expensive
                if (_seekActualCooldown <= 0)
                {
                    VideoFullScreenPlayer.Position = pos;
                    _seekActualCooldown = SeekActualInterval.TotalMilliseconds;
                }

                string dir = seconds > 0 ? "\u25B6\u25B6" : "\u25C0\u25C0";
                ShowFsOsd($"{dir}  {(seconds > 0 ? "+" : "")}{seconds}s", 800);
            }
            else if (_isMediaPlayerActive)
            {
                MediaPreview.Seek(TimeSpan.FromSeconds(seconds));
            }
        }

        /// <summary>
        /// Update progress bar and time text without touching the video decoder.
        /// </summary>
        private void UpdateSeekVisual(TimeSpan position)
        {
            if (VideoFullScreenPlayer.NaturalDuration.HasTimeSpan)
            {
                var total = VideoFullScreenPlayer.NaturalDuration.TimeSpan;
                if (total.TotalSeconds > 0)
                {
                    FSProgress.Value = (position.TotalSeconds / total.TotalSeconds) * 100;
                    FSTimeText.Text = $"{FormatFsTime(position)} / {FormatFsTime(total)}";
                }
            }
        }

        /// <summary>
        /// Commit pending seek to video decoder (called on trigger release).
        /// </summary>
        private void CommitPendingSeek()
        {
            if (VideoFullScreenPanel.Visibility == Visibility.Visible)
            {
                VideoFullScreenPlayer.Position = _seekPendingPosition;
            }
        }

        public void OnLeftStickMove(float x, float y)
        {
            if (ImageFullScreen.IsOpen)
            {
                ImageFullScreen.HandleRightStick(x, y);
                return;
            }
            if (VideoFullScreenPanel.Visibility == Visibility.Visible)
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
            if (VideoFullScreenPanel.Visibility == Visibility.Visible)
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

        public async Task ShowMediaFullscreenAsync(Uri source, bool isVideo, TimeSpan position)
        {
            if (!isVideo) return;
            VideoFullScreenPlayer.Source = source;
            VideoFullScreenPlayer.Position = position;
            VideoFullScreenPlayer.Volume = _fsVolume;
            VideoFullScreenPlayer.Play();
            _fsVideoPlaying = true;
            FSPlayPauseIcon.Glyph = "\uE769";
            FSVolumeText.Text = $"Vol {(int)(_fsVolume * 100)}%";
            _fsProgressTimer.Start();
            VideoFullScreenPanel.Visibility = Visibility.Visible;
            ShowFsControls();
            ShowFsOsd("\u25B6  PLAY");
            Log.Information("ShowMediaFullscreenAsync: started fullscreen video at {Position}", position);
            await System.Threading.Tasks.Task.CompletedTask;
        }

        private void CloseVideoFullScreen()
        {
            _fsProgressTimer.Stop();
            _fsHideTimer.Stop();
            _fsOsdHideTimer.Stop();
            VideoFullScreenPlayer.Stop();
            VideoFullScreenPlayer.Source = null;
            _fsVideoPlaying = false;
            VideoFullScreenPanel.Visibility = Visibility.Collapsed;
            Log.Information("CloseVideoFullScreen: stopped");
        }

        private void OnFSProgressTimerTick(object sender, object e)
        {
            if (VideoFullScreenPlayer.NaturalDuration.HasTimeSpan)
            {
                var current = VideoFullScreenPlayer.Position;
                var total = VideoFullScreenPlayer.NaturalDuration.TimeSpan;
                if (total.TotalSeconds > 0)
                {
                    FSProgress.Value = (current.TotalSeconds / total.TotalSeconds) * 100;
                    FSTimeText.Text = $"{FormatFsTime(current)} / {FormatFsTime(total)}";
                }
            }
            // Decrement seek cooldown so discrete seeks (DPad/LB/RB) can fire again
            if (_seekActualCooldown > 0) _seekActualCooldown -= 250;
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

        private void ShowFsOsd(string text, double hideDelayMs = 1500)
        {
            FsOsdText.Text = text;
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
                VideoFullScreenPlayer.Pause();
                FSPlayPauseIcon.Glyph = "\uE768";
                _fsVideoPlaying = false;
                ShowFsOsd("\u275A\u275A  PAUSE");
            }
            else
            {
                VideoFullScreenPlayer.Play();
                FSPlayPauseIcon.Glyph = "\uE769";
                _fsVideoPlaying = true;
                ShowFsOsd("\u25B6  PLAY");
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
            _fsVolume = Math.Max(0.0, Math.Min(1.0, _fsVolume + delta));
            if (VideoFullScreenPanel.Visibility == Visibility.Visible)
            {
                ShowFsControls();
                VideoFullScreenPlayer.Volume = _fsVolume;
                FSVolumeText.Text = $"Vol {(int)(_fsVolume * 100)}%";
                ShowFsOsd($"Vol {(int)(_fsVolume * 100)}%", 1200);
            }
            else if (_isMediaPlayerActive)
            {
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

        // Smooth seek: visual bar updates instantly, actual video seek is throttled
        private double _seekVisualCooldown;
        private double _seekActualCooldown;
        private TimeSpan _seekPendingPosition;
        private static readonly TimeSpan SeekVisualInterval = TimeSpan.FromMilliseconds(60);
        private static readonly TimeSpan SeekActualInterval = TimeSpan.FromMilliseconds(300);

        private DispatcherTimer _fsProgressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };

        private DispatcherTimer _fsHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };

        private DispatcherTimer _fsOsdHideTimer = new DispatcherTimer();

        public void StopAllTimers()
        {
            _fsProgressTimer.Stop();
            _fsHideTimer.Stop();
            _fsOsdHideTimer.Stop();
            ImageFullScreen?.Close();
            MediaPreview?.StopPlayer();
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
                    await HandleCreateFolderAsync();
                    break;
                case FileAction.CreateZip:
                    await HandleCreateZipAsync(entry);
                    break;
            }
        }

        private async Task HandleCopyAsync(FileEntry entry)
        {
            Log.Information("HandleCopyAsync: {File}", entry.FullPath);
            CurrentStatus.Text = $"Copy: {entry.Name} (not yet implemented)";
        }

        private async Task HandleMoveAsync(FileEntry entry)
        {
            Log.Information("HandleMoveAsync: {File}", entry.FullPath);
            CurrentStatus.Text = $"Move: {entry.Name} (not yet implemented)";
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
                await _navigator.RefreshCurrentAsync();
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
            CurrentStatus.Text = $"Extract: {entry.Name} (not yet implemented)";
        }

        private async Task HandleCreateFolderAsync()
        {
            Log.Information("HandleCreateFolderAsync");
            var folderName = await InputDialogControl.ShowAsync("New Folder", "New Folder");
            if (string.IsNullOrEmpty(folderName))
            {
                Log.Verbose("HandleCreateFolderAsync: cancelled");
                return;
            }

            var currentPath = _navigator.Current?.Path;
            if (string.IsNullOrEmpty(currentPath)) return;

            var fullPath = System.IO.Path.Combine(currentPath, folderName);
            var result = await FileOperations.CreateFolderAsync(fullPath);
            if (result == FileOperations.OperationResult.Success)
            {
                Log.Information("HandleCreateFolderAsync: success — refreshing");
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
            var result = await FileOperations.CreateZipAsync(entry.FullPath, zipPath);
            if (result == FileOperations.OperationResult.Success)
            {
                Log.Information("HandleCreateZipAsync: success — refreshing");
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
                var uri = new Uri("https://github.com/MarceloLins76/x-files-uwp/issues/new?template=bug_report.md&title=" +
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
