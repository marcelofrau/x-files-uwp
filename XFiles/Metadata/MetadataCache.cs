using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;

namespace XFiles.Metadata
{
    public class MetadataCache
    {
        private const string CacheFolderName = "metadata-cache";
        private const int MaxCacheAgeDays = 90;

        private StorageFolder _cacheFolder;

        private async Task<StorageFolder> GetCacheFolderAsync()
        {
            if (_cacheFolder != null) return _cacheFolder;

            try
            {
                var localFolder = ApplicationData.Current.LocalFolder;
                _cacheFolder = await localFolder.CreateFolderAsync(
                    CacheFolderName, CreationCollisionOption.OpenIfExists);
            }
            catch (Exception ex)
            {
                Log.Warning("MetadataCache: failed to create cache folder: {Error}", ex.Message);
            }

            return _cacheFolder;
        }

        public async Task<MetadataMatch> GetAsync(string cacheKey)
        {
            var folder = await GetCacheFolderAsync();
            if (folder == null) return null;

            string hash = HashKey(cacheKey);
            try
            {
                string fileName = hash + ".json";
                var file = await folder.GetFileAsync(fileName);
                string json = await FileIO.ReadTextAsync(file);
                var obj = JsonObject.Parse(json);

                long timestamp = (long)obj.GetNamedNumber("timestamp", 0);
                long ageMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - timestamp;
                if (ageMs > MaxCacheAgeDays * 24 * 60 * 60 * 1000L)
                {
                    Log.Verbose("MetadataCache: expired entry for key={Key}", cacheKey);
                    return null;
                }

                var meta = new TrackMetadata
                {
                    Title = obj.GetNamedString("title", null),
                    Artist = obj.GetNamedString("artist", null),
                    Album = obj.GetNamedString("album", null),
                    Genre = obj.GetNamedString("genre", null),
                    Year = obj.GetNamedString("year", null),
                    TrackNumber = obj.GetNamedString("trackNumber", null),
                    DurationSeconds = (int)obj.GetNamedNumber("duration", 0)
                };

                string coverFile = hash + ".bin";
                try
                {
                    var coverEntry = await folder.GetFileAsync(coverFile);
                    var coverBytes = await FileIO.ReadBufferAsync(coverEntry);
                    if (coverBytes.Length > 0)
                    {
                        meta.AlbumArt = new byte[coverBytes.Length];
                        System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions
                            .CopyTo(coverBytes, 0, meta.AlbumArt, 0, (int)coverBytes.Length);
                        meta.AlbumArtMime = "image/jpeg";
                        Log.Verbose("MetadataCache: loaded cover art ({Size} bytes) for {Key}",
                            meta.AlbumArt.Length, cacheKey);
                    }
                }
                catch { }

                string mbid = obj.GetNamedString("musicbrainzId", null);
                float confidence = (float)obj.GetNamedNumber("confidence", 1.0);

                Log.Information("MetadataCache: hit for key={Key} title={Title} art={HasArt}",
                    cacheKey, meta.Title, meta.HasAlbumArt);
                return MetadataMatch.FromMusicBrainz(meta, confidence, mbid);
            }
            catch (Exception ex)
            {
                Log.Verbose("MetadataCache: miss for key={Key}: {Error}", cacheKey, ex.Message);
                return null;
            }
        }

        public async Task SetAsync(string cacheKey, MetadataMatch match)
        {
            if (match == null || match.Metadata == null) return;

            var folder = await GetCacheFolderAsync();
            if (folder == null) return;

            string hash = HashKey(cacheKey);
            try
            {
                var obj = new JsonObject();
                obj.Add("title", JsonValue.CreateStringValue(match.Metadata.Title ?? ""));
                obj.Add("artist", JsonValue.CreateStringValue(match.Metadata.Artist ?? ""));
                obj.Add("album", JsonValue.CreateStringValue(match.Metadata.Album ?? ""));
                obj.Add("genre", JsonValue.CreateStringValue(match.Metadata.Genre ?? ""));
                obj.Add("year", JsonValue.CreateStringValue(match.Metadata.Year ?? ""));
                obj.Add("trackNumber", JsonValue.CreateStringValue(match.Metadata.TrackNumber ?? ""));
                obj.Add("duration", JsonValue.CreateNumberValue(match.Metadata.DurationSeconds));
                obj.Add("confidence", JsonValue.CreateNumberValue(match.Confidence));
                obj.Add("musicbrainzId", JsonValue.CreateStringValue(match.MusicBrainzId ?? ""));
                obj.Add("timestamp", JsonValue.CreateNumberValue(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

                string fileName = hash + ".json";
                var file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, obj.Stringify());

                if (match.Metadata.HasAlbumArt)
                {
                    string coverFile = hash + ".bin";
                    var coverEntry = await folder.CreateFileAsync(coverFile, CreationCollisionOption.ReplaceExisting);
                    await FileIO.WriteBytesAsync(coverEntry, match.Metadata.AlbumArt);
                    Log.Verbose("MetadataCache: stored cover art ({Size} bytes) for {Key}",
                        match.Metadata.AlbumArt.Length, cacheKey);
                }

                Log.Verbose("MetadataCache: stored key={Key} title={Title}", cacheKey, match.Metadata.Title);
            }
            catch (Exception ex)
            {
                Log.Warning("MetadataCache: failed to store: {Error}", ex.Message);
            }
        }

        private static string HashKey(string key)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(key);
                byte[] hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").Substring(0, 16);
            }
        }

        public static string BuildCacheKey(string artist, string title, string album)
        {
            string combined = $"{(artist ?? "").ToLowerInvariant()}|{(title ?? "").ToLowerInvariant()}|{(album ?? "").ToLowerInvariant()}";
            return combined;
        }
    }
}
