using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public static class SupplierOrderQueue
{
    public const string PendingGroup = "Da ordinare";

    public static bool NeedsOrdering(QuoteHistorySummary quote) => quote.Status == QuoteStatus.Confermato &&
        !quote.MaterialsOrderedByCustomer &&
        (string.Equals(quote.MaterialStatus?.Trim(), PendingGroup, StringComparison.OrdinalIgnoreCase) ||
         string.IsNullOrWhiteSpace(quote.MaterialStatus) && !quote.MaterialOrderDate.HasValue);

    public static string StatusDisplay(QuoteHistorySummary quote) => NeedsOrdering(quote) ? PendingGroup : quote.MaterialStatusDisplay;

    public static string GroupName(QuoteHistorySummary quote, bool groupBySupplier) => NeedsOrdering(quote)
        ? PendingGroup : groupBySupplier ? quote.OrderSupplierGroup : "Ordini registrati";

    public static IReadOnlyList<QuoteHistorySummary> Sort(IEnumerable<QuoteHistorySummary> quotes,
        SupplierOrderSortMode mode, bool groupBySupplier)
    {
        var rows = quotes.ToList();
        var pending = SupplierOrderSortService.Sort(rows.Where(NeedsOrdering), mode);
        var registered = rows.Where(row => !NeedsOrdering(row));
        return pending.Concat(groupBySupplier ? SupplierOrderSortService.SortBySupplier(registered, mode)
            : SupplierOrderSortService.Sort(registered, mode)).ToList();
    }
}
