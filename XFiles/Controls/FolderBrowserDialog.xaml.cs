using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using XFiles.FileSystem;
using XFiles.Navigation;

namespace XFiles.Controls
{
    /// <summary>
    /// Toggles a row element between the section-divider state and the normal
    /// icon+name state (parameter "normal" shows the divider when IsSeparator,
    /// "reverse" shows the content row when NOT a separator).
    /// </summary>
    internal sealed class IsSeparatorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool isSep = value is bool b && b;
            bool divider = parameter as string == "normal" ? isSep : !isSep;
            return divider ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }

    public enum PickerMode
    {
        Folder,
        File
    }

    public sealed partial class FolderBrowserDialog : UserControl
    {
        private TaskCompletionSource<string> _tcs;
        private string _currentPath;
        private List<BrowserEntry> _entries = new List<BrowserEntry>();
        private PickerMode _mode = PickerMode.Folder;
        private IReadOnlyList<string> _fileExtensions;
        private string _confirmLabel;
        private string _confirmIcon;
        private DispatcherTimer _loadingTimer;

        public bool IsOpen => Visibility == Visibility.Visible;

        public FolderBrowserDialog()
        {
            this.InitializeComponent();
            _loadingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _loadingTimer.Tick += (s, e) =>
            {
                _loadingTimer.Stop();
                LoadingRing.IsActive = true;
            };
        }

        public Task<string> ShowAsync(string initialPath = null)
        {
            return ShowAsync(initialPath, PickerMode.Folder, null);
        }

        public Task<string> ShowAsync(string initialPath, PickerMode mode,
            IReadOnlyList<string> fileExtensions = null)
        {
            return ShowAsync(initialPath, mode, fileExtensions, null, null);
        }

        /// <summary>
        /// Shows the picker. <paramref name="confirmLabel"/> overrides the confirm
        /// action label ("Move Here" by default); <paramref name="confirmIcon"/>
        /// overrides the confirm entry icon. A null label keeps the existing
        /// Move/copy behavior for all current callers.
        /// </summary>
        public Task<string> ShowAsync(string initialPath, PickerMode mode,
            IReadOnlyList<string> fileExtensions, string confirmLabel, string confirmIcon)
        {
            _mode = mode;
            _fileExtensions = fileExtensions;
            _confirmLabel = confirmLabel;
            _confirmIcon = confirmIcon;
            _tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _currentPath = initialPath;

            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;

            TitleText.Text = mode == PickerMode.File
                ? "Select file"
                : "Select destination";

            LoadDirectory(initialPath);

            EntryList.Focus(FocusState.Programmatic);

            return _tcs.Task;
        }

        private async void LoadDirectory(string path)
        {
            Log.Info("FolderBrowserDialog.LoadDirectory: {Path}", path ?? "(root)");

            // Only real local disk paths are supported. Portals, archives and any
            // other non-local path fall back to the drives root.
            if (!string.IsNullOrEmpty(path) && !IsLocalDiskPath(path))
            {
                Log.Warn("FolderBrowserDialog.LoadDirectory: non-local path {Path} - showing drives", path);
                path = null;
            }

            _currentPath = path;

            // Show loading spinner after 1s if directory scan is slow.
            LoadingRing.IsActive = false;
            _loadingTimer.Start();

            bool isRoot = string.IsNullOrEmpty(path);
            string dirName = isRoot ? "Drives" : System.IO.Path.GetFileName(path.TrimEnd('\\'));
            if (string.IsNullOrEmpty(dirName))
                dirName = path.TrimEnd('\\');
            CurrentPathText.Text = isRoot ? "Drives" : path;

            bool fileMode = _mode == PickerMode.File;
            // At the drives root the confirm action makes no sense (there is no
            // destination), so the A button reads "Navigate" and the virtual
            // confirm entry is not shown.
            string footerA = fileMode || isRoot ? "Navigate" : ConfirmLabelForPath(_currentPath);
            string moveHereName = ConfirmLabelForPath(_currentPath);
            if (!isRoot)
            {
                footerA = $"{footerA} ({dirName})";
                moveHereName = $"{moveHereName} ({dirName})";
            }
            FooterALabel.Text = footerA;

            // Rebuild virtual entry with updated name (folder mode only — file
            // mode selects actual files instead).
            var moveHereEntry = new BrowserEntry
            {
                Name = moveHereName,
                FullPath = null,
                IsDirectory = false,
                IsVirtual = true,
                Icon = _confirmIcon ?? "ms-appx:///Assets/Views/FileActionSheet/fileactionsheet-move-48.png"
            };

            List<FileEntry> rawEntries;
            try
            {
                rawEntries = isRoot
                    ? DirectoryScanner.ScanDrivesOnly()
                    : await DirectoryScanner.ScanAsync(path);
            }
            catch (Exception ex)
            {
                _loadingTimer.Stop();
                LoadingRing.IsActive = false;
                if (isRoot)
                {
                    Log.Err("FolderBrowserDialog.LoadDirectory: root scan failed", ex);
                    CurrentPathText.Text = $"ERROR: {ex.Message}";
                    _entries.Clear();
                    EntryList.ItemsSource = _entries;
                    EntryList.SelectedIndex = 0;
                    return;
                }
                Log.Warn("FolderBrowserDialog.LoadDirectory: scan failed for {Path} - showing drives", ex, path);
                LoadDirectory(null);
                return;
            }

            _entries = new List<BrowserEntry>();

            // Groups, in display order, separated by divider rows:
            //   [action row: Move Here / Extract Here / Copy Here]
            //      |sep|
            //   [Drives jump + drive entries]
            //      |sep|
            //   [folders] (+ files in file mode)
            var driveGroup = new List<BrowserEntry>();
            if (!isRoot)
            {
                driveGroup.Add(new BrowserEntry
                {
                    Name = "Drives",
                    FullPath = null,
                    IsDirectory = true,
                    IsDrive = false,
                    Icon = "ms-appx:///Assets/Views/FileActionSheet/fileactionsheet-hdd-48.png"
                });
            }

            string driveIcon = "ms-appx:///Assets/Views/FileActionSheet/fileactionsheet-hdd-48.png";
            string folderIcon = $"ms-appx:///Assets/FileTypes/folder-{EntryViewModel.FolderColor}-24.png";

            var driveEntries = rawEntries
                .Where(e => e.IsDirectory && e.IsDrive)
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Select(e => new BrowserEntry
                {
                    Name = e.Name,
                    FullPath = e.FullPath,
                    IsDirectory = true,
                    IsDrive = true,
                    Icon = driveIcon
                });
            driveGroup.AddRange(driveEntries);

            var folderGroup = rawEntries
                .Where(e => e.IsDirectory && !e.IsDrive)
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Select(e => new BrowserEntry
                {
                    Name = e.Name,
                    FullPath = e.FullPath,
                    IsDirectory = true,
                    IsDrive = false,
                    Icon = folderIcon
                })
                .ToList();

            // File mode: also list files (directories above). When a filter is
            // given, only matching extensions are shown; a null filter lists all.
            var fileGroup = new List<BrowserEntry>();
            if (fileMode)
            {
                var filter = _fileExtensions;
                fileGroup.AddRange(rawEntries
                    .Where(e => !e.IsDirectory
                        && (filter == null
                            || filter.Contains(System.IO.Path.GetExtension(e.Name), StringComparer.OrdinalIgnoreCase)))
                    .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(e => new BrowserEntry
                    {
                        Name = e.Name,
                        FullPath = e.FullPath,
                        IsDirectory = false,
                        IsDrive = false,
                        Icon = FileIcon(e.Name)
                    }));
            }

            // Compose the rows with divider rows between groups.
            if (!fileMode && !isRoot)
                _entries.Add(moveHereEntry);
            if (!isRoot)
            {
                if (_entries.Count > 0)
                    _entries.Add(SeparatorEntry());
                _entries.AddRange(driveGroup);
            }
            else
            {
                _entries.AddRange(driveGroup);
            }
            if (folderGroup.Count > 0)
            {
                _entries.Add(SeparatorEntry());
                _entries.AddRange(folderGroup);
            }
            if (fileGroup.Count > 0)
            {
                _entries.Add(SeparatorEntry());
                _entries.AddRange(fileGroup);
            }

            EntryList.ItemsSource = _entries;
            EntryList.SelectedIndex = 0;

            // Hide loading spinner (may have been shown by timer).
            _loadingTimer.Stop();
            LoadingRing.IsActive = false;

            EntryList.Focus(FocusState.Programmatic);
        }

        /// <summary>
        /// Resolves the confirm label for a given path: the explicit override when
        /// set, otherwise "Move Here" (with the folder name when inside a folder).
        /// </summary>
        private string ConfirmLabelForPath(string path)
        {
            if (!string.IsNullOrEmpty(_confirmLabel))
                return _confirmLabel;
            return "Move Here";
        }

        private static string FileIcon(string fileName)
        {
            string ext = System.IO.Path.GetExtension(fileName);
            if (MusicFormatClassifier.IsChiptune(ext))
                return "ms-appx:///Assets/FileTypes/filetype-audio-x-generic-24.png";
            switch (ext.ToLowerInvariant())
            {
                case ".mp3": return "ms-appx:///Assets/FileTypes/filetype-audio-mp3-24.png";
                case ".flac": return "ms-appx:///Assets/FileTypes/filetype-audio-flac-24.png";
                case ".wav": return "ms-appx:///Assets/FileTypes/filetype-audio-wav-24.png";
                case ".ogg": return "ms-appx:///Assets/FileTypes/filetype-audio-ogg-24.png";
                case ".m4a": return "ms-appx:///Assets/FileTypes/filetype-audio-m4a-24.png";
                case ".pdf": return "ms-appx:///Assets/FileTypes/filetype-application-pdf-24.png";
                default: return "ms-appx:///Assets/FileTypes/file-generic-24.png";
            }
        }

        private static bool IsLocalDiskPath(string path)
        {
            try
            {
                return System.IO.Path.IsPathRooted(path)
                    && System.IO.Path.GetPathRoot(path) != null
                    && System.IO.Directory.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        private static BrowserEntry SeparatorEntry() => new BrowserEntry
        {
            Name = "",
            FullPath = null,
            IsDirectory = false,
            IsSeparator = true
        };

        private void EntryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(EntryList.SelectedItem is BrowserEntry selected)) return;
            if (selected.IsSeparator) return;

            if (_mode == PickerMode.File)
            {
                // File mode: files select, directories navigate.
                FooterALabel.Text = !selected.IsDirectory && !selected.IsVirtual
                    ? "Select File"
                    : "Navigate";
                return;
            }

            // Update A button label based on selection
            if (selected.IsVirtual)
            {
                FooterALabel.Text = ConfirmLabelForPath(_currentPath);
            }
            else
                FooterALabel.Text = "Navigate";
        }

        private void EntryList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is BrowserEntry clicked)
            {
                if (clicked.IsSeparator) return;
                if (clicked.IsVirtual)
                {
                    ConfirmSelection(_currentPath);
                    return;
                }
                if (clicked.IsDirectory)
                {
                    LoadDirectory(clicked.FullPath);
                }
                else if (_mode == PickerMode.File)
                {
                    ConfirmSelection(clicked.FullPath);
                }
            }
        }

        public void HandleDPad(VirtualKey key)
        {
            switch (key)
            {
                case VirtualKey.GamepadDPadUp:
                case VirtualKey.Up:
                    MoveSelection(-1);
                    break;

                case VirtualKey.GamepadDPadDown:
                case VirtualKey.Down:
                    MoveSelection(1);
                    break;
            }
        }

        public void HandleButton(VirtualKey key)
        {
            switch (key)
            {
                case VirtualKey.GamepadA:
                case VirtualKey.Enter:
                    OnConfirm();
                    break;

                case VirtualKey.GamepadB:
                case VirtualKey.Escape:
                    OnCancel();
                    break;
            }
        }

        public void HandleStick(float y)
        {
            if (Math.Abs(y) < 0.15f) return;

            MoveSelection(y < 0 ? -1 : 1);
        }

        private void MoveSelection(int direction)
        {
            if (_entries.Count == 0) return;
            int newIndex = EntryList.SelectedIndex + direction;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (newIndex < 0) newIndex = _entries.Count - 1;
                else if (newIndex >= _entries.Count) newIndex = 0;
                if (!_entries[newIndex].IsSeparator) break;
                newIndex += direction;
            }
            EntryList.SelectedIndex = newIndex;
            EntryList.ScrollIntoView(EntryList.SelectedItem);
        }

        private void OnConfirm()
        {
            if (!(EntryList.SelectedItem is BrowserEntry selected)) return;
            if (selected.IsSeparator) return;

            if (selected.IsVirtual)
            {
                ConfirmSelection(_currentPath);
                return;
            }

            if (selected.IsDirectory)
            {
                LoadDirectory(selected.FullPath);
            }
            else if (_mode == PickerMode.File)
            {
                ConfirmSelection(selected.FullPath);
            }
        }

        private void OnCancel()
        {
            Close(null);
        }

        private void ConfirmSelection(string result)
        {
            Log.Info("FolderBrowserDialog: confirmed '{Result}'", result ?? "(root)");
            Close(result);
        }

        private void Close(string result)
        {
            Overlay.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Collapsed;
            _tcs?.TrySetResult(result);
        }

        private class BrowserEntry
        {
            public string Name { get; set; }
            public string FullPath { get; set; }
            public bool IsDirectory { get; set; }
            public bool IsDrive { get; set; }
            public bool IsVirtual { get; set; }
            public bool IsSeparator { get; set; }
            public string Icon { get; set; }
        }
    }
}
