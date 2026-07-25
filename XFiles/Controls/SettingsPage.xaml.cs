using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using XFiles.Metadata;
using XFiles.Settings;

namespace XFiles.Controls
{
    public class SettingsMenuItem
    {
        public string Label { get; set; }
        public string Description { get; set; }
        public string IconPath { get; set; }
        public string Action { get; set; }
    }

    public sealed partial class SettingsPage : UserControl
    {
        private TaskCompletionSource<bool> _tcs;
        private bool _cacheWasCleared;
        public Action OnClosed;

        private static readonly string IconBase = "ms-appx:///Assets/Views/StartMenu/";
        private static readonly string[] LogLevels = { "Verbose", "Debug", "Info", "Warning", "Error" };

        public SettingsPage()
        {
            this.InitializeComponent();
        }

        public async Task<bool> ShowAsync()
        {
            _tcs = new TaskCompletionSource<bool>();
            _cacheWasCleared = false;
            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;

            int cacheCount = 0;
            try
            {
                var cache = new MetadataCache();
                cacheCount = await cache.GetEntryCountAsync();
            }
            catch (Exception ex)
            {
                Log.Warning("SettingsPage: failed to read cache count", ex);
            }

            string currentLevel = await XFilesSettings.GetLogLevelAsync();

            CacheStatsText.Text = $"{cacheCount} cached entries";

            var items = new List<SettingsMenuItem>
            {
                new SettingsMenuItem
                {
                    Label = "Clear Cache",
                    Description = $"Remove all {cacheCount} cached metadata and cover art entries",
                    IconPath = IconBase + "startmenu-close-48.png",
                    Action = "clear-cache"
                },
                new SettingsMenuItem
                {
                    Label = "Log Level",
                    Description = $"Current: {currentLevel}",
                    IconPath = IconBase + "startmenu-settings-48.png",
                    Action = "log-level"
                }
            };

            SettingsList.ItemsSource = items;
            SettingsList.SelectedIndex = 0;
            SettingsList.Focus(FocusState.Programmatic);

            return await _tcs.Task;
        }

        public void HandleDPad(VirtualKey key)
        {
            if (!IsVisible) return;
            if (AlertDialogControl.IsDialogVisible)
            {
                AlertDialogControl.HandleButton(key);
                return;
            }
            switch (key)
            {
                case VirtualKey.Up:
                    if (SettingsList.SelectedIndex > 0)
                        SettingsList.SelectedIndex--;
                    break;
                case VirtualKey.Down:
                    if (SettingsList.SelectedIndex < SettingsList.Items.Count - 1)
                        SettingsList.SelectedIndex++;
                    break;
                case VirtualKey.GamepadA:
                case VirtualKey.Enter:
                    if (SettingsList.SelectedItem is SettingsMenuItem item)
                        ExecuteAction(item);
                    break;
                case VirtualKey.GamepadB:
                case VirtualKey.Escape:
                    Close();
                    break;
            }
        }

        private async void ExecuteAction(SettingsMenuItem item)
        {
            if (item.Action == "clear-cache")
            {
                Overlay.Visibility = Visibility.Collapsed;
                bool confirmed = await AlertDialogControl.ShowConfirmAsync(
                    $"Clear all cached metadata and cover art?");

                if (confirmed)
                {
                    try
                    {
                        var cache = new MetadataCache();
                        int cleared = await cache.ClearAsync();
                        CacheStatsText.Text = $"Cleared {cleared} entries";
                        Log.Information("SettingsPage: cleared {Count} cache entries", cleared);
                        _cacheWasCleared = true;

                        var items = new List<SettingsMenuItem>
                        {
                            new SettingsMenuItem
                            {
                                Label = "Clear Cache",
                                Description = "Remove all 0 cached metadata and cover art entries",
                                IconPath = "ms-appx:///Assets/Views/FileActionSheet/fileactionsheet-delete-48.png",
                                Action = "clear-cache"
                            },
                            new SettingsMenuItem
                            {
                                Label = "Log Level",
                                Description = $"Current: {Log.GetCurrentLevel()}",
                                IconPath = IconBase + "startmenu-settings-48.png",
                                Action = "log-level"
                            }
                        };
                        SettingsList.ItemsSource = items;
                    }
                    catch (Exception ex)
                    {
                        CacheStatsText.Text = "Failed to clear cache";
                        Log.Warning("SettingsPage: clear cache failed", ex);
                    }
                }

                Overlay.Visibility = Visibility.Visible;
                SettingsList.Focus(FocusState.Programmatic);
            }
            else if (item.Action == "log-level")
            {
                string current = await XFilesSettings.GetLogLevelAsync();
                int idx = Array.IndexOf(LogLevels, current);
                if (idx < 0) idx = 2; // default to Info
                int next = (idx + 1) % LogLevels.Length;
                string newLevel = LogLevels[next];

                await XFilesSettings.SetLogLevelAsync(newLevel);
                Log.SetLogLevel(newLevel);
                Log.Information("SettingsPage: log level changed to {Level}", newLevel);

                // Refresh the item description
                var items = new List<SettingsMenuItem>
                {
                    new SettingsMenuItem
                    {
                        Label = "Clear Cache",
                        Description = "Remove cached metadata and cover art entries",
                        IconPath = IconBase + "startmenu-close-48.png",
                        Action = "clear-cache"
                    },
                    new SettingsMenuItem
                    {
                        Label = "Log Level",
                        Description = $"Current: {newLevel}",
                        IconPath = IconBase + "startmenu-settings-48.png",
                        Action = "log-level"
                    }
                };
                SettingsList.ItemsSource = items;
                SettingsList.SelectedIndex = 1;
                SettingsList.Focus(FocusState.Programmatic);
            }
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSelectionColors();
        }

        private void UpdateSelectionColors()
        {
            var gray = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x99, 0x99, 0x99));
            var dark = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x1A, 0x1D, 0x23));

            for (int i = 0; i < SettingsList.Items.Count; i++)
            {
                var container = SettingsList.ContainerFromIndex(i) as ListViewItem;
                if (container != null)
                    container.Foreground = container.IsSelected ? dark : gray;
            }
        }

        private void OnContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.ItemContainer is ListViewItem container)
                container.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x99, 0x99, 0x99));
        }

        private void OnOverlayTapped(object sender, TappedRoutedEventArgs e)
        {
            Close();
        }

        public bool IsVisible => Visibility == Visibility.Visible;

        private void Close()
        {
            Overlay.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Collapsed;
            _tcs?.TrySetResult(_cacheWasCleared);
            OnClosed?.Invoke();
        }
    }
}
