using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using System.Text.Json;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class LocalDraftServiceTests
{
    [Fact]
    public async Task SaveIfChangedIgnoresVolatileDatesAndDetectsAttachmentChanges()
    {
        string temporaryPath = Path.Combine(
            Path.GetTempPath(),
            "EdilPaintPreventivi.Tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var service = new LocalDraftService(temporaryPath);
            Assert.True(await service.SaveIfChangedAsync(CreateDraft([1, 2, 3], DateTime.UtcNow)));

            Assert.False(await service.SaveIfChangedAsync(
                CreateDraft([1, 2, 3], DateTime.UtcNow.AddHours(1))));

            var databaseEnrichedDraft = CreateDraft([1, 2, 3], DateTime.UtcNow.AddHours(1));
            databaseEnrichedDraft.BaseRevision = 4;
            databaseEnrichedDraft.Revision = 4;
            databaseEnrichedDraft.IsEditingExistingQuoteDraft = true;
            databaseEnrichedDraft.IsDraftQuoteNumberAllocated = true;
            databaseEnrichedDraft.WasCreatedByDraftAutosave = true;
            databaseEnrichedDraft.SharedDraftContentHash = "hash-confermato";
            databaseEnrichedDraft.Events.Add(new QuoteEventEntry
            {
                EventType = "bozza",
                Description = "Metadati database"
            });
            Assert.True(await service.SaveIfChangedAsync(
                databaseEnrichedDraft,
                forceWrite: true));

            var nextEditorSnapshot = CreateDraft([1, 2, 3], DateTime.UtcNow.AddHours(2));
            nextEditorSnapshot.IsDraftQuoteNumberAllocated = true;
            Assert.False(await service.SaveIfChangedAsync(nextEditorSnapshot));
            var restored = Assert.IsType<QuoteHistoryEntry>(await service.LoadAsync());
            Assert.Equal(4, restored.BaseRevision);
            Assert.Single(restored.Events);
            Assert.True(restored.WasCreatedByDraftAutosave);
            Assert.Equal("hash-confermato", restored.SharedDraftContentHash);

            Assert.True(await service.SaveIfChangedAsync(
                CreateDraft([1, 2, 4], DateTime.UtcNow.AddHours(3))));
            Assert.Single(Directory.GetFiles(
                Path.Combine(temporaryPath, "Drafts", "Content"),
                "*.bin"));
        }
        finally
        {
            if (Directory.Exists(temporaryPath))
                Directory.Delete(temporaryPath, recursive: true);
        }
    }

    [Fact]
    public async Task SaveIfChangedDetectsAChangeBetweenCustomersWithTheSameName()
    {
        string temporaryPath = Path.Combine(
            Path.GetTempPath(),
            "EdilPaintPreventivi.Tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var service = new LocalDraftService(temporaryPath);
            var firstCustomerDraft = CreateDraft([], DateTime.UtcNow);
            firstCustomerDraft.CustomerName = "Cliente omonimo";
            firstCustomerDraft.CustomerSyncId = Guid.NewGuid();
            Assert.True(await service.SaveIfChangedAsync(firstCustomerDraft));

            var secondCustomerDraft = CreateDraft([], DateTime.UtcNow.AddMinutes(1));
            secondCustomerDraft.CustomerName = "Cliente omonimo";
            secondCustomerDraft.CustomerSyncId = Guid.NewGuid();
            Assert.True(await service.SaveIfChangedAsync(secondCustomerDraft));

            var restored = Assert.IsType<QuoteHistoryEntry>(await service.LoadAsync());
            Assert.Equal(secondCustomerDraft.CustomerSyncId, restored.CustomerSyncId);
        }
        finally
        {
            if (Directory.Exists(temporaryPath))
                Directory.Delete(temporaryPath, recursive: true);
        }
    }

    [Fact]
    public async Task TextOnlyChangeDoesNotRewriteContentBlobAndLoadRestoresBytes()
    {
        string temporaryPath = Path.Combine(
            Path.GetTempPath(),
            "EdilPaintPreventivi.Tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            byte[] content = new byte[256 * 1024];
            for (int index = 0; index < content.Length; index++)
                content[index] = (byte)(index % 251);

            var service = new LocalDraftService(temporaryPath);
            var first = CreateDraft(content, DateTime.UtcNow);
            first.CustomerName = "Cliente iniziale";
            Assert.True(await service.SaveIfChangedAsync(first));

            string contentDirectory = Path.Combine(temporaryPath, "Drafts", "Content");
            string blobPath = Assert.Single(Directory.GetFiles(contentDirectory, "*.bin"));
            DateTime marker = new(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(blobPath, marker);

            var textOnlyChange = CreateDraft(content, DateTime.UtcNow.AddMinutes(1));
            textOnlyChange.CustomerName = "Cliente modificato";
            Assert.True(await service.SaveIfChangedAsync(textOnlyChange));

            Assert.Equal(marker, File.GetLastWriteTimeUtc(blobPath));
            var restored = Assert.IsType<QuoteHistoryEntry>(await service.LoadAsync());
            Assert.Equal(content, Assert.Single(restored.Attachments).Content);

            string envelopePath = Path.Combine(temporaryPath, "Drafts", "current-draft.json");
            string envelopeJson = await File.ReadAllTextAsync(envelopePath);
            Assert.DoesNotContain(Convert.ToBase64String(content), envelopeJson, StringComparison.Ordinal);
            Assert.True(new FileInfo(envelopePath).Length < 32 * 1024);

            string siblingFile = Path.Combine(temporaryPath, "Drafts", "keep-me.txt");
            await File.WriteAllTextAsync(siblingFile, "sentinella");
            await service.DeleteAsync();
            Assert.False(Directory.Exists(contentDirectory));
            Assert.True(File.Exists(siblingFile));
        }
        finally
        {
            if (Directory.Exists(temporaryPath))
                Directory.Delete(temporaryPath, recursive: true);
        }
    }

    [Fact]
    public async Task LegacyRawDraftWithBase64ContentLoadsAndMigratesOnNextSave()
    {
        string temporaryPath = Path.Combine(
            Path.GetTempPath(),
            "EdilPaintPreventivi.Tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            byte[] legacyContent = [7, 8, 9, 10];
            string draftDirectory = Path.Combine(temporaryPath, "Drafts");
            Directory.CreateDirectory(draftDirectory);
            string draftPath = Path.Combine(draftDirectory, "current-draft.json");
            string legacyJson = $$"""
                {
                  "QuoteNumber": "LEGACY-1",
                  "CustomerName": "Cliente legacy",
                  "Attachments": [
                    {
                      "FileName": "legacy.bin",
                      "ContentType": "application/octet-stream",
                      "Content": "{{Convert.ToBase64String(legacyContent)}}",
                      "ImportedAt": "2020-01-02T03:04:05Z"
                    }
                  ]
                }
                """;
            await File.WriteAllTextAsync(draftPath, legacyJson);

            var service = new LocalDraftService(temporaryPath);
            var legacyDraft = Assert.IsType<QuoteHistoryEntry>(await service.LoadAsync());
            Assert.Equal(legacyContent, Assert.Single(legacyDraft.Attachments).Content);

            Assert.True(await service.SaveIfChangedAsync(legacyDraft));
            using var envelope = JsonDocument.Parse(await File.ReadAllTextAsync(draftPath));
            Assert.Equal(1, envelope.RootElement.GetProperty("Version").GetInt32());
            Assert.True(envelope.RootElement.TryGetProperty("Draft", out _));

            var migrated = Assert.IsType<QuoteHistoryEntry>(await service.LoadAsync());
            Assert.Equal(legacyContent, Assert.Single(migrated.Attachments).Content);
        }
        finally
        {
            if (Directory.Exists(temporaryPath))
                Directory.Delete(temporaryPath, recursive: true);
        }
    }

    private static QuoteHistoryEntry CreateDraft(byte[] attachmentContent, DateTime timestamp) => new()
    {
        QuoteNumber = "BOZZA-TEST",
        Date = timestamp,
        LastModifiedUtc = timestamp,
        Status = QuoteStatus.Bozza,
        Attachments =
        [
            new StoredFile
            {
                FileName = "foto.jpg",
                ContentType = "image/jpeg",
                Content = attachmentContent,
                ImportedAt = timestamp
            }
        ]
    };
}
