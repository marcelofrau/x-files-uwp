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
            _ = Windows.UI.Xaml.Input.FocusManager.TryFocusAsync(Overlay, FocusState.Programmatic);
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
                case Windows.System.VirtualKey.GamepadA:
                case Windows.System.VirtualKey.Enter:
                    e.Handled = true;
                    Close(true);
                    break;
                case Windows.System.VirtualKey.GamepadB:
                case Windows.System.VirtualKey.Escape:
                    e.Handled = true;
                    Close(false);
                    break;
            }
        }

        private void OnOverlayTapped(object sender, TappedRoutedEventArgs e)
        {
            Close(false);
        }

        private void Close(bool result)
        {
            Overlay.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Collapsed;
            _tcs?.TrySetResult(result);
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
