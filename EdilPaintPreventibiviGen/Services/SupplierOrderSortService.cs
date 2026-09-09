using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public enum SupplierOrderSortMode
{
    OrderDateDescending,
    OrderDateAscending,
    ExpectedDeliveryAscending,
    ExpectedDeliveryDescending,
    CustomerAscending,
    CustomerDescending,
    Status
}

public sealed record SupplierOrderSortOption(SupplierOrderSortMode Mode, string Label)
{
    public override string ToString() => Label;
}

public static class SupplierOrderSortService
{
    public static IReadOnlyList<SupplierOrderSortOption> Options { get; } =
    [
        new(SupplierOrderSortMode.OrderDateDescending, "Data ordine - più recenti"),
        new(SupplierOrderSortMode.OrderDateAscending, "Data ordine - meno recenti"),
        new(SupplierOrderSortMode.ExpectedDeliveryAscending, "Consegna - più vicina"),
        new(SupplierOrderSortMode.ExpectedDeliveryDescending, "Consegna - più lontana"),
        new(SupplierOrderSortMode.CustomerAscending, "Cliente - A/Z"),
        new(SupplierOrderSortMode.CustomerDescending, "Cliente - Z/A"),
        new(SupplierOrderSortMode.Status, "Stato ordine")
    ];

    public static IReadOnlyList<QuoteHistorySummary> Sort(
        IEnumerable<QuoteHistorySummary> orders,
        SupplierOrderSortMode mode)
    {
        ArgumentNullException.ThrowIfNull(orders);

        IOrderedEnumerable<QuoteHistorySummary> sorted = mode switch
        {
            SupplierOrderSortMode.OrderDateAscending => orders
                .OrderBy(OrderDateMissing)
                .ThenBy(order => order.MaterialOrderDate)
                .ThenByDescending(order => order.Date),
            SupplierOrderSortMode.ExpectedDeliveryAscending => orders
                .OrderBy(ExpectedDeliveryMissing)
                .ThenBy(order => order.ExpectedDeliveryDate)
                .ThenByDescending(order => order.MaterialOrderDate)
                .ThenByDescending(order => order.Date),
            SupplierOrderSortMode.ExpectedDeliveryDescending => orders
                .OrderBy(ExpectedDeliveryMissing)
                .ThenByDescending(order => order.ExpectedDeliveryDate)
                .ThenByDescending(order => order.MaterialOrderDate)
                .ThenByDescending(order => order.Date),
            SupplierOrderSortMode.CustomerAscending => orders
                .OrderBy(order => order.CustomerReferenceDisplay, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(order => order.MaterialOrderDate)
                .ThenByDescending(order => order.Date),
            SupplierOrderSortMode.CustomerDescending => orders
                .OrderByDescending(order => order.CustomerReferenceDisplay, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(order => order.MaterialOrderDate)
                .ThenByDescending(order => order.Date),
            SupplierOrderSortMode.Status => orders
                .OrderBy(order => GetStatusRank(order.MaterialStatus))
                .ThenBy(ExpectedDeliveryMissing)
                .ThenBy(order => order.ExpectedDeliveryDate)
                .ThenByDescending(order => order.MaterialOrderDate)
                .ThenByDescending(order => order.Date),
            _ => orders
                .OrderBy(OrderDateMissing)
                .ThenByDescending(order => order.MaterialOrderDate)
                .ThenByDescending(order => order.Date)
        };

        return sorted.ToArray();
    }

    private static int OrderDateMissing(QuoteHistorySummary order) =>
        order.MaterialOrderDate.HasValue ? 0 : 1;

    private static int ExpectedDeliveryMissing(QuoteHistorySummary order) =>
        order.ExpectedDeliveryDate.HasValue ? 0 : 1;

    private static int GetStatusRank(string? status) => status?.Trim().ToUpperInvariant() switch
    {
        "DA ORDINARE" => 0,
        "ORDINATO" => 1,
        "DA RITIRARE" => 2,
        "IN MAGAZZINO" => 3,
        "CONSEGNATO" => 4,
        "NON DISPONIBILE" => 5,
        _ => 6
    };
}
