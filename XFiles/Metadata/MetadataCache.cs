using System;
using System.IO;
using System.Threading.Tasks;
using SQLite;
using Windows.Storage;

namespace XFiles.Metadata
{
    public class MetadataCache
    {
        private const string DbFileName = "metadata.db";
        private const long MaxCacheAgeMs = 90L * 24 * 60 * 60 * 1000;
        private const long MaxCoverArtAgeMs = 180L * 24 * 60 * 60 * 1000;
        private const int CurrentSchemaVersion = 1;

        private static SQLiteAsyncConnection _db;
        private static readonly object _initLock = new object();

        private static async Task<SQLiteAsyncConnection> GetDbAsync()
        {
            if (_db != null) return _db;

            lock (_initLock)
            {
                if (_db != null) return _db;

                string dbPath = Path.Combine(
                    ApplicationData.Current.LocalFolder.Path, DbFileName);
                _db = new SQLiteAsyncConnection(dbPath);
            }

            // Schema version tracking
            await _db.CreateTableAsync<SchemaVersionEntry>();
            var versionEntry = await _db.Table<SchemaVersionEntry>()
                .Where(v => v.Id == 1).FirstOrDefaultAsync();
            int currentVersion = versionEntry?.Version ?? 0;

            if (currentVersion < CurrentSchemaVersion)
            {
                Log.Info("MetadataCache: migrating schema v{Old} → v{New}", currentVersion, CurrentSchemaVersion);
                await RunMigrationsAsync(_db, currentVersion);
            }

            await _db.CreateTableAsync<MetadataCacheEntry>();
            await _db.CreateTableAsync<CoverArtEntry>();
            await _db.CreateTableAsync<AppSettingEntry>();
            Log.Info("MetadataCache: database opened at {Path} schema v{Version}", DbFileName, CurrentSchemaVersion);
            return _db;
        }

        private static async Task RunMigrationsAsync(SQLiteAsyncConnection db, int fromVersion)
        {
            if (fromVersion < 1)
            {
                // v1: SchemaVersion + AppSettings tables (created by CreateTableAsync above)
            }
            // Future migrations:
            // if (fromVersion < 2) { ... }

            // Update version
            var existing = await db.Table<SchemaVersionEntry>()
                .Where(v => v.Id == 1).FirstOrDefaultAsync();
            if (existing != null)
            {
                existing.Version = CurrentSchemaVersion;
                await db.UpdateAsync(existing);
            }
            else
            {
                await db.InsertAsync(new SchemaVersionEntry { Id = 1, Version = CurrentSchemaVersion });
            }
        }

        public static async Task<string> GetSettingAsync(string key, string defaultValue = null)
        {
            try
            {
                var db = await GetDbAsync();
                var entry = await db.Table<AppSettingEntry>()
                    .Where(s => s.Key == key).FirstOrDefaultAsync();
                return entry?.Value ?? defaultValue;
            }
            catch (Exception ex)
            {
                Log.Warn("MetadataCache: GetSettingAsync failed key='{Key}'", ex, key);
                return defaultValue;
            }
        }

        public static async Task SetSettingAsync(string key, string value)
        {
            try
            {
                var db = await GetDbAsync();
                var existing = await db.Table<AppSettingEntry>()
                    .Where(s => s.Key == key).FirstOrDefaultAsync();
                if (existing != null)
                {
                    existing.Value = value;
                    await db.UpdateAsync(existing);
                }
                else
                {
                    await db.InsertAsync(new AppSettingEntry { Key = key, Value = value });
                }
            }
            catch (Exception ex)
            {
                Log.Warn("MetadataCache: SetSettingAsync failed key='{Key}'", ex, key);
            }
        }

