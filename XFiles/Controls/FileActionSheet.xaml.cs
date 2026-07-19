using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using XFiles.FileSystem;

namespace XFiles.Controls
{
    public enum FileAction
    {
        Copy,
        Move,
        Rename,
        Delete,
        Extract
    }

    public class ActionItem
    {
        public FileAction Action { get; set; }
        public string Label { get; set; }
        public string Icon { get; set; }
        public SolidColorBrush IconBrush { get; set; }
    }

    public sealed partial class FileActionSheet : UserControl
    {
        private TaskCompletionSource<FileAction?> _tcs;

        public FileActionSheet()
        {
            this.InitializeComponent();
        }

        public Task<FileAction?> ShowAsync(FileEntry entry)
        {
            _tcs = new TaskCompletionSource<FileAction?>();

            var actions = new List<ActionItem>();
            string brush = "#33AA55";

            var accent = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xE8, 0x85, 0x1A));
            var orange = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xF3, 0x9C, 0x12));
            var red = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xE7, 0x4C, 0x3C));

            if (entry.IsArchive)
            {
                actions.Add(new ActionItem
                {
                    Action = FileAction.Extract,
                    Label = "Extract",
                    Icon = "\uE8F5",  // Extract icon placeholder
                    IconBrush = accent
                });
            }

            if (!entry.IsDirectory)
            {
                actions.Add(new ActionItem
                {
                    Action = FileAction.Copy,
                    Label = "Copy",
                    Icon = "\uE8C8",
                    IconBrush = accent
                });

                actions.Add(new ActionItem
                {
                    Action = FileAction.Move,
                    Label = "Move",
                    Icon = "\uE8B8",
                    IconBrush = accent
                });
            }

            actions.Add(new ActionItem
            {
                Action = FileAction.Rename,
                Label = "Rename",
                Icon = "\uE8D0",
                IconBrush = orange
            });

            actions.Add(new ActionItem
            {
                Action = FileAction.Delete,
                Label = "Delete",
                Icon = "\uE74D",
                IconBrush = red
            });

            ActionList.ItemsSource = actions;
            FileNameText.Text = entry.Name;

            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;

            ActionList.SelectedIndex = 0;
            ActionList.Focus(FocusState.Programmatic);

            return _tcs.Task;
        }

        private void OnActionSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ActionList.SelectedIndex < 0) return;

            var item = ActionList.SelectedItem as ActionItem;
            if (item != null)
            {
                Close(item.Action);
            }
        }

        private void OnActionListKeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.Escape:
                case Windows.System.VirtualKey.GamepadB:
                    e.Handled = true;
                    Close(null);
                    break;
            }
        }

        private void OnOverlayTapped(object sender, TappedRoutedEventArgs e)
        {
            Close(null);
        }

        private void Close(FileAction? result)
        {
            Overlay.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Collapsed;
            _tcs?.TrySetResult(result);
        }
    }
}
