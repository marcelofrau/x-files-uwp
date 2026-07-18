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

        // Extensions that are plain text — no syntax highlighting.
        private static readonly HashSet<string> PlainTextExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".txt", ".log", ".out", ".err",
                ".csv", ".tsv",
                ".md", ".markdown", ".rst",
                ".ini", ".cfg", ".conf", ".config",
                ".toml", ".yaml", ".yml",
                ".json", ".jsonc", ".json5", ".jsonl",
                ".plist",
                ".env", ".properties", ".props", ".targets",
                ".gitignore", ".gitattributes", ".gitmodules",
                ".editorconfig", ".prettierrc", ".eslintrc",
                ".babelrc", ".stylelintrc", ".dockerignore",
                ".srt", ".vtt", ".sub",
                ".lrc", ".ly",
                ".tex", ".latex", ".bib",
                ".pod", ".opml", ".feed",
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
                        if (PlainTextExtensions.Contains(ext))
                        {
                            PreviewTextBlock.Text = _navigator.Preview.PreviewTextContent ?? "";
                            PreviewTextScroll.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            PreviewCodeView.NavigateToString(BuildHighlightHtml(
                                _navigator.Preview.PreviewTextContent ?? "", ext));
                            PreviewCodeView.Visibility = Visibility.Visible;
                        }
                        break;

                    case FilePreviewType.Image:
                        PreviewImage.Source = _navigator.Preview.PreviewImageSource;
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

            return $@"<!DOCTYPE html>
<html>
<head>
<meta charset=""utf-8"">
<style>
  body {{ margin:0; padding:12px; background:#1e1e1e; }}
  pre {{ margin:0; white-space:pre-wrap; word-wrap:break-word;
         font-family:'Consolas','Courier New',monospace; font-size:14px;
         color:#dcdcdc; line-height:1.4; }}
  code {{ font-family:inherit; }}
</style>
<style>{_highlightCss}</style>
</head>
<body><pre><code class=""hljs {lang}"">{escaped}</code></pre>
<script>{_highlightJs}</script>
<script>hljs.highlightAll();</script>
</body></html>";
        }

        private static void EnsureHighlightAssetsLoaded()
        {
            if (_highlightJs != null && _highlightCss != null) return;

            try
            {
                var jsFile = StorageFile.GetFileFromApplicationUriAsync(
                    new Uri("ms-appx:///Assets/highlight.min.js")).GetAwaiter().GetResult();
                _highlightJs = FileIO.ReadTextAsync(jsFile).GetAwaiter().GetResult();

                var cssFile = StorageFile.GetFileFromApplicationUriAsync(
                    new Uri("ms-appx:///Assets/highlight-vs2015.min.css")).GetAwaiter().GetResult();
                _highlightCss = FileIO.ReadTextAsync(cssFile).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to load highlight.js assets: {Error}", ex.Message);
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
                case Windows.System.VirtualKey.PageUp:
                case Windows.System.VirtualKey.PageDown:
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
                // Block ListView native PageUp/PageDown — GamepadInputService handles LB/RB
                case Windows.System.VirtualKey.PageUp:
                case Windows.System.VirtualKey.PageDown:
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
            if (CurrentList.SelectedIndex > 0)
                CurrentList.SelectedIndex = Math.Max(0, CurrentList.SelectedIndex - 8);
        }

        public void OnPageDown()
        {
            if (_navigator.Current != null && CurrentList.Items.Count > 0)
                CurrentList.SelectedIndex = Math.Min(CurrentList.Items.Count - 1, CurrentList.SelectedIndex + 8);
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
