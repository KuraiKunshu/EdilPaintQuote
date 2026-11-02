using EdilPaintPreventibiviGen.Data;
using EdilPaintPreventibiviGen.Data.Entities;
using EdilPaintPreventibiviGen.Data.Mappers;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class TrelloPersistenceTests
{
    [Fact]
    public void MirrorHasNoCustomerOrQuoteRelationshipAndDoesNotPopulateThem()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=not_used;Username=not_used;Password=not_used").Options);
        var mirror = db.Model.FindEntityType(typeof(TrelloCard));
        Assert.NotNull(mirror);
        Assert.Empty(mirror.GetForeignKeys());
        Assert.Null(mirror.FindProperty(nameof(TrelloCard.IsOfflineSnapshot)));
        var quote = new QuoteHistoryEntry();
        Assert.Empty(quote.TrelloCardId);
        Assert.Equal(Guid.Empty, quote.CustomerSyncId);
    }

    [Fact]
    public void EntityMapperPreservesExplicitAssociationWithoutChangingCustomer()
    {
        var entity = new QuoteEntity { TrelloCardId = "card-a", TrelloCardSnapshotJson = "snapshot-a" };
        var quote = entity.ToModel();
        Assert.Equal("card-a", quote.TrelloCardId);
        Assert.Equal("snapshot-a", quote.TrelloCardSnapshotJson);
        Assert.Empty(quote.CustomerName);
        Assert.Equal(Guid.Empty, quote.CustomerSyncId);
    }

    [Fact]
    public async Task CacheDistinguishesMissingCompleteAndPartialCopies()
    {
        using var directory = new TestDirectory();
        var store = new LocalJsonStoreService(directory.Path);
        Assert.Null(await store.LoadTrelloCardsAsync());
        var card = new TrelloCard { Id = "card-a", Name = "Sopralluogo" };
        await store.CacheTrelloCardAsync(card);
        Assert.Equal(card.Id, (await store.LoadTrelloCardAsync(card.Id))?.Id);
        Assert.Null(await store.LoadTrelloCardsAsync());
        await store.SaveTrelloCardsAsync([]);
        Assert.Empty((await store.LoadTrelloCardsAsync())!);
        Assert.Null(await store.LoadTrelloCardAsync(card.Id));
        Assert.Empty(await store.LoadCustomersAsync());
        Assert.Empty(await store.LoadHistoryAsync());
    }

    [Fact]
    public async Task QuoteHistoryAndDraftRetainManualAssociationAndDetectRemoval()
    {
        using var directory = new TestDirectory();
        var quote = new QuoteHistoryEntry
        {
            QuoteNumber = "2026-123",
            TrelloCardId = "card-a",
            TrelloCardSnapshotJson = new TrelloCard { Id = "card-a", Name = "Solo scheda lavoro" }.SerializeSnapshot()
        };
        var store = new LocalJsonStoreService(directory.Path);
        await store.SaveOrUpdateQuoteAsync(quote);
        var saved = Assert.Single(await store.LoadHistoryAsync());
        Assert.Equal(quote.TrelloCardId, saved.TrelloCardId);
        Assert.Equal(quote.TrelloCardSnapshotJson, saved.TrelloCardSnapshotJson);
        var drafts = new LocalDraftService(directory.Path);
        Assert.True(await drafts.SaveIfChangedAsync(quote));
        var recovered = await drafts.LoadAsync();
        Assert.Equal(quote.TrelloCardId, recovered?.TrelloCardId);
        Assert.Equal(quote.TrelloCardSnapshotJson, recovered?.TrelloCardSnapshotJson);
        Assert.False(await drafts.SaveIfChangedAsync(quote));
        quote.TrelloCardId = "";
        quote.TrelloCardSnapshotJson = "";
        Assert.True(await drafts.SaveIfChangedAsync(quote));
        Assert.Empty((await drafts.LoadAsync())!.TrelloCardId);
    }

    [Fact]
    public void ManualAssociationParticipatesInSyncHash()
    {
        var quote = new QuoteHistoryEntry();
        string unlinked = QuoteSyncHashService.Compute(quote);
        quote.TrelloCardId = "card-a";
        string linked = QuoteSyncHashService.Compute(quote);
        Assert.NotEqual(unlinked, linked);
        quote.TrelloCardSnapshotJson = "changed-snapshot";
        Assert.NotEqual(linked, QuoteSyncHashService.Compute(quote));
        quote.TrelloCardId = "";
        quote.TrelloCardSnapshotJson = "";
        Assert.Equal(unlinked, QuoteSyncHashService.Compute(quote));
    }

    [Fact]
    public void AssociationPatchRejectsMismatchedSnapshotAndAcceptsExplicitUnlink()
    {
        string snapshot = new TrelloCard { Id = "card-a" }.SerializeSnapshot();
        SqlDataService.ValidateTrelloAssociation("card-a", snapshot);
        SqlDataService.ValidateTrelloAssociation("", "");
        Assert.Throws<ArgumentException>(() => SqlDataService.ValidateTrelloAssociation("card-b", snapshot));
        Assert.Throws<ArgumentException>(() => SqlDataService.ValidateTrelloAssociation("", snapshot));
        Assert.Throws<ArgumentException>(() => SqlDataService.ValidateTrelloAssociation("card-a", "invalid"));
    }

    [Theory]
    [InlineData("", "", "card-a", "snapshot-a")]
    [InlineData("card-a", "snapshot-a", "", "")]
    [InlineData("card-a", "snapshot-a", "card-b", "snapshot-b")]
    [InlineData("card-a", "snapshot-old", "card-a", "snapshot-new")]
    public void LegacyHashNormalizationCannotDiscardAPendingManualChoice(
        string databaseId, string databaseSnapshot, string localId, string localSnapshot)
    {
        var database = new QuoteHistoryEntry
        {
            TrelloCardId = databaseId, TrelloCardSnapshotJson = databaseSnapshot
        };
        var local = new QuoteHistoryEntry
        {
            TrelloCardId = localId, TrelloCardSnapshotJson = localSnapshot,
            HasPendingDatabaseWrite = true
        };
        Assert.Equal(QuoteSyncHashService.ComputeLegacy(local), QuoteSyncHashService.ComputeLegacy(database));
        Assert.NotEqual(QuoteSyncHashService.Compute(local), QuoteSyncHashService.Compute(database));
        Assert.True(SyncService.HasExplicitPendingTrelloAssociationChange(local, database));
        var normalizationGuard = typeof(SyncService).GetMethod("HasExplicitPendingIdentityChange",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        Assert.True((bool)normalizationGuard.Invoke(null, [local, database])!);
        local.HasPendingDatabaseWrite = false;
        Assert.False(SyncService.HasExplicitPendingTrelloAssociationChange(local, database));
        Assert.False((bool)normalizationGuard.Invoke(null, [local, database])!);
    }

    [Theory]
    [InlineData("https://trello.com/c/abc123/sopralluogo", true)]
    [InlineData("https://www.trello.com/c/abc123", true)]
    [InlineData("http://trello.com/c/abc123", false)]
    [InlineData("https://trello.com.example.com/c/abc123", false)]
    [InlineData("https://user:password@trello.com/c/abc123", false)]
    [InlineData("file:///c:/Windows/notepad.exe", false)]
    [InlineData("https://trello.com:444/c/abc123", false)]
    public void CardLinksOnlyOpenHttpsTrelloCards(string url, bool expected)
    {
        var card = new TrelloCard { Id = "card-a", Url = url, IsOfflineSnapshot = true };
        Assert.Equal(expected, !string.IsNullOrEmpty(card.GetSafeUrl()));
        var restored = TrelloCard.FromSnapshot(card.SerializeSnapshot());
        Assert.NotNull(restored);
        Assert.Equal(card.Id, restored.Id);
        Assert.False(restored.IsOfflineSnapshot);
        Assert.Null(TrelloCard.FromSnapshot("invalid json"));
    }

    private sealed class TestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EdilPaint-Trello-tests", Guid.NewGuid().ToString("N"));
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
