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
    public sealed partial class FolderBrowserDialog : UserControl
    {
        private TaskCompletionSource<string> _tcs;
        private string _currentPath;
        private List<BrowserEntry> _entries = new List<BrowserEntry>();

        public bool IsOpen => Visibility == Visibility.Visible;

        public FolderBrowserDialog()
        {
            this.InitializeComponent();
        }

        public Task<string> ShowAsync(string initialPath = null)
        {
            _tcs = new TaskCompletionSource<string>();
            _currentPath = initialPath;

            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;

            LoadDirectory(initialPath);

            EntryList.Focus(FocusState.Programmatic);

            return _tcs.Task;
        }

        private async void LoadDirectory(string path)
        {
            Log.Info("FolderBrowserDialog.LoadDirectory: {Path}", path ?? "(root)");
            _currentPath = path;

            string dirName = string.IsNullOrEmpty(path)
                ? "\\\\ (Drives)"
                : System.IO.Path.GetFileName(path.TrimEnd('\\')) ?? path;
            CurrentPathText.Text = path ?? "\\\\ (Drives)";

            // Update Move Here label with current folder name
            string moveHereName = string.IsNullOrEmpty(path)
                ? "Move Here"
                : $"Move Here ({dirName})";
            FooterALabel.Text = moveHereName;

            // Rebuild virtual entry with updated name
            var moveHereEntry = new BrowserEntry
            {
                Name = moveHereName,
                FullPath = null,
                IsDirectory = false,
                IsVirtual = true,
                Icon = "ms-appx:///Assets/Views/FileActionSheet/fileactionsheet-move-48.png"
            };

            List<FileEntry> rawEntries;
            try
            {
                rawEntries = await DirectoryScanner.ScanAsync(path);
            }
            catch (Exception ex)
            {
                Log.Err("FolderBrowserDialog.LoadDirectory: scan failed", ex);
                CurrentPathText.Text = $"ERROR: {ex.Message}";
                _entries.Clear();
                _entries.Add(moveHereEntry);
                EntryList.ItemsSource = _entries;
                EntryList.SelectedIndex = 0;
                return;
            }

            _entries = new List<BrowserEntry> { moveHereEntry };
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
                    Icon = $"ms-appx:///Assets/FileTypes/folder-{EntryViewModel.FolderColor}-24.png"
                }));

            EntryList.ItemsSource = _entries;
            EntryList.SelectedIndex = 0;

            EntryList.Focus(FocusState.Programmatic);
        }

        private void EntryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(EntryList.SelectedItem is BrowserEntry selected)) return;

            // Update A button label based on selection
            if (selected.IsVirtual)
                FooterALabel.Text = _currentPath != null
                    ? $"Move Here ({System.IO.Path.GetFileName(_currentPath.TrimEnd('\\'))})"
                    : "Move Here";
            else
                FooterALabel.Text = "Navigate";
        }

        private void EntryList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is BrowserEntry clicked)
            {
                if (clicked.IsVirtual)
                {
                    ConfirmSelection();
                    return;
                }
                if (clicked.IsDirectory)
                {
                    LoadDirectory(clicked.FullPath);
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
                ConfirmSelection();
                return;
            }

            if (selected.IsDirectory)
            {
                LoadDirectory(selected.FullPath);
            }
        }

        private void OnCancel()
        {
            Close(null);
        }

        private void ConfirmSelection()
        {
            var destDir = _currentPath;
            Log.Info("FolderBrowserDialog: confirmed destination '{Dest}'", destDir ?? "(root)");
            Close(destDir);
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
