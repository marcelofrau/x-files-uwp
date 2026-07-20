using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace XFiles.Controls
{
    public enum StartMenuItem
    {
        Settings,
        StartupPreferences,
        About,
        CloseApplication
    }

    public class MenuItem
    {
        public StartMenuItem Item { get; set; }
        public string Label { get; set; }
        public string IconPath { get; set; }
    }

    public sealed partial class StartMenu : UserControl
    {
        private TaskCompletionSource<StartMenuItem?> _tcs;

        public bool IsOpen => Visibility == Visibility.Visible;

        public StartMenu()
        {
            this.InitializeComponent();
        }

        private static readonly string IconBase = "ms-appx:///Assets/Views/StartMenu/";

        public Task<StartMenuItem?> ShowAsync()
        {
            _tcs = new TaskCompletionSource<StartMenuItem?>();

            var items = new List<MenuItem>
            {
                new MenuItem
                {
                    Item = StartMenuItem.Settings,
                    Label = "Settings",
                    IconPath = IconBase + "startmenu-settings-48.png"
                },
                new MenuItem
                {
                    Item = StartMenuItem.StartupPreferences,
                    Label = "Startup and Preferences",
                    IconPath = IconBase + "startmenu-startup-48.png"
                },
                new MenuItem
                {
                    Item = StartMenuItem.About,
                    Label = "About",
                    IconPath = IconBase + "startmenu-about-48.png"
                },
                new MenuItem
                {
                    Item = StartMenuItem.CloseApplication,
                    Label = "Close Application",
                    IconPath = IconBase + "startmenu-close-48.png"
                }
            };

            MenuList.ItemsSource = items;
            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;

            MenuList.SelectedIndex = 0;
            MenuList.Focus(FocusState.Programmatic);

            return _tcs.Task;
        }

        private void OnMenuContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.ItemContainer is ListViewItem container)
            {
                container.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x99, 0x99, 0x99));
            }
        }

        private void OnMenuSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateMenuSelectionColors();
        }

        private void UpdateMenuSelectionColors()
        {
            var gray = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x99, 0x99, 0x99));
            var dark = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x1A, 0x1D, 0x23));

            for (int i = 0; i < MenuList.Items.Count; i++)
            {
                var container = MenuList.ContainerFromIndex(i) as ListViewItem;
                if (container != null)
                {
                    container.Foreground = container.IsSelected ? dark : gray;
                }
            }
        }

        public void ForwardDPad(VirtualKey key)
        {
            if (!IsOpen) return;
            switch (key)
            {
                case VirtualKey.Up:
                    if (MenuList.SelectedIndex > 0)
                        MenuList.SelectedIndex--;
                    break;
                case VirtualKey.Down:
                    if (MenuList.SelectedIndex < MenuList.Items.Count - 1)
                        MenuList.SelectedIndex++;
                    break;
                case VirtualKey.GamepadA:
                case VirtualKey.Enter:
                    if (MenuList.SelectedItem is MenuItem item)
                        Close(item.Item);
                    break;
                case VirtualKey.GamepadB:
                case VirtualKey.Escape:
                    Close(null);
                    break;
            }
        }

        private void OnOverlayTapped(object sender, TappedRoutedEventArgs e)
        {
            Close(null);
        }

        private void Close(StartMenuItem? result)
        {
            Overlay.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Collapsed;
            _tcs?.TrySetResult(result);
        }
    }
}
