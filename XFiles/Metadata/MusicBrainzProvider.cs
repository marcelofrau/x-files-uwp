using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;

namespace XFiles.Metadata
{
    public class MusicBrainzProvider
    {
        private static readonly HttpClient _http;
        private static readonly SemaphoreSlim _rateLimiter = new SemaphoreSlim(1, 1);
        private const string BaseUrl = "https://musicbrainz.org/ws/2/";
        private const string CoverArtUrl = "https://coverartarchive.org/release/";
        private const string UserAgent = "XFiles/1.0 (https://github.com/opencode/x-files-uwp)";

        static MusicBrainzProvider()
        {
            _http = new HttpClient();
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        }

        public async Task<MetadataMatch> SearchRecordingAsync(string artist, string title, string album, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(artist))
                return null;

            string cleanTitle = CleanSearchTerm(title);

            var queries = new List<(string query, string qArtist, string qTitle, string qAlbum)>();

            if (!string.IsNullOrWhiteSpace(cleanTitle) && !string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(album))
                queries.Add((BuildRecordingQuery(artist, cleanTitle, album), artist, cleanTitle, album));

            if (!string.IsNullOrWhiteSpace(cleanTitle) && !string.IsNullOrWhiteSpace(artist))
                queries.Add((BuildRecordingQuery(artist, cleanTitle, null), artist, cleanTitle, null));

            if (!string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(album))
                queries.Add((BuildReleaseQuery(artist, album), artist, cleanTitle, album));

            if (queries.Count == 0) return null;

            foreach (var (query, qArtist, qTitle, qAlbum) in queries)
            {
                if (string.IsNullOrWhiteSpace(query)) continue;
                var result = await SearchSingleQueryAsync(query, qArtist, qTitle, qAlbum, ct);
                if (result != null) return result;
            }

            return null;
        }

        private string CleanSearchTerm(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;
            string cleaned = input.Replace("_", " ").Trim();
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s*-\s*\w+\s*$", "");
            return cleaned.Trim();
        }

        private async Task<MetadataMatch> SearchSingleQueryAsync(string query, string qArtist, string qTitle, string qAlbum, CancellationToken ct)
        {
            await _rateLimiter.WaitAsync(ct);
            try
            {
                string url = $"{BaseUrl}recording/?query={Uri.EscapeDataString(query)}&fmt=json&limit=15";
                Log.Information("MusicBrainz: searching URL={Url} query={Query}", url, query);

                var response = await _http.GetAsync(url, ct);
                Log.Information("MusicBrainz: HTTP {Status}", response.StatusCode);

                if (!response.IsSuccessStatusCode) return null;

                string json = await response.Content.ReadAsStringAsync();
                Log.Information("MusicBrainz: response (first 800 chars)={Json}", json.Length > 800 ? json.Substring(0, 800) : json);
                var root = JsonObject.Parse(json);
                var items = root.GetNamedArray("recordings", new JsonArray());
                Log.Information("MusicBrainz: found {Count} recordings for query={Query}", items.Count, query);
                if (items.Count == 0) return null;

                return FindBestMatch(items, qArtist, qTitle, qAlbum);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Warning("MusicBrainz: search failed: {Error}", ex.Message);
                return null;
            }
            finally
            {
                try { await Task.Delay(1100); } catch { }
                _rateLimiter.Release();
            }
        }

        public async Task<byte[]> FetchCoverArtAsync(string releaseMbid, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(releaseMbid))
            {
                Log.Information("CoverArt: no release MBID, skipping");
                return null;
            }