        public async Task<MetadataMatch> GetAsync(string cacheKey)
        {
            try
            {
                var db = await GetDbAsync();
                var entry = await db.Table<MetadataCacheEntry>()
                    .Where(e => e.CacheKey == cacheKey)
                    .FirstOrDefaultAsync();

                if (entry == null)
                {
                    Log.Dbg("MetadataCache: miss key='{Key}'", cacheKey);
                    return null;
                }

                long ageMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - entry.Timestamp;
                if (ageMs > MaxCacheAgeMs)
                {
                    Log.Dbg("MetadataCache: expired key='{Key}' age={Age}d", cacheKey, ageMs / 86400000);
                    await db.DeleteAsync(entry);
                    return null;
                }

                byte[] coverArt = null;
                string coverMime = null;

                if (!string.IsNullOrEmpty(entry.CoverArtAlbumKey))
                {
                    var coverEntry = await db.Table<CoverArtEntry>()
                        .Where(c => c.AlbumKey == entry.CoverArtAlbumKey)
                        .FirstOrDefaultAsync();

                    if (coverEntry != null)
                    {
                        long coverAgeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - coverEntry.Timestamp;
                        if (coverAgeMs <= MaxCoverArtAgeMs)
                        {
                            coverArt = coverEntry.ArtData;
                            coverMime = coverEntry.Mime;
                        }
                    }
                }

                var meta = new TrackMetadata
                {
                    Title = entry.Title,
                    Artist = entry.Artist,
                    Album = entry.Album,
                    Genre = entry.Genre,
                    Year = entry.Year,
                    TrackNumber = entry.TrackNumber,
                    DurationSeconds = entry.DurationSeconds,
                    AlbumArt = coverArt,
                    AlbumArtMime = coverMime
                };

                float confidence = entry.Confidence;
                string mbid = entry.MusicBrainzId;

                Log.Dbg("MetadataCache: hit key='{Key}' title='{Title}' art={HasArt}",
                    cacheKey, meta.Title, meta.HasAlbumArt);
                return MetadataMatch.FromMusicBrainz(meta, confidence, mbid);
            }
            catch (Exception ex)
            {
                Log.Warn("MetadataCache: GetAsync failed key='{Key}'", ex, cacheKey);
                return null;
            }
        }

        public async Task SetAsync(string cacheKey, MetadataMatch match)
        {
            if (match == null || match.Metadata == null) return;

            try
            {
                var db = await GetDbAsync();

                string albumKey = BuildCoverArtKey(match.Metadata.Artist, match.Metadata.Album);

                if (match.CoverArtBytes != null && match.CoverArtBytes.Length > 0 && !string.IsNullOrEmpty(albumKey))
                {
                    await UpsertCoverArtAsync(db, albumKey, match.CoverArtBytes, match.Metadata.AlbumArtMime ?? "image/jpeg");
                    Log.Dbg("MetadataCache: stored cover art key='{Key}' size={Size}", albumKey, match.CoverArtBytes.Length);
                }

                if (!string.IsNullOrEmpty(match.CoverArtUrl) && !string.IsNullOrEmpty(albumKey))
                {
                    var existing = await db.Table<CoverArtEntry>()
                        .Where(c => c.AlbumKey == albumKey)
                        .FirstOrDefaultAsync();
                    if (existing == null)
                    {
                        await db.InsertAsync(new CoverArtEntry
                        {
                            AlbumKey = albumKey,
                            CoverUrl = match.CoverArtUrl,
                            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        });
                        Log.Dbg("MetadataCache: stored cover URL key='{Key}' url={Url}", albumKey, match.CoverArtUrl);
                    }
                }

                var entryExisting = await db.Table<MetadataCacheEntry>()
                    .Where(e => e.CacheKey == cacheKey)
                    .FirstOrDefaultAsync();

                var entry = entryExisting ?? new MetadataCacheEntry { CacheKey = cacheKey };

                entry.Artist = match.Metadata.Artist;
                entry.Title = match.Metadata.Title;
                entry.Album = match.Metadata.Album;
                entry.Genre = match.Metadata.Genre;
                entry.Year = match.Metadata.Year;
                entry.TrackNumber = match.Metadata.TrackNumber;
                entry.DurationSeconds = match.Metadata.DurationSeconds;
                entry.MusicBrainzId = match.MusicBrainzId;
                entry.ReleaseMbid = match.ReleaseMbid;
                entry.Confidence = match.Confidence;
                entry.Source = match.Source.ToString();
                entry.CoverArtAlbumKey = albumKey;
                entry.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                if (entryExisting != null)
                    await db.UpdateAsync(entry);
                else
                    await db.InsertAsync(entry);

                Log.Dbg("MetadataCache: stored key='{Key}' title='{Title}' coverKey='{CoverKey}'",
                    cacheKey, match.Metadata.Title, albumKey);
            }
            catch (Exception ex)
            {
                Log.Warn("MetadataCache: SetAsync failed key='{Key}'", ex, cacheKey);
            }
        }

