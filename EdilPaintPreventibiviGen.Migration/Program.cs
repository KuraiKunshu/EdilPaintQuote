using EdilPaintPreventibiviGen.Data;
using EdilPaintPreventibiviGen.Data.Entities;
using Microsoft.EntityFrameworkCore;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

try
{
    var options = MigrationOptions.Parse(args);
    if (!options.IsValid)
    {
        MigrationOptions.PrintUsage();
        Environment.ExitCode = 2;
        return;
    }

    await SqlServerToPostgresMigrator.RunAsync(options);
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("Migrazione fallita.");
    Console.ResetColor();
    Console.WriteLine(ex);
    Environment.ExitCode = 1;
}

internal sealed class MigrationOptions
{
    public string SourceSqlServerConnectionString { get; private init; } = string.Empty;
    public string TargetPostgresConnectionString { get; private init; } = string.Empty;
    public bool OverwriteTarget { get; private init; }

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(SourceSqlServerConnectionString) &&
        !string.IsNullOrWhiteSpace(TargetPostgresConnectionString);

    public static MigrationOptions Parse(string[] args)
    {
        string source = string.Empty;
        string target = string.Empty;
        bool overwrite = false;

        for (int i = 0; i < args.Length; i++)
        {
            string current = args[i];
            if (Matches(current, "--source-sqlserver", "--source") && i + 1 < args.Length)
            {
                source = args[++i];
                continue;
            }

            if (Matches(current, "--target-postgres", "--target") && i + 1 < args.Length)
            {
                target = args[++i];
                continue;
            }

            if (Matches(current, "--overwrite-target", "--overwrite"))
                overwrite = true;
        }

        return new MigrationOptions
        {
            SourceSqlServerConnectionString = source,
            TargetPostgresConnectionString = target,
            OverwriteTarget = overwrite
        };
    }

    public static void PrintUsage()
    {
        Console.WriteLine("Uso:");
        Console.WriteLine("  dotnet run --project .\\EdilPaintPreventibiviGen.Migration -- --source-sqlserver \"<SQL_SERVER_CONNECTION>\" --target-postgres \"<NEON_CONNECTION>\" [--overwrite-target]");
        Console.WriteLine();
        Console.WriteLine("Note:");
        Console.WriteLine("  - Usa --overwrite-target solo su un database Neon vuoto o di prova: cancella i dati gia' presenti nel target.");
        Console.WriteLine("  - La connection string Neon deve includere SSL Mode=Require oppure sslmode=require.");
    }

    private static bool Matches(string value, params string[] candidates)
        => candidates.Any(candidate => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));
}

internal static class SqlServerToPostgresMigrator
{
    private static readonly string[] TablesWithIdentity =
    [
        "Customers",
        "CompanySettings",
        "LaborCatalog",
        "PersonalMaterials",
        "Quotes",
        "QuoteMaterials",
        "QuoteLabors"
    ];

    public static async Task RunAsync(MigrationOptions options)
    {
        await using var source = CreateSqlServerContext(options.SourceSqlServerConnectionString);
        await using var target = CreatePostgresContext(options.TargetPostgresConnectionString);

        Console.WriteLine("Controllo connessione SQL Server/Azure...");
        if (!await source.Database.CanConnectAsync())
            throw new InvalidOperationException("Impossibile connettersi al database SQL Server sorgente.");

        Console.WriteLine("Controllo connessione PostgreSQL/Neon...");
        if (!await target.Database.CanConnectAsync())
            throw new InvalidOperationException("Impossibile connettersi al database PostgreSQL target.");

        Console.WriteLine("Controllo coerenza dati sorgente...");
        await ValidateSourceAsync(source);

        Console.WriteLine("Creo schema PostgreSQL se mancante...");
        await target.Database.EnsureCreatedAsync();

        if (await TargetHasDataAsync(target))
        {
            if (!options.OverwriteTarget)
                throw new InvalidOperationException("Il database PostgreSQL target contiene gia' dati. Riesegui con --overwrite-target solo se vuoi cancellarli.");

            Console.WriteLine("Target non vuoto: cancello dati esistenti...");
            await TruncateTargetAsync(target);
        }

        Console.WriteLine("Copio dati...");
        var counts = new List<(string Name, int Source, int Target)>();

        counts.Add(await CopySetAsync(
            "CompanySettings",
            source.CompanySettings.AsNoTracking().OrderBy(x => x.Id),
            target.CompanySettings,
            target,
            () => target.CompanySettings.CountAsync()));

        counts.Add(await CopySetAsync(
            "Customers",
            source.Customers.AsNoTracking().OrderBy(x => x.Id),
            target.Customers,
            target,
            () => target.Customers.CountAsync()));

        counts.Add(await CopySetAsync(
            "LaborCatalog",
            source.LaborCatalog.AsNoTracking().OrderBy(x => x.Id),
            target.LaborCatalog,
            target,
            () => target.LaborCatalog.CountAsync()));

        counts.Add(await CopySetAsync(
            "PersonalMaterials",
            source.PersonalMaterials.AsNoTracking().OrderBy(x => x.Id),
            target.PersonalMaterials,
            target,
            () => target.PersonalMaterials.CountAsync()));

        counts.Add(await CopySetAsync(
            "Quotes",
            source.Quotes.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id),
            target.Quotes,
            target,
            () => target.Quotes.IgnoreQueryFilters().CountAsync()));

