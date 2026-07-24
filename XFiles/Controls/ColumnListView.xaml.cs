using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls;
using XFiles.FileSystem;
using XFiles.Navigation;

namespace XFiles.Controls
{
    public class EntryViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _isHighlighted;

        public string Name { get; set; }
        public string FullPath { get; set; }
        public bool IsDirectory { get; set; }
        public bool IsArchive { get; set; }
        public long SizeBytes { get; set; }

        private static string _folderColor = "orange";

        public static string FolderColor
        {
            get => _folderColor;
            set => _folderColor = value;
        }

        public bool IsDrive { get; set; }
        public string ArchiveRootPath { get; set; }

        public bool IsHighlighted
        {
            get => _isHighlighted;
            set
            {
                if (_isHighlighted != value)
                {
                    _isHighlighted = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsHighlighted)));
                }
            }
        }

        private static readonly Dictionary<string, string> ExtIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Images
            [".png"]  = "filetype-image-png-24.png",
            [".jpg"]  = "filetype-image-jpeg-24.png",
            [".jpeg"] = "filetype-image-jpeg-24.png",
            [".gif"]  = "filetype-image-gif-24.png",
            [".bmp"]  = "filetype-image-bmp-24.png",
            [".svg"]  = "filetype-image-svg-24.png",
            [".tiff"] = "filetype-image-tiff-24.png",
            [".tif"]  = "filetype-image-tiff-24.png",
            [".pbm"]  = "filetype-image-pbm-24.png",
            [".pgm"]  = "filetype-image-pbm-24.png",
            [".ppm"]  = "filetype-image-pbm-24.png",
            [".tga"]  = "filetype-image-tga-24.png",
            [".ico"]  = "filetype-image-png-24.png",
            [".webp"] = "filetype-image-png-24.png",
            [".heic"] = "filetype-image-jpeg-24.png",
            [".heif"] = "filetype-image-jpeg-24.png",
            [".raw"]  = "filetype-image-jpeg-24.png",
            [".cr2"]  = "filetype-image-jpeg-24.png",
            [".nef"]  = "filetype-image-jpeg-24.png",
            [".arw"]  = "filetype-image-jpeg-24.png",

            // Video
            [".mp4"]  = "filetype-video-mp4-24.png",
            [".avi"]  = "filetype-video-avi-24.png",
            [".mkv"]  = "filetype-video-mkv-24.png",
            [".webm"] = "filetype-video-webm-24.png",
            [".flv"]  = "filetype-video-flv-24.png",
            [".wmv"]  = "filetype-video-wmv-24.png",
            [".mov"]  = "filetype-video-mp4-24.png",
            [".mpg"]  = "filetype-video-mp4-24.png",
            [".mpeg"] = "filetype-video-mp4-24.png",
            [".m4v"]  = "filetype-video-mp4-24.png",
            [".ts"]   = "filetype-video-mp4-24.png",
            [".vob"]  = "filetype-video-mp4-24.png",
            [".3gp"]  = "filetype-video-mp4-24.png",

            // Audio
            [".mp3"]  = "filetype-audio-mp3-24.png",
            [".flac"] = "filetype-audio-flac-24.png",
            [".wav"]  = "filetype-audio-wav-24.png",
            [".ogg"]  = "filetype-audio-ogg-24.png",
            [".m4a"]  = "filetype-audio-m4a-24.png",
            [".wma"]  = "filetype-audio-generic-24.png",
            [".aac"]  = "filetype-audio-generic-24.png",
            [".opus"] = "filetype-audio-ogg-24.png",
            [".mid"]  = "filetype-audio-generic-24.png",
            [".midi"] = "filetype-audio-generic-24.png",

            // Archives
            [".zip"]  = "file-archive-24.png",
            [".7z"]   = "file-archive-24.png",
            [".rar"]  = "file-archive-24.png",
            [".tar"]  = "filetype-application-tar-24.png",
            [".gz"]   = "filetype-application-gzip-24.png",
            [".bz2"]  = "filetype-application-tar-24.png",
            [".xz"]   = "filetype-application-tar-24.png",
            [".tgz"]  = "filetype-application-tar-24.png",
            [".zst"]  = "filetype-application-tar-24.png",

            // ISO / Disc
            [".iso"]  = "filetype-application-iso-24.png",
            [".img"]  = "filetype-application-iso-24.png",
            [".cue"]  = "filetype-application-iso-24.png",
            [".nrg"]  = "filetype-application-iso-24.png",
            [".mdf"]  = "filetype-application-iso-24.png",

            // Documents
            [".pdf"]  = "filetype-application-pdf-24.png",
            [".doc"]  = "filetype-text-generic-24.png",
            [".docx"] = "filetype-text-generic-24.png",
            [".xls"]  = "filetype-text-generic-24.png",
            [".xlsx"] = "filetype-text-generic-24.png",
            [".ppt"]  = "filetype-text-generic-24.png",
            [".pptx"] = "filetype-text-generic-24.png",
            [".odt"]  = "filetype-text-generic-24.png",
            [".ods"]  = "filetype-text-generic-24.png",
            [".rtf"]  = "filetype-text-generic-24.png",

            // Text
            [".txt"]  = "filetype-text-generic-24.png",
            [".log"]  = "filetype-text-log-24.png",
            [".out"]  = "filetype-text-log-24.png",
            [".err"]  = "filetype-text-log-24.png",
            [".md"]   = "filetype-text-markdown-24.png",
            [".rst"]  = "filetype-text-markdown-24.png",
            [".csv"]  = "filetype-text-generic-24.png",
            [".tsv"]  = "filetype-text-generic-24.png",
            [".nfo"]  = "filetype-text-generic-24.png",
            [".ini"]  = "filetype-text-generic-24.png",
            [".cfg"]  = "filetype-text-generic-24.png",
            [".conf"] = "filetype-text-generic-24.png",
            [".yaml"] = "filetype-text-generic-24.png",
            [".yml"]  = "filetype-text-generic-24.png",
            [".toml"] = "filetype-text-generic-24.png",
            [".env"]  = "filetype-text-log-24.png",
            [".srt"]  = "filetype-text-generic-24.png",
            [".sub"]  = "filetype-text-generic-24.png",
            [".ass"]  = "filetype-text-generic-24.png",
            [".sql"]  = "filetype-text-generic-24.png",
            [".tex"]  = "filetype-text-generic-24.png",
            [".bib"]  = "filetype-text-generic-24.png",

            // Code
            [".py"]   = "filetype-text-python-24.png",
            [".js"]   = "filetype-text-javascript-24.png",
            [".jsx"]  = "filetype-text-javascript-24.png",
            [".ts"]   = "filetype-text-javascript-24.png",
            [".tsx"]  = "filetype-text-javascript-24.png",
            [".c"]    = "filetype-text-c-24.png",
            [".h"]    = "filetype-text-c-24.png",
            [".cpp"]  = "filetype-text-cpp-24.png",
            [".hpp"]  = "filetype-text-cpp-24.png",
            [".cs"]   = "filetype-text-csharp-24.png",
            [".java"] = "filetype-text-java-24.png",
            [".rs"]   = "filetype-text-rust-24.png",
            [".go"]   = "filetype-text-go-24.png",
            [".rb"]   = "filetype-text-ruby-24.png",
            [".pl"]   = "filetype-text-perl-24.png",
            [".pm"]   = "filetype-text-perl-24.png",
            [".lua"]  = "filetype-text-lua-24.png",
            [".sh"]   = "filetype-text-shell-24.png",
            [".bash"] = "filetype-text-shell-24.png",
            [".zsh"]  = "filetype-text-shell-24.png",
            [".ps1"]  = "filetype-text-shell-24.png",
            [".bat"]  = "filetype-text-shell-24.png",
            [".cmd"]  = "filetype-text-shell-24.png",
            [".css"]  = "filetype-text-css-24.png",
            [".scss"] = "filetype-text-css-24.png",
            [".less"] = "filetype-text-css-24.png",
            [".html"] = "filetype-text-css-24.png",
            [".htm"]  = "filetype-text-css-24.png",
            [".xml"]  = "filetype-text-xml-24.png",
            [".json"] = "filetype-text-javascript-24.png",
            [".vue"]  = "filetype-text-javascript-24.png",
            [".swift"] = "filetype-text-csharp-24.png",
            [".kt"]   = "filetype-text-java-24.png",
            [".kts"]  = "filetype-text-java-24.png",
            [".dart"] = "filetype-text-java-24.png",
            [".ex"]   = "filetype-text-ruby-24.png",
            [".exs"]  = "filetype-text-ruby-24.png",
            [".hs"]   = "filetype-text-ruby-24.png",
            [".ml"]   = "filetype-text-ruby-24.png",
            [".zig"]  = "filetype-text-rust-24.png",
            [".nim"]  = "filetype-text-rust-24.png",
            [".v"]    = "filetype-text-c-24.png",
            [".vh"]   = "filetype-text-c-24.png",
            [".asm"]  = "filetype-text-c-24.png",
            [".s"]    = "filetype-text-c-24.png",

            // Executables
            [".exe"]  = "filetype-application-executable-24.png",
            [".msi"]  = "filetype-application-executable-24.png",
            [".appx"] = "filetype-application-executable-24.png",
            [".dll"]  = "filetype-application-executable-24.png",
            [".so"]   = "filetype-application-executable-24.png",
            [".dylib"] = "filetype-application-executable-24.png",
        };

        private static readonly string GenericIcon = "file-generic-24.png";

        public string Icon => IsDrive
            ? "ms-appx:///Assets/FileTypes/drive-harddisk-24.png"
            : IsDirectory
                ? $"ms-appx:///Assets/FileTypes/folder-{_folderColor}-24.png"
                : (IsArchive ? "ms-appx:///Assets/FileTypes/file-archive-24.png"
                              : $"ms-appx:///Assets/FileTypes/{GetFileIcon(Name)}");

        public static string GetFileIcon(string fileName)
        {
            var ext = System.IO.Path.GetExtension(fileName);
            if (!string.IsNullOrEmpty(ext) && ExtIcons.TryGetValue(ext, out var icon))
                return icon;
            return GenericIcon;
        }

        public string SizeDisplay => IsDirectory ? "" : FormatSize(SizeBytes);

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }

    public sealed partial class ColumnListView : UserControl, INavigable, INotifyPropertyChanged
    {
        private string _currentPath;
        private string _statusText = "";
        private List<EntryViewModel> _entries = new List<EntryViewModel>();

        public string CurrentPath
        {
            get => _currentPath;
            set
            {
                if (_currentPath != value)
                {
                    _currentPath = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentPath)));
                    HeaderText.Text = value ?? "\\\\ (Drives)";
                }
            }
        }

        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
                StatusTextBlock.Text = value;
            }
        }

        public bool IsRoot => string.IsNullOrEmpty(_currentPath);

        public event PropertyChangedEventHandler PropertyChanged;

        public event EventHandler<FileEntry> ItemOpened;
        public event EventHandler BackRequested;
        public event EventHandler<string> DirectoryChanged;

        public ColumnListView()
        {
            Log.Information("ColumnListView.ctor");
            this.InitializeComponent();
        }

        public async void LoadAsync(string path)
        {
            Log.Information("ColumnListView.LoadAsync: path={Path}", path ?? "(root)");
            CurrentPath = path;

            List<FileEntry> entries;
            try
            {
                entries = await DirectoryScanner.ScanAsync(path);
            }
            catch (Exception ex)
            {
                Log.Error("ColumnListView.LoadAsync: scan failed for '{Path}'", ex, path ?? "(root)");
                StatusText = $"ERROR: {ex.Message}";
                EntryList.ItemsSource = null;
                _entries.Clear();
                return;
            }

            _entries = entries
                .OrderBy(e => e.IsDirectory ? 0 : 1)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Select(e => new EntryViewModel
                {
                    Name = e.Name,
                    FullPath = e.FullPath,
                    IsDirectory = e.IsDirectory,
                    IsDrive = e.IsDrive,
                    IsArchive = e.IsArchive,
                    SizeBytes = e.SizeBytes
                })
                .ToList();

            EntryList.ItemsSource = _entries;
            StatusText = $"{_entries.Count} items";
            Log.Information("ColumnListView.LoadAsync: displaying {Count} entries", _entries.Count);

            if (EntryList.Items.Count > 0)
                EntryList.SelectedIndex = 0;

            DirectoryChanged?.Invoke(this, path);
        }

        private void EntryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EntryList.SelectedItem is EntryViewModel selected)
                Log.Verbose("ColumnListView.SelectionChanged: '{Name}'", selected.Name);
        }

        private void EntryList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is EntryViewModel clicked)
            {
                Log.Information("ColumnListView.ItemClick: '{Name}' (isDir={IsDir})",
                    clicked.Name, clicked.IsDirectory);
                ItemOpened?.Invoke(this, ToFileEntry(clicked));
            }
        }

        private FileEntry ToFileEntry(EntryViewModel vm)
        {
            return new FileEntry
            {
                Name = vm.Name,
                FullPath = vm.FullPath,
                IsDirectory = vm.IsDirectory,
                IsArchive = vm.IsArchive,
                SizeBytes = vm.SizeBytes
            };
        }

        public FileEntry GetSelectedEntry()
        {
            if (EntryList.SelectedItem is EntryViewModel selected)
                return ToFileEntry(selected);
            return null;
        }

        public void ClearSelection()
        {
            EntryList.SelectedItem = null;
        }

        // --- INavigable ---

        public bool IsMediaFullscreen => false;
        public bool IsMediaPlayerActive => false;

        public void OnDPadUp(bool isRepeat = false)
        {
            if (EntryList.SelectedIndex > 0)
                EntryList.SelectedIndex--;
            EntryList.ScrollIntoView(EntryList.SelectedItem);
            Log.Verbose("ColumnListView.OnDPadUp: index={Index}", EntryList.SelectedIndex);
        }

        public void OnDPadDown(bool isRepeat = false)
        {
            if (EntryList.SelectedIndex < _entries.Count - 1)
                EntryList.SelectedIndex++;
            EntryList.ScrollIntoView(EntryList.SelectedItem);
            Log.Verbose("ColumnListView.OnDPadDown: index={Index}", EntryList.SelectedIndex);
        }

        public void OnDPadLeft() { }
        public void OnDPadRight() { }

        public void OnConfirm()
        {
            if (EntryList.SelectedItem is EntryViewModel selected)
            {
                Log.Information("ColumnListView.OnConfirm: '{Name}' (isDir={IsDir})",
                    selected.Name, selected.IsDirectory);
                ItemOpened?.Invoke(this, ToFileEntry(selected));
            }
        }

        public void OnBack()
        {
            Log.Information("ColumnListView.OnBack");
            BackRequested?.Invoke(this, EventArgs.Empty);
        }

        public void OnContextMenu() { }

        public void OnRefresh() { }

        public void OnSettings() { }

        public void OnPageUp()
        {
            EntryList.SelectedIndex = Math.Max(0, EntryList.SelectedIndex - 10);
            EntryList.ScrollIntoView(EntryList.SelectedItem);
            Log.Verbose("ColumnListView.OnPageUp: index={Index}", EntryList.SelectedIndex);
        }

        public void OnPageDown()
        {
            EntryList.SelectedIndex = Math.Min(_entries.Count - 1, EntryList.SelectedIndex + 10);
            EntryList.ScrollIntoView(EntryList.SelectedItem);
            Log.Verbose("ColumnListView.OnPageDown: index={Index}", EntryList.SelectedIndex);
        }

        public void OnSeekBack() { }
        public void OnSeekForward() { }
        public void OnSeekRepeat(int seconds) { }
        public void OnTriggerHeld(float leftTrigger, float rightTrigger) { }
        public void OnLeftStickMove(float x, float y) { }
        public void OnRightStickMove(float x, float y) { }

        public void OnScrollHorizontal(double delta) { }

        public void OnScrollVertical(double delta) { }

        public void OnSelectVisualizer() { }
    }

    /// <summary>
    /// Custom ListView that blocks built-in PageUp/PageDown scrolling.
    /// GamepadInputService handles all navigation — ListView should not process keyboard nav events.
    /// </summary>
    public class RetroListView : Windows.UI.Xaml.Controls.ListView
    {
        protected override void OnKeyDown(Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.Up:
                case Windows.System.VirtualKey.Down:
                case Windows.System.VirtualKey.Left:
                case Windows.System.VirtualKey.Right:
                case Windows.System.VirtualKey.Enter:
                case Windows.System.VirtualKey.GamepadA:
                case Windows.System.VirtualKey.GamepadB:
                case Windows.System.VirtualKey.GamepadDPadUp:
                case Windows.System.VirtualKey.GamepadDPadDown:
                case Windows.System.VirtualKey.GamepadDPadLeft:
                case Windows.System.VirtualKey.GamepadDPadRight:
                case Windows.System.VirtualKey.PageUp:
                case Windows.System.VirtualKey.PageDown:
                case Windows.System.VirtualKey.GamepadLeftTrigger:
                case Windows.System.VirtualKey.GamepadRightTrigger:
                    e.Handled = true;
                    return;
            }
            base.OnKeyDown(e);
        }
    }
}
