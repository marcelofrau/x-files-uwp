using System;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using XFiles.FileSystem;

namespace XFiles.Controls
{
    public sealed partial class PermissionsDialog : UserControl
    {
        private TaskCompletionSource<bool> _tcs;
        private string _path;
        private bool _isBusy;

        public bool IsOpen => Visibility == Visibility.Visible;

        private const uint ATTR_READONLY = 0x1;
        private const uint ATTR_HIDDEN = 0x2;

        private static readonly Brush SelectedBg = new SolidColorBrush(Windows.UI.Color.FromArgb(60, 0x6A, 0xC2, 0x5A));
        private static readonly Brush CheckedFill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x6A, 0xC2, 0x5A));
        private static readonly Brush ClearBg = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));

        private bool _readOnly;
        private bool _hidden;
        private bool _recursive;
        private int _selection; // 0 = read-only, 1 = hidden, 2 = recursive

        public PermissionsDialog()
        {
            this.InitializeComponent();
        }

        public Task<bool> ShowAsync(string path, bool isDirectory)
        {
            Log.Info("PermissionsDialog.ShowAsync: {Path} isDir={IsDir}", path, isDirectory);
            _path = path;
            _isBusy = false;
            _selection = 0;
            _recursive = false;

            bool readOnly = false;
            bool hidden = false;
            if (FileOperations.TryGetFileAttributes(path, out uint attrs))
            {
                readOnly = (attrs & ATTR_READONLY) != 0;
                hidden = (attrs & ATTR_HIDDEN) != 0;
            }
            _readOnly = readOnly;
            _hidden = hidden;

            RecursiveRow.Visibility = isDirectory ? Visibility.Visible : Visibility.Collapsed;

            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;
            ApplyBoxes();
            UpdateSelection();
            Log.Info("PermissionsDialog.ShowAsync: ro={Ro} hidden={Hidden} recursive={Recursive}", _readOnly, _hidden, isDirectory);

            _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            return _tcs.Task;
        }

        private void ApplyBoxes()
        {
            SetBox(ReadOnlyBox, _readOnly);
            SetBox(HiddenBox, _hidden);
            SetBox(RecursiveBox, _recursive);
        }

        private static void SetBox(Border box, bool on)
        {
            box.Background = on ? CheckedFill : ClearBg;
        }

        private void UpdateSelection()
        {
            ReadOnlyRow.Background = _selection == 0 ? SelectedBg : ClearBg;
            HiddenRow.Background = _selection == 1 ? SelectedBg : ClearBg;
            RecursiveRow.Background = _selection == 2 ? SelectedBg : ClearBg;
        }

        /// <summary>Router entry: dpad moves the selection ring between rows.</summary>
        public void HandleDPad(VirtualKey key)
        {
            if (_isBusy) return;
            if (key == VirtualKey.Up) _selection = (_selection + 2) % 3;
            else if (key == VirtualKey.Down) _selection = (_selection + 1) % 3;
            UpdateSelection();
        }

        /// <summary>Router entry: A toggles the selected row, Y applies, B cancels.</summary>
        public void HandleButton(VirtualKey key)
        {
            if (_isBusy) return;
            switch (key)
            {
                case VirtualKey.GamepadA:
                case VirtualKey.Enter:
                    ToggleSelected();
                    break;
                case VirtualKey.GamepadY:
                case VirtualKey.Y:
                    _ = ApplyAsync();
                    break;
                case VirtualKey.GamepadB:
                case VirtualKey.Escape:
                    Log.Info("PermissionsDialog: cancelled");
                    Close(false);
                    break;
            }
        }

        private void ToggleSelected()
        {
            switch (_selection)
            {
                case 0: _readOnly = !_readOnly; break;
                case 1: _hidden = !_hidden; break;
                case 2: _recursive = !_recursive; break;
            }
            ApplyBoxes();
        }

        private async Task ApplyAsync()
        {
            if (_isBusy || _path == null) return;
            _isBusy = true;
            Log.Info("PermissionsDialog.ApplyAsync: {Path} ro={Ro} hidden={Hidden} recursive={Recursive}",
                _path, _readOnly, _hidden, _recursive);

            uint desired = 0;
            if (_readOnly) desired |= ATTR_READONLY;
            if (_hidden) desired |= ATTR_HIDDEN;

            await Task.Run(() => FileOperations.ApplyAttributesRecursive(_path, desired, _recursive));

            _isBusy = false;
            Close(true);
        }

        public void Close(bool result)
        {
            Overlay.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Collapsed;
            Log.Info("PermissionsDialog.Close: result={Result}", result);
            _tcs?.TrySetResult(result);
        }
    }
}