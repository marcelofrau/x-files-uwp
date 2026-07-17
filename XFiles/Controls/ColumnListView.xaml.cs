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

        public string Name { get; set; }
        public string FullPath { get; set; }
        public bool IsDirectory { get; set; }
        public bool IsArchive { get; set; }
        public long SizeBytes { get; set; }

        private static string _folderColor = "blue";

        public static string FolderColor
        {
            get => _folderColor;
            set => _folderColor = value;
        }

        public string Icon => IsDirectory
            ? $"ms-appx:///Assets/FileTypes/folder-{_folderColor}-24.png"
            : (IsArchive ? "ms-appx:///Assets/FileTypes/file-archive-24.png"
                          : "ms-appx:///Assets/FileTypes/file-generic-24.png");

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

        public void OnDPadUp()
        {
            if (EntryList.SelectedIndex > 0)
                EntryList.SelectedIndex--;
            Log.Verbose("ColumnListView.OnDPadUp: index={Index}", EntryList.SelectedIndex);
        }

        public void OnDPadDown()
        {
            if (EntryList.SelectedIndex < _entries.Count - 1)
                EntryList.SelectedIndex++;
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

        public void OnPageUp()
        {
            EntryList.SelectedIndex = Math.Max(0, EntryList.SelectedIndex - 10);
            Log.Verbose("ColumnListView.OnPageUp: index={Index}", EntryList.SelectedIndex);
        }

        public void OnPageDown()
        {
            EntryList.SelectedIndex = Math.Min(_entries.Count - 1, EntryList.SelectedIndex + 10);
            Log.Verbose("ColumnListView.OnPageDown: index={Index}", EntryList.SelectedIndex);
        }
    }
}
