using System.Diagnostics;
using System.Text.Json;
using EdilPaintPreventibiviGen.Data;
using EdilPaintPreventibiviGen.Data.Entities;
using EdilPaintPreventibiviGen.Data.Mappers;
using EdilPaintPreventibiviGen.Models;
using Microsoft.EntityFrameworkCore;

namespace EdilPaintPreventibiviGen.Services;
public partial class SqlDataService
{
    public async Task InitializeAsync()
    {
        
        await using var db = AppDbContextFactory.Create();
        await db.Database.EnsureCreatedAsync();
        await EnsureQuoteFilesSchemaAsync(db);

        if (!await db.CompanySettings.AnyAsync())
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

            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsureQuoteFilesSchemaAsync(AppDbContext db)
    {
        // --- tabelle esistenti (invariate) ---
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
    """);

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
    """);

        // --- NUOVO: aggiunge LastModifiedUtc alla tabella Quotes se non esiste ---
        await db.Database.ExecuteSqlRawAsync("""
    IF NOT EXISTS (
        SELECT 1 FROM sys.columns 
        WHERE object_id = OBJECT_ID(N'[dbo].[Quotes]') 
          AND name = 'LastModifiedUtc'
    )
    BEGIN
        ALTER TABLE [dbo].[Quotes] 
        ADD [LastModifiedUtc] DATETIME2 NOT NULL DEFAULT '0001-01-01T00:00:00.0000000Z';
    END
    """);

        // --- NUOVO: aggiunge SyncHash alla tabella Quotes se non esiste ---
        await db.Database.ExecuteSqlRawAsync("""
    IF NOT EXISTS (
        SELECT 1 FROM sys.columns 
        WHERE object_id = OBJECT_ID(N'[dbo].[Quotes]') 
          AND name = 'SyncHash'
    )
    BEGIN
        ALTER TABLE [dbo].[Quotes] 
        ADD [SyncHash] NVARCHAR(100) NOT NULL DEFAULT '';
    END
    """);
        await db.Database.ExecuteSqlRawAsync("""
     IF NOT EXISTS (
         SELECT 1 FROM sys.columns 
         WHERE object_id = OBJECT_ID(N'[dbo].[Customers]') 
           AND name = 'LastModifiedUtc'
     )
     BEGIN
         ALTER TABLE [dbo].[Customers] 
         ADD [LastModifiedUtc] DATETIME2 NOT NULL DEFAULT '0001-01-01T00:00:00.0000000Z';
     END
     """);
        await db.Database.ExecuteSqlRawAsync("""
                                             IF NOT EXISTS (
                                                 SELECT 1 FROM sys.columns 
                                                 WHERE object_id = OBJECT_ID(N'[dbo].[Quotes]') 
                                                   AND name = 'MaterialDiscount'
                                             )
                                             BEGIN
                                                 ALTER TABLE [dbo].[Quotes] 
                                                 ADD [MaterialDiscount] FLOAT NOT NULL DEFAULT 0;
                                             END
                                             """);

        await db.Database.ExecuteSqlRawAsync("""
                                             IF NOT EXISTS (
                                                 SELECT 1 FROM sys.columns 
                                                 WHERE object_id = OBJECT_ID(N'[dbo].[Quotes]') 
                                                   AND name = 'LaborDiscount'
                                             )
                                             BEGIN
                                                 ALTER TABLE [dbo].[Quotes] 
                                                 ADD [LaborDiscount] FLOAT NOT NULL DEFAULT 0;
                                             END
                                             """);
        await db.Database.ExecuteSqlRawAsync("""
                                             IF NOT EXISTS (
                                                 SELECT 1 FROM sys.columns 
                                                 WHERE object_id = OBJECT_ID(N'[dbo].[Quotes]') 
                                                   AND name = 'IsJointVenture'
                                             )
                                             BEGIN
                                                 ALTER TABLE [dbo].[Quotes] ADD [IsJointVenture] BIT NOT NULL DEFAULT 0;
                                             END
                                             """);

        await db.Database.ExecuteSqlRawAsync("""
                                             IF NOT EXISTS (
                                                 SELECT 1 FROM sys.columns 
                                                 WHERE object_id = OBJECT_ID(N'[dbo].[Quotes]') 
                                                   AND name = 'PartnerCompanyName'
                                             )
                                             BEGIN
                                                 ALTER TABLE [dbo].[Quotes] ADD [PartnerCompanyName] NVARCHAR(250) NOT NULL DEFAULT '';
                                             END
                                             """);

        await db.Database.ExecuteSqlRawAsync("""
                                             IF NOT EXISTS (
                                                 SELECT 1 FROM sys.columns 
                                                 WHERE object_id = OBJECT_ID(N'[dbo].[Quotes]') 
                                                   AND name = 'CostAllocationsJson'
                                             )
                                             BEGIN
                                                 ALTER TABLE [dbo].[Quotes] ADD [CostAllocationsJson] NVARCHAR(MAX) NOT NULL DEFAULT '';
                                             END
                                             """);
        await db.Database.ExecuteSqlRawAsync("""
                                             IF NOT EXISTS (
                                                 SELECT 1 FROM sys.columns 
                                                 WHERE object_id = OBJECT_ID(N'[dbo].[Quotes]') 
                                                   AND name = 'IsJointVenture'
                                             )
                                             BEGIN
                                                 ALTER TABLE [dbo].[Quotes] 
                                                 ADD [IsJointVenture] BIT NOT NULL DEFAULT 0;
                                             END
                                             """);

        await db.Database.ExecuteSqlRawAsync("""
                                             IF NOT EXISTS (
                                                 SELECT 1 FROM sys.columns 
                                                 WHERE object_id = OBJECT_ID(N'[dbo].[Quotes]') 
                                                   AND name = 'PartnerCompanyName'
                                             )
                                             BEGIN
                                                 ALTER TABLE [dbo].[Quotes] 
                                                 ADD [PartnerCompanyName] NVARCHAR(500) NOT NULL DEFAULT '';
                                             END
                                             """);

        await db.Database.ExecuteSqlRawAsync("""
                                             IF NOT EXISTS (
                                                 SELECT 1 FROM sys.columns 
                                                 WHERE object_id = OBJECT_ID(N'[dbo].[Quotes]') 
                                                   AND name = 'CostAllocationsJson'
                                             )
                                             BEGIN
                                                 ALTER TABLE [dbo].[Quotes] 
                                                 ADD [CostAllocationsJson] NVARCHAR(MAX) NOT NULL DEFAULT '';
                                             END
                                             """);
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

