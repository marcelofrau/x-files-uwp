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
        public Action OnClosed;

        public InputDialog()
        {
            this.InitializeComponent();
        }

        public Task<string> ShowAsync(string title, string defaultValue)
        {
            Log.Information("InputDialog.ShowAsync: title=\"{Title}\" default=\"{Default}\"", title, defaultValue);
            TitleText.Text = title;
            InputBox.Text = defaultValue ?? "";
            _tcs = new TaskCompletionSource<string>();
            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;

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
                case Windows.System.VirtualKey.GamepadMenu:
                    e.Handled = true;
                    Log.Information("InputDialog: key {Key} → confirm", e.Key);
                    Close(InputBox.Text);
                    break;
                case Windows.System.VirtualKey.Escape:
                case Windows.System.VirtualKey.GamepadB:
                    e.Handled = true;
                    Log.Information("InputDialog: key {Key} → cancel", e.Key);
                    Close(null);
                    break;
            }
        }

        private void OnOverlayKeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.GamepadA:
                case Windows.System.VirtualKey.Enter:
                    e.Handled = true;
                    Log.Information("InputDialog: overlay key {Key} → confirm", e.Key);
                    Close(InputBox.Text);
                    break;
                case Windows.System.VirtualKey.GamepadB:
                case Windows.System.VirtualKey.Escape:
                    e.Handled = true;
                    Log.Information("InputDialog: overlay key {Key} → cancel", e.Key);
                    Close(null);
                    break;
                case Windows.System.VirtualKey.GamepadDPadUp:
                case Windows.System.VirtualKey.GamepadDPadDown:
                case Windows.System.VirtualKey.GamepadDPadLeft:
                case Windows.System.VirtualKey.GamepadDPadRight:
                case Windows.System.VirtualKey.Up:
                case Windows.System.VirtualKey.Down:
                case Windows.System.VirtualKey.Left:
                case Windows.System.VirtualKey.Right:
                    e.Handled = true;
                    break;
            }
        }

        private void OnOkClicked(object sender, RoutedEventArgs e)
        {
            Log.Information("InputDialog: OK button → confirm");
            Close(InputBox.Text);
        }

        private void OnOverlayTapped(object sender, TappedRoutedEventArgs e)
        {
            Log.Information("InputDialog: overlay tapped → cancel");
            Close(null);
        }

        private void Close(string result)
        {
            Log.Information("InputDialog.Close: result={Result}", result == null ? "null" : $"\"{result}\"");
            Overlay.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Collapsed;
            _tcs?.TrySetResult(result);
            OnClosed?.Invoke();
        }

        public void HandleButton(Windows.System.VirtualKey key)
        {
            Log.Information("InputDialog.HandleButton: key={Key}", key);
            switch (key)
            {
                case Windows.System.VirtualKey.GamepadA:
                case Windows.System.VirtualKey.Enter:
                    Close(InputBox.Text);
                    break;
                case Windows.System.VirtualKey.GamepadB:
                case Windows.System.VirtualKey.Escape:
                    Close(null);
                    break;
            }
        }
    }
}
