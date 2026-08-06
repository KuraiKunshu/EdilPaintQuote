using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class LocalJsonStoreCustomerTests
{
    [Fact]
    public async Task BulkUpdateRemovesLegacyNameAliasAndPreservesStableId()
    {
        string temporaryPath = CreateTemporaryTestPath();
        try
        {
            var store = new LocalJsonStoreService(temporaryPath);
            DateTime legacyTimestamp = DateTime.UtcNow.AddDays(-1);
            await store.SaveCustomersAsync([
                new Customer
                {
                    SyncId = Guid.Empty,
                    BusinessName = "Cliente legacy",
                    Address = "Indirizzo precedente",
                    LastModifiedUtc = legacyTimestamp
                }
            ]);

            Guid stableId = Guid.NewGuid();
            await store.BulkUpdateCustomersAsync([
                new Customer
                {
                    SyncId = stableId,
                    BusinessName = "Cliente legacy",
                    Address = "Indirizzo aggiornato",
                    LastModifiedUtc = legacyTimestamp.AddHours(1)
                }
            ]);

            var restored = Assert.Single(await store.LoadCustomersAsync());
            Assert.Equal(stableId, restored.SyncId);
            Assert.Equal("Indirizzo aggiornato", restored.Address);
        }
        finally
        {
            DeleteTemporaryTestPath(temporaryPath);
        }
    }

    [Fact]
    public async Task BulkUpdateUsesIncomingDatabaseVersionToResolvePendingAlias()
    {
        string temporaryPath = CreateTemporaryTestPath();
        try
        {
            var store = new LocalJsonStoreService(temporaryPath);
            DateTime pendingTimestamp = DateTime.UtcNow.AddHours(-2);
            await store.SaveCustomersAsync([
                new Customer
                {
                    SyncId = Guid.Empty,
                    BusinessName = "Cliente pending",
                    Address = "Modifica locale",
                    LastModifiedUtc = pendingTimestamp,
                    HasPendingDatabaseWrite = true
                }
            ]);

            Guid stableId = Guid.NewGuid();
            await store.BulkUpdateCustomersAsync([
                new Customer
                {
                    SyncId = stableId,
                    BusinessName = "Cliente pending",
                    Address = "Versione database",
                    LastModifiedUtc = pendingTimestamp.AddHours(1)
                }
            ]);

            var restored = Assert.Single(await store.LoadCustomersAsync());
            Assert.Equal(stableId, restored.SyncId);
            Assert.False(restored.HasPendingDatabaseWrite);
            Assert.Equal("Versione database", restored.Address);
        }
        finally
        {
            DeleteTemporaryTestPath(temporaryPath);
        }
    }

    [Fact]
    public async Task SaveOrUpdateRemovesEveryMatchingNameAndIdAlias()
    {
        string temporaryPath = CreateTemporaryTestPath();
        try
        {
            var store = new LocalJsonStoreService(temporaryPath);
            Guid stableId = Guid.NewGuid();
            await store.SaveCustomersAsync([
                new Customer { SyncId = Guid.Empty, BusinessName = "Cliente duplicato" },
                new Customer { SyncId = stableId, BusinessName = "Cliente duplicato" },
                new Customer { SyncId = stableId, BusinessName = "Vecchio alias" }
            ]);

            await store.SaveOrUpdateCustomerAsync(new Customer
            {
                SyncId = stableId,
                BusinessName = "Cliente duplicato",
                Address = "Record definitivo"
            });

            var restored = Assert.Single(await store.LoadCustomersAsync());
            Assert.Equal(stableId, restored.SyncId);
            Assert.Equal("Record definitivo", restored.Address);
        }
        finally
        {
            DeleteTemporaryTestPath(temporaryPath);
        }
    }

    [Fact]
    public async Task BulkUpdateKeepsDifferentStableCustomersWithTheSameName()
    {
        string temporaryPath = CreateTemporaryTestPath();
        try
        {
            var store = new LocalJsonStoreService(temporaryPath);
            Guid firstId = Guid.NewGuid();
            Guid secondId = Guid.NewGuid();
            await store.SaveCustomersAsync([
                new Customer { SyncId = firstId, BusinessName = "Cliente omonimo", Address = "Via Uno" },
                new Customer { SyncId = secondId, BusinessName = "Cliente omonimo", Address = "Via Due" }
            ]);

            await store.BulkUpdateCustomersAsync([
                new Customer { SyncId = firstId, BusinessName = "Cliente omonimo", Address = "Via Uno aggiornata" }
            ]);

            var restored = await store.LoadCustomersAsync();
            Assert.Equal(2, restored.Count);
            Assert.Contains(restored, customer => customer.SyncId == firstId && customer.Address == "Via Uno aggiornata");
            Assert.Contains(restored, customer => customer.SyncId == secondId && customer.Address == "Via Due");
        }
        finally
        {
            DeleteTemporaryTestPath(temporaryPath);
        }
    }

    [Fact]
    public async Task BulkUpdateKeepsAmbiguousLegacyAliasWhenTwoStableCustomersShareItsName()
    {
        string temporaryPath = CreateTemporaryTestPath();
        try
        {
            var store = new LocalJsonStoreService(temporaryPath);
            await store.SaveCustomersAsync([
                new Customer
                {
                    SyncId = Guid.Empty,
                    BusinessName = "Cliente omonimo legacy",
                    Address = "Indirizzo da verificare"
                }
            ]);

            Guid firstId = Guid.NewGuid();
            Guid secondId = Guid.NewGuid();
            await store.BulkUpdateCustomersAsync([
                new Customer
                {
                    SyncId = firstId,
                    BusinessName = "Cliente omonimo legacy",
                    Address = "Via Uno"
                },
                new Customer
                {
                    SyncId = secondId,
                    BusinessName = "Cliente omonimo legacy",
                    Address = "Via Due"
                }
            ]);

            var restored = await store.LoadCustomersAsync();
            Assert.Equal(3, restored.Count);
            Assert.Contains(restored, customer => customer.SyncId == Guid.Empty);
            Assert.Contains(restored, customer => customer.SyncId == firstId);
            Assert.Contains(restored, customer => customer.SyncId == secondId);
        }
        finally
        {
            DeleteTemporaryTestPath(temporaryPath);
        }
    }

    [Fact]
    public async Task BulkDeleteByStableIdDoesNotDeleteLegacyOrHomonymousCustomers()
    {
        string temporaryPath = CreateTemporaryTestPath();
        try
        {
            var store = new LocalJsonStoreService(temporaryPath);
            Guid deletedId = Guid.NewGuid();
            Guid preservedId = Guid.NewGuid();
            await store.SaveCustomersAsync([
                new Customer { SyncId = deletedId, BusinessName = "Cliente omonimo" },
                new Customer { SyncId = preservedId, BusinessName = "Cliente omonimo" },
                new Customer { SyncId = Guid.Empty, BusinessName = "Cliente omonimo" }
            ]);

            await store.DeleteCustomersAsync([
                new Customer { SyncId = deletedId, BusinessName = "Cliente omonimo" }
            ]);

            var restored = await store.LoadCustomersAsync();
            Assert.Equal(2, restored.Count);
            Assert.Contains(restored, customer => customer.SyncId == preservedId);
            Assert.Contains(restored, customer => customer.SyncId == Guid.Empty);
        }
        finally
        {
            DeleteTemporaryTestPath(temporaryPath);
        }
    }

    [Fact]
    public async Task BulkDeleteLegacyAliasDoesNotDeleteStableCustomerWithSameName()
    {
        string temporaryPath = CreateTemporaryTestPath();
        try
        {
            var store = new LocalJsonStoreService(temporaryPath);
            Guid stableId = Guid.NewGuid();
            await store.SaveCustomersAsync([
                new Customer { SyncId = stableId, BusinessName = "Cliente legacy" },
                new Customer { SyncId = Guid.Empty, BusinessName = "Cliente legacy" }
            ]);

            await store.DeleteCustomersAsync([
                new Customer { SyncId = Guid.Empty, BusinessName = "Cliente legacy" }
            ]);

            var restored = Assert.Single(await store.LoadCustomersAsync());
            Assert.Equal(stableId, restored.SyncId);
        }
        finally
        {
            DeleteTemporaryTestPath(temporaryPath);
        }
    }

    private static string CreateTemporaryTestPath() =>
        Path.Combine(Path.GetTempPath(), "EdilPaintPreventivi.Tests", Guid.NewGuid().ToString("N"));

    private static void DeleteTemporaryTestPath(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
