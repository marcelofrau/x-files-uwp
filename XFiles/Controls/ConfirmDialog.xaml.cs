using System;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace XFiles.Controls
{
    public sealed partial class ConfirmDialog : UserControl
    {
        private TaskCompletionSource<bool> _tcs;
        public Action OnClosed;

        public ConfirmDialog()
        {
            this.InitializeComponent();
        }

        public Task<bool> ShowAsync(string message)
        {
            MessageText.Text = message;
            _tcs = new TaskCompletionSource<bool>();
            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;
            return _tcs.Task;
        }

        private void OnYesClicked(object sender, RoutedEventArgs e)
        {
            Close(true);
        }

        private void OnNoClicked(object sender, RoutedEventArgs e)
        {
            Close(false);
        }

        private void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case VirtualKey.GamepadA:
                case VirtualKey.Enter:
                    e.Handled = true;
                    Close(true);
                    break;
                case VirtualKey.GamepadB:
                case VirtualKey.Escape:
                    e.Handled = true;
                    Close(false);
                    break;
                case VirtualKey.GamepadDPadUp:
                case VirtualKey.GamepadDPadDown:
                case VirtualKey.GamepadDPadLeft:
                case VirtualKey.GamepadDPadRight:
                case VirtualKey.Up:
                case VirtualKey.Down:
                case VirtualKey.Left:
                case VirtualKey.Right:
                    e.Handled = true;
                    break;
            }
        }

        private void OnOverlayTapped(object sender, TappedRoutedEventArgs e)
        {
            Close(false);
        }

        private void Close(bool result)
        {
            Log.Information("ConfirmDialog.Close: result={Result}", result);
            Overlay.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Collapsed;
            _tcs?.TrySetResult(result);
            OnClosed?.Invoke();
        }

        public bool IsDialogVisible => Visibility == Visibility.Visible;

        public void HandleButton(VirtualKey key)
        {
            switch (key)
            {
                case VirtualKey.GamepadA:
                case VirtualKey.Enter:
                    Close(true);
                    break;
                case VirtualKey.GamepadB:
                case VirtualKey.Escape:
                    Close(false);
                    break;
            }
        }
    }
}
