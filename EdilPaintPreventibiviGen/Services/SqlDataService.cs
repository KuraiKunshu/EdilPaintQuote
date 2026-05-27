using System.Diagnostics;
using EdilPaintPreventibiviGen.Data;
using EdilPaintPreventibiviGen.Data.Entities;
using EdilPaintPreventibiviGen.Data.Mappers;
using EdilPaintPreventibiviGen.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
namespace EdilPaintPreventibiviGen.Services;

public class SqlDataService : IDataService
{
    private readonly AppSettingsService _appSettings;

    public SqlDataService(AppSettingsService appSettings)
    {
        _appSettings = appSettings;
    }

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

    public async Task<Dictionary<string, QuoteMetadata>> GetQuoteMetadataAsync()
    {
        await using var db = AppDbContextFactory.Create();

        var metadata = await db.Quotes
            .AsNoTracking()
            .Select(q => new QuoteMetadata
            {
                QuoteNumber = q.QuoteNumber,
                LastModifiedUtc = q.LastModifiedUtc,
                SyncHash = q.SyncHash
            })
            .ToListAsync()
            .ConfigureAwait(false);

        return metadata.ToDictionary(
            q => q.QuoteNumber,
            q => q,
            StringComparer.OrdinalIgnoreCase);
    }
    
    public async Task<List<QuoteHistoryEntry>> GetQuotesByNumbersAsync(IEnumerable<string> quoteNumbers)
    {
        await using var db = AppDbContextFactory.Create();
    
        var numberList = quoteNumbers.ToList();
    
        var quotes = await db.Quotes
            .AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.ReferenceCustomer)
            .Include(x => x.Materials)
            .Include(x => x.Labors)
            .Include(x => x.PdfFile)
            .Include(x => x.Attachments)
            .Where(x => numberList.Contains(x.QuoteNumber))
            .ToListAsync()
            .ConfigureAwait(false);

