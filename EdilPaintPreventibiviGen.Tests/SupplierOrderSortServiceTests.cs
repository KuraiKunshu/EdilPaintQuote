using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class SupplierOrderSortServiceTests
{
    [Fact]
    public void OrderDateDescendingPlacesNewestFirstAndUndatedLast()
    {
        QuoteHistorySummary[] orders =
        [
            CreateOrder("SENZA-DATA", null),
            CreateOrder("VECCHIO", new DateTime(2026, 8, 20)),
            CreateOrder("RECENTE", new DateTime(2026, 9, 2))
        ];

        IReadOnlyList<QuoteHistorySummary> sorted = SupplierOrderSortService.Sort(
            orders,
            SupplierOrderSortMode.OrderDateDescending);

        Assert.Equal(["RECENTE", "VECCHIO", "SENZA-DATA"], sorted.Select(order => order.QuoteNumber));
    }

    [Fact]
    public void ExpectedDeliveryAscendingPlacesNearestFirstAndMissingLast()
    {
        QuoteHistorySummary[] orders =
        [
            CreateOrder("SENZA-CONSEGNA", new DateTime(2026, 9, 1)),
            CreateOrder("LONTANA", new DateTime(2026, 9, 2), new DateTime(2026, 10, 15)),
            CreateOrder("VICINA", new DateTime(2026, 9, 3), new DateTime(2026, 9, 8))
        ];

        IReadOnlyList<QuoteHistorySummary> sorted = SupplierOrderSortService.Sort(
            orders,
            SupplierOrderSortMode.ExpectedDeliveryAscending);

        Assert.Equal(["VICINA", "LONTANA", "SENZA-CONSEGNA"], sorted.Select(order => order.QuoteNumber));
    }

    private static QuoteHistorySummary CreateOrder(
        string quoteNumber,
        DateTime? orderDate,
        DateTime? expectedDeliveryDate = null) => new()
    {
        QuoteNumber = quoteNumber,
        Date = new DateTimeOffset(2026, 9, 3, 8, 0, 0, TimeSpan.Zero),
        CustomerName = quoteNumber,
        MaterialOrderDate = orderDate,
        ExpectedDeliveryDate = expectedDeliveryDate
    };
}
