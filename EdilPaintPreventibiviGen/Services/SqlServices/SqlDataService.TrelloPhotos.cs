using System.Text.Json;
using EdilPaintPreventibiviGen.Data;
using EdilPaintPreventibiviGen.Models;
using Microsoft.EntityFrameworkCore;

namespace EdilPaintPreventibiviGen.Services;

public partial class SqlDataService
{
    public async Task<TrelloPhotoBatch> GetTrelloPhotosAsync(string cardId, CancellationToken cancellationToken = default)
    {
        ValidateTrelloPhotoCardId(cardId);
        await using var db = AppDbContextFactory.Create();
        var request = await db.TrelloPhotoRequests.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CardId == cardId, cancellationToken).ConfigureAwait(false);
        if (request == null) return new TrelloPhotoBatch { CardId = cardId };
        var batch = new TrelloPhotoBatch
        {
            CardId = request.CardId,
            Status = request.Status,
            Error = request.Error,
            RequestedAtUtc = request.RequestedAtUtc,
            StartedAtUtc = request.StartedAtUtc,
            CompletedAtUtc = request.CompletedAtUtc,
            ExpectedAttachmentIdsJson = request.ExpectedAttachmentIdsJson
        };
        string[] expectedIds = batch.GetExpectedAttachmentIds();
        batch.Photos = expectedIds.Length == 0 ? [] : await db.TrelloPhotos.AsNoTracking()
            .Where(x => x.CardId == cardId && expectedIds.Contains(x.AttachmentId))
            .OrderBy(x => x.AttachmentId).ToListAsync(cancellationToken).ConfigureAwait(false);
        return batch;
    }

    public async Task RequestTrelloPhotosAsync(string cardId, IReadOnlyList<string> expectedAttachmentIds,
        CancellationToken cancellationToken = default)
    {
        ValidateTrelloPhotoCardId(cardId);
        string idsJson = SerializeTrelloPhotoIds(expectedAttachmentIds);
        bool empty = expectedAttachmentIds.Count == 0;
        DateTime now = DateTime.UtcNow;
        string nextStatus = empty ? "completed" : "pending";
        DateTime? completedAt = empty ? now : null;
        await using var db = AppDbContextFactory.Create();
        // A photo request never establishes a quote association. An operator must already have saved it.
        if (!await db.Quotes.AnyAsync(q => q.TrelloCardId == cardId, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Le fotografie si possono richiedere solo per una scheda già associata manualmente a un preventivo salvato.");

        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "TrelloPhotoRequests" AS saved
                    ("CardId", "RequestedAtUtc", "StartedAtUtc", "CompletedAtUtc", "Status", "Error", "ExpectedAttachmentIdsJson")
                SELECT {cardId}, {now}, NULL, CAST({completedAt} AS timestamptz), {nextStatus}, '', {idsJson}
                WHERE EXISTS (SELECT 1 FROM "Quotes" WHERE "TrelloCardId" = {cardId} AND "IsDeleted" = false)
                ON CONFLICT ("CardId") DO UPDATE SET
                    "RequestedAtUtc" = EXCLUDED."RequestedAtUtc", "StartedAtUtc" = NULL,
                    "CompletedAtUtc" = EXCLUDED."CompletedAtUtc", "Status" = EXCLUDED."Status",
                    "Error" = '', "ExpectedAttachmentIdsJson" = EXCLUDED."ExpectedAttachmentIdsJson"
                WHERE saved."ExpectedAttachmentIdsJson" <> EXCLUDED."ExpectedAttachmentIdsJson"
                    OR saved."Status" NOT IN ('pending', 'processing', 'completed')
                    OR (saved."Status" = 'completed' AND EXISTS (
                        SELECT 1 FROM jsonb_array_elements_text(CAST({idsJson} AS jsonb)) AS expected(id)
                        WHERE NOT EXISTS (SELECT 1 FROM "TrelloPhotos" AS photo
                            WHERE photo."CardId" = {cardId} AND photo."AttachmentId" = expected.id
                            AND btrim(photo."ContentBase64") <> '')));
                """, cancellationToken).ConfigureAwait(false);
        }
        else if (db.Database.IsSqlServer())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                MERGE [dbo].[TrelloPhotoRequests] WITH (HOLDLOCK) AS saved
                USING (SELECT {cardId} AS CardId
                    WHERE EXISTS (SELECT 1 FROM [dbo].[Quotes] WHERE [TrelloCardId] = {cardId} AND [IsDeleted] = 0)) AS incoming
                ON saved.[CardId] = incoming.CardId
                WHEN MATCHED AND (saved.[ExpectedAttachmentIdsJson] <> {idsJson}
                    OR saved.[Status] NOT IN ('pending', 'processing', 'completed')
                    OR (saved.[Status] = 'completed' AND EXISTS (
                        SELECT 1 FROM OPENJSON({idsJson}) AS expected
                        WHERE NOT EXISTS (SELECT 1 FROM [dbo].[TrelloPhotos] AS photo
                            WHERE photo.[CardId] = {cardId} AND photo.[AttachmentId] = expected.[value]
                            AND LTRIM(RTRIM(photo.[ContentBase64])) <> ''))))
                THEN UPDATE SET [RequestedAtUtc] = {now}, [StartedAtUtc] = NULL,
                    [CompletedAtUtc] = {completedAt}, [Status] = {nextStatus}, [Error] = '', [ExpectedAttachmentIdsJson] = {idsJson}
                WHEN NOT MATCHED THEN INSERT
                    ([CardId], [RequestedAtUtc], [StartedAtUtc], [CompletedAtUtc], [Status], [Error], [ExpectedAttachmentIdsJson])
                    VALUES ({cardId}, {now}, NULL, {completedAt}, {nextStatus}, '', {idsJson});
                """, cancellationToken).ConfigureAwait(false);
        }
        else throw new NotSupportedException("Il database configurato non supporta la richiesta delle fotografie Trello.");
    }

    internal static string SerializeTrelloPhotoIds(IReadOnlyList<string> attachmentIds)
    {
        ArgumentNullException.ThrowIfNull(attachmentIds);
        if (attachmentIds.Count > 1000 || attachmentIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 64))
            throw new ArgumentException("Elenco delle fotografie Trello non valido.", nameof(attachmentIds));
        return JsonSerializer.Serialize(attachmentIds.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal));
    }

    internal static void ValidateTrelloPhotoCardId(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId) || cardId.Length > 64)
            throw new ArgumentException("Selezionare una scheda Trello valida.", nameof(cardId));
    }

    private static async Task EnsureTrelloPhotosSchemaAsync(AppDbContext db, CancellationToken token)
    {
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "TrelloPhotos" (
                    "CardId" varchar(64) NOT NULL, "AttachmentId" varchar(64) NOT NULL,
                    "FileName" text NOT NULL DEFAULT '', "ContentType" varchar(100) NOT NULL DEFAULT '',
                    "ContentBase64" text NOT NULL DEFAULT '', "SourceUrl" text NOT NULL DEFAULT '',
                    "SyncedAtUtc" timestamptz NOT NULL,
                    PRIMARY KEY ("CardId", "AttachmentId")
                );
                CREATE TABLE IF NOT EXISTS "TrelloPhotoRequests" (
                    "CardId" varchar(64) PRIMARY KEY, "RequestedAtUtc" timestamptz NOT NULL,
                    "StartedAtUtc" timestamptz NULL, "CompletedAtUtc" timestamptz NULL,
                    "Status" varchar(20) NOT NULL DEFAULT 'pending', "Error" text NOT NULL DEFAULT '',
                    "ExpectedAttachmentIdsJson" text NOT NULL DEFAULT '[]'
                );
                CREATE INDEX IF NOT EXISTS "IX_TrelloPhotoRequests_Status_RequestedAtUtc"
                    ON "TrelloPhotoRequests" ("Status", "RequestedAtUtc");
                """, token).ConfigureAwait(false);
        else if (db.Database.IsSqlServer())
            await db.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'[dbo].[TrelloPhotos]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[TrelloPhotos] (
                        [CardId] nvarchar(64) NOT NULL, [AttachmentId] nvarchar(64) NOT NULL,
                        [FileName] nvarchar(max) NOT NULL DEFAULT '', [ContentType] nvarchar(100) NOT NULL DEFAULT '',
                        [ContentBase64] nvarchar(max) NOT NULL DEFAULT '', [SourceUrl] nvarchar(max) NOT NULL DEFAULT '',
                        [SyncedAtUtc] datetime2 NOT NULL,
                        CONSTRAINT [PK_TrelloPhotos] PRIMARY KEY ([CardId], [AttachmentId])
                    );
                END;
                IF OBJECT_ID(N'[dbo].[TrelloPhotoRequests]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[TrelloPhotoRequests] (
                        [CardId] nvarchar(64) NOT NULL PRIMARY KEY, [RequestedAtUtc] datetime2 NOT NULL,
                        [StartedAtUtc] datetime2 NULL, [CompletedAtUtc] datetime2 NULL,
                        [Status] nvarchar(20) NOT NULL DEFAULT 'pending', [Error] nvarchar(max) NOT NULL DEFAULT '',
                        [ExpectedAttachmentIdsJson] nvarchar(max) NOT NULL DEFAULT '[]'
                    );
                    CREATE INDEX [IX_TrelloPhotoRequests_Status_RequestedAtUtc]
                        ON [dbo].[TrelloPhotoRequests] ([Status], [RequestedAtUtc]);
                END;
                """, token).ConfigureAwait(false);
    }
}
