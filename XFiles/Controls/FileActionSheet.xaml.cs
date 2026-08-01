using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using XFiles.FileSystem;

namespace XFiles.Controls
{
    public enum FileAction
    {
        Copy,
        Cut,
        Paste,
        Move,
        Rename,
        Delete,
        Extract,
        ExtractFile,
        ExtractToFolder,
        ExtractHere,
        CreateFolder,
        CreateZip,
        Refresh,
        Edit,
        Share,
        AddToFavorites,
        RemoveFromFavorites
    }

    public class ActionItem
    {
        public FileAction Action { get; set; }
        public string Label { get; set; }
        public string IconPath { get; set; }
        public SolidColorBrush LabelBrush { get; set; }
        public bool IsEnabled { get; set; } = true;
    }

    public sealed partial class FileActionSheet : UserControl
    {
        private TaskCompletionSource<FileAction?> _tcs;
        public Action OnClosed;

        private static readonly string IconBase = "ms-appx:///Assets/Views/FileActionSheet/";

        private static readonly string DriveIcon = "ctx-drive-120.png";
        private static readonly string GenericIcon = "ctx-generic-120.png";

        private static readonly string ActionCopy = "fileactionsheet-copy-48.png";
        private static readonly string ActionMove = "fileactionsheet-move-48.png";
        private static readonly string ActionRename = "fileactionsheet-rename-48.png";
        private static readonly string ActionDelete = "fileactionsheet-delete-48.png";
        private static readonly string ActionExtract = "fileactionsheet-extract-48.png";
        private static readonly string ActionExtractToFolder = "fileactionsheet-extractfolder-100.png";
        private static readonly string ActionCreateFolder = "fileactionsheet-createfolder-48.png";
        private static readonly string ActionCreateZip = "fileactionsheet-createzip-48.png";
        private static readonly string ActionRefresh = "fileactionsheet-refresh-48.png";
        private static readonly string ActionPaste = "fileactionsheet-paste-48.png";
        private static readonly string ActionEdit = "ctx-text-120.png";
        private static readonly string ActionShare = "fileactionsheet-share-48.png";
        private static readonly string ActionFavorite = "fileactionsheet-favorite-48.png";

        private static readonly Dictionary<string, string> ExtIconMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Text / Code
            [".txt"]  = "ctx-text-120.png",
            [".log"]  = "ctx-text-120.png",
            [".md"]   = "ctx-markdown-120.png",
            [".json"] = "ctx-json-120.png",
            [".xml"]  = "ctx-xml-120.png",
            [".yaml"] = "ctx-yaml-120.png",
            [".yml"]  = "ctx-yaml-120.png",
            [".py"]   = "ctx-python-120.png",
            [".js"]   = "ctx-javascript-120.png",
            [".ts"]   = "ctx-typescript-120.png",
            [".c"]    = "ctx-c-120.png",
            [".cpp"]  = "ctx-cpp-120.png",
            [".cc"]   = "ctx-cpp-120.png",
            [".h"]    = "ctx-c-120.png",
            [".hpp"]  = "ctx-cpp-120.png",
            [".cs"]   = "ctx-csharp-120.png",
            [".java"] = "ctx-java-120.png",
            [".go"]   = "ctx-go-120.png",
            [".rs"]   = "ctx-rust-120.png",
            [".rb"]   = "ctx-ruby-120.png",
            [".lua"]  = "ctx-lua-120.png",
            [".sh"]   = "ctx-shell-120.png",
            [".bat"]  = "ctx-shell-120.png",
            [".ps1"]  = "ctx-shell-120.png",
            [".html"] = "ctx-html-120.png",
            [".htm"]  = "ctx-html-120.png",
            [".css"]  = "ctx-css-120.png",
            // PDF
            [".pdf"]  = "ctx-pdf-120.png",
            // Images
            [".png"]  = "ctx-png-120.png",
            [".jpg"]  = "ctx-jpeg-120.png",
            [".jpeg"] = "ctx-jpeg-120.png",
            [".gif"]  = "ctx-gif-120.png",
            [".bmp"]  = "ctx-bmp-120.png",
            [".svg"]  = "ctx-svg-120.png",
            [".tiff"] = "ctx-tiff-120.png",
            [".tif"]  = "ctx-tiff-120.png",
            [".webp"] = "ctx-webp-120.png",
            // Audio
            [".mp3"]  = "ctx-mp3-120.png",
            [".flac"] = "ctx-flac-120.png",
            [".ogg"]  = "ctx-ogg-120.png",
            [".wav"]  = "ctx-wav-120.png",
            [".m4a"]  = "ctx-m4a-120.png",
            [".aac"]  = "ctx-aac-120.png",
            [".wma"]  = "ctx-mp3-120.png",
            [".opus"] = "ctx-opus-120.png",
            // Video
            [".mp4"]  = "ctx-mp4-120.png",
            [".mkv"]  = "ctx-mkv-120.png",
            [".avi"]  = "ctx-avi-120.png",
            [".webm"] = "ctx-webm-120.png",
            [".flv"]  = "ctx-flv-120.png",
            [".wmv"]  = "ctx-wmv-120.png",
            [".mov"]  = "ctx-mov-120.png",
            [".m4v"]  = "ctx-mp4-120.png",
            // Archives
            [".zip"]  = "ctx-zip-120.png",
            [".7z"]   = "ctx-7z-120.png",
            [".rar"]  = "ctx-rar-120.png",
        };

