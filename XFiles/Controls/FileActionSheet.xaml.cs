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
        CreateFolder,
        CreateInside,
        CreateNextTo,
        CreateZip,
        Refresh
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

        private static readonly string ImageIcon = "ctx-image-120.png";
        private static readonly string VideoIcon = "ctx-video-120.png";
        private static readonly string AudioIcon = "ctx-audio-120.png";
        private static readonly string TextIcon  = "ctx-text-120.png";
        private static readonly string ArchiveIcon = "ctx-archive-120.png";
        private static readonly string DriveIcon = "ctx-drive-120.png";
        private static readonly string GenericIcon = "ctx-generic-120.png";

        private static readonly string ActionCopy = "fileactionsheet-copy-48.png";
        private static readonly string ActionMove = "fileactionsheet-move-48.png";
        private static readonly string ActionRename = "fileactionsheet-rename-48.png";
        private static readonly string ActionDelete = "fileactionsheet-delete-48.png";
        private static readonly string ActionExtract = "fileactionsheet-extract-48.png";
        private static readonly string ActionCreateFolder = "fileactionsheet-createfolder-48.png";
        private static readonly string ActionCreateZip = "fileactionsheet-createzip-48.png";
        private static readonly string ActionRefresh = "fileactionsheet-refresh-48.png";

        private static readonly HashSet<string> ImageExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png",".jpg",".jpeg",".gif",".bmp",".tiff",".tif",".webp",".svg"
        };

        private static readonly HashSet<string> VideoExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4",".mkv",".avi",".wmv",".mov",".webm",".flv",".m4v"
        };

        private static readonly HashSet<string> AudioExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3",".flac",".ogg",".wav",".aac",".m4a",".wma"
        };

        private static readonly HashSet<string> TextExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".txt",".log",".out",".err",".md",".json",".xml",".cs",".js",".ts",
            ".py",".c",".cpp",".h",".java",".csproj",".sln",".yaml",".yml",
            ".ini",".cfg",".conf",".bat",".sh",".ps1",".cmd",".css",".html",".htm"
        };

        private static readonly HashSet<string> ArchiveExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".zip",".7z",".rar",".tar",".gz",".bz2",".xz"
        };

        private static string ResolveContextFileIcon(FileEntry entry)
        {
            if (entry.IsDrive) return IconBase + DriveIcon;
            if (entry.IsDirectory)
            {
                var color = EntryViewModel.FolderColor;
                return IconBase + $"ctx-folder-{color}-120.png";
            }
            if (entry.IsArchive) return IconBase + ArchiveIcon;

            var ext = System.IO.Path.GetExtension(entry.Name);
            if (!string.IsNullOrEmpty(ext))
            {
                if (ImageExts.Contains(ext)) return IconBase + ImageIcon;
                if (VideoExts.Contains(ext)) return IconBase + VideoIcon;
                if (AudioExts.Contains(ext)) return IconBase + AudioIcon;
                if (TextExts.Contains(ext))  return IconBase + TextIcon;
                if (ArchiveExts.Contains(ext)) return IconBase + ArchiveIcon;
            }
            return IconBase + GenericIcon;
        }

        public bool IsOpen => Visibility == Visibility.Visible;

        public FileActionSheet()
        {
            this.InitializeComponent();
        }

        public Task<FileAction?> ShowAsync(FileEntry entry, bool isArchiveRoot = false)
        {
            _tcs = new TaskCompletionSource<FileAction?>();

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
                // Extract disabled for now
                // if (isArchiveFile)
                // {
                //     actions.Add(new ActionItem
                //     {
                //         Action = FileAction.Extract,
                //         Label = "Extract",
                //         IconPath = IconBase + ActionExtract,
                //         LabelBrush = accent
                //     });
                // }

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

                // Clipboard actions disabled for now — enable one by one later
                // if (ClipboardState.HasItems)
                // {
                //     actions.Add(new ActionItem
                //     {
                //         Action = FileAction.Paste,
                //         Label = ClipboardState.IsCut ? "Paste (move)" : "Paste (copy)",
                //         IconPath = IconBase + ActionCopy,
                //         LabelBrush = accent
                //     });
                // }

                // actions.Add(new ActionItem
                // {
                //     Action = FileAction.Copy,
                //     Label = "Copy",
                //     IconPath = IconBase + ActionCopy,
                //     LabelBrush = accent
                // });

                // actions.Add(new ActionItem
                // {
                //     Action = FileAction.Cut,
                //     Label = "Cut",
                //     IconPath = IconBase + ActionMove,
                //     LabelBrush = dim
                // });

                // Move disabled for now
                // actions.Add(new ActionItem
                // {
                //     Action = FileAction.Move,
                //     Label = "Move",
                //     IconPath = IconBase + ActionMove,
                //     LabelBrush = accent
                // });

                actions.Add(new ActionItem
                {
                    Action = FileAction.Rename,
                    Label = "Rename",
                    IconPath = IconBase + ActionRename,
                    LabelBrush = dim
                });

                // New folder disabled for now
                // actions.Add(new ActionItem
                // {
                //     Action = FileAction.CreateFolder,
                //     Label = "New Folder",
                //     IconPath = IconBase + ActionCreateFolder,
                //     LabelBrush = accent
                // });

                // Create ZIP disabled for now
                // if (isFolder)
                // {
                //     actions.Add(new ActionItem
                //     {
                //         Action = FileAction.CreateZip,
                //         Label = "Create ZIP",
                //         IconPath = IconBase + ActionCreateZip,
                //         LabelBrush = accent
                //     });
                // }

                // Extract disabled for now
                // else if (isArchiveFile)
                // {
                //     actions.Add(new ActionItem
                //     {
                //         Action = FileAction.Extract,
                //         Label = "Extract",
                //         IconPath = IconBase + ActionExtract,
                //         LabelBrush = accent
                //     });
                // }

                // Delete disabled for now
                // actions.Add(new ActionItem
                // {
                //     Action = FileAction.Delete,
                //     Label = "Delete",
                //     IconPath = IconBase + ActionDelete,
                //     LabelBrush = red
                // });
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

        public Task<FileAction?> ShowLocationChoiceAsync(string folderName)
        {
            _tcs = new TaskCompletionSource<FileAction?>();

            var actions = new List<ActionItem>();
            var accent = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x93, 0xC4, 0x3C));
            var dim = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x7A, 0xA8, 0x32));

            actions.Add(new ActionItem
            {
                Action = FileAction.CreateInside,
                Label = $"Inside \"{folderName}\"",
                IconPath = IconBase + ActionCreateFolder,
                LabelBrush = accent
            });

            actions.Add(new ActionItem
            {
                Action = FileAction.CreateNextTo,
                Label = $"Next to \"{folderName}\"",
                IconPath = IconBase + ActionCreateFolder,
                LabelBrush = dim
            });

            ActionList.ItemsSource = actions;
            FileNameText.Text = "New folder";

            FileIconImage.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(
                new Uri(IconBase + ActionCreateFolder));

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
                    break;
                case VirtualKey.Down:
                    if (ActionList.SelectedIndex < ActionList.Items.Count - 1)
                        ActionList.SelectedIndex++;
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
            Log.Information("FileActionSheet.Close: result={Result}", result?.ToString() ?? "null");
            Overlay.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Collapsed;
            _tcs?.TrySetResult(result);
            OnClosed?.Invoke();
        }
    }
}