        return quotes.Select(x => new QuoteHistoryEntry
        {
            QuoteNumber = x.QuoteNumber,
            Date = x.Date,
            CustomerName = x.Customer?.BusinessName ?? string.Empty,
            ReferenceName = x.ReferenceCustomer?.BusinessName ?? string.Empty,
            PdfPath = x.PdfPath,
            PaymentTerms = x.PaymentTerms,
            IvaType = x.IvaType,
            Notes = x.Notes,
            Imponibile = x.Imponibile,
            Total = x.Total,
            Status = x.Status,
            IsJointVenture = x.IsJointVenture,
            PartnerCompanyName = x.PartnerCompanyName,
            OurCosts = DeserializeCostAllocations(x.CostAllocationsJson)?.OurCosts ?? new(),
            PartnerCosts = DeserializeCostAllocations(x.CostAllocationsJson)?.PartnerCosts ?? new(),
            AdditionalCosts = DeserializeCostAllocations(x.CostAllocationsJson)?.AdditionalCosts ?? new(),
            Materials = x.Materials.Select(m => new Item
            {
                Name = m.Name,
                Description = m.Description,
                UnitPrice = m.UnitPrice,
                Quantity = m.Quantity,
                Discount = m.Discount,
                IsSignificant = m.IsSignificant
            }).ToList(),
            Labors = x.Labors.Select(l => new Item
            {
                Name = l.Name,
                Description = l.Description,
                UnitPrice = l.UnitPrice,
                Quantity = l.Quantity,
                Discount = l.Discount,
                IsSignificant = l.IsSignificant
            }).ToList(),
            PdfFile = x.PdfFile == null ? null : new StoredFile
            {
                FileName = x.PdfFile.FileName,
                ContentType = x.PdfFile.ContentType,
                Content = x.PdfFile.Content,
                ImportedAt = x.PdfFile.ImportedAt
            },
            Attachments = x.Attachments.Select(a => new StoredFile
            {
                FileName = a.FileName,
                ContentType = a.ContentType,
                Content = a.Content,
                ImportedAt = a.ImportedAt
            }).ToList()
        }).ToList();
    }
    
    public async Task EnsureAllHistoryPdfFilesAsync()
    {
        var storagePathService = StoragePathService.Instance;
        var quoteHistoryService = new QuoteHistoryService(this, storagePathService);

        var quotes = await GetQuotesAsync();

        foreach (var entry in quotes.OrderBy(x => x.Date))
        {
            quoteHistoryService.EnsurePdfExists(entry);
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
    

    public async Task<List<Customer>> GetCustomersAsync()
    {
        await using var db = AppDbContextFactory.Create();

        return await db.Customers
            .AsNoTracking()
            .OrderBy(x => x.BusinessName)
            .Select(x => x.ToModel())
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<Customer> AddCustomerAsync(Customer customer)
    {
        await using var db = AppDbContextFactory.Create();

        // Controlla se esiste già un cliente con lo stesso nome (evita duplicati)
        var existing = await db.Customers
            .FirstOrDefaultAsync(x => x.BusinessName == customer.BusinessName);

        if (existing != null)
        {
            // Aggiorna i dati esistenti
            existing.Address = customer.Address;
            existing.Email = customer.Email;
            existing.Phone = customer.Phone;
            existing.MaterialDiscount = customer.MaterialDiscount;
            existing.LaborDiscount = customer.LaborDiscount;
            await db.SaveChangesAsync();
            return existing.ToModel();
        }

        // Nuovo cliente
        var entity = customer.ToEntity();
        db.Customers.Add(entity);
        await db.SaveChangesAsync();
        return entity.ToModel();
    }

    public async Task DeleteCustomerAsync(string businessName)
    {
        await using var db = AppDbContextFactory.Create();
        var entity = await db.Customers.FirstOrDefaultAsync(x => x.BusinessName == businessName);
        if (entity == null) return;
        db.Customers.Remove(entity);
        await db.SaveChangesAsync();
    }
    
    public async Task<Company?> GetCompanyAsync()
    {
        await using var db = AppDbContextFactory.Create();

        var entity = await db.CompanySettings
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync();

        return entity?.ToModel();
    }

    public async Task SaveCompanyAsync(Company company, string selectedLogo)
    {
        await using var db = AppDbContextFactory.Create();

        var entity = await db.CompanySettings.OrderBy(x => x.Id).FirstOrDefaultAsync();

        if (entity == null)
        {
            entity = company.ToEntity(selectedLogo);
            db.CompanySettings.Add(entity);
        }
        else
        {
            entity.Nome = company.Nome;
            entity.Indirizzo = company.Indirizzo;
            entity.Piva = company.Piva;
            entity.Email = company.Email;
            entity.LogosJson = System.Text.Json.JsonSerializer.Serialize(company.Logo ?? new List<string>());
            entity.LogoIndex = company.Logo_index;
            // NON abbassare mai il counter — prendi il massimo tra quello attuale nel DB e quello locale
            entity.Counter = Math.Max(entity.Counter, company.Counter);
            entity.PaymentTerms = company.Termini_pagamento;
            entity.SelectedLogo = selectedLogo;
        }

        Debug.WriteLine($"[SAVE COMPANY] Logo ricevuti: {string.Join(" | ", company.Logo)}");
        Debug.WriteLine($"[SAVE COMPANY] selectedLogo: {selectedLogo}");
        Debug.WriteLine($"[SAVE COMPANY] Counter salvato: {entity.Counter} (locale: {company.Counter})");
        await db.SaveChangesAsync();
    }

    public async Task<List<Item>> GetLaborCatalogAsync()
    {
        await using var db = AppDbContextFactory.Create();

        return await db.LaborCatalog
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => x.ToModel())
            .ToListAsync();
    }

    public async Task SaveLaborCatalogAsync(IEnumerable<Item> labors)
    {
        await using var db = AppDbContextFactory.Create();

        db.LaborCatalog.RemoveRange(db.LaborCatalog);
        await db.SaveChangesAsync();

        var entities = labors.Select(x => x.ToLaborCatalogEntity()).ToList();
        await db.LaborCatalog.AddRangeAsync(entities);
        await db.SaveChangesAsync();
    }

    public async Task<List<Item>> GetPersonalMaterialsAsync()
    {
        await using var db = AppDbContextFactory.Create();

        return await db.PersonalMaterials
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => x.ToModel())
            .ToListAsync();
    }

    public async Task SavePersonalMaterialsAsync(IEnumerable<Item> materials)
    {
        await using var db = AppDbContextFactory.Create();

        db.PersonalMaterials.RemoveRange(db.PersonalMaterials);
        await db.SaveChangesAsync();

        var entities = materials.Select(x => x.ToPersonalMaterialEntity()).ToList();
        await db.PersonalMaterials.AddRangeAsync(entities);
        await db.SaveChangesAsync();
    }
    public async Task<List<QuoteHistoryEntry>> GetQuotesAsync()
    {
        await using var db = AppDbContextFactory.Create();

        var quotes = await db.Quotes
            .AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.ReferenceCustomer)
            .Include(x => x.Materials)
            .Include(x => x.Labors)
            .Include(x => x.PdfFile)
            .Include(x => x.Attachments)
            .OrderByDescending(x => x.Date)
            .ToListAsync();

        return quotes.Select(x => new QuoteHistoryEntry
        {
            QuoteNumber = x.QuoteNumber,
            Date = x.Date,
            CustomerName = x.Customer?.BusinessName ?? string.Empty,
            ReferenceName = x.ReferenceCustomer?.BusinessName ?? string.Empty,
            PdfPath = x.PdfPath,
            PaymentTerms = x.PaymentTerms,
            IvaType = x.IvaType,
            Notes = x.Notes,
            Imponibile = x.Imponibile,
            MaterialDiscount = x.MaterialDiscount,
            LaborDiscount = x.LaborDiscount,
            IsJointVenture = x.IsJointVenture,
            PartnerCompanyName = x.PartnerCompanyName,
            OurCosts = DeserializeCostAllocations(x.CostAllocationsJson)?.OurCosts ?? new(),
            PartnerCosts = DeserializeCostAllocations(x.CostAllocationsJson)?.PartnerCosts ?? new(),
            AdditionalCosts = DeserializeCostAllocations(x.CostAllocationsJson)?.AdditionalCosts ?? new(),
            LastModifiedUtc = x.LastModifiedUtc,
            Total = x.Total,
            Status = x.Status,
            Materials = x.Materials.Select(m => new Item
            {
                Name = m.Name,
                Description = m.Description,
                UnitPrice = m.UnitPrice,
                Quantity = m.Quantity,
                Discount = m.Discount,
                IsSignificant = m.IsSignificant
            }).ToList(),
            Labors = x.Labors.Select(l => new Item
            {
                Name = l.Name,
                Description = l.Description,
                UnitPrice = l.UnitPrice,
                Quantity = l.Quantity,
                Discount = l.Discount,
                IsSignificant = l.IsSignificant
            }).ToList(),
            PdfFile = x.PdfFile == null ? null : new StoredFile
            {
                FileName = x.PdfFile.FileName,
                ContentType = x.PdfFile.ContentType,
                Content = x.PdfFile.Content,
                ImportedAt = x.PdfFile.ImportedAt
            },
            Attachments = x.Attachments.Select(a => new StoredFile
            {
                FileName = a.FileName,
                ContentType = a.ContentType,
                Content = a.Content,
                ImportedAt = a.ImportedAt
            }).ToList()
        }).ToList();
    }

    public async Task<List<QuoteHistoryEntry>> GetQuotesAsync(int take)
    {
        await using var db = AppDbContextFactory.Create();

        var quotes = await db.Quotes
            .AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.ReferenceCustomer)
            .Include(x => x.Materials)
            .Include(x => x.Labors)
            .OrderByDescending(x => x.Date)
            .Take(Math.Max(1, take))
            .ToListAsync();

        return quotes.Select(x => new QuoteHistoryEntry
        {
            QuoteNumber = x.QuoteNumber,
            Date = x.Date,
            CustomerName = x.Customer?.BusinessName ?? string.Empty,
            ReferenceName = x.ReferenceCustomer?.BusinessName ?? string.Empty,
            PdfPath = x.PdfPath,
            PaymentTerms = x.PaymentTerms,
            IvaType = x.IvaType,
            Notes = x.Notes,
            Imponibile = x.Imponibile,
            MaterialDiscount = x.MaterialDiscount,
            LaborDiscount = x.LaborDiscount,
            Total = x.Total,
            Status = x.Status,
            Materials = x.Materials.Select(m => new Item
            {
                Name = m.Name,
                Description = m.Description,
                UnitPrice = m.UnitPrice,
                Quantity = m.Quantity,
                Discount = m.Discount,
                IsSignificant = m.IsSignificant
            }).ToList(),
            Labors = x.Labors.Select(l => new Item
            {
                Name = l.Name,
                Description = l.Description,
                UnitPrice = l.UnitPrice,
                Quantity = l.Quantity,
                Discount = l.Discount,
                IsSignificant = l.IsSignificant
            }).ToList(),
            PdfFile = null,       // Caricato on-demand
            Attachments = []      // Caricato on-demand
        }).ToList();
    }

    public async Task<List<QuoteHistoryEntry>> SearchQuotesAsync(string searchText, int take)
    {
        await using var db = AppDbContextFactory.Create();

        IQueryable<QuoteEntity> query = db.Quotes
            .AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.ReferenceCustomer)
            .Include(x => x.Materials)
            .Include(x => x.Labors)
            .Include(x => x.PdfFile)
            .Include(x => x.Attachments);

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            string term = searchText.Trim();

            query = query.Where(x =>
                x.QuoteNumber.Contains(term) ||
                x.Total.ToString().Contains(term) ||
                x.Status.ToString().Contains(term) ||
                (x.Customer != null && x.Customer.BusinessName.Contains(term)) ||
                (x.ReferenceCustomer != null && x.ReferenceCustomer.BusinessName.Contains(term)));
        }

        var quotes = await query
            .OrderByDescending(x => x.Date)
            .Take(Math.Max(1, take))
            .ToListAsync();

        return quotes.Select(x => new QuoteHistoryEntry
        {
            QuoteNumber = x.QuoteNumber,
            Date = x.Date,
            CustomerName = x.Customer?.BusinessName ?? string.Empty,
            ReferenceName = x.ReferenceCustomer?.BusinessName ?? string.Empty,
            PdfPath = x.PdfPath,
            PaymentTerms = x.PaymentTerms,
            IvaType = x.IvaType,
            Notes = x.Notes,
            Imponibile = x.Imponibile,
            MaterialDiscount = x.MaterialDiscount,
            LaborDiscount = x.LaborDiscount,
            Total = x.Total,
            Status = x.Status,
            Materials = x.Materials.Select(m => new Item
            {
                Name = m.Name,
                Description = m.Description,
                UnitPrice = m.UnitPrice,
                Quantity = m.Quantity,
                Discount = m.Discount,
                IsSignificant = m.IsSignificant
            }).ToList(),
            Labors = x.Labors.Select(l => new Item
            {
                Name = l.Name,
                Description = l.Description,
                UnitPrice = l.UnitPrice,
                Quantity = l.Quantity,
                Discount = l.Discount,
                IsSignificant = l.IsSignificant
            }).ToList(),
            PdfFile = x.PdfFile == null ? null : new StoredFile
            {
                FileName = x.PdfFile.FileName,
                ContentType = x.PdfFile.ContentType,
                Content = x.PdfFile.Content,
                ImportedAt = x.PdfFile.ImportedAt
            },
            Attachments = x.Attachments.Select(a => new StoredFile
            {
                FileName = a.FileName,
                ContentType = a.ContentType,
                Content = a.Content,
                ImportedAt = a.ImportedAt
            }).ToList()
        }).ToList();
    }

    public async Task<List<QuoteHistoryEntry>> SearchQuotesAsync(string searchText, int skip, int take)
    {
        await using var db = AppDbContextFactory.Create();

        IQueryable<QuoteEntity> query = db.Quotes
            .AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.ReferenceCustomer)
            .Include(x => x.Materials)
            .Include(x => x.Labors)
            .Include(x => x.PdfFile)
            .Include(x => x.Attachments);

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            query = query.Where(x =>
                x.QuoteNumber.Contains(searchText) ||
                (x.Customer != null && x.Customer.BusinessName.Contains(searchText)) ||
                (x.ReferenceCustomer != null && x.ReferenceCustomer.BusinessName.Contains(searchText)));
        }

        var quotes = await query
            .OrderByDescending(x => x.Date)
            .Skip(Math.Max(0, skip))
            .Take(Math.Max(1, take))
            .ToListAsync();

        return quotes.Select(x => new QuoteHistoryEntry
        {
            QuoteNumber = x.QuoteNumber,
            Date = x.Date,
            CustomerName = x.Customer?.BusinessName ?? string.Empty,
            ReferenceName = x.ReferenceCustomer?.BusinessName ?? string.Empty,
            PdfPath = x.PdfPath,
            PaymentTerms = x.PaymentTerms,
            IvaType = x.IvaType,
            Notes = x.Notes,
            Imponibile = x.Imponibile,
            MaterialDiscount = x.MaterialDiscount,
            LaborDiscount = x.LaborDiscount,
            Total = x.Total,
            Status = x.Status,
            Materials = x.Materials.Select(m => new Item
            {
                Name = m.Name,
                Description = m.Description,
                UnitPrice = m.UnitPrice,
                Quantity = m.Quantity,
                Discount = m.Discount,
                IsSignificant = m.IsSignificant
            }).ToList(),
            Labors = x.Labors.Select(l => new Item
            {
                Name = l.Name,
                Description = l.Description,
                UnitPrice = l.UnitPrice,
                Quantity = l.Quantity,
                Discount = l.Discount,
                IsSignificant = l.IsSignificant
            }).ToList(),
            PdfFile = x.PdfFile == null ? null : new StoredFile
            {
                FileName = x.PdfFile.FileName,
                ContentType = x.PdfFile.ContentType,
                Content = x.PdfFile.Content,
                ImportedAt = x.PdfFile.ImportedAt
            },
            Attachments = x.Attachments.Select(a => new StoredFile
            {
                FileName = a.FileName,
                ContentType = a.ContentType,
                Content = a.Content,
                ImportedAt = a.ImportedAt
            }).ToList()
        }).ToList();
    }
    
    

    public async Task<List<QuoteHistorySummary>> GetQuoteSummariesAsync(int take)
    {
        await using var db = AppDbContextFactory.Create();

        return await db.Quotes
            .AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.ReferenceCustomer)
            .OrderByDescending(x => x.Date)
            .Take(Math.Max(1, take))
            .Select(x => new QuoteHistorySummary
            {
                QuoteNumber = x.QuoteNumber,
                Date = x.Date,
                CustomerName = x.Customer != null ? x.Customer.BusinessName : string.Empty,
                ReferenceName = x.ReferenceCustomer != null ? x.ReferenceCustomer.BusinessName : string.Empty,
                PdfPath = x.PdfPath,
                Total = (decimal)x.Total,
                Status = x.Status,
                Notes = x.Notes,
                IsJointVenture = x.IsJointVenture,
                PartnerCompanyName = x.PartnerCompanyName
            })
            .ToListAsync();
    }

    public async Task<List<QuoteHistorySummary>> SearchQuoteSummariesAsync(string searchText, int take)
    {
        await using var db = AppDbContextFactory.Create();

        IQueryable<QuoteEntity> query = db.Quotes
            .AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.ReferenceCustomer);

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            string term = searchText.Trim();
            query = query.Where(x =>
                x.QuoteNumber.Contains(term) ||
                (x.Customer != null && x.Customer.BusinessName.Contains(term)) ||
                (x.ReferenceCustomer != null && x.ReferenceCustomer.BusinessName.Contains(term)));
        }

        return await query
            .OrderByDescending(x => x.Date)
            .Take(Math.Max(1, take))
            .Select(x => new QuoteHistorySummary
            {
                QuoteNumber = x.QuoteNumber,
                Date = x.Date,
                CustomerName = x.Customer != null ? x.Customer.BusinessName : string.Empty,
                ReferenceName = x.ReferenceCustomer != null ? x.ReferenceCustomer.BusinessName : string.Empty,
                PdfPath = x.PdfPath,
                Total = (decimal)x.Total,
                Status = x.Status,
                Notes = x.Notes,
                IsJointVenture = x.IsJointVenture,
                PartnerCompanyName = x.PartnerCompanyName
            })
            .ToListAsync();
    }
    
    public async Task<List<QuoteHistorySummary>> SearchQuoteSummariesAsync(string searchText, int skip, int take)
    {
        await using var db = AppDbContextFactory.Create();

        IQueryable<QuoteEntity> query = db.Quotes
            .AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.ReferenceCustomer);

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            query = query.Where(x =>
                x.QuoteNumber.Contains(searchText) ||
                (x.Customer != null && x.Customer.BusinessName.Contains(searchText)) ||
                (x.ReferenceCustomer != null && x.ReferenceCustomer.BusinessName.Contains(searchText)));
        }

        return await query
            .OrderByDescending(x => x.Date)
            .Skip(Math.Max(0, skip))
            .Take(Math.Max(1, take))
            .Select(x => new QuoteHistorySummary
            {
                QuoteNumber = x.QuoteNumber,
                Date = x.Date,
                CustomerName = x.Customer != null ? x.Customer.BusinessName : string.Empty,
                ReferenceName = x.ReferenceCustomer != null ? x.ReferenceCustomer.BusinessName : string.Empty,
                PdfPath = x.PdfPath,
                Total = (decimal)x.Total,
                Status = x.Status,
                Notes = x.Notes,                // ← AGGIUNTO
                IsJointVenture = x.IsJointVenture,
                PartnerCompanyName = x.PartnerCompanyName
            })
            .ToListAsync();
    }
    
    public async Task<HashSet<string>> GetAllQuoteNumbersAsync()
    {
        await using var db = AppDbContextFactory.Create();
        var numbers = await db.Quotes
            .AsNoTracking()
            .Select(x => x.QuoteNumber)
            .ToListAsync();
        return new HashSet<string>(numbers, StringComparer.OrdinalIgnoreCase);
    }
    
    public async Task<QuoteHistoryEntry?> GetQuoteByNumberAsync(string quoteNumber)
    {
        await using var db = AppDbContextFactory.Create();

        var q = await db.Quotes
            .AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.ReferenceCustomer)
            .Include(x => x.Materials)
            .Include(x => x.Labors)
            .Include(x => x.PdfFile)
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.QuoteNumber == quoteNumber);

        if (q == null) return null;

        return new QuoteHistoryEntry
        {
            QuoteNumber = q.QuoteNumber,
            Date = q.Date,
            CustomerName = q.Customer?.BusinessName ?? string.Empty,
            ReferenceName = q.ReferenceCustomer?.BusinessName ?? string.Empty,
            PdfPath = q.PdfPath,
            PaymentTerms = q.PaymentTerms,
            IvaType = q.IvaType,
            Notes = q.Notes,
            Imponibile = q.Imponibile,
            MaterialDiscount = q.MaterialDiscount,
            LaborDiscount = q.LaborDiscount,
            Total = q.Total,
            Status = q.Status,
            Materials = q.Materials.Select(m => new Item
            {
                Name = m.Name, Description = m.Description, UnitPrice = m.UnitPrice,
                Quantity = m.Quantity, Discount = m.Discount, IsSignificant = m.IsSignificant
            }).ToList(),
            Labors = q.Labors.Select(l => new Item
            {
                Name = l.Name, Description = l.Description, UnitPrice = l.UnitPrice,
                Quantity = l.Quantity, Discount = l.Discount, IsSignificant = l.IsSignificant
            }).ToList(),
            PdfFile = q.PdfFile == null ? null : new StoredFile
            {
                FileName = q.PdfFile.FileName, ContentType = q.PdfFile.ContentType,
                Content = q.PdfFile.Content, ImportedAt = q.PdfFile.ImportedAt
            },
            Attachments = q.Attachments.Select(a => new StoredFile
            {
                FileName = a.FileName, ContentType = a.ContentType,
                Content = a.Content, ImportedAt = a.ImportedAt
            }).ToList()
        };
    }
    
    public async Task DeleteQuoteAsync(string quoteNumber)
    {
        await using var db = AppDbContextFactory.Create();
        var strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                var existing = await db.Quotes.FirstOrDefaultAsync(x => x.QuoteNumber == quoteNumber);
                if (existing != null)
                {
                    db.Quotes.Remove(existing);
                    await db.SaveChangesAsync();
                }
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }
    
    private static QuoteHistoryEntry CreateLightEntry(QuoteHistoryEntry entry)
    {
        return new QuoteHistoryEntry
        {
            QuoteNumber = entry.QuoteNumber,
            Date = entry.Date,
            CustomerName = entry.CustomerName,
            ReferenceName = entry.ReferenceName,
            PdfPath = entry.PdfPath,
            PaymentTerms = entry.PaymentTerms,
            IvaType = entry.IvaType,
            Notes = entry.Notes,
            Imponibile = entry.Imponibile,
            MaterialDiscount = entry.MaterialDiscount,
            LaborDiscount = entry.LaborDiscount,
            Total = entry.Total,
            Status = entry.Status,
            LastModifiedUtc = entry.LastModifiedUtc,
            SyncHash = entry.SyncHash,
            IsJointVenture = entry.IsJointVenture,
            PartnerCompanyName = entry.PartnerCompanyName,
            OurCosts = entry.OurCosts,
            PartnerCosts = entry.PartnerCosts,
            AdditionalCosts = entry.AdditionalCosts,
            Materials = entry.Materials,
            Labors = entry.Labors,
            PdfFile = entry.PdfFile == null ? null : new StoredFile
            {
                FileName = entry.PdfFile.FileName,
                ContentType = entry.PdfFile.ContentType,
                Content = [],
                ImportedAt = entry.PdfFile.ImportedAt
            },
            Attachments = entry.Attachments.Select(a => new StoredFile
            {
                FileName = a.FileName,
                ContentType = a.ContentType,
                Content = [],
                ImportedAt = a.ImportedAt
            }).ToList()
        };
    }
