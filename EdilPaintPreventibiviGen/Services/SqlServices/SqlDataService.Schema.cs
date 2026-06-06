using EdilPaintPreventibiviGen.Data;
using EdilPaintPreventibiviGen.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EdilPaintPreventibiviGen.Services;
public partial class SqlDataService
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = AppDbContextFactory.Create();

        await db.Database.EnsureCreatedAsync(cancellationToken);
        try
        {
            await db.Database.MigrateAsync(cancellationToken);
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("PendingModelChangesWarning", StringComparison.OrdinalIgnoreCase))
        {
            System.Diagnostics.Debug.WriteLine(
                "[SQL] Migrazioni EF non aggiornate rispetto al modello corrente; continuo con compatibilita' schema manuale.");
        }

        await EnsureLegacySchemaCompatibilityAsync(db, cancellationToken);

        if (!await db.CompanySettings.AnyAsync(cancellationToken))
        {
            db.CompanySettings.Add(new CompanySettingsEntity
            {
                Nome = string.Empty,
                Indirizzo = string.Empty,
                Piva = string.Empty,
                Email = string.Empty,
                SelectedLogo = string.Empty,
                LogosJson = "[]",
                LogoIndex = 0,
                Counter = 1,
                PaymentTerms = string.Empty
            });

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        await using var db = AppDbContextFactory.Create();
        return await db.Database.CanConnectAsync(cancellationToken);
    }

    private static async Task EnsureLegacySchemaCompatibilityAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync("""
    IF OBJECT_ID(N'[dbo].[QuotePdfFiles]', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[QuotePdfFiles]
        (
            [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            [QuoteId] INT NOT NULL,
            [FileName] NVARCHAR(500) NOT NULL,
            [ContentType] NVARCHAR(200) NOT NULL,
            [Content] VARBINARY(MAX) NOT NULL,
            [ImportedAt] DATETIME2 NOT NULL,
            CONSTRAINT [FK_QuotePdfFiles_Quotes_QuoteId]
                FOREIGN KEY ([QuoteId]) REFERENCES [dbo].[Quotes]([Id]) ON DELETE CASCADE,
            CONSTRAINT [AK_QuotePdfFiles_QuoteId] UNIQUE ([QuoteId])
        );
        CREATE INDEX [IX_QuotePdfFiles_QuoteId] ON [dbo].[QuotePdfFiles]([QuoteId]);
    END
    """, cancellationToken);

        await db.Database.ExecuteSqlRawAsync("""
    IF OBJECT_ID(N'[dbo].[QuoteAttachments]', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[QuoteAttachments]
        (
            [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            [QuoteId] INT NOT NULL,
            [FileName] NVARCHAR(500) NOT NULL,
            [ContentType] NVARCHAR(200) NOT NULL,
            [Content] VARBINARY(MAX) NOT NULL,
            [ImportedAt] DATETIME2 NOT NULL,
            CONSTRAINT [FK_QuoteAttachments_Quotes_QuoteId]
                FOREIGN KEY ([QuoteId]) REFERENCES [dbo].[Quotes]([Id]) ON DELETE CASCADE
        );
        CREATE INDEX [IX_QuoteAttachments_QuoteId] ON [dbo].[QuoteAttachments]([QuoteId]);
    END
    """, cancellationToken);

        await db.Database.ExecuteSqlRawAsync("""
    IF OBJECT_ID(N'[dbo].[QuoteCostsPdfFiles]', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[QuoteCostsPdfFiles]
        (
            [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            [QuoteId] INT NOT NULL,
            [FileName] NVARCHAR(500) NOT NULL,
            [ContentType] NVARCHAR(200) NOT NULL,
            [Content] VARBINARY(MAX) NOT NULL,
            [ImportedAt] DATETIME2 NOT NULL,
            CONSTRAINT [FK_QuoteCostsPdfFiles_Quotes_QuoteId]
                FOREIGN KEY ([QuoteId]) REFERENCES [dbo].[Quotes]([Id]) ON DELETE CASCADE,
            CONSTRAINT [AK_QuoteCostsPdfFiles_QuoteId] UNIQUE ([QuoteId])
        );
        CREATE INDEX [IX_QuoteCostsPdfFiles_QuoteId] ON [dbo].[QuoteCostsPdfFiles]([QuoteId]);
    END
    """, cancellationToken);

        await EnsureColumnAsync(db, "Quotes", "LastModifiedUtc",
            "DATETIME2 NOT NULL DEFAULT '0001-01-01T00:00:00.0000000Z'", cancellationToken);
        await EnsureColumnAsync(db, "Quotes", "SyncHash", "NVARCHAR(100) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, "Customers", "LastModifiedUtc",
            "DATETIME2 NOT NULL DEFAULT '0001-01-01T00:00:00.0000000Z'", cancellationToken);
        await EnsureCustomerSyncIdentityAsync(db, cancellationToken);
        await EnsureColumnAsync(db, "Customers", "IsDeleted", "BIT NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(db, "Quotes", "MaterialDiscount", "FLOAT NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(db, "Quotes", "LaborDiscount", "FLOAT NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(db, "Quotes", "IsJointVenture", "BIT NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(db, "Quotes", "PartnerCompanyName", "NVARCHAR(250) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, "Quotes", "CostAllocationsJson", "NVARCHAR(MAX) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, "Quotes", "IsDeleted", "BIT NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(db, "Quotes", "CreatedByDevice", "NVARCHAR(120) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, "Quotes", "LastModifiedByDevice", "NVARCHAR(120) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, "Quotes", "SentAtUtc", "DATETIME2 NULL", cancellationToken);
        await EnsureColumnAsync(db, "Quotes", "SentMethod", "NVARCHAR(80) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, "Quotes", "SentRecipient", "NVARCHAR(250) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, "Quotes", "SentByDevice", "NVARCHAR(120) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, "Quotes", "LastReminderAtUtc", "DATETIME2 NULL", cancellationToken);
        await EnsureColumnAsync(db, "Quotes", "ReminderCount", "INT NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(db, "Quotes", "LastReminderByDevice", "NVARCHAR(120) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, "Quotes", "EventsJson", "NVARCHAR(MAX) NOT NULL DEFAULT ''", cancellationToken);
    }

    private static Task EnsureColumnAsync(
        AppDbContext db,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        string sql = $"""
    IF COL_LENGTH(N'[dbo].[{tableName}]', N'{columnName}') IS NULL
    BEGIN
        ALTER TABLE [dbo].[{tableName}] ADD [{columnName}] {columnDefinition};
    END
    """;

        return db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    private static async Task EnsureCustomerSyncIdentityAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync("""
     IF COL_LENGTH(N'[dbo].[Customers]', N'SyncId') IS NULL
     BEGIN
         ALTER TABLE [dbo].[Customers] ADD [SyncId] UNIQUEIDENTIFIER NULL;
     END
     """, cancellationToken);

        await db.Database.ExecuteSqlRawAsync("""
     UPDATE [dbo].[Customers] SET [SyncId] = NEWID() WHERE [SyncId] IS NULL;
     """, cancellationToken);

        await db.Database.ExecuteSqlRawAsync("""
     IF EXISTS (
         SELECT 1 FROM sys.columns
         WHERE object_id = OBJECT_ID(N'[dbo].[Customers]')
           AND name = 'SyncId'
           AND is_nullable = 1
     )
     BEGIN
         ALTER TABLE [dbo].[Customers] ALTER COLUMN [SyncId] UNIQUEIDENTIFIER NOT NULL;
     END
     """, cancellationToken);

        await db.Database.ExecuteSqlRawAsync("""
     IF NOT EXISTS (
         SELECT 1 FROM sys.indexes
         WHERE object_id = OBJECT_ID(N'[dbo].[Customers]')
           AND name = 'IX_Customers_SyncId'
     )
     BEGIN
         CREATE UNIQUE INDEX [IX_Customers_SyncId] ON [dbo].[Customers]([SyncId]);
     END
     """, cancellationToken);
    }

    public async Task<bool> IsDatabaseEmptyAsync()
    {
        await using var db = AppDbContextFactory.Create();

        var hasCustomers = await db.Customers.AnyAsync();
        var hasQuotes = await db.Quotes.AnyAsync();
        var hasLabors = await db.LaborCatalog.AnyAsync();
        var hasMaterials = await db.PersonalMaterials.AnyAsync();

        var company = await db.CompanySettings.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync();
        bool hasCompanyData = company != null &&
                              (!string.IsNullOrWhiteSpace(company.Nome) ||
                               !string.IsNullOrWhiteSpace(company.Email) ||
                               company.Counter > 1);

        return !hasCustomers && !hasQuotes && !hasLabors && !hasMaterials && !hasCompanyData;
    }
}

