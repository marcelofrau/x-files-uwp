using System.Threading.Tasks;
using XFiles.Metadata;

namespace XFiles.Settings
{
    public static class XFilesSettings
    {
        public static async Task<bool> GetBoolAsync(string key, bool defaultValue = false)
        {
            string val = await MetadataCache.GetSettingAsync(key, null);
            if (val == null) return defaultValue;
            return val == "true";
        }

        public static async Task SetBoolAsync(string key, bool value)
        {
            await MetadataCache.SetSettingAsync(key, value ? "true" : "false");
        }

        public static async Task<int> GetIntAsync(string key, int defaultValue = 0)
        {
            string val = await MetadataCache.GetSettingAsync(key, null);
            if (val == null) return defaultValue;
            return int.TryParse(val, out int result) ? result : defaultValue;
        }

        public static async Task SetIntAsync(string key, int value)
        {
            await MetadataCache.SetSettingAsync(key, value.ToString());
        }

        public static async Task<string> GetStringAsync(string key, string defaultValue = "")
        {
            return await MetadataCache.GetSettingAsync(key, defaultValue);
        }

        public static async Task SetStringAsync(string key, string value)
        {
            await MetadataCache.SetSettingAsync(key, value);
        }

        // Convenience properties
        public static async Task<bool> GetFirstRunShownAsync()
            => await GetBoolAsync("FirstRunShown", false);

        public static async Task SetFirstRunShownAsync(bool value)
            => await SetBoolAsync("FirstRunShown", value);

        public static async Task<string> GetLogLevelAsync()
            => await GetStringAsync("LogLevel", "Info");

        public static async Task SetLogLevelAsync(string level)
            => await SetStringAsync("LogLevel", level);

        // Settings schema version — bumped by App startup migration to avoid
        // re-running upgrade steps (e.g. compressing old logs, resetting defaults).
        private const int CurrentSettingsVersion = 1;

        public static async Task<int> GetSettingsVersionAsync()
            => await GetIntAsync("SettingsVersion", 0);

        public static async Task SetSettingsVersionAsync(int version)
            => await SetIntAsync("SettingsVersion", version);

        public static int GetCurrentSettingsVersion() => CurrentSettingsVersion;

        // Portal credentials (Device Portal). Stored in SQLite like every other
        // setting — the console has no build-time .env.
        public static async Task<string> GetPortalUserAsync()
            => await GetStringAsync("PortalUser", "");

        public static async Task<string> GetPortalPassAsync()
            => await GetStringAsync("PortalPass", "");

        public static async Task SetPortalCredentialsAsync(string user, string pass)
        {
            await SetStringAsync("PortalUser", user ?? "");
            await SetStringAsync("PortalPass", pass ?? "");
        }

        // Background music (BGM)
        public static async Task<bool> GetBgmEnabledAsync()
            => await GetBoolAsync("BgmEnabled", true);

        public static async Task SetBgmEnabledAsync(bool value)
            => await SetBoolAsync("BgmEnabled", value);

        public static async Task<string> GetBgmFileNameAsync()
            => await GetStringAsync("BgmFileName", "");

        public static async Task SetBgmFileNameAsync(string value)
            => await SetStringAsync("BgmFileName", value);

        public static async Task<string> GetBgmSourceNameAsync()
            => await GetStringAsync("BgmSourceName", "");

        public static async Task SetBgmSourceNameAsync(string value)
            => await SetStringAsync("BgmSourceName", value);

        public static async Task<int> GetBgmVolumeAsync()
            => await GetIntAsync("BgmVolume", 50);

        public static async Task SetBgmVolumeAsync(int value)
            => await SetIntAsync("BgmVolume", value);

        public static async Task<int> GetMediaVolumeAsync()
            => await GetIntAsync("MediaVolume", 75);

        public static async Task SetMediaVolumeAsync(int value)
            => await SetIntAsync("MediaVolume", value);

        // Hide empty/inaccessible drives (root/drive scan). Cached statically so
        // the sync DirectoryScanner can read it without touching SQLite; the cache
        // is seeded at startup and kept current by the async getter/setter.
        public static bool HideEmptyDrivesCached = true;

        public static async Task<bool> GetHideEmptyDrivesAsync()
        {
            bool value = await GetBoolAsync("HideEmptyDrives", true);
            HideEmptyDrivesCached = value;
            return value;
        }

        public static async Task SetHideEmptyDrivesAsync(bool value)
        {
            HideEmptyDrivesCached = value;
            await SetBoolAsync("HideEmptyDrives", value);
        }
    }
}
