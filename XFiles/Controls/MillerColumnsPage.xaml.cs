using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using XFiles.FileSystem;
using XFiles.Navigation;

namespace XFiles.Controls
{
    public sealed partial class MillerColumnsPage : Page, INavigable, INotifyPropertyChanged
    {
        private readonly ColumnNavigator _navigator = new ColumnNavigator();
        private bool _updating;
        private static string _highlightJs;
        private static string _highlightCss;
        private static string _fontBase64;

        // Extensions that are plain text — no syntax highlighting.
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
            _navigator.Error += OnError;

            PreviewCodeView.NavigationStarting += OnPreviewNavigationStarting;
            PreviewCodeView.NavigationCompleted += OnPreviewNavigationCompleted;

            // Start loading root
            _ = _navigator.LoadRootAsync();
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

        private void OnColumnsChanged()
        {
            UpdateUI();
        }

        private void OnPreviewChanged()
        {
            UpdatePreviewColumn();
        }

        private void OnError(string message)
        {
            Log.Error("MillerColumnsPage error: {Message}", args: message);
            CurrentStatus.Text = $"ERROR: {message}";
        }

        private void UpdateUI()
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

                // Preview column
                UpdatePreviewColumn();
            }
            finally
            {
                _updating = false;
            }
        }

        private void UpdatePreviewColumn()
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
                // Folder listing (existing behavior)
                BindList(PreviewList, _navigator.Preview);
                PreviewStatus.Text = $"{_navigator.Preview.Entries.Count} items";
                PreviewList.Visibility = Visibility.Visible;
            }
            else
            {
                // File preview — check type
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
                            string html = BuildHighlightHtml(
                                _navigator.Preview.PreviewTextContent ?? "", ext);
                            _ = LoadHighlightHtml(html);
                            PreviewCodeView.Visibility = Visibility.Visible;
                        }
                        break;

                    case FilePreviewType.Image:
                        PreviewImage.Source = _navigator.Preview.PreviewImageSource;
                        // Cap upscale for small images (ICO, small PNGs, etc.)
                        // Images < 256px on either axis get max 4x scale
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
                        // FilePreviewType.None or unexpected — show file info
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

        private static string BuildHighlightHtml(string code, string extension)
        {
            string lang = GetHighlightLang(extension);
            string escaped = HtmlEncode(code);

            EnsureHighlightAssetsLoaded();

            Log.Information("BuildHighlightHtml: ext={Ext} lang={Lang} cssLen={CssLen} codeLen={CodeLen} jsLen={JsLen}",
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
  html, body {{ margin:0; padding:0; background:#111111; overflow-x:auto; }}
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
  body {{ margin:0; padding:12px; background:#111111; display:flex;
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

        private static void EnsureHighlightAssetsLoaded()
        {
            if (_highlightJs != null && _highlightCss != null && _fontBase64 != null) return;

            try
            {
                Log.Information("EnsureHighlightAssetsLoaded: loading JS...");
                var jsFile = StorageFile.GetFileFromApplicationUriAsync(
                    new Uri("ms-appx:///Assets/highlight.min.js")).GetAwaiter().GetResult();
                _highlightJs = FileIO.ReadTextAsync(jsFile).GetAwaiter().GetResult();
                Log.Information("EnsureHighlightAssetsLoaded: JS loaded, {Len} chars", _highlightJs.Length);

                Log.Information("EnsureHighlightAssetsLoaded: loading CSS...");
                var cssFile = StorageFile.GetFileFromApplicationUriAsync(
                    new Uri("ms-appx:///Assets/highlight-aco.css")).GetAwaiter().GetResult();
                _highlightCss = FileIO.ReadTextAsync(cssFile).GetAwaiter().GetResult();
                Log.Information("EnsureHighlightAssetsLoaded: CSS loaded, {Len} chars", _highlightCss.Length);

                Log.Information("EnsureHighlightAssetsLoaded: loading font...");
                var fontFile = StorageFile.GetFileFromApplicationUriAsync(
                    new Uri("ms-appx:///Assets/Inconsolata-Regular.ttf")).GetAwaiter().GetResult();
                var fontBytes = System.IO.File.ReadAllBytes(fontFile.Path);
                _fontBase64 = Convert.ToBase64String(fontBytes);
                Log.Information("EnsureHighlightAssetsLoaded: font loaded, {Len} bytes, b64={B64Len}",
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
                IsArchive = e.IsArchive,
                SizeBytes = e.SizeBytes
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
                IsArchive = e.IsArchive,
                SizeBytes = e.SizeBytes
            }).ToList();

            CurrentList.ItemsSource = vms;

            Log.Information("BindCurrentList: state.SelectedIndex={StateIndex}, itemCount={Count}", state.SelectedIndex, vms.Count);
            if (state.SelectedIndex >= 0 && state.SelectedIndex < CurrentList.Items.Count)
                CurrentList.SelectedIndex = state.SelectedIndex;

            CurrentList.Focus(FocusState.Programmatic);
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
                _ = _navigator.OnSelectionChangedAsync();
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

        // --- INavigable ---

        public void OnDPadUp()
        {
            var before = CurrentList.SelectedIndex;
            if (CurrentList.SelectedIndex > 0)
                CurrentList.SelectedIndex--;
            Log.Information("OnDPadUp: before={Before} after={After}", before, CurrentList.SelectedIndex);
        }

        public void OnDPadDown()
        {
            var before = CurrentList.SelectedIndex;
            if (CurrentList.SelectedIndex < _navigator.Current?.Entries.Count - 1)
                CurrentList.SelectedIndex++;
            Log.Information("OnDPadDown: before={Before} after={After}", before, CurrentList.SelectedIndex);
        }

        public void OnDPadLeft()
        {
            _ = _navigator.DrillOutAsync();
        }

        public void OnDPadRight()
        {
            _ = _navigator.DrillInAsync();
        }

        public void OnConfirm()
        {
            _ = _navigator.DrillInAsync();
        }

        public void OnBack()
        {
            _ = _navigator.DrillOutAsync();
        }

        public void OnContextMenu()
        {
            Log.Verbose("MillerColumnsPage.OnContextMenu — not implemented yet");
        }

        public void OnPageUp()
        {
            var before = CurrentList.SelectedIndex;
            if (CurrentList.SelectedIndex > 0)
                CurrentList.SelectedIndex = Math.Max(0, CurrentList.SelectedIndex - 8);
            Log.Information("OnPageUp: before={Before} after={After}", before, CurrentList.SelectedIndex);
        }

        public void OnPageDown()
        {
            var before = CurrentList.SelectedIndex;
            if (_navigator.Current != null && CurrentList.Items.Count > 0)
                CurrentList.SelectedIndex = Math.Min(CurrentList.Items.Count - 1, CurrentList.SelectedIndex + 8);
            Log.Information("OnPageDown: before={Before} after={After}", before, CurrentList.SelectedIndex);
        }

        public void OnHome()
        {
            var before = CurrentList.SelectedIndex;
            if (CurrentList.Items.Count > 0)
                CurrentList.SelectedIndex = 0;
            Log.Information("OnHome: before={Before} after={After}", before, CurrentList.SelectedIndex);
        }

        public void OnEnd()
        {
            var before = CurrentList.SelectedIndex;
            if (_navigator.Current != null && CurrentList.Items.Count > 0)
                CurrentList.SelectedIndex = CurrentList.Items.Count - 1;
            Log.Information("OnEnd: before={Before} after={After}", before, CurrentList.SelectedIndex);
        }

        public void OnScrollVertical(double delta)
        {
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

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
