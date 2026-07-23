using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;

namespace XFiles.Metadata
{
    public class DeezerProvider
    {
        private static readonly HttpClient _http;
        private static readonly Queue<DateTime> _requestTimes = new Queue<DateTime>();
        private static readonly object _rateLock = new object();
        private const string SearchUrl = "https://api.deezer.com/search";
        private const int MaxRequestsPerWindow = 45;
        private const int WindowSeconds = 5;

        static DeezerProvider()
        {
            _http = new HttpClient();
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("XFiles/1.0");
        }

        private static async Task ThrottleAsync(CancellationToken ct)
        {
            while (true)
            {
                lock (_rateLock)
                {
                    var now = DateTime.UtcNow;
                    while (_requestTimes.Count > 0 && (now - _requestTimes.Peek()).TotalSeconds > WindowSeconds)
                        _requestTimes.Dequeue();

                    if (_requestTimes.Count < MaxRequestsPerWindow)
                    {
                        _requestTimes.Enqueue(now);
                        return;
                    }
                }
                Log.Information("Deezer: rate limit reached, waiting 500ms");
                await Task.Delay(500, ct);
            }
        }

        public async Task<MetadataMatch> SearchAsync(string artist, string title, string album, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(artist))
                return null;

            string cleanTitle = CleanSearchTerm(title);

            var queries = new List<string>();

            if (!string.IsNullOrWhiteSpace(cleanTitle) && !string.IsNullOrWhiteSpace(artist))
                queries.Add($"artist:\"{artist.Trim()}\" track:\"{cleanTitle.Trim()}\"");

            if (!string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(album))
                queries.Add($"artist:\"{artist.Trim()}\" album:\"{album.Trim()}\"");

            if (queries.Count == 0) return null;

            foreach (var query in queries)
            {
                var result = await SearchSingleAsync(query, artist, cleanTitle, album, ct);
                if (result != null) return result;
            }

            return null;
        }

        private async Task<MetadataMatch> SearchSingleAsync(string query, string qArtist, string qTitle, string qAlbum, CancellationToken ct)
        {
            await ThrottleAsync(ct);

            try
            {
                string url = $"{SearchUrl}?q={Uri.EscapeDataString(query)}&limit=10";
                Log.Information("Deezer: searching query={Query} url={Url}", query, url);

                var response = await _http.GetAsync(url, ct);
                Log.Information("Deezer: HTTP {Status}", response.StatusCode);

                if (!response.IsSuccessStatusCode) return null;

                string json = await response.Content.ReadAsStringAsync();
                Log.Information("Deezer: response (first 600 chars)={Json}", json.Length > 600 ? json.Substring(0, 600) : json);

                var root = JsonObject.Parse(json);

                if (root.ContainsKey("error"))
                {
                    var error = root.GetNamedObject("error");
                    Log.Warning("Deezer: API error — {Message}", error?.GetNamedString("message", "unknown"));
                    return null;
                }

                var items = root.GetNamedArray("data", new JsonArray());
                Log.Information("Deezer: found {Count} results for query={Query}", items.Count, query);

                if (items.Count == 0) return null;

                return FindBestMatch(items, qArtist, qTitle, qAlbum);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Warning("Deezer: search failed: {Error}", ex.Message);
                return null;
            }
        }

        public async Task<byte[]> FetchCoverArtAsync(string coverUrl, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(coverUrl))
            {
                Log.Information("Deezer: no cover URL, skipping");
                return null;
            }

            try
            {
                Log.Information("Deezer: fetching cover art URL={Url}", coverUrl);
                var response = await _http.GetAsync(coverUrl, ct);
                Log.Information("Deezer: cover art HTTP {Status}", response.StatusCode);

                if (!response.IsSuccessStatusCode) return null;

                byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                Log.Information("Deezer: downloaded {Size} bytes cover art", bytes.Length);
                return bytes;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Warning("Deezer: cover art fetch failed: {Error}", ex.Message);
                return null;
            }
        }

        private MetadataMatch FindBestMatch(JsonArray items, string queryArtist, string queryTitle, string queryAlbum)
        {
            MetadataMatch bestMatch = null;
            float bestScore = 0f;

            Log.Information("Deezer: evaluating {Count} candidates for title='{Title}' artist='{Artist}' album='{Album}'",
                items.Count, queryTitle, queryArtist, queryAlbum);

            for (int i = 0; i < items.Count; i++)
            {
                try
                {
                    var obj = items[i].GetObject();
                    string dzTitle = obj.GetNamedString("title", "");
                    int duration = SafeGetNumber(obj, "duration");

                    string dzArtist = "";
                    if (obj.ContainsKey("artist"))
                    {
                        var artistObj = obj.GetNamedObject("artist", new JsonObject());
                        dzArtist = artistObj.GetNamedString("name", "");
                    }

                    string dzAlbum = "";
                    string coverUrl = "";
                    if (obj.ContainsKey("album"))
                    {
                        var albumObj = obj.GetNamedObject("album", new JsonObject());
                        dzAlbum = albumObj.GetNamedString("title", "");
                        coverUrl = SafeGetString(albumObj, "cover_xl");
                        if (string.IsNullOrEmpty(coverUrl))
                            coverUrl = SafeGetString(albumObj, "cover_big");
                    }

                    string releaseDate = SafeGetString(obj, "release_date");

                    Log.Information("Deezer: [{Index}] title='{DzTitle}' artist='{DzArtist}' album='{DzAlbum}' cover={HasCover}",
                        i, dzTitle, dzArtist, dzAlbum, !string.IsNullOrEmpty(coverUrl));

                    float score = CalculateMatchScore(queryArtist, queryTitle, queryAlbum, dzArtist, dzTitle, dzAlbum);
                    Log.Information("Deezer: [{Index}] score={Score:F2}", i, score);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        var meta = new TrackMetadata
                        {
                            Title = dzTitle,
                            Artist = dzArtist,
                            Album = dzAlbum
                        };

                        if (duration > 0)
                            meta.DurationSeconds = duration;

                        int year = 0;
                        if (!string.IsNullOrEmpty(releaseDate) && releaseDate.Length >= 4
                            && int.TryParse(releaseDate.Substring(0, 4), out int parsed))
                            year = parsed;
                        if (year > 0)
                            meta.Year = year.ToString();

                        var match = new MetadataMatch
                        {
                            Metadata = meta,
                            Confidence = score,
                            Source = MatchSource.MusicBrainz,
                            ReleaseMbid = dzAlbum
                        };

                        if (!string.IsNullOrEmpty(coverUrl))
                            match.CoverArtUrl = coverUrl;

                        bestMatch = match;
                    }
                }
                catch (Exception itemEx)
                {
                    Log.Warning("Deezer: error parsing item[{Index}]: {Error}", i, itemEx.Message);
                }
            }

            if (bestMatch != null)
                Log.Information("Deezer: best match score={Score:F2} title='{Title}' artist='{Artist}' album='{Album}' cover={HasCover}",
                    bestMatch.Confidence, bestMatch.Metadata.Title, bestMatch.Metadata.Artist, bestMatch.Metadata.Album, !string.IsNullOrEmpty(bestMatch.CoverArtUrl));
            else
                Log.Information("Deezer: no match found among {Count} candidates", items.Count);

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

        private string CleanSearchTerm(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;
            string cleaned = input.Replace("_", " ").Trim();
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s*-\s*\w+\s*$", "");
            return cleaned.Trim();
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
