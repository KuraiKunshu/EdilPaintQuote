using System.Text.Json;
using EdilPaintPreventibiviGen.Data;
using EdilPaintPreventibiviGen.Data.Entities;
using EdilPaintPreventibiviGen.Models;
using Microsoft.EntityFrameworkCore;

namespace EdilPaintPreventibiviGen.Services;

public partial class SqlDataService
{
    private bool _posSchemaReady;
    private async Task EnsurePosSchemaAsync(AppDbContext db, CancellationToken token)
    {
        if (Volatile.Read(ref _posSchemaReady)) return;
        await db.Database.ExecuteSqlRawAsync(BuildPosSchemaSql(db.Database.IsNpgsql()), token).ConfigureAwait(false);
        Volatile.Write(ref _posSchemaReady, true);
    }

    public async Task<List<PosWorkItem>> LoadPosTemplatesAsync(CancellationToken token = default)
    {
        await using var db = AppDbContextFactory.Create();
        await EnsurePosSchemaAsync(db, token).ConfigureAwait(false);
        var rows = await db.PosTemplates.AsNoTracking().Where(x => !x.IsArchived).OrderBy(x => x.Name).ToListAsync(token).ConfigureAwait(false);
        return rows.Select(TemplateModel).ToList();
    }

    public async Task<PosWorkItem> SavePosTemplateAsync(PosWorkItem template, CancellationToken token = default)
    {
        var requested = PosDraftRules.ValidateTemplate(template);
        return await PosWriteAsync(async (db, ct) =>
        {
            var row = await db.PosTemplates.SingleOrDefaultAsync(x => x.Id == requested.Id, ct).ConfigureAwait(false);
            PosDraftRules.RequireRevision(requested.Revision, row?.Revision);
            if (row != null && TemplateModel(row).Kind != requested.Kind)
                throw new InvalidOperationException("Una scheda lavorazione non può diventare una scheda attrezzatura, o viceversa. Crea una nuova scheda.");
            if (row == null) { row = new() { Id = requested.Id }; db.PosTemplates.Add(row); }
            var saved = PosDraftRules.Copy(requested);
            saved.Revision = checked(row.Revision + 1);
            row.Name = saved.Name; row.IsArchived = saved.IsArchived;
            row.Revision = saved.Revision; row.PayloadJson = JsonSerializer.Serialize(saved);
            return saved;
        }, token).ConfigureAwait(false);
    }