        internal static readonly HashSet<string> TextExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".txt",".log",".out",".err",".md",".json",".xml",".cs",".js",".ts",
            ".py",".c",".cpp",".h",".java",".csproj",".sln",".yaml",".yml",
            ".ini",".cfg",".conf",".bat",".sh",".ps1",".cmd",".css",".html",".htm"
        };

        private static string ResolveContextFileIcon(FileEntry entry)
        {
            if (entry.IsDrive) return IconBase + DriveIcon;
            if (entry.IsDirectory)
            {
                var color = EntryViewModel.FolderColor;
                return IconBase + $"ctx-folder-{color}-120.png";
            }
            if (entry.IsArchive) return IconBase + "ctx-archive-120.png";

            var ext = System.IO.Path.GetExtension(entry.Name);
            if (!string.IsNullOrEmpty(ext) && ExtIconMap.TryGetValue(ext, out var icon))
                return IconBase + icon;

            return IconBase + GenericIcon;
        }

        public bool IsOpen => Visibility == Visibility.Visible;

        public FileActionSheet()
        {
            this.InitializeComponent();
        }

        public Task<FileAction?> ShowAsync(FileEntry entry, bool isArchiveRoot = false)
        {
            _tcs = new TaskCompletionSource<FileAction?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var actions = new List<ActionItem>();

            var accent = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x93, 0xC4, 0x3C));
            var dim = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x7A, 0xA8, 0x32));
            var red = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xE7, 0x4C, 0x3C));
            var muted = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x5A, 0x5C, 0x60));

            bool isInArchive = !string.IsNullOrEmpty(entry.ArchiveRootPath);
            bool isArchiveFile = entry.IsArchive && !entry.IsDirectory;
            bool isFolder = entry.IsDirectory;

            if (entry.IsDrive)
            {
                actions.Add(new ActionItem
                {
                    Action = FileAction.Refresh,
                    Label = "Refresh",
                    IconPath = IconBase + ActionRefresh,
                    LabelBrush = accent
                });
            }
            else if (isInArchive)
            {
                actions.Add(new ActionItem
                {
                    Action = FileAction.Extract,
                    Label = "Extract All",
                    IconPath = IconBase + ActionExtractToFolder,
                    LabelBrush = accent
                });

                actions.Add(new ActionItem
                {
                    Action = FileAction.ExtractFile,
                    Label = "Extract File",
                    IconPath = IconBase + ActionExtract,
                    LabelBrush = dim
                });

                actions.Add(new ActionItem
                {
                    Action = FileAction.Refresh,
                    Label = "Refresh",
                    IconPath = IconBase + ActionRefresh,
                    LabelBrush = accent
                });
            }
            else
            {
                actions.Add(new ActionItem
                {
                    Action = FileAction.Refresh,
                    Label = "Refresh",
                    IconPath = IconBase + ActionRefresh,
                    LabelBrush = accent
                });

                var ext = System.IO.Path.GetExtension(entry.Name);
                if (!entry.IsDirectory && TextExts.Contains(ext))
                {
                    actions.Add(new ActionItem
                    {
                        Action = FileAction.Edit,
                        Label = "Edit",
                        IconPath = IconBase + ActionEdit,
                        LabelBrush = accent
                    });
                }

                actions.Add(new ActionItem
                {
                    Action = FileAction.Copy,
                    Label = "Copy",
                    IconPath = IconBase + ActionCopy,
                    LabelBrush = accent
                });

                if (ClipboardState.HasItems)
                {
                    actions.Add(new ActionItem
                    {
                        Action = FileAction.Paste,
                        Label = "Paste",
                        IconPath = IconBase + ActionPaste,
                        LabelBrush = accent
                    });
                }

                actions.Add(new ActionItem
                {
                    Action = FileAction.Move,
                    Label = "Move",
                    IconPath = IconBase + ActionMove,
                    LabelBrush = accent
                });

                actions.Add(new ActionItem
                {
                    Action = FileAction.Rename,
                    Label = "Rename",
                    IconPath = IconBase + ActionRename,
                    LabelBrush = dim
                });

                actions.Add(new ActionItem
                {
                    Action = FileAction.Share,
                    Label = "Share",
                    IconPath = IconBase + ActionShare,
                    LabelBrush = accent
                });

                actions.Add(new ActionItem
                {
                    Action = FileAction.CreateFolder,
                    Label = "New Folder",
                    IconPath = IconBase + ActionCreateFolder,
                    LabelBrush = accent
                });

                if (!isArchiveFile)
                {
                    actions.Add(new ActionItem
                    {
                        Action = FileAction.CreateZip,
                        Label = "Create ZIP",
                        IconPath = IconBase + ActionCreateZip,
                        LabelBrush = accent
                    });
                }

                if (isArchiveFile)
                {
                    actions.Add(new ActionItem
                    {
                        Action = FileAction.Extract,
                        Label = "Extract",
                        IconPath = IconBase + ActionExtract,
                        LabelBrush = accent
                    });
                }

                actions.Add(new ActionItem
                {
                    Action = FileAction.Delete,
                    Label = "Delete",
                    IconPath = IconBase + ActionDelete,
                    LabelBrush = red
                });
            }

            ActionList.ItemsSource = actions;
            FileNameText.Text = entry.Name;

            FileIconImage.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(
                new Uri(ResolveContextFileIcon(entry)));

            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;

            ActionList.SelectedIndex = 0;
            ActionList.Focus(FocusState.Programmatic);

            return _tcs.Task;
        }

        public Task<FileAction?> ShowExtractChoiceAsync(string archiveName)
        {
            _tcs = new TaskCompletionSource<FileAction?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var actions = new List<ActionItem>();
            var accent = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x93, 0xC4, 0x3C));
            var dim = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x7A, 0xA8, 0x32));

            actions.Add(new ActionItem
            {
                Action = FileAction.ExtractToFolder,
                Label = $"Extract to \"{archiveName}/\"",
                IconPath = IconBase + ActionExtractToFolder,
                LabelBrush = accent
            });

            actions.Add(new ActionItem
            {
                Action = FileAction.ExtractHere,
                Label = "Extract here",
                IconPath = IconBase + ActionExtract,
                LabelBrush = dim
            });

            ActionList.ItemsSource = actions;
            FileNameText.Text = archiveName;

            FileIconImage.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(
                new Uri(IconBase + "ctx-archive-120.png"));

            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;

            ActionList.SelectedIndex = 0;
            ActionList.Focus(FocusState.Programmatic);

            return _tcs.Task;
        }

        public Task<FileAction?> ShowBatchAsync(int selectedCount)
        {
            _tcs = new TaskCompletionSource<FileAction?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var actions = new List<ActionItem>();
            var accent = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x93, 0xC4, 0x3C));
            var red = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xE7, 0x4C, 0x3C));

            actions.Add(new ActionItem
            {
                Action = FileAction.Copy,
                Label = "Copy",
                IconPath = IconBase + ActionCopy,
                LabelBrush = accent
            });

            actions.Add(new ActionItem
            {
                Action = FileAction.Move,
                Label = "Move",
                IconPath = IconBase + ActionMove,
                LabelBrush = accent
            });

            actions.Add(new ActionItem
            {
                Action = FileAction.Delete,
                Label = "Delete",
                IconPath = IconBase + ActionDelete,
                LabelBrush = red
            });

            actions.Add(new ActionItem
            {
                Action = FileAction.CreateZip,
                Label = "Create ZIP",
                IconPath = IconBase + ActionCreateZip,
                LabelBrush = accent
            });

            actions.Add(new ActionItem
            {
                Action = FileAction.Share,
                Label = "Share",
                IconPath = IconBase + ActionShare,
                LabelBrush = accent
            });

            ActionList.ItemsSource = actions;
            FileNameText.Text = $"{selectedCount} file{(selectedCount == 1 ? "" : "s")}";

            FileIconImage.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(
                new Uri(IconBase + "ctx-generic-120.png"));

            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;

            ActionList.SelectedIndex = 0;
            ActionList.Focus(FocusState.Programmatic);

            return _tcs.Task;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        public Task<FileAction?> ShowFavoritesActionsAsync(FileEntry entry)
        {
            _tcs = new TaskCompletionSource<FileAction?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var actions = new List<ActionItem>();
            var accent = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x93, 0xC4, 0x3C));
            var red = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xE7, 0x4C, 0x3C));

            actions.Add(new ActionItem
            {
                Action = FileAction.RemoveFromFavorites,
                Label = "Remove from Favorites",
                IconPath = IconBase + ActionFavorite,
                LabelBrush = red
            });

            var ext = System.IO.Path.GetExtension(entry.Name);
            if (!entry.IsDirectory && FileActionSheet.TextExts.Contains(ext))
            {
                actions.Add(new ActionItem
                {
                    Action = FileAction.Edit,
                    Label = "Edit",
                    IconPath = IconBase + ActionEdit,
                    LabelBrush = accent
                });
            }

            ActionList.ItemsSource = actions;
            FileNameText.Text = entry.Name;

            FileIconImage.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(
                new Uri(ResolveContextFileIcon(entry)));

            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;

            ActionList.SelectedIndex = 0;
            ActionList.Focus(FocusState.Programmatic);

            return _tcs.Task;
        }

        private void OnActionContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.ItemContainer is ListViewItem container)
            {
                container.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x99, 0x99, 0x99));
            }
        }

        private void OnActionSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateActionSelectionColors();
        }

        private void UpdateActionSelectionColors()
        {
            var gray = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x99, 0x99, 0x99));

            for (int i = 0; i < ActionList.Items.Count; i++)
            {
                var container = ActionList.ContainerFromIndex(i) as ListViewItem;
                if (container != null)
                {
                    var item = ActionList.Items[i] as ActionItem;
                    container.Foreground = container.IsSelected ? item?.LabelBrush ?? gray : gray;
                }
            }
        }

        public void ForwardDPad(VirtualKey key)
        {
            if (!IsOpen) return;
            switch (key)
            {
                case VirtualKey.Up:
                    if (ActionList.SelectedIndex > 0)
                        ActionList.SelectedIndex--;
                    else if (ActionList.Items.Count > 0)
                        ActionList.SelectedIndex = ActionList.Items.Count - 1;
                    break;
                case VirtualKey.Down:
                    if (ActionList.SelectedIndex < ActionList.Items.Count - 1)
                        ActionList.SelectedIndex++;
                    else if (ActionList.Items.Count > 0)
                        ActionList.SelectedIndex = 0;
                    break;
                case VirtualKey.GamepadA:
                case VirtualKey.Enter:
                    if (ActionList.SelectedItem is ActionItem item)
                        Close(item.Action);
                    break;
                case VirtualKey.GamepadB:
                case VirtualKey.Escape:
                    Close(null);
                    break;
            }
        }

        private void OnOverlayTapped(object sender, TappedRoutedEventArgs e)
        {
            Close(null);
        }

        private void Close(FileAction? result)
        {
            Log.Dbg("FileActionSheet.Close: result={Result}", result?.ToString() ?? "null");
            Overlay.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Collapsed;
            _tcs?.TrySetResult(result);
            OnClosed?.Invoke();
        }
    }
}