        public async Task<byte[]> GetCoverArtAsync(string artist, string album)
        {
            try
            {
                string key = BuildCoverArtKey(artist, album);
                if (string.IsNullOrEmpty(key)) return null;

                var db = await GetDbAsync();
                var entry = await db.Table<CoverArtEntry>()
                    .Where(c => c.AlbumKey == key)
                    .FirstOrDefaultAsync();

                if (entry == null) return null;

                long ageMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - entry.Timestamp;
                if (ageMs > MaxCoverArtAgeMs)
                {
                    await db.DeleteAsync(entry);
                    return null;
                }

                return entry.ArtData;
            }
            catch (Exception ex)
            {
                Log.Warn("MetadataCache: GetCoverArtAsync failed", ex);
                return null;
            }
        }

        public async Task StoreCoverArtAsync(string artist, string album, byte[] artData, string mime = "image/jpeg")
        {
            try
            {
                string key = BuildCoverArtKey(artist, album);
                if (string.IsNullOrEmpty(key) || artData == null || artData.Length == 0) return;

                var db = await GetDbAsync();
                await UpsertCoverArtAsync(db, key, artData, mime);
                Log.Dbg("MetadataCache: stored cover art album='{Key}' size={Size}", key, artData.Length);
            }
            catch (Exception ex)
            {
                Log.Warn("MetadataCache: StoreCoverArtAsync failed", ex);
            }
        }

        public async Task<string> GetCoverArtUrlAsync(string artist, string album)
        {
            try
            {
                string key = BuildCoverArtKey(artist, album);
                if (string.IsNullOrEmpty(key)) return null;

                var db = await GetDbAsync();
                var entry = await db.Table<CoverArtEntry>()
                    .Where(c => c.AlbumKey == key)
                    .FirstOrDefaultAsync();

                return entry?.CoverUrl;
            }
            catch
            {
                return null;
            }
        }

        private async Task UpsertCoverArtAsync(SQLiteAsyncConnection db, string albumKey, byte[] artData, string mime)
        {
            var existing = await db.Table<CoverArtEntry>()
                .Where(c => c.AlbumKey == albumKey)
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                existing.ArtData = artData;
                existing.Mime = mime;
                existing.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await db.UpdateAsync(existing);
            }
            else
            {
                await db.InsertAsync(new CoverArtEntry
                {
                    AlbumKey = albumKey,
                    ArtData = artData,
                    Mime = mime,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
            }
        }

        public async Task<int> ClearAsync()
        {
            try
            {
                var db = await GetDbAsync();
                int count = await db.Table<MetadataCacheEntry>().CountAsync();
                int coverCount = await db.Table<CoverArtEntry>().CountAsync();
                await db.DeleteAllAsync<MetadataCacheEntry>();
                await db.DeleteAllAsync<CoverArtEntry>();
                Log.Info("MetadataCache: cleared {Count} entries + {CoverCount} cover arts", count, coverCount);
                return count;
            }
            catch (Exception ex)
            {
                Log.Warn("MetadataCache: ClearAsync failed", ex);
                return 0;
            }
        }

        public async Task<int> GetEntryCountAsync()
        {
            try
            {
                var db = await GetDbAsync();
                return await db.Table<MetadataCacheEntry>().CountAsync();
            }
            catch
            {
                return 0;
            }
        }

        public static string BuildCacheKey(string artist, string title, string album)
        {
            return $"{(artist ?? "").ToLowerInvariant()}|{(title ?? "").ToLowerInvariant()}|{(album ?? "").ToLowerInvariant()}";
        }

        public static string BuildCoverArtKey(string artist, string album)
        {
            if (string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(album))
                return null;
            return $"{(artist ?? "").ToLowerInvariant()}|{(album ?? "").ToLowerInvariant()}";
        }
    }
}
