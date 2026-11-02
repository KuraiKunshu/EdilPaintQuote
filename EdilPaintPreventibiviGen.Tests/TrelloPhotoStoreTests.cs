using System.Text.Json;
using EdilPaintPreventibiviGen.Data;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class TrelloPhotoStoreTests
{
    [Fact]
    public void PhotoStorageCannotCreateCustomerOrQuoteAssociations()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=not_used;Username=not_used").Options);
        var photos = db.Model.FindEntityType(typeof(TrelloPhoto))!;
        var requests = db.Model.FindEntityType(typeof(TrelloPhotoRequest))!;
        Assert.Equal(new[] { "CardId", "AttachmentId" }, photos.FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Empty(photos.GetForeignKeys());
        Assert.Empty(requests.GetForeignKeys());
        Assert.Equal(64, photos.FindProperty(nameof(TrelloPhoto.CardId))!.GetMaxLength());
        Assert.Equal(100, photos.FindProperty(nameof(TrelloPhoto.ContentType))!.GetMaxLength());
        Assert.Equal("TrelloPhotos", photos.GetTableName());
        Assert.Equal("TrelloPhotoRequests", requests.GetTableName());
        Assert.Equal(Guid.Empty, new QuoteHistoryEntry().CustomerSyncId);
        Assert.Empty(new QuoteHistoryEntry().TrelloCardId);
    }

    [Fact]
    public void ExpectedIdsAreCanonicalAndRemainParametersRatherThanSql()
    {
        string json = SqlDataService.SerializeTrelloPhotoIds(["photo-b", "photo-a", "photo-b", "quote'\"$1"]);
        Assert.Equal(new[] { "photo-a", "photo-b", "quote'\"$1" }, JsonSerializer.Deserialize<string[]>(json));
        Assert.Equal(json, SqlDataService.SerializeTrelloPhotoIds(["quote'\"$1", "photo-a", "photo-b"]));
        Assert.Equal("[]", SqlDataService.SerializeTrelloPhotoIds([]));
        Assert.Throws<ArgumentException>(() => SqlDataService.SerializeTrelloPhotoIds([""]));
        Assert.Throws<ArgumentException>(() => SqlDataService.SerializeTrelloPhotoIds([new string('x', 65)]));
        Assert.Throws<ArgumentException>(() => SqlDataService.SerializeTrelloPhotoIds(Enumerable.Range(1, 1001).Select(i => i.ToString()).ToArray()));
    }

    [Fact]
    public void CompletedFlagDoesNotMakeAnIncompleteOrMismatchedBatchPrintable()
    {
        var batch = CompleteBatch();
        Assert.True(batch.IsComplete);
        batch.Photos = [];
        Assert.False(batch.IsComplete);
        batch.Photos = [Photo("other-card", "image-a")];
        Assert.False(batch.IsComplete);
        batch.Photos = [Photo("card-a", "other-image")];
        Assert.False(batch.IsComplete);
        var emptyPhoto = Photo("card-a", "image-a"); emptyPhoto.ContentBase64 = "";
        batch.Photos = [emptyPhoto];
        Assert.False(batch.IsComplete);
        batch.Photos = [Photo("card-a", "image-a")];
        batch.Error = "Download incompleto";
        Assert.False(batch.IsComplete);
        batch.Error = ""; batch.Status = "failed";
        Assert.False(batch.IsComplete);
    }

    [Fact]
    public void ZeroPhotosCanBeCompleteButMissingOrMalformedManifestCannot()
    {
        var batch = new TrelloPhotoBatch { CardId = "card-a" };
        Assert.False(batch.IsComplete);
        batch.Status = "completed";
        Assert.True(batch.IsComplete);
        batch.ExpectedAttachmentIdsJson = "not-json";
        Assert.Throws<InvalidDataException>(() => batch.GetExpectedAttachmentIds());
        batch.ExpectedAttachmentIdsJson = "null";
        Assert.Throws<InvalidDataException>(() => batch.GetExpectedAttachmentIds());
        batch.ExpectedAttachmentIdsJson = "[null]";
        Assert.Throws<InvalidDataException>(() => batch.GetExpectedAttachmentIds());
    }

    [Fact]
    public async Task PhotoCacheRetainsBytesAndManualCardIdentityWithoutChangingOtherData()
    {
        using var folder = new TestDirectory();
        var store = new LocalJsonStoreService(folder.Path);
        Assert.Null(await store.LoadTrelloPhotoBatchAsync("card-a"));
        var batch = CompleteBatch(); batch.IsOfflineSnapshot = true;
        await store.SaveTrelloPhotoBatchAsync(batch);
        var restored = await store.LoadTrelloPhotoBatchAsync("card-a");
        Assert.NotNull(restored);
        Assert.True(restored.IsComplete);
        Assert.False(restored.IsOfflineSnapshot);
        Assert.Equal(batch.Photos[0].ContentBase64, Assert.Single(restored.Photos).ContentBase64);
        Assert.Null(await store.LoadTrelloPhotoBatchAsync("card-b"));
        Assert.Empty(await store.LoadCustomersAsync());
        Assert.Empty(await store.LoadHistoryAsync());
    }

    [Fact]
    public async Task FailedOrIncompleteRefreshCannotReplaceTheLastCompletePhotoCache()
    {
        using var folder = new TestDirectory();
        var store = new LocalJsonStoreService(folder.Path);
        await store.SaveTrelloPhotoBatchAsync(CompleteBatch());
        var failed = CompleteBatch(); failed.Status = "failed"; failed.Photos = [];
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveTrelloPhotoBatchAsync(failed));
        var saved = await store.LoadTrelloPhotoBatchAsync("card-a");
        Assert.True(saved!.IsComplete);
        Assert.Single(saved.Photos);
    }

    [Fact]
    public async Task CardIdsCannotEscapeThePhotoCacheDirectory()
    {
        using var folder = new TestDirectory();
        var store = new LocalJsonStoreService(folder.Path);
        var batch = CompleteBatch();
        batch.CardId = "../photos";
        batch.Photos = [Photo(batch.CardId, "image-a")];
        await store.SaveTrelloPhotoBatchAsync(batch);
        var files = Directory.GetFiles(Path.Combine(folder.Path, "TrelloPhotos"), "*.json");
        Assert.Single(files);
        Assert.Matches("^[A-F0-9]{64}\\.json$", Path.GetFileName(files[0]));
        Assert.True((await store.LoadTrelloPhotoBatchAsync(batch.CardId))!.IsComplete);
    }

    private static TrelloPhoto Photo(string cardId = "card-a", string id = "image-a") => new()
    {
        CardId = cardId, AttachmentId = id, FileName = "foto.png", ContentType = "image/png",
        ContentBase64 = Convert.ToBase64String([1, 2, 3]), SourceUrl = "https://trello.com/1/cards/card-a/attachments/image-a/download/foto.png",
        SyncedAtUtc = new DateTime(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc)
    };

    private static TrelloPhotoBatch CompleteBatch() => new()
    {
        CardId = "card-a", Status = "completed", ExpectedAttachmentIdsJson = "[\"image-a\"]",
        RequestedAtUtc = new DateTime(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc),
        CompletedAtUtc = new DateTime(2026, 11, 2, 10, 1, 0, DateTimeKind.Utc), Photos = [Photo()]
    };

    private sealed class TestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EdilPaintTrelloPhotoTest", Guid.NewGuid().ToString("N"));
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
}
