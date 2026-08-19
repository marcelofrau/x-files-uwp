using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using XFiles.Audio;
using XFiles.FileSystem;
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
        public List<SettingsMenuItem> Children { get; set; }
    }

    public sealed partial class SettingsPage : UserControl
    {
        private TaskCompletionSource<bool> _tcs;
        private bool _cacheWasCleared;
        public Action OnClosed;

        /// <summary>Action of the submenu currently shown, or null for the top level.</summary>
        private string _activeSubmenuAction;

        private static readonly SettingsMenuItem BackItem = new SettingsMenuItem
        {
            Label = "Back",
            Description = "Return to the previous level",
            IconPath = "ms-appx:///Assets/Views/SettingsPage/settingspage-back-48.png",
            Action = "back"
        };

        private static readonly string IconBase = "ms-appx:///Assets/Views/StartMenu/";
        private static readonly string[] LogLevels = { "Verbose", "Debug", "Info", "Warning", "Error" };

        private static async Task<List<SettingsMenuItem>> BuildMenuItemsAsync()
        {
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

            string logLevel = Log.GetCurrentLevel();
            string portalDesc = await GetPortalDescAsync();
            bool bgmOn = await XFilesSettings.GetBgmEnabledAsync();
            string bgmFile = await XFilesSettings.GetBgmFileNameAsync();
            string bgmSource = await XFilesSettings.GetBgmSourceNameAsync();
            string bgmName = string.IsNullOrEmpty(bgmSource) ? bgmFile : bgmSource;
            int bgmVol = await XFilesSettings.GetBgmVolumeAsync();
            bool hideDrives = await XFilesSettings.GetHideEmptyDrivesAsync();

            int logFileCount = 0;
            try
            {
                string logsDir = Log.GetLogsDirectory();
                if (System.IO.Directory.Exists(logsDir))
                {
                    logFileCount = System.IO.Directory.GetFiles(logsDir, "xfiles-*.log").Length
                                 + System.IO.Directory.GetFiles(logsDir, "xfiles-*.log.gz").Length;
                }
            }
            catch { }

            return new List<SettingsMenuItem>
            {
                new SettingsMenuItem
                {
                    Label = "Clear Data",
                    Description = "Cache, portal credentials, and log files",
                    IconPath = IconBase + "startmenu-close-48.png",
                    Action = "menu-clear-data",
                    Children = new List<SettingsMenuItem>
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
                            Label = "Clear Portal Credentials",
                            Description = portalDesc,
                            IconPath = "ms-appx:///Assets/Views/SettingsPage/settingspage-clear-credentials-48.png",
                            Action = "clear-portal-creds"
                        },
                        new SettingsMenuItem
                        {
                            Label = "Clear Logs",
                            Description = logFileCount > 0
                                ? $"Delete {logFileCount} archived log file(s)"
                                : "No archived log files",
                            IconPath = IconBase + "startmenu-close-48.png",
                            Action = "clear-logs"
                        }
                    }
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
                    Label = "Background Music",
                    Description = bgmOn ? $"On: {bgmName}" : "Off",
                    IconPath = "ms-appx:///Assets/Views/SettingsPage/settingspage-bgm-48.png",
                    Action = "menu-bgm",
                    Children = new List<SettingsMenuItem>
                    {
                        new SettingsMenuItem
                        {
                            Label = "Enable Background Music",
                            Description = bgmOn ? "On" : "Off",
                            IconPath = "ms-appx:///Assets/Views/SettingsPage/settingspage-bgm-48.png",
                            Action = "bgm-toggle"
                        },
                        new SettingsMenuItem
                        {
                            Label = "Choose Music File",
                            Description = bgmOn ? $"Track: {bgmName}" : "Choose a music file for background playback",
                            IconPath = "ms-appx:///Assets/Views/SettingsPage/settingspage-bgm-pick-48.png",
                            Action = "bgm-pick"
                        },
                        new SettingsMenuItem
                        {
                            Label = "BGM Volume",
                            Description = $"Current: {bgmVol}%",
                            IconPath = "ms-appx:///Assets/Views/SettingsPage/settingspage-volume-48.png",
                            Action = "bgm-volume"
                        },
                        new SettingsMenuItem
                        {
                            Label = "Media Volume",
                            Description = $"Current: {await XFilesSettings.GetMediaVolumeAsync()}%",
                            IconPath = "ms-appx:///Assets/Views/SettingsPage/settingspage-volume-48.png",
                            Action = "media-volume"
                        }
                    }
                },
                new SettingsMenuItem
                {
                    Label = "Hide Empty Drives",
                    Description = hideDrives
                        ? "On: inaccessible or empty drives are hidden from the drive list"
                        : "Off: all drives are shown",
                    IconPath = "ms-appx:///Assets/Views/SettingsPage/settingspage-hide-drives-48.png",
                    Action = "hide-drives"
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
            _activeSubmenuAction = null;
            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;

            await RenderAsync();
            return await _tcs.Task;
        }

        /// <summary>
        /// Rebuilds the menu tree and binds the visible list (top level, or the
        /// active submenu with a leading Back row). Keeps the item whose Action
        /// matches <paramref name="reselectAction"/> selected when provided.
        /// </summary>
        private async Task RenderAsync(string reselectAction = null)
        {
            var top = await BuildMenuItemsAsync();
            List<SettingsMenuItem> view;
            if (_activeSubmenuAction != null)
            {
                var parent = top.FirstOrDefault(i => i.Action == _activeSubmenuAction);
                view = new List<SettingsMenuItem> { BackItem };
                if (parent?.Children != null)
                    view.AddRange(parent.Children);
            }
            else
            {
                view = top;
            }

            SettingsList.ItemsSource = view;

            int idx = -1;
            if (reselectAction != null)
                idx = view.FindIndex(i => i.Action == reselectAction);
            if (idx < 0)
                idx = _activeSubmenuAction != null ? 1 : 0;
            if (idx >= view.Count)
                idx = 0;

            SettingsList.SelectedIndex = idx;
            SettingsList.ScrollIntoView(SettingsList.SelectedItem);
            SettingsList.Focus(FocusState.Programmatic);
        }

        private async void EnterSubmenu(SettingsMenuItem parent)
        {
            _activeSubmenuAction = parent.Action;
            await RenderAsync();
        }

        private async void GoBack()
        {
            if (_activeSubmenuAction == null)
            {
                Close();
                return;
            }
            string parentAction = _activeSubmenuAction;
            _activeSubmenuAction = null;
            await RenderAsync(parentAction);
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

            // Block all input while a chiptune render/copy is in flight.
            if (BgmLoadingOverlay.Visibility == Visibility.Visible)
                return;

            // File picker owns all input while open.
            if (BgmPickerControl.IsOpen)
            {
                BgmPickerControl.HandleDPad(key);
                if (key == VirtualKey.GamepadA || key == VirtualKey.Enter
                    || key == VirtualKey.GamepadB || key == VirtualKey.Escape)
                    BgmPickerControl.HandleButton(key);
                return;
            }

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
                    SettingsList.ScrollIntoView(SettingsList.SelectedItem);
                    break;
                case VirtualKey.Down:
                    if (SettingsList.SelectedIndex < SettingsList.Items.Count - 1)
                        SettingsList.SelectedIndex++;
                    else if (SettingsList.Items.Count > 0)
                        SettingsList.SelectedIndex = 0;
                    SettingsList.ScrollIntoView(SettingsList.SelectedItem);
                    break;
                case VirtualKey.GamepadA:
                case VirtualKey.Enter:
                    if (SettingsList.SelectedItem is SettingsMenuItem item)
                    {
                        if (item.Action == "back")
                            GoBack();
                        else if (item.Children != null)
                            EnterSubmenu(item);
                        else
                            ExecuteAction(item);
                    }
                    break;
                case VirtualKey.GamepadB:
                case VirtualKey.Escape:
                    if (_activeSubmenuAction != null)
                        GoBack();
                    else
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
                    }
                    catch (Exception ex)
                    {
                        CacheStatsText.Text = "Failed to clear cache";
                        Log.Warn("SettingsPage: clear cache failed", ex);
                    }
                }

                Overlay.Visibility = Visibility.Visible;
                await RenderAsync(item.Action);
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
                Controls.MillerColumnsPage.UpdateFtpTraceFilter();
                Log.Info("SettingsPage: log level changed to {Level}", newLevel);

                await RenderAsync(item.Action);
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
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("SettingsPage: clear portal credentials failed", ex);
                    }
                }

                Overlay.Visibility = Visibility.Visible;
                await RenderAsync(item.Action);
            }
            else if (item.Action == "clear-logs")
            {
                Overlay.Visibility = Visibility.Collapsed;
                bool confirmed = await AlertDialogControl.ShowConfirmAsync(
                    "Delete all archived session log files?");

                if (confirmed)
                {
                    try
                    {
                        Log.ClearAllLogs();
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("SettingsPage: clear logs failed", ex);
                    }
                }

                Overlay.Visibility = Visibility.Visible;
                await RenderAsync(item.Action);
            }
            else if (item.Action == "bgm-toggle")
            {
                bool enabled = await XFilesSettings.GetBgmEnabledAsync();
                var bgm = BackgroundMusicService.Instance;
                await bgm.SetEnabledAsync(!enabled);
                await RenderAsync(item.Action);
            }
            else if (item.Action == "bgm-pick")
            {
                string picked = await BgmPickerControl.ShowAsync(null, PickerMode.File, MusicFormatClassifier.MusicExtensions);
                if (!string.IsNullOrEmpty(picked))
                {
                    BgmLoadingOverlay.Visibility = Visibility.Visible;
                    BgmLoadingRing.IsActive = true;
                    bool ok = await BackgroundMusicService.Instance.SetTrackAsync(picked);
                    BgmLoadingRing.IsActive = false;
                    BgmLoadingOverlay.Visibility = Visibility.Collapsed;
                    if (!ok)
                    {
                        Log.Warn("SettingsPage: BGM pick failed for '{Path}'", picked);
                        await AlertDialogControl.ShowConfirmAsync("Could not load the selected music file.");
                    }
                    else
                    {
                        Log.Info("SettingsPage: BGM track set to '{Path}'", picked);
                    }
                }
                await RenderAsync(item.Action);
            }
            else if (item.Action == "bgm-volume")
            {
                int current = await XFilesSettings.GetBgmVolumeAsync();
                int next = MusicFormatClassifier.NextVolumeLevel(current);
                await BackgroundMusicService.Instance.SetVolumeAsync(
                    MusicFormatClassifier.PercentToGain(next));
                await RenderAsync(item.Action);
            }
            else if (item.Action == "media-volume")
            {
                int current = await XFilesSettings.GetMediaVolumeAsync();
                int next = MusicFormatClassifier.NextVolumeLevel(current);
                // SetVolume persists to settings + applies to active AudioGraph
                Audio.AudioLevelService.Instance?.SetVolume(next / 100.0);
                await RenderAsync(item.Action);
            }
            else if (item.Action == "hide-drives")
            {
                bool current = await XFilesSettings.GetHideEmptyDrivesAsync();
                await XFilesSettings.SetHideEmptyDrivesAsync(!current);
                Log.Info("SettingsPage: hide empty drives set to {Value}", !current);
                await RenderAsync(item.Action);
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
