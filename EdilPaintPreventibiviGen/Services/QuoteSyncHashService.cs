using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

internal static class QuoteSyncHashService
{
    public static string Compute(QuoteHistoryEntry entry) =>
        ComputeCore(entry, includeCustomerIdentity: true, includeCatalogIdentity: true);

    public static string ComputeLegacy(QuoteHistoryEntry entry) =>
        ComputeCore(entry, includeCustomerIdentity: false, includeCatalogIdentity: false);

    private static string ComputeCore(
        QuoteHistoryEntry entry,
        bool includeCustomerIdentity,
        bool includeCatalogIdentity)
    {
        static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        var materialsHash = string.Join("|", entry.Materials
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Select(m => $"{(includeCatalogIdentity ? $"{m.PersistentId}:" : string.Empty)}{m.SortOrder}:{m.Name}:{m.Description}:{Number(m.UnitPrice)}:{m.Quantity}:{Number(m.Discount)}:{m.IsSignificant}"));

        var laborsHash = string.Join("|", entry.Labors
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .Select(l => $"{(includeCatalogIdentity ? $"{l.PersistentId}:" : string.Empty)}{l.SortOrder}:{l.Name}:{l.Description}:{Number(l.UnitPrice)}:{l.Quantity}:{Number(l.Discount)}:{l.IsSignificant}"));

        var costsHash =
            string.Join("|", entry.OurCosts.Select(c => $"{c.Description}:{Number(c.Amount)}:{c.Notes}")) + "|" +
            string.Join("|", entry.PartnerCosts.Select(c => $"{c.Description}:{Number(c.Amount)}:{c.Notes}")) + "|" +
            string.Join("|", entry.AdditionalCosts.Select(c => $"{c.Description}:{Number(c.Amount)}:{c.Notes}"));

        var eventsHash = string.Join("|", entry.Events
            .OrderBy(e => e.CreatedAtUtc)
            .ThenBy(e => e.EventType, StringComparer.OrdinalIgnoreCase)
            .Select(e => $"{e.CreatedAtUtc.ToUniversalTime():O}:{e.DeviceName}:{e.EventType}:{e.Description}"));

        string realProfitHash = entry.RealProfit == null
            ? string.Empty
            : JsonSerializer.Serialize(entry.RealProfit);

        var commonPrefix = string.Join("|",
            entry.QuoteNumber,
            entry.Date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            entry.CustomerName,
            entry.ReferenceName);
        var commonSuffix = string.Join("|",
            entry.SiteName,
            entry.BillingCustomerName,
            entry.PaymentTerms,
            entry.CustomerNotes,
            entry.IvaType,
            entry.Notes,
            Number(entry.Imponibile),
            Number(entry.MaterialDiscount),
            Number(entry.LaborDiscount),
            Number(entry.Total),
            entry.Status,
            entry.CreatedByDevice,
            entry.LastModifiedByDevice,
            entry.SentAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            entry.SentMethod,
            entry.SentRecipient,
            entry.SentByDevice,
            entry.LastReminderAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            entry.ReminderCount.ToString(CultureInfo.InvariantCulture),
            entry.LastReminderByDevice,
            entry.SupplierName,
            entry.MaterialsOrderedByCustomer,
            entry.MaterialOrderDate?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            entry.ExpectedDeliveryDate?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            entry.MaterialStatus,
            entry.IsJointVenture,
            entry.PartnerCompanyName,
            materialsHash,
            laborsHash,
            costsHash,
            realProfitHash,
            eventsHash);
        var data = includeCustomerIdentity
            ? string.Join(
                "|",
                commonPrefix,
                $"customer-sync-v1:{entry.CustomerSyncId:N}:{entry.ReferenceCustomerSyncId:N}:{entry.BillingCustomerSyncId:N}",
                commonSuffix)
            : string.Join("|", commonPrefix, commonSuffix);

        var bytes = Encoding.UTF8.GetBytes(data);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }
}
