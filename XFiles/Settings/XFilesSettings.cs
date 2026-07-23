using Windows.Storage;

namespace XFiles.Settings
{
    public static class XFilesSettings
    {
        private static ApplicationDataContainer LocalSettings => ApplicationData.Current.LocalSettings;

        public static bool FirstRunShown
        {
            get => GetBool("FirstRunShown", false);
            set => SetBool("FirstRunShown", value);
        }

        public static int GetInt(string key, int defaultValue = 0)
        {
            if (LocalSettings.Values.ContainsKey(key) && LocalSettings.Values[key] is int val)
                return val;
            return defaultValue;
        }

        public static void SetInt(string key, int value)
        {
            LocalSettings.Values[key] = value;
        }

        public static bool GetBool(string key, bool defaultValue = false)
        {
            if (LocalSettings.Values.ContainsKey(key) && LocalSettings.Values[key] is bool val)
                return val;
            return defaultValue;
        }

        public static void SetBool(string key, bool value)
        {
            LocalSettings.Values[key] = value;
        }

        public static string GetString(string key, string defaultValue = "")
        {
            if (LocalSettings.Values.ContainsKey(key) && LocalSettings.Values[key] is string val)
                return val;
            return defaultValue;
        }

        public static void SetString(string key, string value)
        {
            LocalSettings.Values[key] = value;
        }
    }
}
