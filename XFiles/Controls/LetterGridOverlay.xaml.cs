using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace XFiles.Controls
{
    public sealed partial class LetterGridOverlay : UserControl
    {
        private TaskCompletionSource<char?> _tcs;

        public bool IsOpen => Visibility == Visibility.Visible;

        public event Action<char> LetterSelected;
        public event Action Closed;

        public LetterGridOverlay()
        {
            this.InitializeComponent();
        }

        public Task<char?> ShowAsync()
        {
            _tcs = new TaskCompletionSource<char?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var letters = new List<char>();
            for (char c = 'A'; c <= 'Z'; c++)
                letters.Add(c);

            LetterGrid.ItemsSource = letters;
            LetterGrid.SelectedIndex = -1;

            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;
            LetterGrid.Focus(FocusState.Programmatic);

            return _tcs.Task;
        }

        public void HandleDPad(VirtualKey key)
        {
            if (!IsOpen) return;

            int cols = 7;
            int total = LetterGrid.Items.Count;

            switch (key)
            {
                case VirtualKey.GamepadDPadLeft:
                case VirtualKey.Left:
                    if (LetterGrid.SelectedIndex > 0)
                        LetterGrid.SelectedIndex--;
                    break;
                case VirtualKey.GamepadDPadRight:
                case VirtualKey.Right:
                    if (LetterGrid.SelectedIndex < total - 1)
                        LetterGrid.SelectedIndex++;
                    break;
                case VirtualKey.GamepadDPadUp:
                case VirtualKey.Up:
                    if (LetterGrid.SelectedIndex - cols >= 0)
                        LetterGrid.SelectedIndex -= cols;
                    break;
                case VirtualKey.GamepadDPadDown:
                case VirtualKey.Down:
                    if (LetterGrid.SelectedIndex + cols < total)
                        LetterGrid.SelectedIndex += cols;
                    break;
                case VirtualKey.GamepadA:
                case VirtualKey.Enter:
                    if (LetterGrid.SelectedItem is char letter)
                        Close(letter);
                    break;
                case VirtualKey.GamepadB:
                case VirtualKey.Escape:
                    Close(null);
                    break;
            }
        }

        private void Close(char? result)
        {
            Overlay.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Collapsed;
            _tcs?.TrySetResult(result);
            if (result.HasValue)
                LetterSelected?.Invoke(result.Value);
            Closed?.Invoke();
        }

        private void OnOverlayTapped(object sender, TappedRoutedEventArgs e)
        {
            Close(null);
        }
    }
}
