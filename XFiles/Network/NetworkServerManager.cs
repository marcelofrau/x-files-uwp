using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Security.Credentials;
using XFiles.Metadata;

namespace XFiles.Network
{
    /// <summary>
    /// Facade for saved network locations. Rows live in the shared metadata.db
    /// (NetworkServerEntry table, created by MetadataCache migration v3);
    /// passwords live in Windows Credential Locker (PasswordVault) keyed by the
    /// canonical URL. Never write a password to the database or settings.
    /// </summary>
    public static class NetworkServerManager
    {
        private const string VaultResourcePrefix = "xfiles-network/";

        private static readonly Lazy<Task<NetworkServerStore>> _storeLazy =
            new Lazy<Task<NetworkServerStore>>(async () =>
            {
                var db = await MetadataCache.GetDbAsync();
                var store = new NetworkServerStore(db);
                await store.CreateTableAsync();
                Log.Info("NetworkServerManager: store ready");
                return store;
            }, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        private static Task<NetworkServerStore> GetStoreAsync() => _storeLazy.Value;

        public static async Task<List<NetworkServerConfig>> GetAllAsync()
        {
            try
            {
                var store = await GetStoreAsync();
                var rows = await store.GetAllAsync();
                var configs = rows.Select(ToConfig).ToList();
                configs.Sort((a, b) =>
                    string.Compare(NetworkUrl.SortKey(a), NetworkUrl.SortKey(b), StringComparison.Ordinal));
                return configs;
            }
            catch (Exception ex)
            {
                Log.Warn("NetworkServerManager.GetAll: failed", ex);
                return new List<NetworkServerConfig>();
            }
        }

        public static async Task<NetworkServerConfig> GetAsync(int id)
        {
            try
            {
                var store = await GetStoreAsync();
                var row = await store.GetByIdAsync(id);
                return row == null ? null : ToConfig(row);
            }
            catch (Exception ex)
            {
                Log.Warn("NetworkServerManager.Get: failed id={Id}", ex, id);
                return null;
            }
        }

        /// <summary>
        /// Adds a location. When a row with the same canonical URL already
        /// exists, it is updated instead (idempotent add). Returns the row id.
        /// </summary>
        public static async Task<int> AddAsync(NetworkServerConfig config, string password)
        {
            try
            {
                string canonical = NetworkUrl.Compose(config);
                if (canonical == null)
                {
                    Log.Warn("NetworkServerManager.Add: refused, no host");
                    return 0;
                }

                var store = await GetStoreAsync();
                var existing = await store.GetByCanonicalUrlAsync(canonical);

                int id;
                if (existing != null)
                {
                    config = MergeInto(existing, config);
                    config.Protocol = NetworkProtocol.Smb;
                    await store.UpdateAsync(ToEntry(config, existing.Id));
                    id = existing.Id;
                    Log.Info("NetworkServerManager.Add: updated existing {Url}", canonical);
                }
                else
                {
                    config.Protocol = NetworkProtocol.Smb;
                    id = await store.InsertAsync(ToEntry(config, 0));
                    Log.Info("NetworkServerManager.Add: inserted {Url} id={Id}", canonical, id);
                }

                await SetPasswordAsync(config, password);
                return id;
            }
            catch (Exception ex)
            {
                Log.Warn("NetworkServerManager.Add: failed", ex);
                return 0;
            }
        }

        public static async Task UpdateAsync(int id, NetworkServerConfig config, string password)
        {
            try
            {
                var store = await GetStoreAsync();
                var row = await store.GetByIdAsync(id);
                if (row == null)
                {
                    Log.Warn("NetworkServerManager.Update: no row id={Id}", id);
                    return;
                }

                string oldCanonical = row.CanonicalUrl;
                string newCanonical = NetworkUrl.Compose(config);
                if (newCanonical == null)
                {
                    Log.Warn("NetworkServerManager.Update: refused, no host id={Id}", id);
                    return;
                }

                var conflict = await store.GetByCanonicalUrlAsync(newCanonical);
                if (conflict != null && conflict.Id != id)
                {
                    Log.Warn("NetworkServerManager.Update: canonical conflict {Url} id={Id}", newCanonical, id);
                    return;
                }

                config.Protocol = NetworkProtocol.Smb;
                await store.UpdateAsync(ToEntry(config, id));

                if (!string.Equals(oldCanonical, newCanonical, StringComparison.Ordinal))
                {
                    RemovePasswordEntry(oldCanonical, row.Username);
                    await SetPasswordAsync(config, password);
                }
                else if (password != null)
                {
                    await SetPasswordAsync(config, password);
                }

                Log.Info("NetworkServerManager.Update: saved id={Id} url={Url}", id, newCanonical);
            }
            catch (Exception ex)
            {
                Log.Warn("NetworkServerManager.Update: failed id={Id}", ex, id);
            }
        }

        public static async Task RemoveAsync(int id)
        {
            try
            {
                var store = await GetStoreAsync();
                var row = await store.GetByIdAsync(id);
                if (row == null)
                {
                    Log.Warn("NetworkServerManager.Remove: no row id={Id}", id);
                    return;
                }

                await store.DeleteAsync(id);
                RemovePasswordEntry(row.CanonicalUrl, row.Username);
                Log.Info("NetworkServerManager.Remove: removed id={Id} url={Url}", id, row.CanonicalUrl);
            }
            catch (Exception ex)
            {
                Log.Warn("NetworkServerManager.Remove: failed id={Id}", ex, id);
            }
        }

        public static async Task<string> GetPasswordAsync(NetworkServerConfig config)
        {
            try
            {
                string resource = NetworkUrl.VaultResource(config);
                if (resource == null) return null;
                var vault = new PasswordVault();
                var cred = vault.Retrieve(resource, (config.Username ?? "").Trim());
                return cred?.Password;
            }
            catch (Exception)
            {
                Log.Verb("NetworkServerManager.GetPassword: none for {Url}", NetworkUrl.Compose(config));
                return null;
            }
        }

        public static async Task SetPasswordAsync(NetworkServerConfig config, string password)
        {
            try
            {
                string resource = NetworkUrl.VaultResource(config);
                if (resource == null) return;
                string username = (config.Username ?? "").Trim();

                var vault = new PasswordVault();
                try
                {
                    var existing = vault.Retrieve(resource, username);
                    if (existing != null)
                    {
                        existing.RetrievePassword();
                        if (existing.Password == password) return;
                        vault.Remove(existing);
                    }
                }
                catch
                {
                    // No stored credential yet — fall through to Add.
                }

                vault.Add(new PasswordCredential(resource, username, password ?? ""));
                Log.Verb("NetworkServerManager.SetPassword: stored {Url}", resource);
            }
            catch (Exception ex)
            {
                Log.Warn("NetworkServerManager.SetPassword: failed {Url}", ex, NetworkUrl.Compose(config));
            }
        }

        private static void RemovePasswordEntry(string resource, string username)
        {
            try
            {
                if (string.IsNullOrEmpty(resource)) return;
                var vault = new PasswordVault();
                var cred = vault.Retrieve(resource, username ?? "");
                if (cred != null)
                    vault.Remove(cred);
            }
            catch (Exception)
            {
                Log.Verb("NetworkServerManager.RemovePassword: none for {Resource}", resource);
            }
        }

        private static NetworkServerConfig MergeInto(NetworkServerEntry existing, NetworkServerConfig incoming)
        {
            return new NetworkServerConfig
            {
                Protocol = (NetworkProtocol)existing.Protocol,
                DisplayName = incoming.DisplayName ?? existing.DisplayName,
                Host = incoming.Host ?? existing.Host,
                Port = incoming.Port > 0 ? incoming.Port : existing.Port,
                Username = incoming.Username ?? existing.Username,
                Share = incoming.Share ?? existing.Share
            };
        }

        private static NetworkServerEntry ToEntry(NetworkServerConfig config, int id)
        {
            return new NetworkServerEntry
            {
                Id = id,
                Protocol = (int)config.Protocol,
                DisplayName = config.DisplayName,
                Host = config.Host,
                Port = config.Port,
                Username = config.Username,
                Share = config.Share,
                CanonicalUrl = NetworkUrl.Compose(config)
            };
        }

        private static NetworkServerConfig ToConfig(NetworkServerEntry row)
        {
            return new NetworkServerConfig
            {
                Protocol = (NetworkProtocol)row.Protocol,
                DisplayName = row.DisplayName,
                Host = row.Host,
                Port = row.Port,
                Username = row.Username,
                Share = row.Share
            };
        }
    }
}
