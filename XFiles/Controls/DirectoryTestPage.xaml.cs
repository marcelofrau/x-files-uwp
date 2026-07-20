using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using XFiles.FileSystem;

using XFiles.Navigation;

namespace XFiles.Controls
{
    public sealed partial class DirectoryTestPage : Page, INavigable, INotifyPropertyChanged
    {
        private string _currentPath;
        private readonly Stack<string> _pathHistory = new Stack<string>();
        private string _statusText = "";

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText))); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public DirectoryTestPage()
        {
            Log.Information("DirectoryTestPage.ctor");
            this.InitializeComponent();
            this.KeyDown += OnKeyDown;
            this.PointerPressed += OnPointerPressed;
            this.Loaded += (s, e) =>
            {
                Log.Verbose("DirectoryTestPage loaded — setting focus");
                this.Focus(FocusState.Programmatic);
                if (App.GamepadInput != null)
                {
                    App.GamepadInput.ActiveNavigable = this;
                    Log.Information("DirectoryTestPage: set as ActiveNavigable");
                }
            };

            // DoubleTapped = open folder (primary navigation gesture)
            EntryList.DoubleTapped += (s, e) =>
            {
                Log.Verbose("EntryList.DoubleTapped");
                OpenSelected();
            };

            _currentPath = null;
            LoadEntriesAsync();
        }

        private async void LoadEntriesAsync()
        {
            Log.Information("LoadEntriesAsync: path={Path}", _currentPath ?? "(root)");

            // Show loading indicator
            LoadingPanel.Visibility = Visibility.Visible;
            EntryList.Visibility = Visibility.Collapsed;

            List<FileEntry> entries;
            try
            {
                entries = await DirectoryScanner.ScanAsync(_currentPath);
            }
            catch (Exception ex)
            {
                Log.Error("LoadEntriesAsync: scan failed for '{Path}'", ex, _currentPath ?? "(root)");
                PathText.Text = $"ERROR: {ex.Message}";
                EntryList.ItemsSource = null;
                LoadingPanel.Visibility = Visibility.Collapsed;
                return;
            }

            // Hide loading, show list
            LoadingPanel.Visibility = Visibility.Collapsed;
            EntryList.Visibility = Visibility.Visible;

            PathText.Text = _currentPath ?? "\\\\ (Drives)";

            var viewModels = new List<EntryViewModel>();
            foreach (var e in entries)
            {
                viewModels.Add(new EntryViewModel
                {
                    Name = e.Name,
                    FullPath = e.FullPath,
                    IsDirectory = e.IsDirectory,
                    IsDrive = e.IsDrive,
                    IsArchive = e.IsArchive,
                    SizeBytes = e.SizeBytes
                });
            }

            EntryList.ItemsSource = viewModels;
            StatusText = $"{entries.Count} items";
            Log.Information("LoadEntriesAsync: displaying {Count} entries", entries.Count);

            if (EntryList.Items.Count > 0)
                EntryList.SelectedIndex = 0;
        }

        private void NavigateTo(string path)
        {
            Log.Information("NavigateTo: '{Path}' (from '{From}')", path, _currentPath ?? "(root)");
            _pathHistory.Push(_currentPath);
            _currentPath = path;
            LoadEntriesAsync();
        }

        private void NavigateBack()
        {
            if (_pathHistory.Count > 0)
            {
                _currentPath = _pathHistory.Pop();
                Log.Information("NavigateBack: to '{Path}'", _currentPath ?? "(root)");
                LoadEntriesAsync();
            }
            else
            {
                Log.Verbose("NavigateBack: already at root, nothing to do");
            }
        }

        private void OpenSelected()
        {
            if (EntryList.SelectedItem is EntryViewModel selected)
            {
                Log.Information("OpenSelected: '{Name}' (isDir={IsDir}, path='{Path}')",
                    selected.Name, selected.IsDirectory, selected.FullPath);
                if (selected.IsDirectory)
                {
                    NavigateTo(selected.FullPath);
                }
            }
            else
            {
                Log.Verbose("OpenSelected: nothing selected");
            }
        }

        private void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            Log.Verbose("OnKeyDown: key={Key}", e.Key);
            switch (e.Key)
            {
                case VirtualKey.Enter:
                case VirtualKey.Right:
                    OpenSelected();
                    e.Handled = true;
                    break;
                case VirtualKey.Back:
                case VirtualKey.Left:
                    NavigateBack();
                    e.Handled = true;
                    break;
                case VirtualKey.Up:
                    if (EntryList.SelectedIndex > 0)
                        EntryList.SelectedIndex--;
                    e.Handled = true;
                    break;
                case VirtualKey.Down:
                    if (EntryList.SelectedIndex < EntryList.Items.Count - 1)
                        EntryList.SelectedIndex++;
                    e.Handled = true;
                    break;
                case VirtualKey.Escape:
                    NavigateBack();
                    e.Handled = true;
                    break;
            }
        }

        private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var props = e.GetCurrentPoint(this).Properties;
            Log.Verbose("OnPointerPressed: left={Left}, right={Right}, middle={Middle}",
                props.IsLeftButtonPressed, props.IsRightButtonPressed, props.IsMiddleButtonPressed);
            if (props.IsRightButtonPressed)
            {
                NavigateBack();
                e.Handled = true;
            }
        }

        private void EntryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EntryList.SelectedItem is EntryViewModel selected)
                Log.Verbose("SelectionChanged: '{Name}'", selected.Name);
        }

        // --- INavigable ---

        public bool IsMediaFullscreen => false;
        public bool IsMediaPlayerActive => false;

        public void OnDPadUp()
        {
            if (EntryList.SelectedIndex > 0)
                EntryList.SelectedIndex--;
        }

        public void OnDPadDown()
        {
            if (EntryList.SelectedIndex < EntryList.Items.Count - 1)
                EntryList.SelectedIndex++;
        }

        public void OnDPadLeft() => NavigateBack();
        public void OnDPadRight() => OpenSelected();

        public void OnConfirm() => OpenSelected();
        public void OnBack() => NavigateBack();
        public void OnContextMenu() { }

        public void OnPageUp()
        {
            EntryList.SelectedIndex = Math.Max(0, EntryList.SelectedIndex - 10);
        }

        public void OnPageDown()
        {
            EntryList.SelectedIndex = Math.Min(EntryList.Items.Count - 1, EntryList.SelectedIndex + 10);
        }

        public void OnSeekBack() { }
        public void OnSeekForward() { }
        public void OnSeekRepeat(int seconds) { }
        public void OnTriggerHeld(float leftTrigger, float rightTrigger) { }
        public void OnLeftStickMove(float x, float y) { }
        public void OnRightStickMove(float x, float y) { }

        public void OnScrollHorizontal(double delta) { }

        public void OnScrollVertical(double delta) { }

        public void OnRefresh() { }

        public void OnSettings() { }
    }
}
