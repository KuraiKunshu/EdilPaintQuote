using System.Data;
using System.Text.Json;
using EdilPaintPreventibiviGen.Data;
using EdilPaintPreventibiviGen.Data.Entities;
using EdilPaintPreventibiviGen.Data.Mappers;
using Microsoft.EntityFrameworkCore;

namespace EdilPaintPreventibiviGen.Services;

public partial class SqlDataService
{
    public Task<AutomaticMaterialSettings> LoadAutomaticMaterialSettingsAsync(AutomaticMaterialSettings? legacy,
        CancellationToken token = default) => ExecuteAutomaticMaterialTransactionAsync(async (db, ct) =>
        {
            var stored = await db.AutomaticMaterialSettings.SingleOrDefaultAsync(x => x.Id == 1, ct).ConfigureAwait(false);
            if (stored == null)
            {
                var defaults = new RealProfitSettingsModel(); defaults.Normalize();
                stored = new() { Id = 1, Revision = 1,
                    SettingsJson = JsonSerializer.Serialize(AutomaticMaterialSettings.FromLocal(defaults)) };
                db.AutomaticMaterialSettings.Add(stored);
            }
            if (!stored.IsConfigured && legacy != null)
            {
                var labors = (await db.LaborCatalog.AsNoTracking().ToListAsync(ct).ConfigureAwait(false)).Select(x => x.ToModel()).ToArray();
                var materials = (await db.PersonalMaterials.AsNoTracking().Where(x => x.IsCompanyMaterial).ToListAsync(ct)
                    .ConfigureAwait(false)).Select(x => x.ToModel()).ToArray();
                var imported = RealProfitAutomaticMaterialBuilder.RebindSettings(legacy, labors, materials,
                    legacy.CatalogIdentity == _appSettings.Database.GetCatalogIdentity());
                foreach (var rule in imported.WindowMaterialRules.Where(x => x.Enabled &&
                    (x.LaborCatalogId == null || x.MaterialCatalogId == null)))
                {
                    rule.Enabled = false;
                    imported.ResolutionWarnings.Add($"La regola per \"{rule.LaborName}\" e \"{rule.MaterialName}\" è stata disattivata durante l'importazione: " +
                        "lavorazione o materiale non riconosciuti in modo univoco. Riselezionali nel catalogo e riattiva la regola.");
                }
                // Persist once; a second PC's old local file must never overwrite shared rules.
                stored.SettingsJson = JsonSerializer.Serialize(imported);
                stored.IsConfigured = true; stored.Revision = checked(stored.Revision + 1);
            }
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return ReadAutomaticMaterialSettings(stored);
        }, token);

    public Task<AutomaticMaterialSettings> SaveAutomaticMaterialSettingsAsync(AutomaticMaterialSettings settings,
        CancellationToken token = default)
    {
        var copy = settings.CreateValidatedCopy();
        return ExecuteAutomaticMaterialTransactionAsync(async (db, ct) =>
        {
            var stored = await db.AutomaticMaterialSettings.SingleOrDefaultAsync(x => x.Id == 1, ct).ConfigureAwait(false);
            if (stored == null || stored.Revision != copy.Revision)
                throw new InvalidOperationException("Le regole dei materiali automatici sono cambiate su un altro PC. Ricaricale prima di salvare.");
            var previous = ReadAutomaticMaterialSettings(stored);
            if (!AutomaticMaterialSettings.SameContent(previous, copy) || !stored.IsConfigured)
            {
                copy.Revision = checked(stored.Revision + 1); copy.CatalogIdentity = "";
                stored.Revision = copy.Revision; stored.IsConfigured = true;
                stored.SettingsJson = JsonSerializer.Serialize(copy);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            return ReadAutomaticMaterialSettings(stored);
        }, token);
    }

    private static AutomaticMaterialSettings ReadAutomaticMaterialSettings(AutomaticMaterialSettingsEntity entity)
    {
        var result = JsonSerializer.Deserialize<AutomaticMaterialSettings>(entity.SettingsJson)?.CreateValidatedCopy()
            ?? throw new InvalidOperationException("Le regole condivise dei materiali non sono valide.");
        result.Revision = entity.Revision; result.CatalogIdentity = "";
        return result;
    }

    private async Task<T> ExecuteAutomaticMaterialTransactionAsync<T>(Func<AppDbContext, CancellationToken, Task<T>> action,
        CancellationToken token)
    {
        await using var strategyContext = AppDbContextFactory.Create();
        return await strategyContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var db = AppDbContextFactory.Create();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
            // Share the planning lock, including schema creation, so two new PCs can initialize safely.
            await WorkScheduleDatabaseLock.AcquireAsync(db, token).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync(db.Database.IsNpgsql() ? """
                CREATE TABLE IF NOT EXISTS "AutomaticMaterialSettings" (
                    "Id" integer PRIMARY KEY, "Revision" bigint NOT NULL,
                    "IsConfigured" boolean NOT NULL DEFAULT false, "SettingsJson" text NOT NULL);
                """ : """
                IF OBJECT_ID(N'[dbo].[AutomaticMaterialSettings]', N'U') IS NULL
                    CREATE TABLE [dbo].[AutomaticMaterialSettings] (
                        [Id] int NOT NULL PRIMARY KEY, [Revision] bigint NOT NULL,
                        [IsConfigured] bit NOT NULL DEFAULT 0, [SettingsJson] nvarchar(max) NOT NULL);
                """, token).ConfigureAwait(false);
            var result = await action(db, token).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return result;
        }).ConfigureAwait(false);
    }
}
