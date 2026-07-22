using System;
using System.Threading;
using System.Threading.Tasks;

namespace XFiles.Metadata
{
    public class MetadataGuesser
    {
        private readonly MusicBrainzProvider _musicBrainz;
        private readonly MetadataCache _cache;
        private bool _internetAvailable;

        public MetadataGuesser()
        {
            _musicBrainz = new MusicBrainzProvider();
            _cache = new MetadataCache();
        }

        public void SetInternetAvailable(bool available)
        {
            _internetAvailable = available;
        }

        public async Task<MetadataMatch> ResolveAsync(string filePath, CancellationToken ct = default)
        {
            Log.Information("MetadataGuesser: resolving {Path}", filePath);

            var id3Tag = await Task.Run(() => FileSystem.Id3Tag.ReadFromFile(filePath), ct);
            var local = TrackMetadata.FromId3Tag(id3Tag, filePath);

            Log.Information("MetadataGuesser: local ID3 title={Title} artist={Artist} album={Album} genre={Genre} year={Year} track={Track} art={HasArt}",
                local.Title, local.Artist, local.Album, local.Genre, local.Year, local.TrackNumber, local.HasAlbumArt);

            var filenameMeta = FilenameParser.ExtractFromPath(filePath);
            Log.Information("MetadataGuesser: filename parsed title={Title} artist={Artist} album={Album} track={Track}",
                filenameMeta.Title, filenameMeta.Artist, filenameMeta.Album, filenameMeta.TrackNumber);

            local.MergeFrom(filenameMeta);

            Log.Information("MetadataGuesser: after merge title={Title} artist={Artist} album={Album}",
                local.Title, local.Artist, local.Album);

            if (!string.IsNullOrWhiteSpace(local.Artist) || !string.IsNullOrWhiteSpace(local.Title))
            {
                string cacheKey = MetadataCache.BuildCacheKey(local.Artist, local.Title, local.Album);
                var cached = await _cache.GetAsync(cacheKey);
                if (cached != null)
                {
                    Log.Information("MetadataGuesser: cache hit — online title={Title} artist={Artist} album={Album} art={HasArt}",
                        cached.Metadata.Title, cached.Metadata.Artist, cached.Metadata.Album, cached.Metadata.HasAlbumArt);
                    cached.Metadata.MergeFrom(local);
                    Log.Information("MetadataGuesser: after cache merge — title={Title} artist={Artist} album={Album} art={HasArt}",
                        cached.Metadata.Title, cached.Metadata.Artist, cached.Metadata.Album, cached.Metadata.HasAlbumArt);
                    return cached;
                }
            }

            if (_internetAvailable)
            {
                var online = await _musicBrainz.SearchRecordingAsync(
                    local.Artist, local.Title, local.Album, ct);

                if (online != null)
                {
                    Log.Information("MetadataGuesser: online result — usable={Usable} score={Score:F2} title={Title} artist={Artist} album={Album} release={Release} art={HasArt}",
                        online.IsUsable, online.Confidence, online.Metadata.Title, online.Metadata.Artist, online.Metadata.Album, online.ReleaseMbid, online.Metadata.HasAlbumArt);
                }
                else
                {
                    Log.Information("MetadataGuesser: online search returned null");
                }

                if (online != null && online.IsUsable)
                {
                    online.Metadata.MergeFrom(local);

                    Log.Information("MetadataGuesser: after online merge — title={Title} artist={Artist} album={Album} art={HasArt}",
                        online.Metadata.Title, online.Metadata.Artist, online.Metadata.Album, online.Metadata.HasAlbumArt);

                    if (!online.Metadata.HasAlbumArt && !string.IsNullOrEmpty(online.ReleaseMbid))
                    {
                        Log.Information("MetadataGuesser: fetching cover art for release {Release}", online.ReleaseMbid);
                        var coverArt = await _musicBrainz.FetchCoverArtAsync(online.ReleaseMbid, ct);
                        if (coverArt != null && coverArt.Length > 0)
                        {
                            online.Metadata.AlbumArt = coverArt;
                            online.Metadata.AlbumArtMime = "image/jpeg";
                            Log.Information("MetadataGuesser: cover art loaded ({Size} bytes)", coverArt.Length);
                        }
                        else
                        {
                            Log.Information("MetadataGuesser: no cover art available for release {Release}", online.ReleaseMbid);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(local.Artist) || !string.IsNullOrWhiteSpace(local.Title))
                    {
                        string cacheKey = MetadataCache.BuildCacheKey(local.Artist, local.Title, local.Album);
                        await _cache.SetAsync(cacheKey, online);
                    }

                    return online;
                }

                Log.Information("MetadataGuesser: no usable online match for {Title}", local.Title);
            }
            else
            {
                Log.Information("MetadataGuesser: internet not available, using local data only");
            }

            return MetadataMatch.FromId3Only(local);
        }
    }
}