        counts.Add(await CopySetAsync(
            "QuoteMaterials",
            source.QuoteMaterials.AsNoTracking().OrderBy(x => x.Id),
            target.QuoteMaterials,
            target,
            () => target.QuoteMaterials.CountAsync()));

        counts.Add(await CopySetAsync(
            "QuoteLabors",
            source.QuoteLabors.AsNoTracking().OrderBy(x => x.Id),
            target.QuoteLabors,
            target,
            () => target.QuoteLabors.CountAsync()));

        Console.WriteLine("Riallineo sequenze PostgreSQL...");
        foreach (string table in TablesWithIdentity)
            await ResetPostgresSequenceAsync(target, table);

        Console.WriteLine();
        Console.WriteLine("Risultato migrazione:");
        foreach (var count in counts)
        {
            string status = count.Source == count.Target ? "OK" : "ATTENZIONE";
            Console.WriteLine($"  {status,-10} {count.Name,-18} sorgente={count.Source} target={count.Target}");
        }

        if (counts.Any(x => x.Source != x.Target))
            throw new InvalidOperationException("Migrazione completata con conteggi non allineati. Controlla l'output prima di usare il database target.");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine();
        Console.WriteLine("Migrazione completata correttamente.");
        Console.ResetColor();
    }

    private static AppDbContext CreateSqlServerContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, sql =>
            {
                sql.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorNumbersToAdd: null);
            })
            .Options;

        return new AppDbContext(options);
    }

    private static AppDbContext CreatePostgresContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, postgres =>
            {
                postgres.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorCodesToAdd: null);
            })
            .Options;

        return new AppDbContext(options);
    }

    private static async Task<bool> TargetHasDataAsync(AppDbContext target)
        => await target.CompanySettings.AnyAsync() ||
           await target.Customers.AnyAsync() ||
           await target.LaborCatalog.AnyAsync() ||
           await target.PersonalMaterials.AnyAsync() ||
           await target.Quotes.IgnoreQueryFilters().AnyAsync() ||
           await target.QuoteMaterials.AnyAsync() ||
           await target.QuoteLabors.AnyAsync();

    private static async Task ValidateSourceAsync(AppDbContext source)
    {
        var duplicateQuoteNumbers = await source.Quotes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.QuoteNumber != string.Empty)
            .GroupBy(x => x.QuoteNumber)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .Take(10)
            .ToListAsync();

        if (duplicateQuoteNumbers.Count > 0)
        {
            throw new InvalidOperationException(
                "Nel database sorgente ci sono numeri preventivo duplicati: " +
                string.Join(", ", duplicateQuoteNumbers));
        }

        var duplicateCustomerSyncIds = await source.Customers
            .AsNoTracking()
            .Where(x => x.SyncId != Guid.Empty)
            .GroupBy(x => x.SyncId)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .Take(10)
            .ToListAsync();

        if (duplicateCustomerSyncIds.Count > 0)
        {
            throw new InvalidOperationException(
                "Nel database sorgente ci sono SyncId cliente duplicati: " +
                string.Join(", ", duplicateCustomerSyncIds));
        }

        var customerIds = (await source.Customers
                .AsNoTracking()
                .Select(x => x.Id)
                .ToListAsync())
            .ToHashSet();

        var orphanQuotes = (await source.Quotes
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Select(x => new
                {
                    x.QuoteNumber,
                    x.CustomerId,
                    x.ReferenceCustomerId
                })
                .ToListAsync())
            .Where(x =>
                (x.CustomerId.HasValue && !customerIds.Contains(x.CustomerId.Value)) ||
                (x.ReferenceCustomerId.HasValue && !customerIds.Contains(x.ReferenceCustomerId.Value)))
            .Take(10)
            .Select(x => x.QuoteNumber)
            .ToList();

        if (orphanQuotes.Count > 0)
        {
            throw new InvalidOperationException(
                "Alcuni preventivi puntano a clienti non presenti nel database sorgente: " +
                string.Join(", ", orphanQuotes));
        }
    }

    private static Task TruncateTargetAsync(AppDbContext target)
        => target.Database.ExecuteSqlRawAsync("""
                                             TRUNCATE TABLE
                                                 "QuoteLabors",
                                                 "QuoteMaterials",
                                                 "Quotes",
                                                 "Customers",
                                                 "CompanySettings",
                                                 "LaborCatalog",
                                                 "PersonalMaterials"
                                             RESTART IDENTITY CASCADE;
                                             """);

    private static async Task<(string Name, int Source, int Target)> CopySetAsync<TEntity>(
        string name,
        IQueryable<TEntity> sourceQuery,
        DbSet<TEntity> targetSet,
        AppDbContext target,
        Func<Task<int>> countTargetAsync)
        where TEntity : class
    {
        var items = await sourceQuery.ToListAsync();
        if (items.Count > 0)
        {
            targetSet.AddRange(items);
            await target.SaveChangesAsync();
            target.ChangeTracker.Clear();
        }

        int targetCount = await countTargetAsync();
        Console.WriteLine($"  {name}: {items.Count} record");
        return (name, items.Count, targetCount);
    }

    private static Task ResetPostgresSequenceAsync(AppDbContext target, string tableName)
    {
        string sql = $"""
                     SELECT setval(
                         pg_get_serial_sequence('"{tableName}"', 'Id'),
                         GREATEST((SELECT COALESCE(MAX("Id"), 0) FROM "{tableName}"), 1),
                         (SELECT EXISTS(SELECT 1 FROM "{tableName}"))
                     );
                     """;

        return target.Database.ExecuteSqlRawAsync(sql);
    }
}
