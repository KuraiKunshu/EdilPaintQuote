using EdilPaintPreventibiviGen.Data;
using EdilPaintPreventibiviGen.Data.Mappers;
using EdilPaintPreventibiviGen.Models;
using Microsoft.EntityFrameworkCore;

namespace EdilPaintPreventibiviGen.Services;

public partial class SqlDataService
{
    public async Task UpdateQuoteTrelloAssociationAsync(string quoteNumber, string trelloCardId,
        string trelloCardSnapshotJson, CancellationToken cancellationToken = default)
    {
        _ = await UpdateQuoteTrelloAssociationCoreAsync(quoteNumber, trelloCardId,
            trelloCardSnapshotJson, null, cancellationToken).ConfigureAwait(false);
    }

    public Task<QuoteMetadata> UpdateQuoteTrelloAssociationAsync(string quoteNumber, string trelloCardId,
        string trelloCardSnapshotJson, long expectedRevision, CancellationToken cancellationToken = default) =>
        UpdateQuoteTrelloAssociationCoreAsync(quoteNumber, trelloCardId, trelloCardSnapshotJson,
            expectedRevision, cancellationToken);

    private async Task<QuoteMetadata> UpdateQuoteTrelloAssociationCoreAsync(string quoteNumber, string trelloCardId,
        string trelloCardSnapshotJson, long? expectedRevision, CancellationToken cancellationToken)
    {
        ValidateTrelloAssociation(trelloCardId, trelloCardSnapshotJson);
        await using var db = AppDbContextFactory.Create();
        var quote = await db.Quotes.Include(x => x.Customer).Include(x => x.ReferenceCustomer)
            .Include(x => x.BillingCustomer).Include(x => x.Materials).Include(x => x.Labors)
            .AsSplitQuery().SingleOrDefaultAsync(x => x.QuoteNumber == quoteNumber, cancellationToken);
        if (quote == null) throw new InvalidOperationException($"Preventivo {quoteNumber} non trovato.");
        if (expectedRevision.HasValue && quote.Revision != expectedRevision.Value)
            throw new DbUpdateConcurrencyException("Il preventivo è cambiato su un'altra postazione. Riaprilo prima di modificare il collegamento Trello.");
        quote.TrelloCardId = trelloCardId;
        quote.TrelloCardSnapshotJson = trelloCardSnapshotJson;
        quote.LastModifiedByDevice = DeviceNameService.GetCurrentDeviceName();
        AppendQuoteEvent(quote, "trello", string.IsNullOrEmpty(trelloCardId)
            ? "Associazione Trello rimossa manualmente" : "Scheda Trello associata manualmente");
        quote.LastModifiedUtc = DateTime.UtcNow;
        quote.Revision += 1;
        quote.SyncHash = QuoteSyncHashService.Compute(quote.ToModel());
        // One atomic write changes only the association and its revision/audit metadata.
        // EF's revision concurrency check rejects an intervening edit from another workstation.
        await db.SaveChangesAsync(cancellationToken);
        return new QuoteMetadata
        {
            QuoteNumber = quote.QuoteNumber,
            Revision = quote.Revision,
            LastModifiedUtc = quote.LastModifiedUtc,
            SyncHash = quote.SyncHash
        };
    }

    internal static void ValidateTrelloAssociation(string cardId, string snapshotJson)
    {
        if (string.IsNullOrEmpty(cardId))
        {
            if (!string.IsNullOrEmpty(snapshotJson))
                throw new ArgumentException("Rimuovendo il collegamento Trello va rimossa anche la sua copia interna.");
            return;
        }
        var snapshot = TrelloCard.FromSnapshot(snapshotJson);
        if (cardId.Length > 64 || snapshot == null || !string.Equals(snapshot.Id, cardId, StringComparison.Ordinal))
            throw new ArgumentException("La scheda Trello selezionata e la sua copia interna non corrispondono.");
    }

    public async Task<List<TrelloCard>> GetTrelloCardsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = AppDbContextFactory.Create();
        return await db.TrelloCards.AsNoTracking()
            .OrderBy(x => x.IsArchived).ThenBy(x => x.BoardName).ThenBy(x => x.ListName).ThenBy(x => x.Name)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TrelloCard?> GetTrelloCardAsync(string cardId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cardId)) return null;
        await using var db = AppDbContextFactory.Create();
        return await db.TrelloCards.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == cardId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureTrelloSchemaAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "TrelloCards" (
                    "Id" character varying(64) PRIMARY KEY,
                    "BoardId" character varying(64) NOT NULL DEFAULT '',
                    "BoardName" text NOT NULL DEFAULT '',
                    "ListId" character varying(64) NOT NULL DEFAULT '',
                    "ListName" text NOT NULL DEFAULT '',
                    "Name" text NOT NULL DEFAULT '',
                    "Description" text NOT NULL DEFAULT '',
                    "Url" text NOT NULL DEFAULT '',
                    "IsArchived" boolean NOT NULL DEFAULT false,
                    "IsDeleted" boolean NOT NULL DEFAULT false,
                    "DueUtc" timestamp with time zone NULL,
                    "LastActivityUtc" timestamp with time zone NULL,
                    "SyncedAtUtc" timestamp with time zone NOT NULL,
                    "PayloadJson" text NOT NULL DEFAULT ''
                );
                CREATE INDEX IF NOT EXISTS "IX_TrelloCards_BoardId" ON "TrelloCards" ("BoardId");
                ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "TrelloCardId" character varying(64) NOT NULL DEFAULT '';
                ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "TrelloCardSnapshotJson" text NOT NULL DEFAULT '';
                """, cancellationToken);
        }
        else if (db.Database.IsSqlServer())
        {
            await db.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'[dbo].[TrelloCards]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[TrelloCards] (
                        [Id] nvarchar(64) NOT NULL PRIMARY KEY,
                        [BoardId] nvarchar(64) NOT NULL DEFAULT '',
                        [BoardName] nvarchar(max) NOT NULL DEFAULT '',
                        [ListId] nvarchar(64) NOT NULL DEFAULT '',
                        [ListName] nvarchar(max) NOT NULL DEFAULT '',
                        [Name] nvarchar(max) NOT NULL DEFAULT '',
                        [Description] nvarchar(max) NOT NULL DEFAULT '',
                        [Url] nvarchar(max) NOT NULL DEFAULT '',
                        [IsArchived] bit NOT NULL DEFAULT 0,
                        [IsDeleted] bit NOT NULL DEFAULT 0,
                        [DueUtc] datetime2 NULL,
                        [LastActivityUtc] datetime2 NULL,
                        [SyncedAtUtc] datetime2 NOT NULL,
                        [PayloadJson] nvarchar(max) NOT NULL DEFAULT ''
                    );
                    CREATE INDEX [IX_TrelloCards_BoardId] ON [dbo].[TrelloCards]([BoardId]);
                END;
                """, cancellationToken);
            await EnsureColumnAsync(db, "Quotes", "TrelloCardId", "NVARCHAR(64) NOT NULL DEFAULT ''", cancellationToken);
            await EnsureColumnAsync(db, "Quotes", "TrelloCardSnapshotJson", "NVARCHAR(MAX) NOT NULL DEFAULT ''", cancellationToken);
        }
    }
}