// ... existing code ...
    public async Task SaveQuoteAsync(QuoteHistoryEntry quote)
    {
        await using var db = AppDbContextFactory.Create();

        var strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();

            try
            {
                CustomerEntity? customer = null;
                CustomerEntity? referenceCustomer = null;

                if (!string.IsNullOrWhiteSpace(quote.CustomerName))
                {
                    customer = await db.Customers
                        .FirstOrDefaultAsync(x => x.BusinessName == quote.CustomerName);
                }

                if (!string.IsNullOrWhiteSpace(quote.ReferenceName))
                {
                    referenceCustomer = await db.Customers
                        .FirstOrDefaultAsync(x => x.BusinessName == quote.ReferenceName);
                }

                var existing = await db.Quotes
                    .Include(x => x.Materials)
                    .Include(x => x.Labors)
                    .Include(x => x.PdfFile)
                    .Include(x => x.Attachments)
                    .FirstOrDefaultAsync(x => x.QuoteNumber == quote.QuoteNumber);

                if (existing != null)
                {
                    existing.Date = quote.Date;
                    existing.CustomerId = customer?.Id;
                    existing.ReferenceCustomerId = referenceCustomer?.Id;
                    existing.PdfPath = quote.PdfPath;
                    existing.PaymentTerms = quote.PaymentTerms;
                    existing.IvaType = quote.IvaType;
                    existing.Notes = quote.Notes;
                    existing.Imponibile = quote.Imponibile;
                    existing.MaterialDiscount = quote.MaterialDiscount;
                    existing.LaborDiscount = quote.LaborDiscount;
                    existing.Total = quote.Total;
                    existing.Status = quote.Status;
                    existing.LastModifiedUtc = quote.LastModifiedUtc; // ← NUOVO
                    existing.SyncHash = quote.SyncHash;               // ← NUOVO
                    
                    existing.IsJointVenture = quote.IsJointVenture;
                    existing.PartnerCompanyName = quote.PartnerCompanyName;
                    existing.CostAllocationsJson = string.IsNullOrEmpty(quote.PartnerCompanyName) && !quote.IsJointVenture
                        ? string.Empty
                        : JsonSerializer.Serialize(new CostAllocations
                        {
                            OurCosts = quote.OurCosts,
                            PartnerCosts = quote.PartnerCosts,
                            AdditionalCosts = quote.AdditionalCosts
                        });
                    // Rimuovi e ricrea i dettagli
                    db.QuoteMaterials.RemoveRange(existing.Materials);
                    db.QuoteLabors.RemoveRange(existing.Labors);

                    existing.Materials = quote.Materials.Select(m => new QuoteMaterialEntity
                    {
                        Name = m.Name,
                        Description = m.Description,
                        UnitPrice = m.UnitPrice,
                        Quantity = m.Quantity,
                        Discount = m.Discount,
                        IsSignificant = m.IsSignificant
                    }).ToList();

                    existing.Labors = quote.Labors.Select(l => new QuoteLaborEntity
                    {
                        Name = l.Name,
                        Description = l.Description,
                        UnitPrice = l.UnitPrice,
                        Quantity = l.Quantity,
                        Discount = l.Discount,
                        IsSignificant = l.IsSignificant
                    }).ToList();

                    // Gestisci il PDF se presente
                    if (quote.PdfFile != null)
                    {
                        if (existing.PdfFile != null)
                        {
                            existing.PdfFile.FileName = quote.PdfFile.FileName;
                            existing.PdfFile.ContentType = quote.PdfFile.ContentType;
                            existing.PdfFile.Content = quote.PdfFile.Content;
                            existing.PdfFile.ImportedAt = quote.PdfFile.ImportedAt;
                        }
                        else
                        {
                            existing.PdfFile = new QuotePdfFileEntity
                            {
                                FileName = quote.PdfFile.FileName,
                                ContentType = quote.PdfFile.ContentType,
                                Content = quote.PdfFile.Content,
                                ImportedAt = quote.PdfFile.ImportedAt
                            };
                        }
                    }

                    // Gestisci gli allegati
                    db.QuoteAttachments.RemoveRange(existing.Attachments);
                    existing.Attachments = quote.Attachments.Select(a => new QuoteAttachmentEntity
                    {
                        FileName = a.FileName,
                        ContentType = a.ContentType,
                        Content = a.Content,
                        ImportedAt = a.ImportedAt
                    }).ToList();
                }
                else
                {
                    // Nuovo record
                    var entity = new QuoteEntity
                    {
                        QuoteNumber = quote.QuoteNumber,
                        Date = quote.Date,
                        CustomerId = customer?.Id,
                        ReferenceCustomerId = referenceCustomer?.Id,
                        PdfPath = quote.PdfPath,
                        PaymentTerms = quote.PaymentTerms,
                        IvaType = quote.IvaType,
                        Notes = quote.Notes,
                        Imponibile = quote.Imponibile,
                        Total = quote.Total,
                        MaterialDiscount = quote.MaterialDiscount,
                        LaborDiscount = quote.LaborDiscount,
                        Status = quote.Status,
                        LastModifiedUtc = quote.LastModifiedUtc,
                        SyncHash = quote.SyncHash,
                        IsJointVenture = quote.IsJointVenture,
                        PartnerCompanyName = quote.PartnerCompanyName,
                        CostAllocationsJson = string.IsNullOrEmpty(quote.PartnerCompanyName) && !quote.IsJointVenture
                            ? string.Empty
                            : JsonSerializer.Serialize(new CostAllocations
                            {
                                OurCosts = quote.OurCosts,
                                PartnerCosts = quote.PartnerCosts,
                                AdditionalCosts = quote.AdditionalCosts
                            }),
                        Materials = quote.Materials.Select(m => new QuoteMaterialEntity
                        {
                            Name = m.Name,
                            Description = m.Description,
                            UnitPrice = m.UnitPrice,
                            Quantity = m.Quantity,
                            Discount = m.Discount,
                            IsSignificant = m.IsSignificant
                        }).ToList(),
                        Labors = quote.Labors.Select(l => new QuoteLaborEntity
                        {
                            Name = l.Name,
                            Description = l.Description,
                            UnitPrice = l.UnitPrice,
                            Quantity = l.Quantity,
                            Discount = l.Discount,
                            IsSignificant = l.IsSignificant
                        }).ToList(),
                        PdfFile = quote.PdfFile == null
                            ? null
                            : new QuotePdfFileEntity
                            {
                                FileName = quote.PdfFile.FileName,
                                ContentType = quote.PdfFile.ContentType,
                                Content = quote.PdfFile.Content,
                                ImportedAt = quote.PdfFile.ImportedAt
                            },
                        Attachments = quote.Attachments.Select(a => new QuoteAttachmentEntity
                        {
                            FileName = a.FileName,
                            ContentType = a.ContentType,
                            Content = a.Content,
                            ImportedAt = a.ImportedAt
                        }).ToList()
                    };

                    db.Quotes.Add(entity);
                }

                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }
    
    public async Task<int> GetNextQuoteNumberAsync()
    {
        await using var db = AppDbContextFactory.Create();

        // UPDATE atomico: incrementa e restituisce il nuovo valore in un'unica operazione SQL
        // Questo evita race condition tra più PC che chiamano contemporaneamente
        var result = await db.Database.SqlQueryRaw<int>("""
                                                        UPDATE TOP(1) [dbo].[CompanySettings]
                                                        SET [Counter] = [Counter] + 1
                                                        OUTPUT INSERTED.[Counter]
                                                        """).ToListAsync();

        if (result.Count > 0)
            return result[0];

        // Fallback: nessuna riga trovata, crea il record base
        var newSettings = new CompanySettingsEntity { Counter = 1 };
        db.CompanySettings.Add(newSettings);
        await db.SaveChangesAsync();
        return newSettings.Counter;
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

    public async Task<byte[]?> GetQuotePdfContentAsync(string quoteNumber)
    {
        await using var db = AppDbContextFactory.Create();

        return await db.QuotePdfFiles
            .AsNoTracking()
            .Where(x => x.Quote.QuoteNumber == quoteNumber)
            .Select(x => x.Content)
            .FirstOrDefaultAsync();
    }

    public async Task<List<StoredFile>> GetQuoteAttachmentsAsync(string quoteNumber)
    {
        await using var db = AppDbContextFactory.Create();

        return await db.QuoteAttachments
            .AsNoTracking()
            .Where(x => x.Quote.QuoteNumber == quoteNumber)
            .OrderBy(x => x.FileName)
            .Select(x => new StoredFile
            {
                FileName = x.FileName,
                ContentType = x.ContentType,
                Content = x.Content,
                ImportedAt = x.ImportedAt
            })
            .ToListAsync();
    }
    private static CostAllocations? DeserializeCostAllocations(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<CostAllocations>(json); }
        catch { return null; }
    }
}

public class CostAllocations
{
    public List<CostAllocationItem> OurCosts { get; set; } = new();
    public List<CostAllocationItem> PartnerCosts { get; set; } = new();
    public List<CostAllocationItem> AdditionalCosts { get; set; } = new();
}