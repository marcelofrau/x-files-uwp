using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using XFiles.FileSystem;
using XFiles.Navigation;

namespace XFiles.Controls
{
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

        public bool IsOpen => Visibility == Visibility.Visible;

        public FolderBrowserDialog()
        {
            this.InitializeComponent();
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

            bool isRoot = string.IsNullOrEmpty(path);
            string dirName = isRoot ? "Drives" : System.IO.Path.GetFileName(path.TrimEnd('\\'));
            if (string.IsNullOrEmpty(dirName))
                dirName = path.TrimEnd('\\');
            CurrentPathText.Text = isRoot ? "Drives" : path;

            bool fileMode = _mode == PickerMode.File;
            // At the drives root the confirm action makes no sense (there is no
            // destination), so the A button reads "Navigate" and the virtual
            // confirm entry is not shown.
            string footerA = fileMode || isRoot ? "Navigate" : (ConfirmLabelForPath(_currentPath) ?? "Move Here");
            string moveHereName = ConfirmLabelForPath(_currentPath) ?? "Move Here";
            if (!isRoot)
                moveHereName = $"{moveHereName} ({dirName})";
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
            if (!fileMode && !isRoot)
                _entries.Add(moveHereEntry);

            // Quick jump to the drives root from any folder
            if (!isRoot)
            {
                _entries.Add(new BrowserEntry
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
            _entries.AddRange(rawEntries
                .Where(e => e.IsDirectory)
                .OrderBy(e => e.IsDrive ? 0 : 1)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Select(e => new BrowserEntry
                {
                    Name = e.Name,
                    FullPath = e.FullPath,
                    IsDirectory = true,
                    IsDrive = e.IsDrive,
                    Icon = e.IsDrive ? driveIcon : folderIcon
                }));

            // File mode: also list files (directories above). When a filter is
            // given, only matching extensions are shown; a null filter lists all.
            if (fileMode)
            {
                var filter = _fileExtensions;
                _entries.AddRange(rawEntries
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

            EntryList.ItemsSource = _entries;
            EntryList.SelectedIndex = 0;

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
            if (string.IsNullOrEmpty(path))
                return "Move Here";
            string name = System.IO.Path.GetFileName(path.TrimEnd('\\'));
            if (string.IsNullOrEmpty(name))
                name = path.TrimEnd('\\');
            return $"Move Here ({name})";
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

        private void EntryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(EntryList.SelectedItem is BrowserEntry selected)) return;

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
            int newIndex = EntryList.SelectedIndex + direction;
            if (newIndex < 0 || newIndex >= _entries.Count) return;
            EntryList.SelectedIndex = newIndex;
            EntryList.ScrollIntoView(EntryList.SelectedItem);
        }

        private void OnConfirm()
        {
            if (!(EntryList.SelectedItem is BrowserEntry selected)) return;

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
            public string Icon { get; set; }
        }
    }
}
