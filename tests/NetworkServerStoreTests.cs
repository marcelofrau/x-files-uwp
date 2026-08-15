using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SQLite;
using XFiles.Metadata;
using XFiles.Network;

namespace XFiles.Tests
{
    [TestClass]
    public class NetworkServerStoreTests
    {
        static NetworkServerStoreTests()
        {
            SQLitePCL.Batteries_V2.Init();
        }

        private static NetworkServerEntry MakeRow(string host, string share, string user = null)
        {
            return new NetworkServerEntry
            {
                Protocol = 0,
                DisplayName = user == null ? host : $"{user}@{host}",
                Host = host,
                Port = 445,
                Username = user,
                Share = share,
                CanonicalUrl = user == null ? $"smb://{host}/{share}" : $"smb://{user}@{host}/{share}"
            };
        }

        private static async Task<NetworkServerStore> CreateStoreAsync()
        {
            // sqlite-net 1.9.172 pools connections by connection-string (static
            // SQLiteConnectionPool) — every ":memory:" in this process shares ONE
            // database. Clearing the table each time keeps tests isolated.
            var db = new SQLiteAsyncConnection(":memory:");
            var store = new NetworkServerStore(db);
            await store.CreateTableAsync();
            await db.DeleteAllAsync<NetworkServerEntry>();
            return store;
        }

        [TestMethod]
        public async Task Insert_GetAll_ReturnsRow()
        {
            var store = await CreateStoreAsync();
            int id = await store.InsertAsync(MakeRow("nas", "media"));

            var all = await store.GetAllAsync();
            Assert.AreEqual(1, all.Count);
            Assert.AreEqual(id, all[0].Id);
            Assert.AreEqual("nas", all[0].Host);
            Assert.AreEqual("media", all[0].Share);
        }

        [TestMethod]
        public async Task Insert_GetById_ReturnsRow()
        {
            var store = await CreateStoreAsync();
            int id = await store.InsertAsync(MakeRow("nas", "media"));

            var row = await store.GetByIdAsync(id);
            Assert.IsNotNull(row);
            Assert.AreEqual("nas", row.Host);
        }

        [TestMethod]
        public async Task Insert_GetByCanonicalUrl_FindsRow()
        {
            var store = await CreateStoreAsync();
            await store.InsertAsync(MakeRow("nas", "media"));

            var row = await store.GetByCanonicalUrlAsync("smb://nas/media");
            Assert.IsNotNull(row);
            Assert.AreEqual("nas", row.Host);
        }

        [TestMethod]
        public async Task GetByCanonicalUrl_NoMatch_Null()
        {
            var store = await CreateStoreAsync();
            Assert.IsNull(await store.GetByCanonicalUrlAsync("smb://other/share"));
        }

        [TestMethod]
        public async Task Insert_DuplicateCanonical_Throws()
        {
            var store = await CreateStoreAsync();
            await store.InsertAsync(MakeRow("nas", "media"));

            await Assert.ThrowsExceptionAsync<SQLiteException>(() =>
                store.InsertAsync(MakeRow("nas", "media")));
        }

        [TestMethod]
        public async Task Insert_MultipleLocations_KeepsBoth()
        {
            var store = await CreateStoreAsync();
            await store.InsertAsync(MakeRow("nas", "media"));
            await store.InsertAsync(MakeRow("nas", "backup"));
            await store.InsertAsync(MakeRow("192.168.1.9", "photos", "alice"));

            var all = await store.GetAllAsync();
            Assert.AreEqual(3, all.Count);
        }

        [TestMethod]
        public async Task Update_ChangesFields()
        {
            var store = await CreateStoreAsync();
            int id = await store.InsertAsync(MakeRow("nas", "media"));

            var row = await store.GetByIdAsync(id);
            row.Share = "music";
            row.DisplayName = "Renamed";
            await store.UpdateAsync(row);

            var reloaded = await store.GetByIdAsync(id);
            Assert.AreEqual("music", reloaded.Share);
            Assert.AreEqual("Renamed", reloaded.DisplayName);
        }

        [TestMethod]
        public async Task Delete_RemovesRow()
        {
            var store = await CreateStoreAsync();
            int id = await store.InsertAsync(MakeRow("nas", "media"));

            int removed = await store.DeleteAsync(id);
            Assert.AreEqual(1, removed);
            Assert.IsNull(await store.GetByIdAsync(id));
            Assert.AreEqual(0, (await store.GetAllAsync()).Count);
        }

        [TestMethod]
        public async Task Delete_MissingId_ReturnsZero()
        {
            var store = await CreateStoreAsync();
            Assert.AreEqual(0, await store.DeleteAsync(999));
        }

        [TestMethod]
        public async Task Insert_AutoIncrement_IdsIncrease()
        {
            var store = await CreateStoreAsync();
            int a = await store.InsertAsync(MakeRow("nas", "media"));
            int b = await store.InsertAsync(MakeRow("nas", "backup"));
            Assert.AreNotEqual(a, b);
            Assert.IsTrue(b > a);
        }
    }
}
