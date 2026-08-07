using System.Globalization;

namespace EdilPaintPreventibiviGen.Android.Models;

public sealed class QuoteDetail
{
    private static readonly CultureInfo ItalianCulture = CultureInfo.GetCultureInfo("it-IT");

    public string QuoteNumber { get; init; } = string.Empty;
    public DateTime Date { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string ReferenceName { get; init; } = string.Empty;
    public string PaymentTerms { get; init; } = string.Empty;
    public string CustomerNotes { get; init; } = string.Empty;
    public string IvaType { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public double Imponibile { get; init; }
    public double Total { get; init; }
    public double MaterialDiscount { get; init; }
    public double LaborDiscount { get; init; }
    public QuoteStatus Status { get; init; }
    public DateTime? SentAtUtc { get; init; }
    public string SentRecipient { get; init; } = string.Empty;
    public string LastModifiedByDevice { get; init; } = string.Empty;
    public List<QuoteLine> Materials { get; init; } = [];
    public List<QuoteLine> Labors { get; init; } = [];

    public string Title => $"Preventivo {QuoteNumber}";
    public string TotalDisplay => Total.ToString("C", ItalianCulture);
    public string ImponibileDisplay => Imponibile.ToString("C", ItalianCulture);
    public string DateDisplay => Date.ToString("dd/MM/yyyy", ItalianCulture);
    public string IvaDisplay => FormatIva(IvaType);
    public string MaterialDiscountDisplay => FormatDiscount(MaterialDiscount);
    public string LaborDiscountDisplay => FormatDiscount(LaborDiscount);
    public string PaymentTermsDisplay => string.IsNullOrWhiteSpace(PaymentTerms) ? "-" : PaymentTerms.Trim();
    public string SentRecipientDisplay => string.IsNullOrWhiteSpace(SentRecipient) ? "-" : SentRecipient.Trim();
    public string LastModifiedByDeviceDisplay => string.IsNullOrWhiteSpace(LastModifiedByDevice) ? "-" : LastModifiedByDevice.Trim();
    public bool HasCustomerNotes => !string.IsNullOrWhiteSpace(CustomerNotes);
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
    public bool HasMaterials => Materials.Count > 0;
    public bool HasLabors => Labors.Count > 0;
    public string SentDisplay => SentAtUtc.HasValue
        ? SentAtUtc.Value.ToLocalTime().ToString("dd/MM/yyyy", ItalianCulture)
        : "Non inviato";
    public string CustomerReferenceDisplay => string.IsNullOrWhiteSpace(ReferenceName)
        ? CustomerName
        : $"{CustomerName} - Rif. {ReferenceName}";
    public string StatusText => Status switch
    {
        QuoteStatus.DaInviare => "Da inviare",
        QuoteStatus.DaSollecitare => "Da sollecitare",
        _ => Status.ToString()
    };

    private static string FormatDiscount(double value) =>
        Math.Abs(value) < 0.001 ? "Nessuno" : $"{value:0.#}%";

    private static string FormatIva(string? ivaType)
    {
        if (string.IsNullOrWhiteSpace(ivaType))
            return "-";

        string compact = ivaType
            .Trim()
            .Replace(" ", string.Empty)
            .Replace("%", string.Empty)
            .ToUpperInvariant();

        if (compact.Contains("10+22", StringComparison.Ordinal) ||
            compact.Contains("10/22", StringComparison.Ordinal) ||
            compact.Contains("10", StringComparison.Ordinal) && compact.Contains("22", StringComparison.Ordinal))
        {
            return "10+22";
        }

        if (compact.Contains("22", StringComparison.Ordinal))
            return "22%";

        if (compact.Contains("10", StringComparison.Ordinal))
            return "10%";

        if (compact.Contains("ESCLUSA", StringComparison.Ordinal) ||
            compact.Contains("NOIVA", StringComparison.Ordinal))
        {
            return "Esclusa";
        }

        return ivaType.Trim();
    }
}
