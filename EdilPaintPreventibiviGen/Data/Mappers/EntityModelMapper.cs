using System.Text.Json;
using EdilPaintPreventibiviGen.Data.Entities;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;

namespace EdilPaintPreventibiviGen.Data.Mappers;

public static class EntityModelMapper
{
    public static Customer ToModel(this CustomerEntity entity)
    {
        return new Customer
        {
            SyncId = entity.SyncId,
            BusinessName = entity.BusinessName,
            Address = entity.Address,
            Email = entity.Email,
            Phone = entity.Phone,
            MaterialDiscount = entity.MaterialDiscount,
            LaborDiscount = entity.LaborDiscount,
            LastModifiedUtc = entity.LastModifiedUtc,
            BaseVersionUtc = entity.LastModifiedUtc,
            HasPendingDatabaseWrite = false
        };
    }

    public static CustomerEntity ToEntity(this Customer model)
    {
        return new CustomerEntity
        {
            SyncId = model.SyncId,
            BusinessName = model.BusinessName,
            Address = model.Address,
            Email = model.Email,
            Phone = model.Phone,
            MaterialDiscount = model.MaterialDiscount,
            LaborDiscount = model.LaborDiscount,
            LastModifiedUtc = model.LastModifiedUtc
        };
    }

    public static Company ToModel(this CompanySettingsEntity entity)
    {
        return new Company
        {
            Nome = entity.Nome,
            Indirizzo = entity.Indirizzo,
            Piva = entity.Piva,
            Email = entity.Email,
            Logo_index = entity.LogoIndex,
            Counter = entity.Counter,
            Termini_pagamento = entity.PaymentTerms,
            Logo = string.IsNullOrWhiteSpace(entity.LogosJson)
                ? new List<string>()
                : (JsonSerializer.Deserialize<List<string>>(entity.LogosJson) ?? new List<string>())
        };
    }

    public static CompanySettingsEntity ToEntity(this Company model, string selectedLogo)
    {
        return new CompanySettingsEntity
        {
            Nome = model.Nome,
            Indirizzo = model.Indirizzo,
            Piva = model.Piva,
            Email = model.Email,
            LogoIndex = model.Logo_index,
            Counter = model.Counter,
            PaymentTerms = model.Termini_pagamento,
            SelectedLogo = selectedLogo,
            LogosJson = JsonSerializer.Serialize(model.Logo ?? new List<string>())
        };
    }

    public static Item ToModel(this PersonalMaterialEntity entity)
    {
        return new Item
        {
            PersistentId = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            UnitPrice = entity.UnitPrice,
            Quantity = 1,
            IsSignificant = entity.IsSignificant
        };
    }

    public static PersonalMaterialEntity ToPersonalMaterialEntity(this Item model)
    {
        return new PersonalMaterialEntity
        {
            Id = model.PersistentId,
            Name = model.Name,
            Description = model.Description,
            UnitPrice = model.UnitPrice,
            IsSignificant = model.IsSignificant
        };
    }

    public static Item ToModel(this LaborCatalogEntity entity)
    {
        return new Item
        {
            PersistentId = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            UnitPrice = entity.UnitPrice,
            Quantity = 1
        };
    }

    public static LaborCatalogEntity ToLaborCatalogEntity(this Item model)
    {
        return new LaborCatalogEntity
        {
            Id = model.PersistentId,
            Name = model.Name,
            Description = model.Description,
            UnitPrice = model.UnitPrice
        };
    }

    public static QuoteHistoryEntry ToModel(this QuoteEntity entity)
    {
        CostAllocations? costAlloc = null;
        if (!string.IsNullOrWhiteSpace(entity.CostAllocationsJson))
        {
            try { costAlloc = JsonSerializer.Deserialize<CostAllocations>(entity.CostAllocationsJson); }
            catch { /* ignora JSON malformato */ }
        }

        List<QuoteEventEntry> events = new();
        if (!string.IsNullOrWhiteSpace(entity.EventsJson))
        {
            try { events = JsonSerializer.Deserialize<List<QuoteEventEntry>>(entity.EventsJson) ?? new(); }
            catch { /* ignora JSON malformato */ }
        }

        return new QuoteHistoryEntry
        {
            QuoteNumber = entity.QuoteNumber,
            Date = entity.Date,
            CustomerName = entity.Customer?.BusinessName ?? string.Empty,
            ReferenceName = entity.ReferenceCustomer?.BusinessName ?? string.Empty,
            PdfPath = entity.PdfPath,
            PaymentTerms = entity.PaymentTerms,
            IvaType = entity.IvaType,
            Notes = entity.Notes,
            Imponibile = entity.Imponibile,
            MaterialDiscount = entity.MaterialDiscount,
            LaborDiscount = entity.LaborDiscount,
            Total = entity.Total,
            Status = entity.Status,
            CreatedByDevice = entity.CreatedByDevice,
            LastModifiedByDevice = entity.LastModifiedByDevice,
            SentAtUtc = entity.SentAtUtc,
            SentMethod = entity.SentMethod,
            SentRecipient = entity.SentRecipient,
            SentByDevice = entity.SentByDevice,
            LastReminderAtUtc = entity.LastReminderAtUtc,
            ReminderCount = entity.ReminderCount,
            LastReminderByDevice = entity.LastReminderByDevice,
            Events = events,
            IsJointVenture = entity.IsJointVenture,
            PartnerCompanyName = entity.PartnerCompanyName,
            OurCosts = costAlloc?.OurCosts ?? new(),
            PartnerCosts = costAlloc?.PartnerCosts ?? new(),
            AdditionalCosts = costAlloc?.AdditionalCosts ?? new(),
            LastModifiedUtc = entity.LastModifiedUtc,
            BaseVersionUtc = entity.LastModifiedUtc,
            Revision = entity.Revision,
            BaseRevision = entity.Revision,
            SyncHash = entity.SyncHash,
            Materials = entity.Materials
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
            Labors = entity.Labors
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
                .ToList()
        };
    }
}
