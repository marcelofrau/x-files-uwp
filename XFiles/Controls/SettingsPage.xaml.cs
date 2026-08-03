using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using XFiles.Metadata;
using XFiles.Services;
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

        private static List<SettingsMenuItem> BuildMenuItems(string cacheDesc, string logLevel, string portalDesc)
        {
            return new List<SettingsMenuItem>
            {
                new SettingsMenuItem
                {
                    Label = "Clear Cache",
                    Description = cacheDesc,
                    IconPath = IconBase + "startmenu-close-48.png",
                    Action = "clear-cache"
                },
                new SettingsMenuItem
                {
                    Label = "Log Level",
                    Description = $"Current: {logLevel}",
                    IconPath = IconBase + "startmenu-settings-48.png",
                    Action = "log-level"
                },
                new SettingsMenuItem
                {
                    Label = "Clear Portal Credentials",
                    Description = portalDesc,
                    IconPath = "ms-appx:///Assets/Views/SettingsPage/settingspage-clear-credentials-48.png",
                    Action = "clear-portal-creds"
                }
            };
        }

        public SettingsPage()
        {
            this.InitializeComponent();
        }

        public async Task<bool> ShowAsync()
        {
            _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
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
                Log.Warn("SettingsPage: failed to read cache count", ex);
            }

            string currentLevel = await XFilesSettings.GetLogLevelAsync();

            string portalDesc = await GetPortalDescAsync();

            CacheStatsText.Text = $"{cacheCount} cached entries";

            SettingsList.ItemsSource = BuildMenuItems(
                $"Remove all {cacheCount} cached metadata and cover art entries", currentLevel, portalDesc);
            SettingsList.SelectedIndex = 0;
            SettingsList.Focus(FocusState.Programmatic);

            return await _tcs.Task;
        }

        private static async Task<string> GetPortalDescAsync()
        {
            string portalUser = "";
            try
            {
                portalUser = await XFilesSettings.GetPortalUserAsync();
            }
            catch (Exception ex)
            {
                Log.Warn("SettingsPage: failed to read portal user", ex);
            }
            return string.IsNullOrEmpty(portalUser)
                ? "No portal credentials stored"
                : $"Portal user: {portalUser}";
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
                    else if (SettingsList.Items.Count > 0)
                        SettingsList.SelectedIndex = SettingsList.Items.Count - 1;
                    break;
                case VirtualKey.Down:
                    if (SettingsList.SelectedIndex < SettingsList.Items.Count - 1)
                        SettingsList.SelectedIndex++;
                    else if (SettingsList.Items.Count > 0)
                        SettingsList.SelectedIndex = 0;
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
                        Log.Info("SettingsPage: cleared {Count} cache entries", cleared);
                        _cacheWasCleared = true;

                        SettingsList.ItemsSource = BuildMenuItems(
                            "Remove all 0 cached metadata and cover art entries", Log.GetCurrentLevel(),
                            await GetPortalDescAsync());
                        SettingsList.SelectedIndex = 0;
                    }
                    catch (Exception ex)
                    {
                        CacheStatsText.Text = "Failed to clear cache";
                        Log.Warn("SettingsPage: clear cache failed", ex);
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
                Log.Info("SettingsPage: log level changed to {Level}", newLevel);

                // Refresh the item description
                SettingsList.ItemsSource = BuildMenuItems(
                    "Remove cached metadata and cover art entries", newLevel,
                    await GetPortalDescAsync());
                SettingsList.SelectedIndex = 1;
                SettingsList.Focus(FocusState.Programmatic);
            }
            else if (item.Action == "clear-portal-creds")
            {
                Overlay.Visibility = Visibility.Collapsed;
                bool confirmed = await AlertDialogControl.ShowConfirmAsync(
                    "Clear stored portal credentials and reset portal connection state?");

                if (confirmed)
                {
                    try
                    {
                        DevicePortalService.ClearPortalCredentials();
                        await XFilesSettings.SetPortalCredentialsAsync("", "");
                        Log.Info("SettingsPage: portal credentials cleared");

                        SettingsList.ItemsSource = BuildMenuItems(
                            "Remove cached metadata and cover art entries", Log.GetCurrentLevel(),
                            "No portal credentials stored");
                        SettingsList.SelectedIndex = 2;
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("SettingsPage: clear portal credentials failed", ex);
                        SettingsList.ItemsSource = BuildMenuItems(
                            "Remove cached metadata and cover art entries", Log.GetCurrentLevel(),
                            await GetPortalDescAsync());
                        SettingsList.SelectedIndex = 2;
                    }
                }

                Overlay.Visibility = Visibility.Visible;
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
