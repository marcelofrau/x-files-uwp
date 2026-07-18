using System;
using System.ComponentModel;
using System.Linq;
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
            if (_navigator.Preview != null && !_navigator.Preview.IsFilePreview)
            {
                PreviewHeader.Text = _navigator.Preview.Label ?? "";
                BindList(PreviewList, _navigator.Preview);
                PreviewStatus.Text = $"{_navigator.Preview.Entries.Count} items";
                PreviewList.Visibility = Visibility.Visible;
                FilePreviewText.Visibility = Visibility.Collapsed;
            }
            else if (_navigator.Preview?.IsFilePreview == true)
            {
                PreviewHeader.Text = _navigator.Preview.Label ?? "";
                FilePreviewText.Text = $"[File: {_navigator.Preview.Label}]\nPreview not implemented yet.";
                PreviewList.Visibility = Visibility.Collapsed;
                FilePreviewText.Visibility = Visibility.Visible;
                PreviewStatus.Text = "";
            }
            else
            {
                PreviewHeader.Text = "";
                PreviewList.ItemsSource = null;
                FilePreviewText.Visibility = Visibility.Collapsed;
                PreviewStatus.Text = "";
            }
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
        }

        private void CurrentList_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.PageUp ||
                e.Key == Windows.System.VirtualKey.PageDown)
            {
                e.Handled = true;
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
