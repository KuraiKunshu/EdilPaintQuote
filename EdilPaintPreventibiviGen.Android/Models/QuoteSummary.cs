using System.Globalization;

namespace EdilPaintPreventibiviGen.Android.Models;

public sealed class QuoteSummary
{
    private static readonly CultureInfo ItalianCulture = CultureInfo.GetCultureInfo("it-IT");

    public string QuoteNumber { get; init; } = string.Empty;
    public DateTime Date { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string ReferenceName { get; init; } = string.Empty;
    public double Total { get; init; }
    public string IvaType { get; init; } = string.Empty;
    public QuoteStatus Status { get; init; }
    public DateTime? SentAtUtc { get; init; }
    public bool HasNotes { get; init; }

    public string TotalDisplay => Total.ToString("C", ItalianCulture);
    public string DateDisplay => Date.ToString("dd/MM/yyyy", ItalianCulture);
    public string QuoteMetaDisplay => $"Preventivo {QuoteNumber} - {DateDisplay}";

    public string ReferenceDisplay => string.IsNullOrWhiteSpace(ReferenceName)
        ? "Nessun riferimento"
        : $"Rif. {ReferenceName}";

    public string CustomerReferenceDisplay => string.IsNullOrWhiteSpace(ReferenceName)
        ? CustomerName
        : $"{CustomerName} - Rif. {ReferenceName}";

    public string StatusText => Status switch
    {
        QuoteStatus.DaInviare => "Da inviare",
        QuoteStatus.DaSollecitare => "Da sollecitare",
        _ => Status.ToString()
    };
}
