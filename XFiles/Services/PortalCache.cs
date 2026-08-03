using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace XFiles.Services
{
    /// <summary>
    /// Managed temp cache for portal (Device Portal) files. A portal file is downloaded
    /// once and reused by preview, playback, and archive drill-in. Keyed by
    /// knownFolder|package|path|name + size + dateCreated so a changed file re-downloads.
    /// Budget: 2 GB, LRU eviction. Cleared at app launch (no cross-session accumulation).
    /// </summary>
    public static class PortalCache
    {
        public const long MaxCacheBytes = 2L * 1024 * 1024 * 1024;
        public const long AutoPreviewMaxBytes = 25L * 1024 * 1024;

        private const string RootFolderName = "portal-cache";
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

        private static readonly Dictionary<string, string> Index = new Dictionary<string, string>();
        private static readonly LinkedList<string> Lru = new LinkedList<string>();
        private static readonly Dictionary<string, LinkedListNode<string>> LruNodes = new Dictionary<string, LinkedListNode<string>>();
        private static long _bytes;
        private static string _rootPath;

        private static string RootPath
        {
            get
            {
                if (_rootPath == null)
                    _rootPath = Path.Combine(ApplicationData.Current.LocalFolder.Path, RootFolderName);
                return _rootPath;
            }
        }

        /// <summary>
        /// Stable cache key for a portal entry. Any change (size or creation time)
        /// produces a different key, so the file re-downloads.
        /// </summary>
        public static string GetKey(PortalFileEntry entry)
        {
            var sb = new StringBuilder();
            sb.Append(entry.KnownFolder).Append('|')
              .Append(entry.PackageFullName).Append('|')
              .Append(entry.PortalPath).Append('|')
              .Append(entry.Name).Append('|')
              .Append(entry.FileSize).Append('|')
              .Append(entry.DateCreated);
            return sb.ToString();
        }

        /// <summary>
        /// Returns the local cache path for a portal entry if already cached, else null.
        /// </summary>
        public static async Task<string> GetCachedPathAsync(PortalFileEntry entry)
        {
            await Gate.WaitAsync();
            try
            {
                if (Index.TryGetValue(GetKey(entry), out string path) && File.Exists(path))
                {
                    TouchLocked(GetKey(entry));
                    return path;
                }
            }
            finally
            {
                Gate.Release();
            }
            return null;
        }

        /// <summary>
        /// Ensures the portal entry is present in the cache, downloading it if needed.
        /// Returns the local path. Reuses an existing download across preview/playback/zip.
        /// </summary>
        public static async Task<string> EnsureAsync(PortalFileEntry entry, IProgress<double> progress)
        {
            string cached = await GetCachedPathAsync(entry);
            if (cached != null) return cached;

            string key = GetKey(entry);
            string hash = ComputeHash(key);
            string ext = SanitizeExtension(entry.Name);
            string final = Path.Combine(RootPath, hash + ext);

            await Gate.WaitAsync();
            try
            {
                // Double-check under the gate (concurrent Ensure for the same entry).
                if (Index.ContainsKey(key) && File.Exists(final))
                {
                    TouchLocked(key);
                    return final;
                }

                Directory.CreateDirectory(RootPath);
                string tmp = Path.Combine(RootPath, hash + ".part");
                try
                {
                    Log.Info("PortalCache.Ensure: downloading {Name} ({Bytes} bytes)", entry.Name, entry.FileSize);
                    using (var fs = File.Create(tmp))
                        await DevicePortalService.DownloadPortalFileAsync(entry, fs, progress);

                    if (File.Exists(final))
                        File.Delete(final);
                    File.Move(tmp, final);

                    Index[key] = final;
                    long size = new FileInfo(final).Length;
                    _bytes += size;
                    TouchLocked(key);

                    Log.Info("PortalCache.Ensure: cached {Name} ({Bytes} bytes, total {Total} bytes)",
                        entry.Name, size, _bytes);
                }
                catch
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                    throw;
                }

                EvictLocked();
                return final;
            }
            finally
            {
                Gate.Release();
            }
        }

        /// <summary>
        /// Wipes the cache directory and resets the in-memory index. Called at launch.
        /// </summary>
        public static async Task ClearAsync()
        {
            await Gate.WaitAsync();
            try
            {
                if (Directory.Exists(RootPath))
                {
                    try { Directory.Delete(RootPath, true); }
                    catch (Exception ex) { Log.Warn("PortalCache.Clear: failed to delete {Path}: {Message}", RootPath, ex.Message); }
                }
                Index.Clear();
                Lru.Clear();
                LruNodes.Clear();
                _bytes = 0;
                Log.Info("PortalCache.Clear: cache cleared");
            }
            finally
            {
                Gate.Release();
            }
        }

        private static void TouchLocked(string key)
        {
            if (LruNodes.TryGetValue(key, out LinkedListNode<string> node))
            {
                Lru.Remove(node);
                LruNodes.Remove(key);
            }
            LruNodes[key] = Lru.AddLast(key);
        }

        private static void EvictLocked()
        {
            while (_bytes > MaxCacheBytes && Lru.Count > 1)
            {
                string oldestKey = Lru.First.Value;
                if (Index.TryGetValue(oldestKey, out string path))
                {
                    try
                    {
                        long size = new FileInfo(path).Length;
                        File.Delete(path);
                        _bytes -= size;
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("PortalCache.Evict: failed to delete {Path}: {Message}", path, ex.Message);
                    }
                }
                Lru.RemoveFirst();
                LruNodes.Remove(oldestKey);
                Index.Remove(oldestKey);
            }
        }

        private static string ComputeHash(string key)
        {
            using (var sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
                var sb = new StringBuilder(40);
                foreach (byte b in hash)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static string SanitizeExtension(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            string ext = Path.GetExtension(name);
            if (string.IsNullOrEmpty(ext) || ext.Length > 16) return "";
            return ext.ToLowerInvariant();
        }
    }
}
