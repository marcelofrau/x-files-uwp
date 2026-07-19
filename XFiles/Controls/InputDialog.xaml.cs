using System;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace XFiles.Controls
{
    public sealed partial class InputDialog : UserControl
    {
        private TaskCompletionSource<string> _tcs;

        public InputDialog()
        {
            this.InitializeComponent();
        }

        public Task<string> ShowAsync(string title, string defaultValue)
        {
            TitleText.Text = title;
            InputBox.Text = defaultValue ?? "";
            _tcs = new TaskCompletionSource<string>();
            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;

            // Select all text for easy replacement
            InputBox.Focus(FocusState.Programmatic);
            InputBox.SelectAll();
            return _tcs.Task;
        }

        private void OnInputGotFocus(object sender, RoutedEventArgs e)
        {
            InputBox.SelectAll();
        }

        private void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.Enter:
                    e.Handled = true;
                    Close(InputBox.Text);
                    break;
                case Windows.System.VirtualKey.Escape:
                    e.Handled = true;
                    Close(null);
                    break;
            }
        }

        private void OnOkClicked(object sender, RoutedEventArgs e)
        {
            Close(InputBox.Text);
        }

        private void OnCancelClicked(object sender, RoutedEventArgs e)
        {
            Close(null);
        }

        private void OnOverlayTapped(object sender, TappedRoutedEventArgs e)
        {
            Close(null);
        }

        private void Close(string result)
        {
            Overlay.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Collapsed;
            _tcs?.TrySetResult(result);
        }
    }
}