    public async Task<PosDraft?> LoadPosDraftAsync(string quoteNumber, CancellationToken token = default)
    {
        await using var db = AppDbContextFactory.Create();
        await EnsurePosSchemaAsync(db, token).ConfigureAwait(false);
        var row = await db.PosDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.QuoteNumber == quoteNumber.Trim(), token).ConfigureAwait(false);
        if (row == null) return null;
        var draft = JsonSerializer.Deserialize<PosDraft>(row.PayloadJson)
            ?? throw new InvalidOperationException("La bozza POS salvata non è leggibile.");
        draft.Id = row.Id; draft.Revision = row.Revision; draft.QuoteNumber = row.QuoteNumber;
        draft.SavedAtUtc = row.SavedAtUtc.Kind == DateTimeKind.Local ? row.SavedAtUtc.ToUniversalTime() :
            DateTime.SpecifyKind(row.SavedAtUtc, DateTimeKind.Utc);
        return PosDraftRules.ValidateDraft(draft);
    }

    public async Task<PosDraft> SavePosDraftAsync(PosDraft draft, CancellationToken token = default)
    {
        var requested = PosDraftRules.ValidateDraft(draft);
        return await PosWriteAsync(async (db, ct) =>
        {
            var row = await db.PosDrafts.SingleOrDefaultAsync(x => x.QuoteNumber == requested.QuoteNumber, ct).ConfigureAwait(false);
            PosDraftRules.RequireRevision(requested.Revision, row?.Revision);
            if (row != null && row.Id != requested.Id) throw PosDraftRules.Conflict();
            if (row == null)
            {
                if (!await db.Quotes.AnyAsync(x => x.QuoteNumber == requested.QuoteNumber, ct).ConfigureAwait(false))
                    throw new InvalidOperationException("Salva e sincronizza prima il preventivo, poi crea la sua bozza POS.");
                row = new() { Id = requested.Id, QuoteNumber = requested.QuoteNumber }; db.PosDrafts.Add(row);
            }
            var saved = PosDraftRules.Copy(requested);
            saved.Revision = checked(row.Revision + 1); saved.SavedAtUtc = DateTime.UtcNow;
            row.Revision = saved.Revision; row.SavedAtUtc = saved.SavedAtUtc.Value;
            row.PayloadJson = JsonSerializer.Serialize(saved);
            return saved;
        }, token).ConfigureAwait(false);
    }

    private async Task<T> PosWriteAsync<T>(Func<AppDbContext, CancellationToken, Task<T>> apply, CancellationToken token)
    {
        await using var strategyDb = AppDbContextFactory.Create();
        await EnsurePosSchemaAsync(strategyDb, token).ConfigureAwait(false);
        try
        {
            return await strategyDb.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var db = AppDbContextFactory.Create();
                await using var transaction = await db.Database.BeginTransactionAsync(token).ConfigureAwait(false);
                var result = await apply(db, token).ConfigureAwait(false);
                await db.SaveChangesAsync(token).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false);
                return result;
            }).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex) { throw PosDraftRules.Conflict(ex); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" } ||
            ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 }) { throw PosDraftRules.Conflict(ex); }
    }

    private static PosWorkItem TemplateModel(PosTemplateEntity row)
    {
        var model = JsonSerializer.Deserialize<PosWorkItem>(row.PayloadJson)
            ?? throw new InvalidOperationException("Il testo standard POS non è leggibile.");
        model.Id = row.Id; model.Revision = row.Revision; model.IsArchived = row.IsArchived; model.Name = row.Name;
        return model;
    }

    internal static string BuildPosSchemaSql(bool postgres) => postgres ? """
        BEGIN;
        SELECT pg_advisory_xact_lock(hashtext('EdilPaint.PosSchema'));
        CREATE TABLE IF NOT EXISTS "PosTemplates" (
            "Id" uuid NOT NULL PRIMARY KEY, "Name" varchar(250) NOT NULL,
            "Revision" bigint NOT NULL, "IsArchived" boolean NOT NULL, "PayloadJson" text NOT NULL);
        CREATE TABLE IF NOT EXISTS "PosDrafts" (
            "Id" uuid NOT NULL PRIMARY KEY, "QuoteNumber" varchar(250) NOT NULL,
            "Revision" bigint NOT NULL, "SavedAtUtc" timestamp with time zone NOT NULL, "PayloadJson" text NOT NULL);
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_PosDrafts_QuoteNumber" ON "PosDrafts" ("QuoteNumber");
        COMMIT;
        """ : """
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;
        DECLARE @lockResult int;
        EXEC @lockResult = sp_getapplock @Resource = 'EdilPaint.PosSchema', @LockMode = 'Exclusive', @LockOwner = 'Transaction';
        IF @lockResult < 0
        BEGIN
            ROLLBACK TRANSACTION;
            THROW 50001, 'Impossibile preparare il catalogo e le bozze POS.', 1;
        END;
        IF OBJECT_ID(N'[dbo].[PosTemplates]', N'U') IS NULL
            CREATE TABLE [dbo].[PosTemplates] (
                [Id] uniqueidentifier NOT NULL PRIMARY KEY, [Name] nvarchar(250) NOT NULL,
                [Revision] bigint NOT NULL, [IsArchived] bit NOT NULL, [PayloadJson] nvarchar(max) NOT NULL);
        IF OBJECT_ID(N'[dbo].[PosDrafts]', N'U') IS NULL
            CREATE TABLE [dbo].[PosDrafts] (
                [Id] uniqueidentifier NOT NULL PRIMARY KEY, [QuoteNumber] nvarchar(250) NOT NULL,
                [Revision] bigint NOT NULL, [SavedAtUtc] datetime2 NOT NULL, [PayloadJson] nvarchar(max) NOT NULL);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PosDrafts_QuoteNumber' AND object_id = OBJECT_ID(N'[dbo].[PosDrafts]'))
            CREATE UNIQUE INDEX [IX_PosDrafts_QuoteNumber] ON [dbo].[PosDrafts] ([QuoteNumber]);
        COMMIT TRANSACTION;
        """;
}