            try
            {
                string metaUrl = $"{CoverArtUrl}{releaseMbid}";
                Log.Information("CoverArt: fetching metadata for {MBID}", releaseMbid);

                var metaResponse = await _http.GetAsync(metaUrl, ct);
                Log.Information("CoverArt: metadata HTTP {Status} for {MBID}", metaResponse.StatusCode, releaseMbid);

                if (!metaResponse.IsSuccessStatusCode)
                    return null;

                string json = await metaResponse.Content.ReadAsStringAsync();
                Log.Information("CoverArt: metadata JSON (first 500) for {MBID}: {Json}", releaseMbid, json.Length > 500 ? json.Substring(0, 500) : json);
                var root = JsonObject.Parse(json);
                var images = root.GetNamedArray("images", new JsonArray());

                if (images.Count == 0)
                {
                    Log.Information("CoverArt: no images for {MBID}", releaseMbid);
                    return null;
                }

                string imageUrl = null;
                for (int i = 0; i < images.Count; i++)
                {
                    try
                    {
                        var imgValue = images[i];
                        if (imgValue == null || imgValue.ValueType != JsonValueType.Object)
                        {
                            Log.Information("CoverArt: image[{Index}] is type {Type}, skipping", i, imgValue?.ValueType);
                            continue;
                        }
                        var img = imgValue.GetObject();
                        bool isFront = SafeGetBoolean(img, "front", false);
                        string url = SafeGetString(img, "image");
                        Log.Information("CoverArt: image[{Index}] front={Front} url={Url}", i, isFront, url ?? "(null)");
                        if (isFront && !string.IsNullOrEmpty(url))
                        {
                            imageUrl = url;
                            break;
                        }
                    }
                    catch (Exception imgEx)
                    {
                        Log.Warning("CoverArt: error parsing image[{Index}]: {Error}", i, imgEx.Message);
                    }
                }

                if (imageUrl == null && images.Count > 0)
                {
                    try
                    {
                        var firstValue = images[0];
                        if (firstValue != null && firstValue.ValueType == JsonValueType.Object)
                        {
                            var firstImg = firstValue.GetObject();
                            imageUrl = SafeGetString(firstImg, "image");
                        }
                    }
                    catch { }
                }

                if (imageUrl == null)
                {
                    Log.Information("CoverArt: no usable image URL for {MBID}", releaseMbid);
                    return null;
                }

                Log.Information("CoverArt: fetching image URL={URL} scheme={Scheme}", imageUrl, imageUrl.StartsWith("https") ? "HTTPS" : "HTTP");

                var imgResponse = await _http.GetAsync(imageUrl, ct);
                Log.Information("CoverArt: image HTTP {Status} for {MBID} finalUrl={FinalUrl}", imgResponse.StatusCode, releaseMbid, imgResponse.RequestMessage?.RequestUri);

                if (!imgResponse.IsSuccessStatusCode)
                    return null;

                byte[] bytes = await imgResponse.Content.ReadAsByteArrayAsync();
                Log.Information("CoverArt: downloaded {Size} bytes for {MBID}", bytes.Length, releaseMbid);
                return bytes;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Warning("CoverArt: exception for {MBID}: {Error}", releaseMbid, ex.Message);
                return null;
            }
        }

        private string BuildRecordingQuery(string artist, string title, string album)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(title))
                parts.Add($"recording:\"{title.Trim()}\"");
            if (!string.IsNullOrWhiteSpace(artist))
                parts.Add($"artist:\"{artist.Trim()}\"");
            if (!string.IsNullOrWhiteSpace(album))
                parts.Add($"release:\"{album.Trim()}\"");
            return string.Join(" AND ", parts);
        }

        private string BuildReleaseQuery(string artist, string album)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(album))
                parts.Add($"release:\"{album.Trim()}\"");
            if (!string.IsNullOrWhiteSpace(artist))
                parts.Add($"artist:\"{artist.Trim()}\"");
            return string.Join(" AND ", parts);
        }

        private MetadataMatch FindBestMatch(JsonArray items, string queryArtist, string queryTitle, string queryAlbum)
        {
            MetadataMatch bestMatch = null;
            float bestScore = 0f;

            Log.Information("MusicBrainz: evaluating {Count} candidates for title='{Title}' artist='{Artist}' album='{Album}'",
                items.Count, queryTitle, queryArtist, queryAlbum);

            for (int i = 0; i < items.Count; i++)
            {
                var obj = items[i].GetObject();
                string mbTitle = obj.GetNamedString("title", "");
                string mbId = obj.GetNamedString("id", "");

                string mbArtist = "";
                var artistCredits = obj.GetNamedArray("artist-credit", new JsonArray());
                if (artistCredits.Count > 0)
                {
                    var firstCredit = artistCredits[0].GetObject();
                    var artistObj = firstCredit.GetNamedObject("artist", new JsonObject());
                    mbArtist = artistObj.GetNamedString("name", "");
                }

                string mbAlbum = "";
                string releaseMbid = "";
                var releases = obj.GetNamedArray("releases", new JsonArray());
                if (releases.Count > 0)
                {
                    var firstRelease = releases[0].GetObject();
                    mbAlbum = firstRelease.GetNamedString("title", "");
                    releaseMbid = firstRelease.GetNamedString("id", "");
                }

                Log.Information("MusicBrainz: [{Index}] title='{MbTitle}' artist='{MbArtist}' album='{MbAlbum}' release={Release}",
                    i, mbTitle, mbArtist, mbAlbum, releaseMbid);

                float score = CalculateMatchScore(
                    queryArtist, queryTitle, queryAlbum,
                    mbArtist, mbTitle, mbAlbum);

                Log.Information("MusicBrainz: [{Index}] score={Score:F2}", i, score);

                if (score > bestScore)
                {
                    bestScore = score;
                    var meta = new TrackMetadata
                    {
                        Title = mbTitle,
                        Artist = mbArtist,
                        Album = mbAlbum
                    };

                    int durationMs = SafeGetNumber(obj, "length");
                    if (durationMs > 0)
                        meta.DurationSeconds = durationMs / 1000;

                    var match = MetadataMatch.FromMusicBrainz(meta, score, mbId);
                    match.ReleaseMbid = releaseMbid;
                    bestMatch = match;
                }
            }

            if (bestMatch != null)
                Log.Information("MusicBrainz: best match score={Score:F2} title='{Title}' artist='{Artist}' release={Release}",
                    bestMatch.Confidence, bestMatch.Metadata.Title, bestMatch.Metadata.Artist, bestMatch.ReleaseMbid);
            else
                Log.Information("MusicBrainz: no match found among {Count} candidates", items.Count);

            return bestMatch;
        }

        private float CalculateMatchScore(
            string qArtist, string qTitle, string qAlbum,
            string mArtist, string mTitle, string mAlbum)
        {
            float score = 0f;
            int fields = 0;

            if (!string.IsNullOrWhiteSpace(qTitle) && !string.IsNullOrWhiteSpace(mTitle))
            {
                fields++;
                score += FuzzyMatch(Normalize(qTitle), Normalize(mTitle));
            }

            if (!string.IsNullOrWhiteSpace(qArtist) && !string.IsNullOrWhiteSpace(mArtist))
            {
                fields++;
                score += FuzzyMatch(Normalize(qArtist), Normalize(mArtist));
            }

            if (!string.IsNullOrWhiteSpace(qAlbum) && !string.IsNullOrWhiteSpace(mAlbum))
            {
                fields++;
                score += FuzzyMatch(Normalize(qAlbum), Normalize(mAlbum));
            }

            if (fields == 0) return 0f;
            return score / fields;
        }

        private float FuzzyMatch(string a, string b)
        {
            if (a == b) return 1.0f;
            if (a.Contains(b) || b.Contains(a)) return 0.85f;

            int distance = LevenshteinDistance(a, b);
            int maxLen = Math.Max(a.Length, b.Length);
            if (maxLen == 0) return 1.0f;

            float similarity = 1f - ((float)distance / maxLen);
            return Math.Max(0f, similarity);
        }

        private int LevenshteinDistance(string s, string t)
        {
            if (s.Length == 0) return t.Length;
            if (t.Length == 0) return s.Length;

            var d = new int[s.Length + 1, t.Length + 1];
            for (int i = 0; i <= s.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= t.Length; j++) d[0, j] = j;

            for (int i = 1; i <= s.Length; i++)
            {
                for (int j = 1; j <= t.Length; j++)
                {
                    int cost = s[i - 1] == t[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[s.Length, t.Length];
        }

        private string Normalize(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            string lower = input.ToLowerInvariant().Trim();
            lower = System.Text.RegularExpressions.Regex.Replace(lower, @"[^\w\s]", "");
            lower = System.Text.RegularExpressions.Regex.Replace(lower, @"\s+", " ");
            return lower;
        }

        private static int SafeGetNumber(JsonObject obj, string key)
        {
            if (!obj.ContainsKey(key)) return 0;
            var val = obj.GetNamedValue(key);
            if (val.ValueType == JsonValueType.Number)
                return (int)val.GetNumber();
            if (val.ValueType == JsonValueType.String)
            {
                if (int.TryParse(val.GetString(), out int result))
                    return result;
            }
            return 0;
        }

        private static bool SafeGetBoolean(JsonObject obj, string key, bool defaultValue)
        {
            if (!obj.ContainsKey(key)) return defaultValue;
            var val = obj.GetNamedValue(key);
            if (val.ValueType == JsonValueType.Boolean)
                return val.GetBoolean();
            if (val.ValueType == JsonValueType.String)
            {
                if (bool.TryParse(val.GetString(), out bool result))
                    return result;
                if (val.GetString() == "1") return true;
                if (val.GetString() == "0") return false;
            }
            return defaultValue;
        }

        private static string SafeGetString(JsonObject obj, string key)
        {
            if (!obj.ContainsKey(key)) return null;
            var val = obj.GetNamedValue(key);
            if (val.ValueType == JsonValueType.String)
                return val.GetString();
            return null;
        }
    }
}
