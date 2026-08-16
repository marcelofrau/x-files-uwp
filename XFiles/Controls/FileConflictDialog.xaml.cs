using System;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using XFiles.FileSystem;

namespace XFiles.Controls
{
    /// <summary>
    /// Name-collision dialog for copy/move: REPLACE ALL / RENAME ALL / CANCEL.
    /// </summary>
    public sealed partial class FileConflictDialog : UserControl
    {
        private TaskCompletionSource<ConflictDecision> _tcs;

        public FileConflictDialog()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Show the collision dialog for the given destination path.
        /// Returns ReplaceAll, RenameAll, or Cancel.
        /// </summary>
        public Task<ConflictDecision> ShowAsync(string path)
        {
            MessageText.Text = $"A file with this name already exists:\n{path}\n\nReplace it, keep both, or cancel?";
            _tcs = new TaskCompletionSource<ConflictDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;
            return _tcs.Task;
        }

        private void OnReplaceAllClicked(object sender, RoutedEventArgs e)
        {
            Close(ConflictDecision.ReplaceAll);
        }

        private void OnRenameAllClicked(object sender, RoutedEventArgs e)
        {
            Close(ConflictDecision.RenameAll);
        }

        private void OnCancelClicked(object sender, RoutedEventArgs e)
        {
            Close(ConflictDecision.Cancel);
        }

        private void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case VirtualKey.GamepadA:
                case VirtualKey.Enter:
                    e.Handled = true;
                    Close(ConflictDecision.ReplaceAll);
                    break;
                case VirtualKey.GamepadY:
                    e.Handled = true;
                    Close(ConflictDecision.RenameAll);
                    break;
                case VirtualKey.GamepadB:
                case VirtualKey.Escape:
                    e.Handled = true;
                    Close(ConflictDecision.Cancel);
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
            Close(ConflictDecision.Cancel);
        }

        private void Close(ConflictDecision result)
        {
            Log.Dbg("FileConflictDialog.Close: result={Result}", result);
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
                    Close(ConflictDecision.ReplaceAll);
                    break;
                case VirtualKey.GamepadY:
                    Close(ConflictDecision.RenameAll);
                    break;
                case VirtualKey.GamepadB:
                case VirtualKey.Escape:
                    Close(ConflictDecision.Cancel);
                    break;
            }
        }
    }
}
