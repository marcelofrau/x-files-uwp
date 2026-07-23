using System;
using System.Threading;
using System.Threading.Tasks;

namespace XFiles.Metadata
{
    public class MetadataGuesser
    {
        private readonly DeezerProvider _deezer;
        private readonly MusicBrainzProvider _musicBrainz;
        private readonly MetadataCache _cache;
        private bool _internetAvailable;

        public MetadataGuesser()
        {
            _deezer = new DeezerProvider();
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

            if (local.HasAlbumArt)
            {
                string coverKey = MetadataCache.BuildCoverArtKey(local.Artist, local.Album);
                if (!string.IsNullOrEmpty(coverKey))
                {
                    await _cache.StoreCoverArtAsync(local.Artist, local.Album, local.AlbumArt, local.AlbumArtMime);
                    Log.Information("MetadataGuesser: stored embedded cover art for album key '{Key}'", coverKey);
                }
            }

            if (!string.IsNullOrWhiteSpace(local.Artist) || !string.IsNullOrWhiteSpace(local.Title))
            {
                string cacheKey = MetadataCache.BuildCacheKey(local.Artist, local.Title, local.Album);
                Log.Information("MetadataGuesser: cache key='{Key}' artist='{Artist}' title='{Title}' album='{Album}'",
                    cacheKey, local.Artist, local.Title, local.Album);
                var cached = await _cache.GetAsync(cacheKey);
                if (cached != null)
                {
                    Log.Information("MetadataGuesser: cache hit — title={Title} artist={Artist} album={Album} art={HasArt}",
                        cached.Metadata.Title, cached.Metadata.Artist, cached.Metadata.Album, cached.Metadata.HasAlbumArt);
                    cached.Metadata.MergeFrom(local);
                    Log.Information("MetadataGuesser: after cache merge — title={Title} artist={Artist} album={Album} art={HasArt}",
                        cached.Metadata.Title, cached.Metadata.Artist, cached.Metadata.Album, cached.Metadata.HasAlbumArt);
                    return cached;
                }

                if (!local.HasAlbumArt && _internetAvailable)
                {
                    string coverKey = MetadataCache.BuildCoverArtKey(local.Artist, local.Album);
                    var existingCover = await _cache.GetCoverArtAsync(local.Artist, local.Album);
                    if (existingCover != null)
                    {
                        local.AlbumArt = existingCover;
                        Log.Information("MetadataGuesser: reusing cached cover art for album key '{Key}'", coverKey);
                    }
                }
            }

            if (_internetAvailable)
            {
                var online = await _deezer.SearchAsync(
                    local.Artist, local.Title, local.Album, ct);

                if (online != null)
                {
                    Log.Information("MetadataGuesser: Deezer result — usable={Usable} score={Score:F2} title={Title} artist={Artist} album={Album} coverUrl={HasUrl}",
                        online.IsUsable, online.Confidence, online.Metadata.Title, online.Metadata.Artist, online.Metadata.Album, !string.IsNullOrEmpty(online.CoverArtUrl));
                }
                else
                {
                    Log.Information("MetadataGuesser: Deezer returned null, trying MusicBrainz fallback");
                }

                if (online == null || !online.IsUsable)
                {
                    Log.Information("MetadataGuesser: trying MusicBrainz fallback");
                    online = await _musicBrainz.SearchRecordingAsync(
                        local.Artist, local.Title, local.Album, ct);

                    if (online != null)
                    {
                        Log.Information("MetadataGuesser: MusicBrainz result — usable={Usable} score={Score:F2} title={Title} artist={Artist} album={Album}",
                            online.IsUsable, online.Confidence, online.Metadata.Title, online.Metadata.Artist, online.Metadata.Album);

                        if (online.IsUsable && !online.Metadata.HasAlbumArt && !string.IsNullOrEmpty(online.ReleaseMbid))
                        {
                            Log.Information("MetadataGuesser: fetching cover art for release {Release}", online.ReleaseMbid);
                            var coverArt = await _musicBrainz.FetchCoverArtAsync(online.ReleaseMbid, ct);
                            if (coverArt != null && coverArt.Length > 0)
                            {
                                online.Metadata.AlbumArt = coverArt;
                                online.Metadata.AlbumArtMime = "image/jpeg";
                                online.CoverArtBytes = coverArt;
                                Log.Information("MetadataGuesser: cover art loaded ({Size} bytes)", coverArt.Length);
                            }
                        }
                    }
                }

                if (online != null && online.IsUsable)
                {
                    online.Metadata.MergeFrom(local);

                    Log.Information("MetadataGuesser: after online merge — title={Title} artist={Artist} album={Album} art={HasArt}",
                        online.Metadata.Title, online.Metadata.Artist, online.Metadata.Album, online.Metadata.HasAlbumArt);

                    bool shouldFetchArt = !online.Metadata.HasAlbumArt && !string.IsNullOrEmpty(online.CoverArtUrl);
                    Log.Information("MetadataGuesser: cover art check — hasArt={HasArt} coverUrl='{Url}' shouldFetch={ShouldFetch}",
                        online.Metadata.HasAlbumArt, online.CoverArtUrl ?? "(null)", shouldFetchArt);

                    if (shouldFetchArt)
                    {
                        string cachedCoverUrl = await _cache.GetCoverArtUrlAsync(online.Metadata.Artist, online.Metadata.Album);
                        if (!string.IsNullOrEmpty(cachedCoverUrl) && cachedCoverUrl == online.CoverArtUrl)
                        {
                            var existingArt = await _cache.GetCoverArtAsync(online.Metadata.Artist, online.Metadata.Album);
                            if (existingArt != null && existingArt.Length > 0)
                            {
                                online.Metadata.AlbumArt = existingArt;
                                online.Metadata.AlbumArtMime = "image/jpeg";
                                online.CoverArtBytes = existingArt;
                                Log.Information("MetadataGuesser: reused cached cover art ({Size} bytes)", existingArt.Length);
                                shouldFetchArt = false;
                            }
                        }
                    }

                    if (shouldFetchArt)
                    {
                        Log.Information("MetadataGuesser: fetching cover art from Deezer URL {Url}", online.CoverArtUrl);
                        var coverArt = await _deezer.FetchCoverArtAsync(online.CoverArtUrl, ct);
                        if (coverArt != null && coverArt.Length > 0)
                        {
                            online.Metadata.AlbumArt = coverArt;
                            online.Metadata.AlbumArtMime = "image/jpeg";
                            online.CoverArtBytes = coverArt;
                            Log.Information("MetadataGuesser: cover art loaded ({Size} bytes)", coverArt.Length);

                            await _cache.StoreCoverArtAsync(online.Metadata.Artist, online.Metadata.Album, coverArt, "image/jpeg");
                        }
                        else
                        {
                            Log.Information("MetadataGuesser: no cover art available");
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(local.Artist) || !string.IsNullOrWhiteSpace(local.Title))
                    {
                        string cacheKey = MetadataCache.BuildCacheKey(local.Artist, local.Title, local.Album);
                        Log.Information("MetadataGuesser: caching result key='{Key}' art={HasArt}", cacheKey, online.Metadata.HasAlbumArt);
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
