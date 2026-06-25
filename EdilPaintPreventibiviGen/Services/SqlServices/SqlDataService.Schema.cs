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
        if (db.Database.IsSqlServer())
            await EnsureLegacySchemaCompatibilityAsync(db, cancellationToken);
        else if (db.Database.IsNpgsql())
            await EnsurePostgreSqlSchemaCompatibilityAsync(db, cancellationToken);

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
        await EnsureIdDefaultGeneratorAsync(db, "Customers", cancellationToken);
        await EnsureIdDefaultGeneratorAsync(db, "CompanySettings", cancellationToken);
        await EnsureIdDefaultGeneratorAsync(db, "LaborCatalog", cancellationToken);
        await EnsureIdDefaultGeneratorAsync(db, "PersonalMaterials", cancellationToken);
        await EnsureIdDefaultGeneratorAsync(db, "Quotes", cancellationToken);
        await EnsureIdDefaultGeneratorAsync(db, "QuoteMaterials", cancellationToken);
        await EnsureIdDefaultGeneratorAsync(db, "QuoteLabors", cancellationToken);

        await EnsureCompanySettingsSchemaAsync(db, cancellationToken);
        await EnsureCustomerSchemaAsync(db, cancellationToken);
        await EnsureCatalogSchemaAsync(db, cancellationToken);
        await EnsureQuoteSchemaAsync(db, cancellationToken);
        await EnsureQuoteDetailSchemaAsync(db, cancellationToken);
        await EnsureQuoteAttachmentSchemaAsync(db, cancellationToken);
    }

    private static async Task EnsureCompanySettingsSchemaAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(db, "CompanySettings", "Nome", "NVARCHAR(250) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, "CompanySettings", "Indirizzo", "NVARCHAR(500) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, "CompanySettings", "Piva", "NVARCHAR(50) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, "CompanySettings", "Email", "NVARCHAR(250) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, "CompanySettings", "SelectedLogo", "NVARCHAR(500) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, "CompanySettings", "LogosJson", "NVARCHAR(MAX) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, "CompanySettings", "LogoIndex", "INT NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(db, "CompanySettings", "Counter", "INT NOT NULL DEFAULT 1", cancellationToken);
        await EnsureColumnAsync(db, "CompanySettings", "TerminiPagamento", "NVARCHAR(MAX) NOT NULL DEFAULT ''", cancellationToken);

        await EnsureTextColumnDefinitionAsync(db, "CompanySettings", "Nome", "NVARCHAR(250) NOT NULL", cancellationToken);
        await EnsureTextColumnDefinitionAsync(db, "CompanySettings", "Indirizzo", "NVARCHAR(500) NOT NULL", cancellationToken);
        await EnsureTextColumnDefinitionAsync(db, "CompanySettings", "Piva", "NVARCHAR(50) NOT NULL", cancellationToken);
        await EnsureTextColumnDefinitionAsync(db, "CompanySettings", "Email", "NVARCHAR(250) NOT NULL", cancellationToken);
        await EnsureTextColumnDefinitionAsync(db, "CompanySettings", "SelectedLogo", "NVARCHAR(500) NOT NULL", cancellationToken);
        await EnsureTextColumnDefinitionAsync(db, "CompanySettings", "LogosJson", "NVARCHAR(MAX) NOT NULL", cancellationToken);
        await EnsureTextColumnDefinitionAsync(db, "CompanySettings", "TerminiPagamento", "NVARCHAR(MAX) NOT NULL", cancellationToken);
    }

    private static async Task EnsureCustomerSchemaAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(db, "Customers", "BusinessName", "NVARCHAR(250) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, "Customers", "Address", "NVARCHAR(500) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, "Customers", "Email", "NVARCHAR(250) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, "Customers", "Phone", "NVARCHAR(100) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, "Customers", "MaterialDiscount", "FLOAT NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(db, "Customers", "LaborDiscount", "FLOAT NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(db, "Customers", "LastModifiedUtc",
            "DATETIME2 NOT NULL DEFAULT '0001-01-01T00:00:00.0000000Z'", cancellationToken);
        await EnsureColumnAsync(db, "Customers", "IsDeleted", "BIT NOT NULL DEFAULT 0", cancellationToken);

        await EnsureTextColumnDefinitionAsync(db, "Customers", "BusinessName", "NVARCHAR(250) NOT NULL", cancellationToken);
        await EnsureTextColumnDefinitionAsync(db, "Customers", "Address", "NVARCHAR(500) NOT NULL", cancellationToken);
        await EnsureTextColumnDefinitionAsync(db, "Customers", "Email", "NVARCHAR(250) NOT NULL", cancellationToken);
        await EnsureTextColumnDefinitionAsync(db, "Customers", "Phone", "NVARCHAR(100) NOT NULL", cancellationToken);
        await EnsureCustomerSyncIdentityAsync(db, cancellationToken);
    }

    private static async Task EnsureCatalogSchemaAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(db, "LaborCatalog", "Name", "NVARCHAR(250) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, "LaborCatalog", "Description", "NVARCHAR(MAX) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, "LaborCatalog", "UnitPrice", "FLOAT NOT NULL DEFAULT 0", cancellationToken);
        await EnsureTextColumnDefinitionAsync(db, "LaborCatalog", "Name", "NVARCHAR(250) NOT NULL", cancellationToken);
        await EnsureTextColumnDefinitionAsync(db, "LaborCatalog", "Description", "NVARCHAR(MAX) NOT NULL", cancellationToken);

        await EnsureColumnAsync(db, "PersonalMaterials", "Name", "NVARCHAR(250) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, "PersonalMaterials", "Description", "NVARCHAR(MAX) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, "PersonalMaterials", "UnitPrice", "FLOAT NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(db, "PersonalMaterials", "IsSignificant", "BIT NOT NULL DEFAULT 0", cancellationToken);
        await EnsureTextColumnDefinitionAsync(db, "PersonalMaterials", "Name", "NVARCHAR(250) NOT NULL", cancellationToken);
        await EnsureTextColumnDefinitionAsync(db, "PersonalMaterials", "Description", "NVARCHAR(MAX) NOT NULL", cancellationToken);
    }

    private static async Task EnsureQuoteSchemaAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(db, "Quotes", "PdfPath", "NVARCHAR(1000) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, "Quotes", "PaymentTerms", "NVARCHAR(MAX) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, "Quotes", "IvaType", "NVARCHAR(50) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, "Quotes", "Notes", "NVARCHAR(MAX) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, "Quotes", "MaterialDiscount", "FLOAT NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(db, "Quotes", "LaborDiscount", "FLOAT NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(db, "Quotes", "IsJointVenture", "BIT NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(db, "Quotes", "PartnerCompanyName", "NVARCHAR(250) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, "Quotes", "CostAllocationsJson", "NVARCHAR(MAX) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, "Quotes", "LastModifiedUtc",
            "DATETIME2 NOT NULL DEFAULT '0001-01-01T00:00:00.0000000Z'", cancellationToken);
        await EnsureColumnAsync(db, "Quotes", "Revision", "BIGINT NOT NULL DEFAULT 0", cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE [dbo].[Quotes] SET [Revision] = 1 WHERE [Revision] = 0;",
            cancellationToken);
        await EnsureColumnAsync(db, "Quotes", "SyncHash", "NVARCHAR(100) NOT NULL DEFAULT ''", cancellationToken);
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

        await EnsureQuoteNumberColumnDefinitionAsync(db, cancellationToken);
        await EnsureTextColumnDefinitionAsync(db, "Quotes", "PdfPath", "NVARCHAR(1000) NOT NULL", cancellationToken);
        await EnsureTextColumnDefinitionAsync(db, "Quotes", "PaymentTerms", "NVARCHAR(MAX) NOT NULL", cancellationToken);
        await EnsureTextColumnDefinitionAsync(db, "Quotes", "IvaType", "NVARCHAR(50) NOT NULL", cancellationToken);
        await EnsureTextColumnDefinitionAsync(db, "Quotes", "Notes", "NVARCHAR(MAX) NOT NULL", cancellationToken);
        await EnsureTextColumnDefinitionAsync(db, "Quotes", "PartnerCompanyName", "NVARCHAR(250) NOT NULL", cancellationToken);
        await EnsureTextColumnDefinitionAsync(db, "Quotes", "CostAllocationsJson", "NVARCHAR(MAX) NOT NULL", cancellationToken);
        await EnsureTextColumnDefinitionAsync(db, "Quotes", "SyncHash", "NVARCHAR(100) NOT NULL", cancellationToken);
        await EnsureTextColumnDefinitionAsync(db, "Quotes", "CreatedByDevice", "NVARCHAR(120) NOT NULL", cancellationToken);
        await EnsureTextColumnDefinitionAsync(db, "Quotes", "LastModifiedByDevice", "NVARCHAR(120) NOT NULL", cancellationToken);
        await EnsureTextColumnDefinitionAsync(db, "Quotes", "SentMethod", "NVARCHAR(80) NOT NULL", cancellationToken);
        await EnsureTextColumnDefinitionAsync(db, "Quotes", "SentRecipient", "NVARCHAR(250) NOT NULL", cancellationToken);
        await EnsureTextColumnDefinitionAsync(db, "Quotes", "SentByDevice", "NVARCHAR(120) NOT NULL", cancellationToken);
        await EnsureTextColumnDefinitionAsync(db, "Quotes", "LastReminderByDevice", "NVARCHAR(120) NOT NULL", cancellationToken);
        await EnsureTextColumnDefinitionAsync(db, "Quotes", "EventsJson", "NVARCHAR(MAX) NOT NULL", cancellationToken);
    }

    private static async Task EnsureQuoteAttachmentSchemaAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync("""
        IF OBJECT_ID(N'[dbo].[QuoteAttachments]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[QuoteAttachments]
            (
                [Id] INT NOT NULL IDENTITY(1,1),
                [QuoteId] INT NOT NULL,
                [FileName] NVARCHAR(500) NOT NULL,
                [ContentType] NVARCHAR(150) NOT NULL,
                [Content] VARBINARY(MAX) NOT NULL,
                [ImportedAtUtc] DATETIME2 NOT NULL,
                CONSTRAINT [PK_QuoteAttachments] PRIMARY KEY ([Id]),
                CONSTRAINT [FK_QuoteAttachments_Quotes_QuoteId]
                    FOREIGN KEY ([QuoteId]) REFERENCES [dbo].[Quotes]([Id]) ON DELETE CASCADE
            );
            CREATE INDEX [IX_QuoteAttachments_QuoteId] ON [dbo].[QuoteAttachments]([QuoteId]);
        END
        """, cancellationToken);
    }

    private static async Task EnsurePostgreSqlSchemaCompatibilityAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync("""
        ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "Revision" bigint NOT NULL DEFAULT 0;
        UPDATE "Quotes" SET "Revision" = 1 WHERE "Revision" = 0;

        CREATE TABLE IF NOT EXISTS "QuoteAttachments"
        (
            "Id" integer GENERATED BY DEFAULT AS IDENTITY,
            "QuoteId" integer NOT NULL,
            "FileName" character varying(500) NOT NULL,
            "ContentType" character varying(150) NOT NULL,
            "Content" bytea NOT NULL,
            "ImportedAtUtc" timestamp with time zone NOT NULL,
            CONSTRAINT "PK_QuoteAttachments" PRIMARY KEY ("Id"),
            CONSTRAINT "FK_QuoteAttachments_Quotes_QuoteId"
                FOREIGN KEY ("QuoteId") REFERENCES "Quotes" ("Id") ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS "IX_QuoteAttachments_QuoteId" ON "QuoteAttachments" ("QuoteId");
        """, cancellationToken);
    }

    private static async Task EnsureQuoteDetailSchemaAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        await EnsureQuoteLineSchemaAsync(db, "QuoteMaterials", cancellationToken);
        await EnsureQuoteLineSchemaAsync(db, "QuoteLabors", cancellationToken);
    }

    private static async Task EnsureQuoteLineSchemaAsync(
        AppDbContext db,
        string tableName,
        CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(db, tableName, "Name", "NVARCHAR(250) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, tableName, "Description", "NVARCHAR(MAX) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(db, tableName, "UnitPrice", "FLOAT NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(db, tableName, "Quantity", "INT NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(db, tableName, "Discount", "FLOAT NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(db, tableName, "IsSignificant", "BIT NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(db, tableName, "SortOrder", "INT NOT NULL DEFAULT 0", cancellationToken);

        await EnsureTextColumnDefinitionAsync(db, tableName, "Name", "NVARCHAR(250) NOT NULL", cancellationToken);
        await EnsureTextColumnDefinitionAsync(db, tableName, "Description", "NVARCHAR(MAX) NOT NULL", cancellationToken);
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

    private static Task EnsureIdDefaultGeneratorAsync(
        AppDbContext db,
        string tableName,
        CancellationToken cancellationToken)
    {
        string sequenceName = $"{tableName}_Id_Seq";
        string defaultName = $"DF_{tableName}_Id_Auto";
        string sql = $"""
    IF OBJECT_ID(N'[dbo].[{tableName}]', N'U') IS NOT NULL
       AND COL_LENGTH(N'[dbo].[{tableName}]', N'Id') IS NOT NULL
       AND EXISTS (
           SELECT 1
           FROM sys.columns
           WHERE object_id = OBJECT_ID(N'[dbo].[{tableName}]')
             AND name = N'Id'
             AND is_identity = 0
       )
    BEGIN
        DECLARE @nextId BIGINT;
        DECLARE @defaultConstraintName SYSNAME;
        DECLARE @dropDefaultSql NVARCHAR(MAX);

        SELECT @nextId = ISNULL(MAX(CONVERT(BIGINT, [Id])), 0) + 1
        FROM [dbo].[{tableName}] WITH (TABLOCKX);

        SELECT @defaultConstraintName = dc.name
        FROM sys.default_constraints dc
        INNER JOIN sys.columns c
            ON c.object_id = dc.parent_object_id
           AND c.column_id = dc.parent_column_id
        WHERE dc.parent_object_id = OBJECT_ID(N'[dbo].[{tableName}]')
          AND c.name = N'Id';

        IF @defaultConstraintName IS NOT NULL
        BEGIN
            SET @dropDefaultSql =
                N'ALTER TABLE [dbo].[{tableName}] DROP CONSTRAINT [' + REPLACE(@defaultConstraintName, N']', N']]') + N'];';
            EXEC(@dropDefaultSql);
        END

        IF OBJECT_ID(N'[dbo].[{sequenceName}]', N'SO') IS NULL
        BEGIN
            DECLARE @createSequenceSql NVARCHAR(MAX) =
                N'CREATE SEQUENCE [dbo].[{sequenceName}] AS INT START WITH ' + CAST(@nextId AS NVARCHAR(20)) + N' INCREMENT BY 1;';
            EXEC(@createSequenceSql);
        END
        ELSE
        BEGIN
            DECLARE @restartSequenceSql NVARCHAR(MAX) =
                N'ALTER SEQUENCE [dbo].[{sequenceName}] RESTART WITH ' + CAST(@nextId AS NVARCHAR(20)) + N';';
            EXEC(@restartSequenceSql);
        END

        ALTER TABLE [dbo].[{tableName}]
        ADD CONSTRAINT [{defaultName}]
        DEFAULT (NEXT VALUE FOR [dbo].[{sequenceName}]) FOR [Id];
    END
    """;

        return db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    private static Task EnsureTextColumnDefinitionAsync(
        AppDbContext db,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        string sql = $"""
    IF COL_LENGTH(N'[dbo].[{tableName}]', N'{columnName}') IS NOT NULL
    BEGIN
        UPDATE [dbo].[{tableName}] SET [{columnName}] = N'' WHERE [{columnName}] IS NULL;
        ALTER TABLE [dbo].[{tableName}] ALTER COLUMN [{columnName}] {columnDefinition};
    END
    """;

        return db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    private static Task EnsureQuoteNumberColumnDefinitionAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        const string sql = """
    IF COL_LENGTH(N'[dbo].[Quotes]', N'QuoteNumber') IS NOT NULL
    BEGIN
        UPDATE [dbo].[Quotes]
        SET [QuoteNumber] = N'RECOVERED-' + CONVERT(NVARCHAR(20), [Id])
        WHERE [QuoteNumber] IS NULL OR LTRIM(RTRIM([QuoteNumber])) = N'';

        ALTER TABLE [dbo].[Quotes] ALTER COLUMN [QuoteNumber] NVARCHAR(50) NOT NULL;
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
     UPDATE [dbo].[Customers]
     SET [SyncId] = NEWID()
     WHERE [SyncId] = '00000000-0000-0000-0000-000000000000';
     """, cancellationToken);

        await db.Database.ExecuteSqlRawAsync("""
     ;WITH DuplicateSyncIds AS
     (
         SELECT [Id],
                ROW_NUMBER() OVER (PARTITION BY [SyncId] ORDER BY [Id]) AS RowNumber
         FROM [dbo].[Customers]
         WHERE [SyncId] IS NOT NULL
     )
     UPDATE c
     SET [SyncId] = NEWID()
     FROM [dbo].[Customers] c
     INNER JOIN DuplicateSyncIds d ON d.[Id] = c.[Id]
     WHERE d.RowNumber > 1;
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
     IF EXISTS (
         SELECT 1 FROM sys.indexes
         WHERE object_id = OBJECT_ID(N'[dbo].[Customers]')
           AND name = 'IX_Customers_SyncId'
           AND is_unique = 0
     )
     BEGIN
         DROP INDEX [IX_Customers_SyncId] ON [dbo].[Customers];
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

