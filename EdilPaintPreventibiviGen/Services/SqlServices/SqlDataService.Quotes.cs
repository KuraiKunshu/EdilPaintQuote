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
    private const int QuoteQueryBatchSize = 500;

    public async Task<Dictionary<string, QuoteMetadata>> GetQuoteMetadataAsync(CancellationToken cancellationToken = default)
    {
        await using var db = AppDbContextFactory.Create();

        var metadata = await db.Quotes
            .AsNoTracking()
            .Select(q => new QuoteMetadata
            {
                QuoteNumber = q.QuoteNumber,
                LastModifiedUtc = q.LastModifiedUtc,
                SyncHash = q.SyncHash,
                Revision = q.Revision
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return metadata.ToDictionary(
            q => q.QuoteNumber,
            q => q,
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<QuoteMetadata?> GetQuoteMetadataByNumberAsync(
        string quoteNumber,
        CancellationToken cancellationToken = default)
    {
        await using var db = AppDbContextFactory.Create();
        return await db.Quotes
            .AsNoTracking()
            .Where(quote => quote.QuoteNumber == quoteNumber)
            .Select(quote => new QuoteMetadata
            {
                QuoteNumber = quote.QuoteNumber,
                LastModifiedUtc = quote.LastModifiedUtc,
                SyncHash = quote.SyncHash,
                Revision = quote.Revision
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<List<QuoteHistoryEntry>> GetQuoteSyncSnapshotsAsync(
        IEnumerable<string> quoteNumbers,
        CancellationToken cancellationToken = default)
    {
        await using var db = AppDbContextFactory.Create();

        var numberList = quoteNumbers
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (numberList.Count == 0)
            return [];

        if (numberList.Count > QuoteQueryBatchSize)
        {
            var combined = new List<QuoteHistoryEntry>();
            foreach (var batch in numberList.Chunk(QuoteQueryBatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                combined.AddRange(await GetQuoteSyncSnapshotsAsync(batch, cancellationToken));
            }

            return combined;
        }

        var quotes = await db.Quotes
            .AsNoTracking()
            .Where(x => numberList.Contains(x.QuoteNumber))
            .AsSplitQuery()
            .Select(x => new
            {
                x.QuoteNumber,
                x.Date,
                CustomerName = x.Customer != null ? x.Customer.BusinessName : string.Empty,
                CustomerSyncId = x.Customer != null ? x.Customer.SyncId : Guid.Empty,
                ReferenceName = x.ReferenceCustomer != null ? x.ReferenceCustomer.BusinessName : string.Empty,
                ReferenceCustomerSyncId = x.ReferenceCustomer != null
                    ? x.ReferenceCustomer.SyncId
                    : Guid.Empty,
                x.SiteName,
                x.BillingCustomerName,
                BillingCustomerSyncId = x.BillingCustomer != null
                    ? x.BillingCustomer.SyncId
                    : Guid.Empty,
                x.PaymentTerms,
                x.CustomerNotes,
                x.IvaType,
                x.Notes,
                x.Imponibile,
                x.MaterialDiscount,
                x.LaborDiscount,
                x.Total,
                x.Status,
                x.CreatedByDevice,
                x.LastModifiedByDevice,
                x.SentAtUtc,
                x.SentMethod,
                x.SentRecipient,
                x.SentByDevice,
                x.LastReminderAtUtc,
                x.ReminderCount,
                x.LastReminderByDevice,
                x.EventsJson,
                x.SupplierName,
                x.MaterialOrderDate,
                x.ExpectedDeliveryDate,
                x.MaterialStatus,
                x.MaterialsOrderedByCustomer,
                x.RealProfitJson,
                x.LastModifiedUtc,
                x.Revision,
                x.IsJointVenture,
                x.PartnerCompanyName,
                x.CostAllocationsJson,
                Materials = x.Materials
                    .OrderBy(m => m.SortOrder)
                    .Select(m => new Item
                    {
                        PersistentId = m.CatalogItemId,
                        Name = m.Name,
                        Description = m.Description,
                        UnitPrice = m.UnitPrice,
                        Quantity = m.Quantity,
                        Discount = m.Discount,
                        IsSignificant = m.IsSignificant,
                        SortOrder = m.SortOrder
                    })
                    .ToList(),
                Labors = x.Labors
                    .OrderBy(l => l.SortOrder)
                    .Select(l => new Item
                    {
                        PersistentId = l.CatalogItemId,
                        Name = l.Name,
                        Description = l.Description,
                        UnitPrice = l.UnitPrice,
                        Quantity = l.Quantity,
                        Discount = l.Discount,
                        IsSignificant = l.IsSignificant,
                        SortOrder = l.SortOrder
                    })
                    .ToList(),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return quotes.Select(x =>
        {
            var costs = DeserializeCostAllocations(x.CostAllocationsJson);
            return new QuoteHistoryEntry
            {
                QuoteNumber = x.QuoteNumber,
                Date = x.Date,
                CustomerName = x.CustomerName,
                CustomerSyncId = x.CustomerSyncId,
                ReferenceName = x.ReferenceName,
                ReferenceCustomerSyncId = x.ReferenceCustomerSyncId,
                SiteName = x.SiteName,
                BillingCustomerName = x.BillingCustomerName,
                BillingCustomerSyncId = x.BillingCustomerSyncId,
                PaymentTerms = x.PaymentTerms,
                CustomerNotes = x.CustomerNotes,
                IvaType = x.IvaType,
                Notes = x.Notes,
                Imponibile = x.Imponibile,
                MaterialDiscount = x.MaterialDiscount,
                LaborDiscount = x.LaborDiscount,
                Total = x.Total,
                Status = x.Status,
                CreatedByDevice = x.CreatedByDevice,
                LastModifiedByDevice = x.LastModifiedByDevice,
                SentAtUtc = x.SentAtUtc,
                SentMethod = x.SentMethod,
                SentRecipient = x.SentRecipient,
                SentByDevice = x.SentByDevice,
                LastReminderAtUtc = x.LastReminderAtUtc,
                ReminderCount = x.ReminderCount,
                LastReminderByDevice = x.LastReminderByDevice,
                Events = DeserializeQuoteEvents(x.EventsJson),
                SupplierName = x.SupplierName,
                MaterialOrderDate = x.MaterialOrderDate,
                ExpectedDeliveryDate = x.ExpectedDeliveryDate,
                MaterialStatus = x.MaterialStatus,
                MaterialsOrderedByCustomer = x.MaterialsOrderedByCustomer,
                RealProfit = DeserializeRealProfit(x.RealProfitJson),
                IsJointVenture = x.IsJointVenture,
                PartnerCompanyName = x.PartnerCompanyName,
                OurCosts = costs?.OurCosts ?? [],
                PartnerCosts = costs?.PartnerCosts ?? [],
                AdditionalCosts = costs?.AdditionalCosts ?? [],
                LastModifiedUtc = x.LastModifiedUtc,
                BaseVersionUtc = x.LastModifiedUtc,
                Revision = x.Revision,
                BaseRevision = x.Revision,
                Materials = x.Materials,
                Labors = x.Labors
            };
        }).ToList();
    }

    public async Task UpdateQuoteSyncHashesAsync(
        IReadOnlyDictionary<string, (string SyncHash, long ExpectedRevision)> updates,
        CancellationToken cancellationToken = default)
    {
        if (updates.Count == 0)
            return;

        if (updates.Count > QuoteQueryBatchSize)
        {
            foreach (var batch in updates.Chunk(QuoteQueryBatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await UpdateQuoteSyncHashesAsync(
                    batch.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
                    cancellationToken);
            }

            return;
        }

        await using var db = AppDbContextFactory.Create();
        var quoteNumbers = updates.Keys.ToList();
        var quotes = await db.Quotes
            .Where(x => quoteNumbers.Contains(x.QuoteNumber))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var quote in quotes)
        {
            if (updates.TryGetValue(quote.QuoteNumber, out var update) &&
                quote.Revision == update.ExpectedRevision)
            {
                quote.SyncHash = update.SyncHash;
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<QuoteHistoryEntry>> GetQuotesByNumbersAsync(
        IEnumerable<string> quoteNumbers,
        CancellationToken cancellationToken = default)
    {
        await using var db = AppDbContextFactory.Create();
    
        var numberList = quoteNumbers
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (numberList.Count > QuoteQueryBatchSize)
        {
            var combined = new List<QuoteHistoryEntry>();
            foreach (var batch in numberList.Chunk(QuoteQueryBatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                combined.AddRange(await GetQuotesByNumbersAsync(batch, cancellationToken));
            }

            return combined;
        }
    
        var quotes = await db.Quotes
            .AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.ReferenceCustomer)
            .Include(x => x.BillingCustomer)
            .Include(x => x.Materials)
            .Include(x => x.Labors)
            .AsSplitQuery()
            .Where(x => numberList.Contains(x.QuoteNumber))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return quotes.Select(x => new QuoteHistoryEntry
        {
            QuoteNumber = x.QuoteNumber,
            Date = x.Date,
            CustomerName = x.Customer?.BusinessName ?? string.Empty,
            CustomerSyncId = x.Customer?.SyncId ?? Guid.Empty,
            ReferenceName = x.ReferenceCustomer?.BusinessName ?? string.Empty,
            ReferenceCustomerSyncId = x.ReferenceCustomer?.SyncId ?? Guid.Empty,
            SiteName = x.SiteName,
            BillingCustomerName = x.BillingCustomerName,
            BillingCustomerSyncId = x.BillingCustomer?.SyncId ?? Guid.Empty,
            PdfPath = x.PdfPath,
            PaymentTerms = x.PaymentTerms,
            CustomerNotes = x.CustomerNotes,
            IvaType = x.IvaType,
            Notes = x.Notes,
            Imponibile = x.Imponibile,
            MaterialDiscount = x.MaterialDiscount,
            LaborDiscount = x.LaborDiscount,
            Total = x.Total,
            Status = x.Status,
            CreatedByDevice = x.CreatedByDevice,
            LastModifiedByDevice = x.LastModifiedByDevice,
            SentAtUtc = x.SentAtUtc,
            SentMethod = x.SentMethod,
            SentRecipient = x.SentRecipient,
            SentByDevice = x.SentByDevice,
            LastReminderAtUtc = x.LastReminderAtUtc,
            ReminderCount = x.ReminderCount,
            LastReminderByDevice = x.LastReminderByDevice,
            Events = DeserializeQuoteEvents(x.EventsJson),
            SupplierName = x.SupplierName,
            MaterialOrderDate = x.MaterialOrderDate,
            ExpectedDeliveryDate = x.ExpectedDeliveryDate,
            MaterialStatus = x.MaterialStatus,
            MaterialsOrderedByCustomer = x.MaterialsOrderedByCustomer,
            RealProfit = DeserializeRealProfit(x.RealProfitJson),
            LastModifiedUtc = x.LastModifiedUtc,
            BaseVersionUtc = x.LastModifiedUtc,
            Revision = x.Revision,
            BaseRevision = x.Revision,
            SyncHash = x.SyncHash,
            IsJointVenture = x.IsJointVenture,
            PartnerCompanyName = x.PartnerCompanyName,
            OurCosts = DeserializeCostAllocations(x.CostAllocationsJson)?.OurCosts ?? new(),
            PartnerCosts = DeserializeCostAllocations(x.CostAllocationsJson)?.PartnerCosts ?? new(),
            AdditionalCosts = DeserializeCostAllocations(x.CostAllocationsJson)?.AdditionalCosts ?? new(),
            Materials = x.Materials.OrderBy(m => m.SortOrder).Select(m => new Item
            {
                PersistentId = m.CatalogItemId,
                Name = m.Name,
                Description = m.Description,
                UnitPrice = m.UnitPrice,
                Quantity = m.Quantity,
                Discount = m.Discount,
                IsSignificant = m.IsSignificant,
                SortOrder = m.SortOrder
            }).ToList(),
            Labors = x.Labors.OrderBy(l => l.SortOrder).Select(l => new Item
            {
                PersistentId = l.CatalogItemId,
                Name = l.Name,
                Description = l.Description,
                UnitPrice = l.UnitPrice,
                Quantity = l.Quantity,
                Discount = l.Discount,
                IsSignificant = l.IsSignificant,
                SortOrder = l.SortOrder
            }).ToList(),
            PdfFile = null,
            Attachments = [],
            HasCompleteAttachmentSnapshot = false
        }).ToList();
    }

    public async Task EnsureAllHistoryPdfFilesAsync()
    {
        var storagePathService = StoragePathService.Instance;
        var quoteHistoryService = new QuoteHistoryService(this, storagePathService);

        var quotes = await GetQuotesAsync();

        foreach (var entry in quotes.OrderBy(x => x.Date))
        {
            await quoteHistoryService.EnsureOfficialPdfExistsAsync(entry);
        }
    }

    public async Task<List<QuoteHistoryEntry>> GetQuotesAsync()
    {
        await using var db = AppDbContextFactory.Create();

        var quotes = await db.Quotes
            .AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.ReferenceCustomer)
            .Include(x => x.BillingCustomer)
            .Include(x => x.Materials)
            .Include(x => x.Labors)
            .AsSplitQuery()
            .OrderByDescending(x => x.Date)
            .ToListAsync();

        return quotes.Select(x => new QuoteHistoryEntry
        {
            QuoteNumber = x.QuoteNumber,
            Date = x.Date,
            CustomerName = x.Customer?.BusinessName ?? string.Empty,
            CustomerSyncId = x.Customer?.SyncId ?? Guid.Empty,
            ReferenceName = x.ReferenceCustomer?.BusinessName ?? string.Empty,
            ReferenceCustomerSyncId = x.ReferenceCustomer?.SyncId ?? Guid.Empty,
            SiteName = x.SiteName,
            BillingCustomerName = x.BillingCustomerName,
            BillingCustomerSyncId = x.BillingCustomer?.SyncId ?? Guid.Empty,
            PdfPath = x.PdfPath,
            PaymentTerms = x.PaymentTerms,
            CustomerNotes = x.CustomerNotes,
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
            BaseVersionUtc = x.LastModifiedUtc,
            Revision = x.Revision,
            BaseRevision = x.Revision,
            Total = x.Total,
            Status = x.Status,
            CreatedByDevice = x.CreatedByDevice,
            LastModifiedByDevice = x.LastModifiedByDevice,
            SentAtUtc = x.SentAtUtc,
            SentMethod = x.SentMethod,
            SentRecipient = x.SentRecipient,
            SentByDevice = x.SentByDevice,
            LastReminderAtUtc = x.LastReminderAtUtc,
            ReminderCount = x.ReminderCount,
            LastReminderByDevice = x.LastReminderByDevice,
            Events = DeserializeQuoteEvents(x.EventsJson),
            SupplierName = x.SupplierName,
            MaterialOrderDate = x.MaterialOrderDate,
            ExpectedDeliveryDate = x.ExpectedDeliveryDate,
            MaterialStatus = x.MaterialStatus,
            MaterialsOrderedByCustomer = x.MaterialsOrderedByCustomer,
            RealProfit = DeserializeRealProfit(x.RealProfitJson),
            Materials = x.Materials.OrderBy(m => m.SortOrder).Select(m => new Item
            {
                PersistentId = m.CatalogItemId,
                Name = m.Name,
                Description = m.Description,
                UnitPrice = m.UnitPrice,
                Quantity = m.Quantity,
                Discount = m.Discount,
                IsSignificant = m.IsSignificant,
                SortOrder = m.SortOrder
            }).ToList(),
            Labors = x.Labors.OrderBy(l => l.SortOrder).Select(l => new Item
            {
                PersistentId = l.CatalogItemId,
                Name = l.Name,
                Description = l.Description,
                UnitPrice = l.UnitPrice,
                Quantity = l.Quantity,
                Discount = l.Discount,
                IsSignificant = l.IsSignificant,
                SortOrder = l.SortOrder
            }).ToList(),
            PdfFile = null,
            Attachments = [],
            HasCompleteAttachmentSnapshot = false
        }).ToList();
    }

    public async Task<List<QuoteHistoryEntry>> GetQuotesAsync(int take)
    {
        await using var db = AppDbContextFactory.Create();

        var quotes = await db.Quotes
            .AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.ReferenceCustomer)
            .Include(x => x.BillingCustomer)
            .Include(x => x.Materials)
            .Include(x => x.Labors)
            .AsSplitQuery()
            .OrderByDescending(x => x.Date)
            .Take(Math.Max(1, take))
            .ToListAsync();

        return quotes.Select(x => new QuoteHistoryEntry
        {
            QuoteNumber = x.QuoteNumber,
            Date = x.Date,
            CustomerName = x.Customer?.BusinessName ?? string.Empty,
            CustomerSyncId = x.Customer?.SyncId ?? Guid.Empty,
            ReferenceName = x.ReferenceCustomer?.BusinessName ?? string.Empty,
            ReferenceCustomerSyncId = x.ReferenceCustomer?.SyncId ?? Guid.Empty,
            SiteName = x.SiteName,
            BillingCustomerName = x.BillingCustomerName,
            BillingCustomerSyncId = x.BillingCustomer?.SyncId ?? Guid.Empty,
            PdfPath = x.PdfPath,
            PaymentTerms = x.PaymentTerms,
            CustomerNotes = x.CustomerNotes,
            IvaType = x.IvaType,
            Notes = x.Notes,
            Imponibile = x.Imponibile,
            MaterialDiscount = x.MaterialDiscount,
            LaborDiscount = x.LaborDiscount,
            Total = x.Total,
            Status = x.Status,
            CreatedByDevice = x.CreatedByDevice,
            LastModifiedByDevice = x.LastModifiedByDevice,
            SentAtUtc = x.SentAtUtc,
            SentMethod = x.SentMethod,
            SentRecipient = x.SentRecipient,
            SentByDevice = x.SentByDevice,
            LastReminderAtUtc = x.LastReminderAtUtc,
            ReminderCount = x.ReminderCount,
            LastReminderByDevice = x.LastReminderByDevice,
            Events = DeserializeQuoteEvents(x.EventsJson),
            SupplierName = x.SupplierName,
            MaterialOrderDate = x.MaterialOrderDate,
            ExpectedDeliveryDate = x.ExpectedDeliveryDate,
            MaterialStatus = x.MaterialStatus,
            MaterialsOrderedByCustomer = x.MaterialsOrderedByCustomer,
            RealProfit = DeserializeRealProfit(x.RealProfitJson),
            LastModifiedUtc = x.LastModifiedUtc,
            BaseVersionUtc = x.LastModifiedUtc,
            Revision = x.Revision,
            BaseRevision = x.Revision,
            SyncHash = x.SyncHash,
            Materials = x.Materials.OrderBy(m => m.SortOrder).Select(m => new Item
            {
                PersistentId = m.CatalogItemId,
                Name = m.Name,
                Description = m.Description,
                UnitPrice = m.UnitPrice,
                Quantity = m.Quantity,
                Discount = m.Discount,
                IsSignificant = m.IsSignificant,
                SortOrder = m.SortOrder
            }).ToList(),
            Labors = x.Labors.OrderBy(l => l.SortOrder).Select(l => new Item
            {
                PersistentId = l.CatalogItemId,
                Name = l.Name,
                Description = l.Description,
                UnitPrice = l.UnitPrice,
                Quantity = l.Quantity,
                Discount = l.Discount,
                IsSignificant = l.IsSignificant,
                SortOrder = l.SortOrder
            }).ToList(),
            PdfFile = null,       // Caricato on-demand
            Attachments = []      // Caricato on-demand
        }).ToList();
    }

    public async Task<List<QuoteHistorySummary>> GetQuoteSummariesAsync(
        int take,
        CancellationToken cancellationToken = default)
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
                SiteName = x.SiteName,
                BillingCustomerName = x.BillingCustomerName,
                PdfPath = x.PdfPath,
                Total = (decimal)x.Total,
                IvaType = x.IvaType,
                MaterialDiscount = x.MaterialDiscount,
                LaborDiscount = x.LaborDiscount,
                Status = x.Status,
                Notes = x.Notes,
                IsJointVenture = x.IsJointVenture,
                PartnerCompanyName = x.PartnerCompanyName,
                CreatedByDevice = x.CreatedByDevice,
                LastModifiedByDevice = x.LastModifiedByDevice,
                SentAtUtc = x.SentAtUtc,
                SentMethod = x.SentMethod,
                SentRecipient = x.SentRecipient,
                SentByDevice = x.SentByDevice,
                LastReminderAtUtc = x.LastReminderAtUtc,
                ReminderCount = x.ReminderCount,
                LastReminderByDevice = x.LastReminderByDevice,
                SupplierName = x.SupplierName,
                MaterialOrderDate = x.MaterialOrderDate,
                ExpectedDeliveryDate = x.ExpectedDeliveryDate,
                MaterialStatus = x.MaterialStatus,
                MaterialsOrderedByCustomer = x.MaterialsOrderedByCustomer
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<QuoteHistorySummary>> GetSentOpenQuoteSummariesAsync(
        DateTime sinceUtc,
        CancellationToken cancellationToken = default)
    {
        await using var db = AppDbContextFactory.Create();

        QuoteStatus[] excludedStatuses =
        [
            QuoteStatus.Confermato,
            QuoteStatus.Finito,
            QuoteStatus.Archiviato,
            QuoteStatus.Rifiutato
        ];

        return await db.Quotes
            .AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.ReferenceCustomer)
            .Where(x =>
                x.SentAtUtc.HasValue &&
                x.SentAtUtc.Value >= sinceUtc &&
                !excludedStatuses.Contains(x.Status))
            .OrderByDescending(x => x.SentAtUtc)
            .ThenByDescending(x => x.Date)
            .Select(x => new QuoteHistorySummary
            {
                QuoteNumber = x.QuoteNumber,
                Date = x.Date,
                CustomerName = x.Customer != null ? x.Customer.BusinessName : string.Empty,
                ReferenceName = x.ReferenceCustomer != null ? x.ReferenceCustomer.BusinessName : string.Empty,
                SiteName = x.SiteName,
                BillingCustomerName = x.BillingCustomerName,
                PdfPath = x.PdfPath,
                Total = (decimal)x.Total,
                IvaType = x.IvaType,
                MaterialDiscount = x.MaterialDiscount,
                LaborDiscount = x.LaborDiscount,
                Status = x.Status,
                Notes = x.Notes,
                IsJointVenture = x.IsJointVenture,
                PartnerCompanyName = x.PartnerCompanyName,
                CreatedByDevice = x.CreatedByDevice,
                LastModifiedByDevice = x.LastModifiedByDevice,
                SentAtUtc = x.SentAtUtc,
                SentMethod = x.SentMethod,
                SentRecipient = x.SentRecipient,
                SentByDevice = x.SentByDevice,
                LastReminderAtUtc = x.LastReminderAtUtc,
                ReminderCount = x.ReminderCount,
                LastReminderByDevice = x.LastReminderByDevice,
                SupplierName = x.SupplierName,
                MaterialOrderDate = x.MaterialOrderDate,
                ExpectedDeliveryDate = x.ExpectedDeliveryDate,
                MaterialStatus = x.MaterialStatus,
                MaterialsOrderedByCustomer = x.MaterialsOrderedByCustomer
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<QuoteHistorySummary>> SearchQuoteSummariesAsync(
        string searchText,
        int take,
        CancellationToken cancellationToken = default)
    {
        await using var db = AppDbContextFactory.Create();

        IQueryable<QuoteEntity> query = db.Quotes
            .AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.ReferenceCustomer);

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            string term = searchText.Trim().ToLower();
            query = query.Where(x =>
                x.QuoteNumber.ToLower().Contains(term) ||
                (x.Customer != null && x.Customer.BusinessName.ToLower().Contains(term)) ||
                (x.ReferenceCustomer != null && x.ReferenceCustomer.BusinessName.ToLower().Contains(term)) ||
                x.SiteName.ToLower().Contains(term) ||
                x.BillingCustomerName.ToLower().Contains(term) ||
                x.SupplierName.ToLower().Contains(term) ||
                x.MaterialStatus.ToLower().Contains(term));
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
                SiteName = x.SiteName,
                BillingCustomerName = x.BillingCustomerName,
                PdfPath = x.PdfPath,
                Total = (decimal)x.Total,
                IvaType = x.IvaType,
                MaterialDiscount = x.MaterialDiscount,
                LaborDiscount = x.LaborDiscount,
                Status = x.Status,
                Notes = x.Notes,
                IsJointVenture = x.IsJointVenture,
                PartnerCompanyName = x.PartnerCompanyName,
                CreatedByDevice = x.CreatedByDevice,
                LastModifiedByDevice = x.LastModifiedByDevice,
                SentAtUtc = x.SentAtUtc,
                SentMethod = x.SentMethod,
                SentRecipient = x.SentRecipient,
                SentByDevice = x.SentByDevice,
                LastReminderAtUtc = x.LastReminderAtUtc,
                ReminderCount = x.ReminderCount,
                LastReminderByDevice = x.LastReminderByDevice,
                SupplierName = x.SupplierName,
                MaterialOrderDate = x.MaterialOrderDate,
                ExpectedDeliveryDate = x.ExpectedDeliveryDate,
                MaterialStatus = x.MaterialStatus,
                MaterialsOrderedByCustomer = x.MaterialsOrderedByCustomer
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<QuoteHistorySummary>> GetSupplierOrderSummariesAsync(
        string searchText,
        int take,
        CancellationToken cancellationToken = default)
    {
        await using var db = AppDbContextFactory.Create();

        IQueryable<QuoteEntity> query = db.Quotes
            .AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.ReferenceCustomer)
            .Where(x =>
                x.MaterialsOrderedByCustomer ||
                (x.Status == QuoteStatus.Confermato &&
                 (x.SupplierName != string.Empty ||
                  x.MaterialOrderDate.HasValue ||
                  x.ExpectedDeliveryDate.HasValue ||
                  x.MaterialStatus != string.Empty)));

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            string term = searchText.Trim().ToLower();
            query = query.Where(x =>
                x.QuoteNumber.ToLower().Contains(term) ||
                (x.Customer != null && x.Customer.BusinessName.ToLower().Contains(term)) ||
                (x.ReferenceCustomer != null && x.ReferenceCustomer.BusinessName.ToLower().Contains(term)) ||
                x.SiteName.ToLower().Contains(term) ||
                x.BillingCustomerName.ToLower().Contains(term) ||
                x.SupplierName.ToLower().Contains(term) ||
                x.MaterialStatus.ToLower().Contains(term));
        }

        return await query
            .OrderByDescending(x => x.MaterialOrderDate.HasValue)
            .ThenByDescending(x => x.MaterialOrderDate)
            .ThenByDescending(x => x.Date)
            .Take(Math.Max(1, take))
            .Select(x => new QuoteHistorySummary
            {
                QuoteNumber = x.QuoteNumber,
                Date = x.Date,
                CustomerName = x.Customer != null ? x.Customer.BusinessName : string.Empty,
                ReferenceName = x.ReferenceCustomer != null ? x.ReferenceCustomer.BusinessName : string.Empty,
                SiteName = x.SiteName,
                BillingCustomerName = x.BillingCustomerName,
                PdfPath = x.PdfPath,
                Total = (decimal)x.Total,
                IvaType = x.IvaType,
                MaterialDiscount = x.MaterialDiscount,
                LaborDiscount = x.LaborDiscount,
                Status = x.Status,
                Notes = x.Notes,
                IsJointVenture = x.IsJointVenture,
                PartnerCompanyName = x.PartnerCompanyName,
                CreatedByDevice = x.CreatedByDevice,
                LastModifiedByDevice = x.LastModifiedByDevice,
                SentAtUtc = x.SentAtUtc,
                SentMethod = x.SentMethod,
                SentRecipient = x.SentRecipient,
                SentByDevice = x.SentByDevice,
                LastReminderAtUtc = x.LastReminderAtUtc,
                ReminderCount = x.ReminderCount,
                LastReminderByDevice = x.LastReminderByDevice,
                SupplierName = x.SupplierName,
                MaterialOrderDate = x.MaterialOrderDate,
                ExpectedDeliveryDate = x.ExpectedDeliveryDate,
                MaterialStatus = x.MaterialStatus,
                MaterialsOrderedByCustomer = x.MaterialsOrderedByCustomer
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<QuoteHistorySummary>> SearchQuoteSummariesAsync(
        string searchText,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        await using var db = AppDbContextFactory.Create();

        IQueryable<QuoteEntity> query = db.Quotes
            .AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.ReferenceCustomer);

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            string term = searchText.Trim().ToLower();
            query = query.Where(x =>
                x.QuoteNumber.ToLower().Contains(term) ||
                (x.Customer != null && x.Customer.BusinessName.ToLower().Contains(term)) ||
                (x.ReferenceCustomer != null && x.ReferenceCustomer.BusinessName.ToLower().Contains(term)) ||
                x.SiteName.ToLower().Contains(term) ||
                x.BillingCustomerName.ToLower().Contains(term) ||
                x.SupplierName.ToLower().Contains(term) ||
                x.MaterialStatus.ToLower().Contains(term));
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
                SiteName = x.SiteName,
                BillingCustomerName = x.BillingCustomerName,
                PdfPath = x.PdfPath,
                Total = (decimal)x.Total,
                IvaType = x.IvaType,
                MaterialDiscount = x.MaterialDiscount,
                LaborDiscount = x.LaborDiscount,
                Status = x.Status,
                Notes = x.Notes,
                IsJointVenture = x.IsJointVenture,
                PartnerCompanyName = x.PartnerCompanyName,
                CreatedByDevice = x.CreatedByDevice,
                LastModifiedByDevice = x.LastModifiedByDevice,
                SentAtUtc = x.SentAtUtc,
                SentMethod = x.SentMethod,
                SentRecipient = x.SentRecipient,
                SentByDevice = x.SentByDevice,
                LastReminderAtUtc = x.LastReminderAtUtc,
                ReminderCount = x.ReminderCount,
                LastReminderByDevice = x.LastReminderByDevice,
                SupplierName = x.SupplierName,
                MaterialOrderDate = x.MaterialOrderDate,
                ExpectedDeliveryDate = x.ExpectedDeliveryDate,
                MaterialStatus = x.MaterialStatus,
                MaterialsOrderedByCustomer = x.MaterialsOrderedByCustomer
            })
            .ToListAsync(cancellationToken);
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

    public async Task<QuoteHistoryEntry?> GetQuoteByNumberAsync(
        string quoteNumber,
        CancellationToken cancellationToken = default,
        bool includeAttachments = true)
    {
        await using var db = AppDbContextFactory.Create();

        IQueryable<QuoteEntity> query = db.Quotes
            .AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.ReferenceCustomer)
            .Include(x => x.BillingCustomer)
            .Include(x => x.Materials)
            .Include(x => x.Labors)
            .AsSplitQuery();

        if (includeAttachments)
            query = query.Include(x => x.Attachments);

        var q = await query
            .FirstOrDefaultAsync(x => x.QuoteNumber == quoteNumber, cancellationToken);

        if (q == null) return null;

        return new QuoteHistoryEntry
        {
            QuoteNumber = q.QuoteNumber,
            Date = q.Date,
            CustomerName = q.Customer?.BusinessName ?? string.Empty,
            CustomerSyncId = q.Customer?.SyncId ?? Guid.Empty,
            ReferenceName = q.ReferenceCustomer?.BusinessName ?? string.Empty,
            ReferenceCustomerSyncId = q.ReferenceCustomer?.SyncId ?? Guid.Empty,
            SiteName = q.SiteName,
            BillingCustomerName = q.BillingCustomerName,
            BillingCustomerSyncId = q.BillingCustomer?.SyncId ?? Guid.Empty,
            PdfPath = q.PdfPath,
            PaymentTerms = q.PaymentTerms,
            CustomerNotes = q.CustomerNotes,
            IvaType = q.IvaType,
            Notes = q.Notes,
            Imponibile = q.Imponibile,
            MaterialDiscount = q.MaterialDiscount,
            LaborDiscount = q.LaborDiscount,
            Total = q.Total,
            Status = q.Status,
            CreatedByDevice = q.CreatedByDevice,
            LastModifiedByDevice = q.LastModifiedByDevice,
            SentAtUtc = q.SentAtUtc,
            SentMethod = q.SentMethod,
            SentRecipient = q.SentRecipient,
            SentByDevice = q.SentByDevice,
            LastReminderAtUtc = q.LastReminderAtUtc,
            ReminderCount = q.ReminderCount,
            LastReminderByDevice = q.LastReminderByDevice,
            Events = DeserializeQuoteEvents(q.EventsJson),
            SupplierName = q.SupplierName,
            MaterialOrderDate = q.MaterialOrderDate,
            ExpectedDeliveryDate = q.ExpectedDeliveryDate,
            MaterialStatus = q.MaterialStatus,
            MaterialsOrderedByCustomer = q.MaterialsOrderedByCustomer,
            RealProfit = DeserializeRealProfit(q.RealProfitJson),
            LastModifiedUtc = q.LastModifiedUtc,
            BaseVersionUtc = q.LastModifiedUtc,
            Revision = q.Revision,
            BaseRevision = q.Revision,
            SyncHash = q.SyncHash,
            IsJointVenture = q.IsJointVenture,
            PartnerCompanyName = q.PartnerCompanyName,
            OurCosts = DeserializeCostAllocations(q.CostAllocationsJson)?.OurCosts ?? new(),
            PartnerCosts = DeserializeCostAllocations(q.CostAllocationsJson)?.PartnerCosts ?? new(),
            AdditionalCosts = DeserializeCostAllocations(q.CostAllocationsJson)?.AdditionalCosts ?? new(),
            Materials = q.Materials.OrderBy(m => m.SortOrder).Select(m => new Item
            {
                PersistentId = m.CatalogItemId,
                Name = m.Name, Description = m.Description, UnitPrice = m.UnitPrice,
                Quantity = m.Quantity, Discount = m.Discount, IsSignificant = m.IsSignificant,
                SortOrder = m.SortOrder
            }).ToList(),
            Labors = q.Labors.OrderBy(l => l.SortOrder).Select(l => new Item
            {
                PersistentId = l.CatalogItemId,
                Name = l.Name, Description = l.Description, UnitPrice = l.UnitPrice,
                Quantity = l.Quantity, Discount = l.Discount, IsSignificant = l.IsSignificant,
                SortOrder = l.SortOrder
            }).ToList(),
            PdfFile = null,
            Attachments = includeAttachments
                ? q.Attachments.Select(ToStoredFile).ToList()
                : [],
            HasCompleteAttachmentSnapshot = includeAttachments
        };
    }

    public async Task DeleteQuoteAsync(
        string quoteNumber,
        CancellationToken cancellationToken = default)
    {
        await using var db = AppDbContextFactory.Create();
        var strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var existing = await db.Quotes
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(x => x.QuoteNumber == quoteNumber, cancellationToken);
                if (existing != null)
                {
                    existing.IsDeleted = true;
                    existing.LastModifiedUtc = DateTime.UtcNow;
                    existing.Revision += 1;
                    await db.SaveChangesAsync(cancellationToken);
                }
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        });
    }

    public async Task<List<string>> GetDeletedQuoteNumbersAsync(CancellationToken cancellationToken = default)
    {
        await using var db = AppDbContextFactory.Create();
        return await db.Quotes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.IsDeleted)
            .Select(x => x.QuoteNumber)
            .ToListAsync(cancellationToken);
    }

    public Task UpdateQuoteNotesAsync(
        string quoteNumber,
        string notes,
        CancellationToken cancellationToken = default) =>
        UpdateQuoteMetadataAsync(quoteNumber, quote =>
        {
            quote.Notes = notes;
            quote.LastModifiedByDevice = DeviceNameService.GetCurrentDeviceName();
            AppendQuoteEvent(quote, "note", string.IsNullOrWhiteSpace(notes) ? "Note svuotate" : "Note aggiornate");
        }, cancellationToken);

    public Task UpdateQuoteStatusAsync(
        string quoteNumber,
        QuoteStatus status,
        CancellationToken cancellationToken = default) =>
        UpdateQuoteMetadataAsync(quoteNumber, quote =>
        {
            quote.Status = status;
            quote.LastModifiedByDevice = DeviceNameService.GetCurrentDeviceName();
            AppendQuoteEvent(quote, "stato", $"Stato aggiornato: {status}");
        }, cancellationToken);

    public Task UpdateQuoteSendInfoAsync(
        string quoteNumber,
        QuoteSendInfo sendInfo,
        CancellationToken cancellationToken = default) =>
        UpdateQuoteMetadataAsync(quoteNumber, quote =>
        {
            string deviceName = string.IsNullOrWhiteSpace(sendInfo.DeviceName)
                ? DeviceNameService.GetCurrentDeviceName()
                : sendInfo.DeviceName.Trim();

            quote.Status = QuoteStatus.Spedito;
            quote.SentAtUtc = sendInfo.SentAtUtc == default ? DateTime.UtcNow : sendInfo.SentAtUtc;
            quote.SentMethod = sendInfo.Method?.Trim() ?? string.Empty;
            quote.SentRecipient = sendInfo.Recipient?.Trim() ?? string.Empty;
            quote.SentByDevice = deviceName;
            quote.LastModifiedByDevice = deviceName;
            AppendQuoteEvent(
                quote,
                "invio",
                $"Preventivo inviato tramite {quote.SentMethod}".Trim(),
                deviceName,
                quote.SentAtUtc.Value);
        }, cancellationToken);

    public Task RegisterQuoteReminderAsync(
        string quoteNumber,
        QuoteReminderInfo reminderInfo,
        CancellationToken cancellationToken = default) =>
        UpdateQuoteMetadataAsync(quoteNumber, quote =>
        {
            string deviceName = string.IsNullOrWhiteSpace(reminderInfo.DeviceName)
                ? DeviceNameService.GetCurrentDeviceName()
                : reminderInfo.DeviceName.Trim();

            quote.Status = QuoteStatus.Spedito;
            quote.LastReminderAtUtc = reminderInfo.ReminderAtUtc == default ? DateTime.UtcNow : reminderInfo.ReminderAtUtc;
            quote.ReminderCount += 1;
            quote.LastReminderByDevice = deviceName;
            quote.LastModifiedByDevice = deviceName;
            AppendQuoteEvent(
                quote,
                "sollecito",
                $"Sollecito registrato (n. {quote.ReminderCount})",
                deviceName,
                quote.LastReminderAtUtc.Value);
        }, cancellationToken);

    public Task UpdateQuoteSupplierInfoAsync(
        string quoteNumber,
        QuoteSupplierInfo supplierInfo,
        CancellationToken cancellationToken = default) =>
        UpdateQuoteMetadataAsync(quoteNumber, quote =>
        {
            string deviceName = string.IsNullOrWhiteSpace(supplierInfo.DeviceName)
                ? DeviceNameService.GetCurrentDeviceName()
                : supplierInfo.DeviceName.Trim();

            quote.SupplierName = supplierInfo.SupplierName?.Trim() ?? string.Empty;
            quote.MaterialsOrderedByCustomer = supplierInfo.MaterialsOrderedByCustomer;
            quote.MaterialOrderDate = supplierInfo.MaterialOrderDate;
            quote.ExpectedDeliveryDate = supplierInfo.ExpectedDeliveryDate;
            quote.MaterialStatus = supplierInfo.MaterialStatus?.Trim() ?? string.Empty;
            quote.LastModifiedByDevice = deviceName;
            AppendQuoteEvent(
                quote,
                "fornitori",
                $"Dati fornitori aggiornati: {FormatSupplierEventDescription(quote)}",
                deviceName);
        }, cancellationToken);

    public Task UpdateQuoteRealProfitAsync(
        string quoteNumber,
        RealProfitSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return UpdateQuoteMetadataAsync(quoteNumber, quote =>
        {
            string deviceName = string.IsNullOrWhiteSpace(snapshot.CalculatedByDevice)
                ? DeviceNameService.GetCurrentDeviceName()
                : snapshot.CalculatedByDevice.Trim();

            snapshot.CalculatedByDevice = deviceName;
            snapshot.CalculatedAtUtc = snapshot.CalculatedAtUtc == default
                ? DateTime.UtcNow
                : snapshot.CalculatedAtUtc.ToUniversalTime();
            quote.RealProfitJson = JsonSerializer.Serialize(snapshot);
            quote.LastModifiedByDevice = deviceName;
            AppendQuoteEvent(
                quote,
                "guadagno-reale",
                $"Guadagno reale ricalcolato: {snapshot.Result.Profit:N2} euro",
                deviceName,
                snapshot.CalculatedAtUtc);
        }, cancellationToken);
    }

    private async Task UpdateQuoteMetadataAsync(
        string quoteNumber,
        Action<QuoteEntity> update,
        CancellationToken cancellationToken)
    {
        await using var db = AppDbContextFactory.Create();
        var quote = await db.Quotes
            .FirstOrDefaultAsync(x => x.QuoteNumber == quoteNumber, cancellationToken);
        if (quote == null)
            throw new InvalidOperationException($"Preventivo {quoteNumber} non trovato.");

        update(quote);
        quote.LastModifiedUtc = DateTime.UtcNow;
        quote.Revision += 1;
        await db.SaveChangesAsync(cancellationToken);

        var snapshot = (await GetQuoteSyncSnapshotsAsync([quoteNumber], cancellationToken)).Single();
        quote.SyncHash = QuoteSyncHashService.Compute(snapshot);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static QuoteHistoryEntry CreateLightEntry(QuoteHistoryEntry entry)
    {
        return new QuoteHistoryEntry
        {
            QuoteNumber = entry.QuoteNumber,
            Date = entry.Date,
            CustomerName = entry.CustomerName,
            CustomerSyncId = entry.CustomerSyncId,
            ReferenceName = entry.ReferenceName,
            ReferenceCustomerSyncId = entry.ReferenceCustomerSyncId,
            SiteName = entry.SiteName,
            BillingCustomerName = entry.BillingCustomerName,
            BillingCustomerSyncId = entry.BillingCustomerSyncId,
            PdfPath = entry.PdfPath,
            PaymentTerms = entry.PaymentTerms,
            CustomerNotes = entry.CustomerNotes,
            IvaType = entry.IvaType,
            Notes = entry.Notes,
            Imponibile = entry.Imponibile,
            MaterialDiscount = entry.MaterialDiscount,
            LaborDiscount = entry.LaborDiscount,
            Total = entry.Total,
            Status = entry.Status,
            CreatedByDevice = entry.CreatedByDevice,
            LastModifiedByDevice = entry.LastModifiedByDevice,
            SentAtUtc = entry.SentAtUtc,
            SentMethod = entry.SentMethod,
            SentRecipient = entry.SentRecipient,
            SentByDevice = entry.SentByDevice,
            LastReminderAtUtc = entry.LastReminderAtUtc,
            ReminderCount = entry.ReminderCount,
            LastReminderByDevice = entry.LastReminderByDevice,
            Events = entry.Events.ToList(),
            SupplierName = entry.SupplierName,
            MaterialOrderDate = entry.MaterialOrderDate,
            ExpectedDeliveryDate = entry.ExpectedDeliveryDate,
            MaterialStatus = entry.MaterialStatus,
            MaterialsOrderedByCustomer = entry.MaterialsOrderedByCustomer,
            RealProfit = entry.RealProfit,
            LastModifiedUtc = entry.LastModifiedUtc,
            BaseVersionUtc = entry.BaseVersionUtc,
            Revision = entry.Revision,
            BaseRevision = entry.BaseRevision,
            HasPendingDatabaseWrite = entry.HasPendingDatabaseWrite,
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

    public Task SaveQuoteAsync(
        QuoteHistoryEntry quote,
        CancellationToken cancellationToken = default) =>
        SaveQuoteWithExpectedRevisionAsync(quote, cancellationToken, expectedRevision: null);

    public async Task SaveQuoteWithExpectedRevisionAsync(
        QuoteHistoryEntry quote,
        CancellationToken cancellationToken,
        long? expectedRevision)
    {
        await using var db = AppDbContextFactory.Create();

        if (string.IsNullOrWhiteSpace(quote.SyncHash))
            quote.SyncHash = QuoteSyncHashService.Compute(quote);

        var strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var existing = await db.Quotes
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(x => x.QuoteNumber == quote.QuoteNumber, cancellationToken);

                if (expectedRevision.HasValue)
                {
                    bool matchesExpectedState = expectedRevision.Value == 0
                        ? existing == null
                        : existing != null && existing.Revision == expectedRevision.Value;
                    if (!matchesExpectedState)
                        throw new QuoteConflictException(quote.QuoteNumber);
                }

                CustomerEntity? customer = await GetOrCreateCustomerForQuoteAsync(
                    db,
                    quote.CustomerName,
                    quote.CustomerSyncId,
                    existing?.CustomerId,
                    cancellationToken);
                CustomerEntity? referenceCustomer = await GetOrCreateCustomerForQuoteAsync(
                    db,
                    quote.ReferenceName,
                    quote.ReferenceCustomerSyncId,
                    existing?.ReferenceCustomerId,
                    cancellationToken);
                CustomerEntity? billingCustomer = await GetOrCreateCustomerForQuoteAsync(
                    db,
                    quote.BillingCustomerName,
                    quote.BillingCustomerSyncId,
                    existing?.BillingCustomerId,
                    cancellationToken);

                if (existing != null)
                {
                    if (existing.IsDeleted)
                        throw new QuoteConflictException(quote.QuoteNumber);

                    // Un salvataggio completo nasce da una modifica esplicita dell'utente:
                    // in questo caso il contenuto aperto nell'editor e' autorevole e deve
                    // sostituire la versione attualmente presente nel database.

                    DateTime savedAtUtc = DateTime.UtcNow;
                    existing.Date = quote.Date;
                    existing.Customer = customer;
                    existing.CustomerId = customer?.Id;
                    existing.ReferenceCustomer = referenceCustomer;
                    existing.ReferenceCustomerId = referenceCustomer?.Id;
                    existing.BillingCustomer = billingCustomer;
                    existing.BillingCustomerId = billingCustomer?.Id;
                    existing.SiteName = quote.SiteName;
                    existing.BillingCustomerName = quote.BillingCustomerName;
                    existing.PdfPath = quote.PdfPath;
                    existing.PaymentTerms = quote.PaymentTerms;
                    existing.CustomerNotes = quote.CustomerNotes;
                    existing.IvaType = quote.IvaType;
                    existing.Notes = quote.Notes;
                    existing.Imponibile = quote.Imponibile;
                    existing.MaterialDiscount = quote.MaterialDiscount;
                    existing.LaborDiscount = quote.LaborDiscount;
                    existing.Total = quote.Total;
                    existing.Status = quote.Status;
                    existing.CreatedByDevice = quote.CreatedByDevice;
                    existing.LastModifiedByDevice = quote.LastModifiedByDevice;
                    existing.SentAtUtc = quote.SentAtUtc;
                    existing.SentMethod = quote.SentMethod;
                    existing.SentRecipient = quote.SentRecipient;
                    existing.SentByDevice = quote.SentByDevice;
                    existing.LastReminderAtUtc = quote.LastReminderAtUtc;
                    existing.ReminderCount = quote.ReminderCount;
                    existing.LastReminderByDevice = quote.LastReminderByDevice;
                    existing.EventsJson = SerializeQuoteEvents(quote.Events);
                    existing.SupplierName = quote.SupplierName;
                    existing.MaterialOrderDate = quote.MaterialOrderDate;
                    existing.ExpectedDeliveryDate = quote.ExpectedDeliveryDate;
                    existing.MaterialStatus = quote.MaterialStatus;
                    existing.MaterialsOrderedByCustomer = quote.MaterialsOrderedByCustomer;
                    existing.RealProfitJson = SerializeRealProfit(quote.RealProfit);
                    existing.LastModifiedUtc = savedAtUtc;
                    existing.Revision += 1;
                    existing.SyncHash = quote.SyncHash;
                    
                    existing.IsJointVenture = quote.IsJointVenture;
                    existing.PartnerCompanyName = quote.PartnerCompanyName;
                    existing.CostAllocationsJson = JsonSerializer.Serialize(new CostAllocations
                    {
                        OurCosts = quote.OurCosts,
                        PartnerCosts = quote.PartnerCosts,
                        AdditionalCosts = quote.AdditionalCosts
                    });
                    // Le collezioni precedenti vengono eliminate direttamente
                    // per QuoteId: caricarle con Include moltiplicava materiali,
                    // lavorazioni e blob allegati in un enorme prodotto cartesiano.
                    await db.QuoteMaterials
                        .Where(item => item.QuoteId == existing.Id)
                        .ExecuteDeleteAsync(cancellationToken);
                    await db.QuoteLabors
                        .Where(item => item.QuoteId == existing.Id)
                        .ExecuteDeleteAsync(cancellationToken);
                    if (quote.HasCompleteAttachmentSnapshot)
                    {
                        await db.QuoteAttachments
                            .Where(item => item.QuoteId == existing.Id)
                            .ExecuteDeleteAsync(cancellationToken);
                    }

                    existing.Materials = quote.Materials.Select(m => new QuoteMaterialEntity
                    {
                        CatalogItemId = m.PersistentId,
                        Name = m.Name,
                        Description = m.Description,
                        UnitPrice = m.UnitPrice,
                        Quantity = m.Quantity,
                        Discount = m.Discount,
                        IsSignificant = m.IsSignificant,
                        SortOrder = m.SortOrder
                    }).ToList();

                    existing.Labors = quote.Labors.Select(l => new QuoteLaborEntity
                    {
                        CatalogItemId = l.PersistentId,
                        Name = l.Name,
                        Description = l.Description,
                        UnitPrice = l.UnitPrice,
                        Quantity = l.Quantity,
                        Discount = l.Discount,
                        IsSignificant = l.IsSignificant,
                        SortOrder = l.SortOrder
                    }).ToList();

                    if (quote.HasCompleteAttachmentSnapshot)
                        existing.Attachments = quote.Attachments.Select(ToAttachmentEntity).ToList();

                    quote.LastModifiedUtc = savedAtUtc;
                    quote.BaseVersionUtc = savedAtUtc;
                    quote.Revision = existing.Revision;
                    quote.BaseRevision = existing.Revision;
                }
                else
                {
                    DateTime savedAtUtc = DateTime.UtcNow;
                    // Nuovo record
                    var entity = new QuoteEntity
                    {
                        QuoteNumber = quote.QuoteNumber,
                        Date = quote.Date,
                        Customer = customer,
                        CustomerId = customer?.Id,
                        ReferenceCustomer = referenceCustomer,
                        ReferenceCustomerId = referenceCustomer?.Id,
                        BillingCustomer = billingCustomer,
                        BillingCustomerId = billingCustomer?.Id,
                        SiteName = quote.SiteName,
                        BillingCustomerName = quote.BillingCustomerName,
                        PdfPath = quote.PdfPath,
                        PaymentTerms = quote.PaymentTerms,
                        CustomerNotes = quote.CustomerNotes,
                        IvaType = quote.IvaType,
                        Notes = quote.Notes,
                        Imponibile = quote.Imponibile,
                        Total = quote.Total,
                        MaterialDiscount = quote.MaterialDiscount,
                        LaborDiscount = quote.LaborDiscount,
                        Status = quote.Status,
                        CreatedByDevice = quote.CreatedByDevice,
                        LastModifiedByDevice = quote.LastModifiedByDevice,
                        SentAtUtc = quote.SentAtUtc,
                        SentMethod = quote.SentMethod,
                        SentRecipient = quote.SentRecipient,
                        SentByDevice = quote.SentByDevice,
                        LastReminderAtUtc = quote.LastReminderAtUtc,
                        ReminderCount = quote.ReminderCount,
                        LastReminderByDevice = quote.LastReminderByDevice,
                        EventsJson = SerializeQuoteEvents(quote.Events),
                        SupplierName = quote.SupplierName,
                        MaterialOrderDate = quote.MaterialOrderDate,
                        ExpectedDeliveryDate = quote.ExpectedDeliveryDate,
                        MaterialStatus = quote.MaterialStatus,
                        MaterialsOrderedByCustomer = quote.MaterialsOrderedByCustomer,
                        RealProfitJson = SerializeRealProfit(quote.RealProfit),
                        LastModifiedUtc = savedAtUtc,
                        Revision = 1,
                        SyncHash = quote.SyncHash,
                        IsJointVenture = quote.IsJointVenture,
                        PartnerCompanyName = quote.PartnerCompanyName,
                        CostAllocationsJson = JsonSerializer.Serialize(new CostAllocations
                        {
                            OurCosts = quote.OurCosts,
                            PartnerCosts = quote.PartnerCosts,
                            AdditionalCosts = quote.AdditionalCosts
                        }),
                        Materials = quote.Materials.Select(m => new QuoteMaterialEntity
                        {
                            CatalogItemId = m.PersistentId,
                            Name = m.Name,
                            Description = m.Description,
                            UnitPrice = m.UnitPrice,
                            Quantity = m.Quantity,
                            Discount = m.Discount,
                            IsSignificant = m.IsSignificant,
                            SortOrder = m.SortOrder
                        }).ToList(),
                        Labors = quote.Labors.Select(l => new QuoteLaborEntity
                        {
                            CatalogItemId = l.PersistentId,
                            Name = l.Name,
                            Description = l.Description,
                            UnitPrice = l.UnitPrice,
                            Quantity = l.Quantity,
                            Discount = l.Discount,
                            IsSignificant = l.IsSignificant,
                            SortOrder = l.SortOrder
                        }).ToList(),
                        Attachments = quote.HasCompleteAttachmentSnapshot
                            ? quote.Attachments.Select(ToAttachmentEntity).ToList()
                            : []
                    };

                    db.Quotes.Add(entity);
                    quote.LastModifiedUtc = savedAtUtc;
                    quote.BaseVersionUtc = savedAtUtc;
                    quote.Revision = 1;
                    quote.BaseRevision = 1;
                }

                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync();
                throw new QuoteConflictException(quote.QuoteNumber);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    private static StoredFile ToStoredFile(QuoteAttachmentEntity attachment) => new()
    {
        FileName = attachment.FileName,
        ContentType = attachment.ContentType,
        Content = attachment.Content,
        ImportedAt = attachment.ImportedAtUtc
    };

    private static QuoteAttachmentEntity ToAttachmentEntity(StoredFile attachment) => new()
    {
        FileName = System.IO.Path.GetFileName(attachment.FileName),
        ContentType = attachment.ContentType,
        Content = attachment.Content,
        ImportedAtUtc = attachment.ImportedAt == default ? DateTime.UtcNow : attachment.ImportedAt.ToUniversalTime()
    };

    private static async Task<CustomerEntity?> GetOrCreateCustomerForQuoteAsync(
        AppDbContext db,
        string? businessName,
        Guid preferredCustomerSyncId,
        int? preferredCustomerId,
        CancellationToken cancellationToken)
    {
        businessName = (businessName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(businessName))
            return null;

        if (preferredCustomerSyncId != Guid.Empty)
        {
            var stableCustomer = await db.Customers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    customer => customer.SyncId == preferredCustomerSyncId,
                    cancellationToken);
            if (stableCustomer != null)
            {
                if (stableCustomer.IsDeleted)
                {
                    stableCustomer.IsDeleted = false;
                    stableCustomer.LastModifiedUtc = DateTime.UtcNow;
                }

                return stableCustomer;
            }

            throw new InvalidOperationException(
                $"Il cliente '{businessName}' non e' ancora sincronizzato nel database. " +
                "Sincronizza prima l'anagrafica e riprova.");
        }

        if (preferredCustomerId.HasValue)
        {
            var preferredCustomer = await db.Customers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(customer => customer.Id == preferredCustomerId.Value, cancellationToken);
            if (preferredCustomer != null &&
                preferredCustomer.BusinessName.Equals(businessName, StringComparison.OrdinalIgnoreCase))
            {
                return preferredCustomer;
            }
        }

        var sameNameCustomers = await db.Customers
            .Where(customer => customer.BusinessName == businessName)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (sameNameCustomers.Count > 1)
        {
            throw new InvalidOperationException(
                $"Esistono piu' clienti chiamati '{businessName}': seleziona l'anagrafica con ID stabile prima di salvare.");
        }

        var customer = sameNameCustomers.SingleOrDefault();

        if (customer != null)
        {
            if (customer.IsDeleted)
            {
                customer.IsDeleted = false;
                customer.LastModifiedUtc = DateTime.UtcNow;
            }

            return customer;
        }

        customer = new CustomerEntity
        {
            SyncId = Guid.NewGuid(),
            BusinessName = businessName,
            LastModifiedUtc = DateTime.UtcNow,
            IsDeleted = false
        };
        db.Customers.Add(customer);
        return customer;
    }

    private static CostAllocations? DeserializeCostAllocations(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<CostAllocations>(json); }
        catch { return null; }
    }

    private static RealProfitSnapshot? DeserializeRealProfit(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try { return JsonSerializer.Deserialize<RealProfitSnapshot>(json); }
        catch { return null; }
    }

    private static string SerializeRealProfit(RealProfitSnapshot? snapshot) =>
        snapshot == null ? string.Empty : JsonSerializer.Serialize(snapshot);

    private static List<QuoteEventEntry> DeserializeQuoteEvents(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<QuoteEventEntry>();

        try { return JsonSerializer.Deserialize<List<QuoteEventEntry>>(json) ?? new List<QuoteEventEntry>(); }
        catch { return new List<QuoteEventEntry>(); }
    }

    private static string SerializeQuoteEvents(IEnumerable<QuoteEventEntry>? events)
    {
        return JsonSerializer.Serialize(events?.ToList() ?? new List<QuoteEventEntry>());
    }

    private static void AppendQuoteEvent(
        QuoteEntity quote,
        string eventType,
        string description,
        string? deviceName = null,
        DateTime? createdAtUtc = null)
    {
        var events = DeserializeQuoteEvents(quote.EventsJson);
        events.Add(new QuoteEventEntry
        {
            CreatedAtUtc = (createdAtUtc ?? DateTime.UtcNow).ToUniversalTime(),
            DeviceName = string.IsNullOrWhiteSpace(deviceName)
                ? DeviceNameService.GetCurrentDeviceName()
                : deviceName.Trim(),
            EventType = eventType,
            Description = description
        });

        quote.EventsJson = SerializeQuoteEvents(events);
    }

    private static string FormatSupplierEventDescription(QuoteEntity quote)
    {
        var parts = new List<string>();
        if (quote.MaterialsOrderedByCustomer)
            parts.Add("materiali ordinati dal cliente");
        if (!string.IsNullOrWhiteSpace(quote.SupplierName))
            parts.Add($"fornitore {quote.SupplierName}");
        if (quote.MaterialOrderDate.HasValue)
            parts.Add($"ordine {quote.MaterialOrderDate.Value:dd/MM/yyyy}");
        if (quote.ExpectedDeliveryDate.HasValue)
            parts.Add($"consegna prevista {quote.ExpectedDeliveryDate.Value:dd/MM/yyyy}");
        if (!string.IsNullOrWhiteSpace(quote.MaterialStatus))
            parts.Add($"stato {quote.MaterialStatus}");

        return parts.Count == 0 ? "campi svuotati" : string.Join(", ", parts);
    }
}
