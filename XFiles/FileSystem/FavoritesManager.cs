using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using XFiles.Settings;

namespace XFiles.FileSystem
{
    public class FavoriteEntry
    {
        public string Path { get; set; }
        public string Name { get; set; }
        public bool IsDirectory { get; set; }
    }

    public static class FavoritesManager
    {
        private static List<FavoriteEntry> _cache;
        private static readonly object _lock = new object();

        private const string SettingsKey = "Favorites";

        public static async Task<List<FavoriteEntry>> GetAllAsync()
        {
            if (_cache != null) return _cache;
            await LoadAsync();
            return _cache;
        }

        public static bool IsFavorite(string path)
        {
            if (_cache == null) return false;
            lock (_lock)
            {
                return _cache.Any(f => f.Path == path);
            }
        }

        public static async Task<bool> IsFavoriteAsync(string path)
        {
            var list = await GetAllAsync();
            return list.Any(f => f.Path == path);
        }

        public static async Task AddAsync(string path, string name, bool isDirectory)
        {
            var list = await GetAllAsync();
            if (list.Any(f => f.Path == path)) return;

            list.Add(new FavoriteEntry
            {
                Path = path,
                Name = name,
                IsDirectory = isDirectory
            });
            await SaveAsync(list);
        }

        public static async Task RemoveAsync(string path)
        {
            var list = await GetAllAsync();
            int removed = list.RemoveAll(f => f.Path == path);
            if (removed > 0)
                await SaveAsync(list);
        }

        private static async Task LoadAsync()
        {
            string json = await XFilesSettings.GetStringAsync(SettingsKey, "[]");
            lock (_lock)
            {
                _cache = ParseJson(json);
            }
        }

        private static async Task SaveAsync(List<FavoriteEntry> list)
        {
            lock (_lock)
            {
                _cache = list;
            }
            string json = ToJson(list);
            await XFilesSettings.SetStringAsync(SettingsKey, json);
        }

        private static List<FavoriteEntry> ParseJson(string json)
        {
            var result = new List<FavoriteEntry>();
            if (!JsonArray.TryParse(json, out var arr))
                return result;

            foreach (var item in arr)
            {
                var obj = item.GetObject();
                if (obj == null) continue;
                result.Add(new FavoriteEntry
                {
                    Path = obj.GetNamedString("path", ""),
                    Name = obj.GetNamedString("name", ""),
                    IsDirectory = obj.GetNamedBoolean("isDir", false)
                });
            }
            return result;
        }

        private static string ToJson(List<FavoriteEntry> list)
        {
            var arr = new JsonArray();
            foreach (var f in list)
            {
                var obj = new JsonObject
                {
                    ["path"] = JsonValue.CreateStringValue(f.Path ?? ""),
                    ["name"] = JsonValue.CreateStringValue(f.Name ?? ""),
                    ["isDir"] = JsonValue.CreateBooleanValue(f.IsDirectory)
                };
                arr.Add(obj);
            }
            return arr.Stringify();
        }
    }
}
