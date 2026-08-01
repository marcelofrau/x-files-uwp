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
        About,
        ViewLogs,
        CloseApplication,
        Search,
        JumpToLetter,
        SearchFiles
    }

    public class MenuItem
    {
        public StartMenuItem Item { get; set; }
        public string Label { get; set; }
        public string IconPath { get; set; }
        public bool IsSeparator { get; set; }
    }

    public class StartMenuSeparatorSelector : DataTemplateSelector
    {
        public DataTemplate DefaultTemplate { get; set; }
        public DataTemplate SeparatorTemplate { get; set; }

        protected override DataTemplate SelectTemplateCore(object item)
        {
            if (item is MenuItem mi && mi.IsSeparator)
                return SeparatorTemplate;
            return DefaultTemplate;
        }
    }

    public sealed partial class StartMenu : UserControl
    {
        private TaskCompletionSource<StartMenuItem?> _tcs;
        private List<MenuItem> _mainItems;
        private bool _inSubMenu;
        public Action OnClosed;

        public bool IsOpen => Visibility == Visibility.Visible;

        public StartMenu()
        {
            this.InitializeComponent();
        }

        private static readonly string IconBase = "ms-appx:///Assets/Views/StartMenu/";

        public Task<StartMenuItem?> ShowAsync()
        {
            _tcs = new TaskCompletionSource<StartMenuItem?>(TaskCreationOptions.RunContinuationsAsynchronously);

            _mainItems = new List<MenuItem>
            {
                new MenuItem { Item = StartMenuItem.Search, Label = "Search", IconPath = IconBase + "startmenu-search-48.png" },
                new MenuItem { IsSeparator = true },
                new MenuItem { Item = StartMenuItem.Settings, Label = "Settings", IconPath = IconBase + "startmenu-settings-48.png" },
                new MenuItem { Item = StartMenuItem.About, Label = "About", IconPath = IconBase + "startmenu-about-48.png" },
                new MenuItem { Item = StartMenuItem.ViewLogs, Label = "View Logs", IconPath = IconBase + "startmenu-logs-48.png" },
                new MenuItem { Item = StartMenuItem.CloseApplication, Label = "Close Application", IconPath = IconBase + "startmenu-close-48.png" }
            };

            ShowMenu(_mainItems, "X-Files");
            return _tcs.Task;
        }

        private void ShowMenu(List<MenuItem> items, string title)
        {
            MenuTitleText.Text = title;
            MenuList.ItemsSource = items;
            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;
            MenuList.SelectedIndex = FindNextNonSeparator(-1, 1);
            MenuList.Focus(FocusState.Programmatic);
        }

        private void OnMenuContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.ItemContainer is ListViewItem container)
            {
                if (args.Item is MenuItem mi && mi.IsSeparator)
                {
                    container.IsEnabled = false;
                    container.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x33, 0x36, 0x3F));
                }
                else
                {
                    container.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x99, 0x99, 0x99));
                }
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
                if (MenuList.Items[i] is MenuItem mi && mi.IsSeparator) continue;
                var container = MenuList.ContainerFromIndex(i) as ListViewItem;
                if (container != null)
                {
                    container.Foreground = container.IsSelected ? dark : gray;
                }
            }
        }

        private int FindNextNonSeparator(int fromIndex, int step)
        {
            int count = MenuList.Items.Count;
            for (int i = fromIndex + step; i >= 0 && i < count; i += step)
            {
                if (MenuList.Items[i] is MenuItem mi && !mi.IsSeparator)
                    return i;
            }
            return -1;
        }

        public void ForwardDPad(VirtualKey key)
        {
            if (!IsOpen) return;
            switch (key)
            {
                case VirtualKey.Up:
                    {
                        int next = FindNextNonSeparator(MenuList.SelectedIndex, -1);
                        if (next >= 0)
                            MenuList.SelectedIndex = next;
                        break;
                    }
                case VirtualKey.Down:
                    {
                        int next = FindNextNonSeparator(MenuList.SelectedIndex, 1);
                        if (next >= 0)
                            MenuList.SelectedIndex = next;
                        break;
                    }
                case VirtualKey.GamepadA:
                case VirtualKey.Enter:
                    if (MenuList.SelectedItem is MenuItem item)
                        HandleSelection(item);
                    break;
                case VirtualKey.GamepadB:
                case VirtualKey.Escape:
                    if (_inSubMenu)
                        ShowMenu(_mainItems, "X-Files");
                    else
                        Close(null);
                    _inSubMenu = false;
                    break;
            }
        }

        private void HandleSelection(MenuItem item)
        {
            if (item.Item == StartMenuItem.Search)
            {
                _inSubMenu = true;
                var subItems = new List<MenuItem>
                {
                    new MenuItem { Item = StartMenuItem.JumpToLetter, Label = "Jump to Letter", IconPath = IconBase + "startmenu-search-48.png" },
                    new MenuItem { Item = StartMenuItem.SearchFiles, Label = "Search Files", IconPath = IconBase + "startmenu-search-48.png" }
                };
                ShowMenu(subItems, "Search");
            }
            else
            {
                Close(item.Item);
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
            _inSubMenu = false;
            _tcs?.TrySetResult(result);
            OnClosed?.Invoke();
        }
    }
}
