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
    public async Task<Dictionary<string, QuoteMetadata>> GetQuoteMetadataAsync(CancellationToken cancellationToken = default)
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
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return metadata.ToDictionary(
            q => q.QuoteNumber,
            q => q,
            StringComparer.OrdinalIgnoreCase);
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

        var quotes = await db.Quotes
            .AsNoTracking()
            .Where(x => numberList.Contains(x.QuoteNumber))
            .Select(x => new
            {
                x.QuoteNumber,
                x.Date,
                CustomerName = x.Customer != null ? x.Customer.BusinessName : string.Empty,
                ReferenceName = x.ReferenceCustomer != null ? x.ReferenceCustomer.BusinessName : string.Empty,
                x.PaymentTerms,
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
                x.LastModifiedUtc,
                x.IsJointVenture,
                x.PartnerCompanyName,
                x.CostAllocationsJson,
                Materials = x.Materials
                    .OrderBy(m => m.SortOrder)
                    .Select(m => new Item
                    {
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
                ReferenceName = x.ReferenceName,
                PaymentTerms = x.PaymentTerms,
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
                IsJointVenture = x.IsJointVenture,
                PartnerCompanyName = x.PartnerCompanyName,
                OurCosts = costs?.OurCosts ?? [],
                PartnerCosts = costs?.PartnerCosts ?? [],
                AdditionalCosts = costs?.AdditionalCosts ?? [],
                LastModifiedUtc = x.LastModifiedUtc,
                BaseVersionUtc = x.LastModifiedUtc,
                Materials = x.Materials,
                Labors = x.Labors
            };
        }).ToList();
    }

    public async Task UpdateQuoteSyncHashesAsync(
        IReadOnlyDictionary<string, string> updates,
        CancellationToken cancellationToken = default)
    {
        if (updates.Count == 0)
            return;

        await using var db = AppDbContextFactory.Create();
        var quoteNumbers = updates.Keys.ToList();
        var quotes = await db.Quotes
            .Where(x => quoteNumbers.Contains(x.QuoteNumber))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var quote in quotes)
        {
            if (updates.TryGetValue(quote.QuoteNumber, out var syncHash))
                quote.SyncHash = syncHash;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<QuoteHistoryEntry>> GetQuotesByNumbersAsync(
        IEnumerable<string> quoteNumbers,
        CancellationToken cancellationToken = default)
    {
        await using var db = AppDbContextFactory.Create();
    
        var numberList = quoteNumbers.ToList();
    
        var quotes = await db.Quotes
            .AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.ReferenceCustomer)
            .Include(x => x.Materials)
            .Include(x => x.Labors)
            .Where(x => numberList.Contains(x.QuoteNumber))
            .ToListAsync(cancellationToken)
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
            LastModifiedUtc = x.LastModifiedUtc,
            BaseVersionUtc = x.LastModifiedUtc,
            SyncHash = x.SyncHash,
            IsJointVenture = x.IsJointVenture,
            PartnerCompanyName = x.PartnerCompanyName,
            OurCosts = DeserializeCostAllocations(x.CostAllocationsJson)?.OurCosts ?? new(),
            PartnerCosts = DeserializeCostAllocations(x.CostAllocationsJson)?.PartnerCosts ?? new(),
            AdditionalCosts = DeserializeCostAllocations(x.CostAllocationsJson)?.AdditionalCosts ?? new(),
            Materials = x.Materials.OrderBy(m => m.SortOrder).Select(m => new Item
            {
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
                Name = l.Name,
                Description = l.Description,
                UnitPrice = l.UnitPrice,
                Quantity = l.Quantity,
                Discount = l.Discount,
                IsSignificant = l.IsSignificant,
                SortOrder = l.SortOrder
            }).ToList(),
            PdfFile = null,
            Attachments = []
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
            .Include(x => x.Materials)
            .Include(x => x.Labors)
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
            BaseVersionUtc = x.LastModifiedUtc,
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
            Materials = x.Materials.OrderBy(m => m.SortOrder).Select(m => new Item
            {
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
                Name = l.Name,
                Description = l.Description,
                UnitPrice = l.UnitPrice,
                Quantity = l.Quantity,
                Discount = l.Discount,
                IsSignificant = l.IsSignificant,
                SortOrder = l.SortOrder
            }).ToList(),
            PdfFile = null,
            Attachments = []
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
            Materials = x.Materials.OrderBy(m => m.SortOrder).Select(m => new Item
            {
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
                LastReminderByDevice = x.LastReminderByDevice
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
                LastReminderByDevice = x.LastReminderByDevice
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
                LastReminderByDevice = x.LastReminderByDevice
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

    public async Task<QuoteHistoryEntry?> GetQuoteByNumberAsync(string quoteNumber)
    {
        await using var db = AppDbContextFactory.Create();

        var q = await db.Quotes
            .AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.ReferenceCustomer)
            .Include(x => x.Materials)
            .Include(x => x.Labors)
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
            LastModifiedUtc = q.LastModifiedUtc,
            BaseVersionUtc = q.LastModifiedUtc,
            SyncHash = q.SyncHash,
            IsJointVenture = q.IsJointVenture,
            PartnerCompanyName = q.PartnerCompanyName,
            OurCosts = DeserializeCostAllocations(q.CostAllocationsJson)?.OurCosts ?? new(),
            PartnerCosts = DeserializeCostAllocations(q.CostAllocationsJson)?.PartnerCosts ?? new(),
            AdditionalCosts = DeserializeCostAllocations(q.CostAllocationsJson)?.AdditionalCosts ?? new(),
            Materials = q.Materials.OrderBy(m => m.SortOrder).Select(m => new Item
            {
                Name = m.Name, Description = m.Description, UnitPrice = m.UnitPrice,
                Quantity = m.Quantity, Discount = m.Discount, IsSignificant = m.IsSignificant,
                SortOrder = m.SortOrder
            }).ToList(),
            Labors = q.Labors.OrderBy(l => l.SortOrder).Select(l => new Item
            {
                Name = l.Name, Description = l.Description, UnitPrice = l.UnitPrice,
                Quantity = l.Quantity, Discount = l.Discount, IsSignificant = l.IsSignificant,
                SortOrder = l.SortOrder
            }).ToList(),
            PdfFile = null,
            Attachments = []
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
                var existing = await db.Quotes
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(x => x.QuoteNumber == quoteNumber);
                if (existing != null)
                {
                    existing.IsDeleted = true;
                    existing.LastModifiedUtc = DateTime.UtcNow;
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
            LastModifiedUtc = entry.LastModifiedUtc,
            BaseVersionUtc = entry.BaseVersionUtc,
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

    public async Task SaveQuoteAsync(QuoteHistoryEntry quote, CancellationToken cancellationToken = default)
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
                CustomerEntity? customer = null;
                CustomerEntity? referenceCustomer = null;

                if (!string.IsNullOrWhiteSpace(quote.CustomerName))
                {
                    customer = await db.Customers
                        .FirstOrDefaultAsync(x => x.BusinessName == quote.CustomerName, cancellationToken);
                }

                if (!string.IsNullOrWhiteSpace(quote.ReferenceName))
                {
                    referenceCustomer = await db.Customers
                        .FirstOrDefaultAsync(x => x.BusinessName == quote.ReferenceName, cancellationToken);
                }

                var existing = await db.Quotes
                    .IgnoreQueryFilters()
                    .Include(x => x.Materials)
                    .Include(x => x.Labors)
                    .FirstOrDefaultAsync(x => x.QuoteNumber == quote.QuoteNumber, cancellationToken);

                if (existing != null)
                {
                    if (existing.IsDeleted)
                        throw new QuoteConflictException(quote.QuoteNumber);

                    if (quote.BaseVersionUtc != default &&
                        existing.LastModifiedUtc > quote.BaseVersionUtc.AddMilliseconds(1))
                    {
                        throw new QuoteConflictException(quote.QuoteNumber);
                    }

                    DateTime savedAtUtc = DateTime.UtcNow;
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
                    existing.LastModifiedUtc = savedAtUtc;
                    existing.SyncHash = quote.SyncHash;
                    
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
                        IsSignificant = m.IsSignificant,
                        SortOrder = m.SortOrder
                    }).ToList();

                    existing.Labors = quote.Labors.Select(l => new QuoteLaborEntity
                    {
                        Name = l.Name,
                        Description = l.Description,
                        UnitPrice = l.UnitPrice,
                        Quantity = l.Quantity,
                        Discount = l.Discount,
                        IsSignificant = l.IsSignificant,
                        SortOrder = l.SortOrder
                    }).ToList();

                    quote.LastModifiedUtc = savedAtUtc;
                    quote.BaseVersionUtc = savedAtUtc;
                }
                else
                {
                    DateTime savedAtUtc = DateTime.UtcNow;
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
                        LastModifiedUtc = savedAtUtc,
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
                            IsSignificant = m.IsSignificant,
                            SortOrder = m.SortOrder
                        }).ToList(),
                        Labors = quote.Labors.Select(l => new QuoteLaborEntity
                        {
                            Name = l.Name,
                            Description = l.Description,
                            UnitPrice = l.UnitPrice,
                            Quantity = l.Quantity,
                            Discount = l.Discount,
                            IsSignificant = l.IsSignificant,
                            SortOrder = l.SortOrder
                        }).ToList()
                    };

                    db.Quotes.Add(entity);
                    quote.LastModifiedUtc = savedAtUtc;
                    quote.BaseVersionUtc = savedAtUtc;
                }

                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    private static CostAllocations? DeserializeCostAllocations(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<CostAllocations>(json); }
        catch { return null; }
    }

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
}

