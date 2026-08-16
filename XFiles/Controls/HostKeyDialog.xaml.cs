using System;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace XFiles.Controls
{
    /// <summary>
    /// First-connect SFTP host-key confirmation. Shows the host:port and the
    /// SHA256 fingerprint; A = trust (persist), B = reject. Called from the
    /// SFTP connect path (SftpBrowser.HostKeyConfirmation), so it returns a
    /// Task&lt;bool&gt; that the browser bridges into its synchronous resolver.
    /// </summary>
    public sealed partial class HostKeyDialog : UserControl
    {
        private TaskCompletionSource<bool> _tcs;

        /// <summary>Called when the dialog closes, for the page to clear its overlay state.</summary>
        public Action OnClosed;

        public HostKeyDialog()
        {
            this.InitializeComponent();
        }

        /// <summary>Shows the confirmation for an untrusted host key.</summary>
        public Task<bool> ShowAsync(string hostPort, string fingerprint)
        {
            Log.Info("HostKeyDialog.ShowAsync: host={Host} fingerprint={Fp}",
                hostPort, fingerprint);

            HostPortText.Text = hostPort;
            FingerprintText.Text = fingerprint;

            _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Canvas.SetZIndex(this, 400);
            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;
            return _tcs.Task;
        }

        private void OnTrustClicked(object sender, RoutedEventArgs e)
        {
            Close(true);
        }

        private void OnRejectClicked(object sender, RoutedEventArgs e)
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
            }
        }

        private void OnOverlayTapped(object sender, TappedRoutedEventArgs e)
        {
            Close(false);
        }

        private void Close(bool result)
        {
            Log.Dbg("HostKeyDialog.Close: result={Result}", result);
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
