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
        for (int attempt = 0; ; attempt++)
        {
            await using var db = AppDbContextFactory.Create();
            await EnsureEmployeeSchemaAsync(db, token).ConfigureAwait(false);
            var stored = await db.Employees.AsNoTracking().ToListAsync(token).ConfigureAwait(false);
            var imports = EmployeeChanges.LegacyImports(employees, stored);
            if (imports.Count == 0) return;
            db.Employees.AddRange(imports);
            try
            {
                await db.SaveChangesAsync(token).ConfigureAwait(false);
                return;
            }
            catch (DbUpdateException) when (attempt < 2)
            {
                // Another PC may have imported these same stable IDs in the meantime.
                // Re-read using a fresh context; never overwrite existing entries.
            }
        }
    }

    public async Task SaveEmployeesAsync(IReadOnlyList<EmployeeSettingsModel> original,
        IReadOnlyList<EmployeeSettingsModel> edited, CancellationToken token = default)
    {
        var changes = EmployeeChanges.Between(original, edited);
        if (changes.Count == 0) return;
        await using var db = AppDbContextFactory.Create();
        await EnsureEmployeeSchemaAsync(db, token).ConfigureAwait(false);
        var ids = changes.Select(x => x.Id).ToArray();
        var stored = await db.Employees.Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, token).ConfigureAwait(false);
        db.Employees.AddRange(EmployeeChanges.Apply(changes, stored));
        try
        {
            // EF saves this batch atomically and checks the original revision of
            // every updated/deleted row, including changes made after the read.
            await db.SaveChangesAsync(token).ConfigureAwait(false);
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
            "Revision" bigint NOT NULL,
            "IsDeleted" boolean NOT NULL
        );
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
                [Revision] bigint NOT NULL,
                [IsDeleted] bit NOT NULL
            );
        COMMIT TRANSACTION;
        """;
}
