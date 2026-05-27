using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

internal static class QuoteSyncHashService
{
    public static string Compute(QuoteHistoryEntry entry)
    {
        static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        var materialsHash = string.Join("|", entry.Materials
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Select(m => $"{m.SortOrder}:{m.Name}:{m.Description}:{Number(m.UnitPrice)}:{m.Quantity}:{Number(m.Discount)}:{m.IsSignificant}"));

        var laborsHash = string.Join("|", entry.Labors
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .Select(l => $"{l.SortOrder}:{l.Name}:{l.Description}:{Number(l.UnitPrice)}:{l.Quantity}:{Number(l.Discount)}:{l.IsSignificant}"));

        var costsHash =
            string.Join("|", entry.OurCosts.Select(c => $"{c.Description}:{Number(c.Amount)}:{c.Notes}")) + "|" +
            string.Join("|", entry.PartnerCosts.Select(c => $"{c.Description}:{Number(c.Amount)}:{c.Notes}")) + "|" +
            string.Join("|", entry.AdditionalCosts.Select(c => $"{c.Description}:{Number(c.Amount)}:{c.Notes}"));

        var attachmentsHash = string.Join("|", entry.Attachments
            .OrderBy(a => a.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(a => $"{a.FileName}:{a.ContentType}"));

        var pdfState = entry.PdfFile == null ? "no-pdf" : "has-pdf";

        var data = string.Join("|",
            entry.QuoteNumber,
            entry.Date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            entry.CustomerName,
            entry.ReferenceName,
            entry.PaymentTerms,
            entry.IvaType,
            entry.Notes,
            Number(entry.Imponibile),
            Number(entry.MaterialDiscount),
            Number(entry.LaborDiscount),
            Number(entry.Total),
            entry.Status,
            entry.IsJointVenture,
            entry.PartnerCompanyName,
            materialsHash,
            laborsHash,
            costsHash,
            attachmentsHash,
            pdfState);

        var bytes = Encoding.UTF8.GetBytes(data);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }
}
