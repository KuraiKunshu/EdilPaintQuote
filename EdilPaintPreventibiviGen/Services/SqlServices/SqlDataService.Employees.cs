using EdilPaintPreventibiviGen.Data;
using EdilPaintPreventibiviGen.Models;
using Microsoft.EntityFrameworkCore;

namespace EdilPaintPreventibiviGen.Services;

public partial class SqlDataService
{
    private bool _employeeSchemaReady;

    private async Task EnsureEmployeeSchemaAsync(AppDbContext db, CancellationToken token)
    {
        if (Volatile.Read(ref _employeeSchemaReady)) return;
        // Also runs after an offline startup: reconnecting alone does not execute
        // FallbackDataService's complete startup schema initialization again.
        await db.Database.ExecuteSqlRawAsync(BuildEmployeeSchemaSql(db.Database.IsNpgsql()), token).ConfigureAwait(false);
        Volatile.Write(ref _employeeSchemaReady, true);
    }

    public async Task<List<EmployeeSettingsModel>> GetEmployeesAsync(CancellationToken token = default)
    {
        await using var db = AppDbContextFactory.Create();
        await EnsureEmployeeSchemaAsync(db, token).ConfigureAwait(false);
        var entities = await db.Employees.AsNoTracking().Where(x => !x.IsDeleted)
            .OrderBy(x => x.FirstName).ThenBy(x => x.LastName).ToListAsync(token).ConfigureAwait(false);
        return entities.Select(EmployeeChanges.ToModel).ToList();
    }

    public async Task ImportLegacyEmployeesAsync(IReadOnlyList<EmployeeSettingsModel> employees,
        CancellationToken token = default)
    {
        if (employees.Count == 0) return;
        await using var strategyDb = AppDbContextFactory.Create();
        await EnsureEmployeeSchemaAsync(strategyDb, token).ConfigureAwait(false);
        await strategyDb.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var db = AppDbContextFactory.Create();
            await using var transaction = await db.Database.BeginTransactionAsync(token).ConfigureAwait(false);
            await WorkScheduleDatabaseLock.AcquireAsync(db, token).ConfigureAwait(false);
            var stored = await db.Employees.AsNoTracking().ToListAsync(token).ConfigureAwait(false);
            var imports = EmployeeChanges.LegacyImports(employees, stored);
            db.Employees.AddRange(imports);
            await db.SaveChangesAsync(token).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task SaveEmployeesAsync(IReadOnlyList<EmployeeSettingsModel> original,
        IReadOnlyList<EmployeeSettingsModel> edited, CancellationToken token = default)
    {
        var changes = EmployeeChanges.Between(original, edited);
        if (changes.Count == 0) return;
        await using var strategyDb = AppDbContextFactory.Create();
        await EnsureEmployeeSchemaAsync(strategyDb, token).ConfigureAwait(false);
        try
        {
            await strategyDb.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var db = AppDbContextFactory.Create();
                await using var transaction = await db.Database.BeginTransactionAsync(token).ConfigureAwait(false);
                await WorkScheduleDatabaseLock.AcquireAsync(db, token).ConfigureAwait(false);
                // Read every active row so aliases added by another PC are checked,
                // while the change set still writes only rows edited on this PC.
                var stored = await db.Employees.ToDictionaryAsync(x => x.Id, token).ConfigureAwait(false);
                db.Employees.AddRange(EmployeeChanges.Apply(changes, stored));
                // Release all edited aliases together before assigning their final
                // values, allowing two employees to exchange aliases atomically.
                var aliases = changes.Where(x => x.Updated != null).ToDictionary(x => x.Id, x => x.Updated!.Abbreviation);
                foreach (var id in aliases.Keys)
                    db.Employees.Local.Single(x => x.Id == id).Abbreviation = string.Empty;
                await db.SaveChangesAsync(token).ConfigureAwait(false);
                foreach (var (id, alias) in aliases)
                    db.Employees.Local.Single(x => x.Id == id).Abbreviation = alias;
                await db.SaveChangesAsync(token).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw EmployeeChanges.Conflict(ex);
        }
    }

    internal static string BuildEmployeeSchemaSql(bool postgres) => postgres ? """
        BEGIN;
        SELECT pg_advisory_xact_lock(hashtext('EdilPaint.EmployeesSchema'));
        CREATE TABLE IF NOT EXISTS "Employees" (
            "Id" uuid NOT NULL PRIMARY KEY,
            "FirstName" varchar(250) NOT NULL,
            "LastName" varchar(250) NOT NULL,
            "Abbreviation" varchar(24) NOT NULL DEFAULT '',
            "Revision" bigint NOT NULL,
            "IsDeleted" boolean NOT NULL
        );
        ALTER TABLE "Employees" ADD COLUMN IF NOT EXISTS "Abbreviation" varchar(24) NOT NULL DEFAULT '';
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_Employees_Abbreviation" ON "Employees" ("Abbreviation")
            WHERE "IsDeleted" = false AND "Abbreviation" <> '';
        COMMIT;
        """ : """
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;
        DECLARE @lockResult int;
        EXEC @lockResult = sp_getapplock @Resource = 'EdilPaint.EmployeesSchema',
            @LockMode = 'Exclusive', @LockOwner = 'Transaction';
        IF @lockResult < 0
        BEGIN
            ROLLBACK TRANSACTION;
            THROW 50001, 'Impossibile preparare la tabella dipendenti.', 1;
        END;
        IF OBJECT_ID(N'[dbo].[Employees]', N'U') IS NULL
            CREATE TABLE [dbo].[Employees] (
                [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                [FirstName] nvarchar(250) NOT NULL,
                [LastName] nvarchar(250) NOT NULL,
                [Abbreviation] nvarchar(24) NOT NULL DEFAULT N'',
                [Revision] bigint NOT NULL,
                [IsDeleted] bit NOT NULL
            );
        IF COL_LENGTH(N'dbo.Employees', N'Abbreviation') IS NULL
            ALTER TABLE [dbo].[Employees] ADD [Abbreviation] nvarchar(24) NOT NULL
                CONSTRAINT [DF_Employees_Abbreviation] DEFAULT N'';
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Employees_Abbreviation'
            AND object_id = OBJECT_ID(N'[dbo].[Employees]'))
            EXEC(N'CREATE UNIQUE INDEX [IX_Employees_Abbreviation] ON [dbo].[Employees] ([Abbreviation])
                WHERE [IsDeleted] = 0 AND [Abbreviation] <> N''''');
        COMMIT TRANSACTION;
        """;
}
