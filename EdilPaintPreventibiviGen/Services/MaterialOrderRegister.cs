using System.Globalization;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public sealed record MaterialOrderRow(QuoteHistorySummary Quote)
{
    public string Number => Quote.QuoteNumber;
    public string Job => Quote.OrderSiteTitle;
    public string Supplier => Quote.OrderSupplierGroup;
    public DateTime? OrderDate => Quote.MaterialOrderDate;
    public string OrderDateText => OrderDate?.ToString("dd/MM/yyyy") ?? "Non indicata";
    public string DeliveryText => Quote.ExpectedDeliveryDate?.ToString("dd/MM/yyyy") ?? "Non indicata";
    public string MaterialStatus => string.IsNullOrWhiteSpace(Quote.MaterialStatus) ? "Da impostare" : Quote.MaterialStatus;
    public string JobStatus => Quote.Status.ToString();
}

public static class MaterialOrderRegister
{
    public static bool IsRecorded(QuoteHistorySummary quote) => quote.MaterialOrderDate.HasValue ||
        quote.ExpectedDeliveryDate.HasValue || quote.MaterialsOrderedByCustomer ||
        !string.IsNullOrWhiteSpace(quote.SupplierName) || !string.IsNullOrWhiteSpace(quote.MaterialStatus);

    public static IReadOnlyList<MaterialOrderRow> Filter(IEnumerable<QuoteHistorySummary> quotes, string search = "",
        string? supplier = null, DateTime? from = null, DateTime? to = null)
    {
        if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
            throw new ArgumentException("La data iniziale deve precedere la data finale.");
        var terms = search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var compare = CultureInfo.GetCultureInfo("it-IT").CompareInfo;
        return quotes.Where(IsRecorded).Select(quote => new MaterialOrderRow(quote))
            .Where(row => (supplier == null || string.Equals(row.Supplier, supplier, StringComparison.CurrentCultureIgnoreCase)) &&
                (!from.HasValue || row.OrderDate?.Date >= from.Value.Date) &&
                (!to.HasValue || row.OrderDate?.Date <= to.Value.Date) &&
                terms.All(term => compare.IndexOf($"{row.Number} {row.Job} {row.Supplier} {row.MaterialStatus} {row.JobStatus}",
                    term, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0))
            .OrderByDescending(row => row.OrderDate.HasValue).ThenByDescending(row => row.OrderDate)
            .ThenByDescending(row => row.Quote.Date).ThenBy(row => row.Number, StringComparer.Ordinal).ToList();
    }
}
