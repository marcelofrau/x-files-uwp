using System.Collections.Generic;
using System.Threading.Tasks;
using SQLite;
using XFiles.Metadata;

namespace XFiles.Network
{
    /// <summary>
    /// Row-level CRUD for <see cref="NetworkServerEntry"/> against a SQLite
    /// connection. Pure SQLite — no UWP types, linkable into unit tests
    /// (tests run against an in-memory database).
    /// </summary>
    public class NetworkServerStore
    {
        private readonly SQLiteAsyncConnection _db;

        public NetworkServerStore(SQLiteAsyncConnection db)
        {
            _db = db;
        }

        public Task CreateTableAsync()
        {
            return _db.CreateTableAsync<NetworkServerEntry>();
        }

        public async Task<List<NetworkServerEntry>> GetAllAsync()
        {
            return await _db.Table<NetworkServerEntry>().ToListAsync();
        }

        public async Task<NetworkServerEntry> GetByIdAsync(int id)
        {
            return await _db.Table<NetworkServerEntry>()
                .Where(e => e.Id == id).FirstOrDefaultAsync();
        }

        public async Task<NetworkServerEntry> GetByCanonicalUrlAsync(string canonicalUrl)
        {
            return await _db.Table<NetworkServerEntry>()
                .Where(e => e.CanonicalUrl == canonicalUrl).FirstOrDefaultAsync();
        }

        /// <summary>Inserts a row; throws on duplicate canonical URL (unique index).</summary>
        public async Task<int> InsertAsync(NetworkServerEntry entry)
        {
            await _db.InsertAsync(entry);
            return entry.Id;
        }

        public async Task UpdateAsync(NetworkServerEntry entry)
        {
            await _db.UpdateAsync(entry);
        }

        public async Task<int> DeleteAsync(int id)
        {
            return await _db.DeleteAsync<NetworkServerEntry>(id);
        }
    }
}
