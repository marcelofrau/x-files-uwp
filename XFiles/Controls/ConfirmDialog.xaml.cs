using System;
using System.Threading.Tasks;
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
            NoButton.Focus(FocusState.Programmatic);
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
    }
}
