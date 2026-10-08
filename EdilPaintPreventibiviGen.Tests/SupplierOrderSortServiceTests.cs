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

    [Fact]
    public void SupplierGroupsAreAlphabeticalFollowedByUnassignedAndCustomerOrders()
    {
        QuoteHistorySummary[] orders =
        [
            CreateSupplierOrder("ZETA", "Zeta", new DateTime(2026, 9, 6)),
            CreateSupplierOrder("CLIENTE", "Azienda cliente", new DateTime(2026, 9, 9), orderedByCustomer: true),
            CreateSupplierOrder("SENZA-FORNITORE", " \t ", new DateTime(2026, 9, 8)),
            CreateSupplierOrder("ALFA", "Alfa", new DateTime(2026, 9, 1))
        ];

        IReadOnlyList<QuoteHistorySummary> sorted = SupplierOrderSortService.SortBySupplier(
            orders,
            SupplierOrderSortMode.OrderDateDescending);

        Assert.Equal(["ALFA", "ZETA", "SENZA-FORNITORE", "CLIENTE"], sorted.Select(order => order.QuoteNumber));
        Assert.Equal("Senza fornitore", sorted[2].OrderSupplierGroup);
        Assert.Equal("Ordinati dal cliente", sorted[3].OrderSupplierGroup);
    }

    [Theory]
    [InlineData(SupplierOrderSortMode.OrderDateDescending, "ALFA-RECENTE", "ALFA-VECCHIO", "ZETA-RECENTE", "ZETA-VECCHIO")]
    [InlineData(SupplierOrderSortMode.OrderDateAscending, "ALFA-VECCHIO", "ALFA-RECENTE", "ZETA-VECCHIO", "ZETA-RECENTE")]
    [InlineData(SupplierOrderSortMode.ExpectedDeliveryAscending, "ALFA-RECENTE", "ALFA-VECCHIO", "ZETA-RECENTE", "ZETA-VECCHIO")]
    [InlineData(SupplierOrderSortMode.ExpectedDeliveryDescending, "ALFA-VECCHIO", "ALFA-RECENTE", "ZETA-VECCHIO", "ZETA-RECENTE")]
    public void SelectedDateSortIsPreservedInsideEachSupplier(
        SupplierOrderSortMode mode,
        string firstAlfa,
        string secondAlfa,
        string firstZeta,
        string secondZeta)
    {
        QuoteHistorySummary[] orders =
        [
            CreateSupplierOrder("ZETA-VECCHIO", "Zeta", new DateTime(2026, 9, 1), new DateTime(2026, 10, 15)),
            CreateSupplierOrder("ALFA-SENZA-DATA", "Alfa", null),
            CreateSupplierOrder("ALFA-RECENTE", "Alfa", new DateTime(2026, 9, 9), new DateTime(2026, 9, 12)),
            CreateSupplierOrder("ZETA-RECENTE", "Zeta", new DateTime(2026, 9, 9), new DateTime(2026, 9, 12)),
            CreateSupplierOrder("ALFA-VECCHIO", "Alfa", new DateTime(2026, 9, 1), new DateTime(2026, 10, 15)),
            CreateSupplierOrder("ZETA-SENZA-DATA", "Zeta", null)
        ];

        IReadOnlyList<QuoteHistorySummary> sorted = SupplierOrderSortService.SortBySupplier(orders, mode);

        Assert.Equal(
            [firstAlfa, secondAlfa, "ALFA-SENZA-DATA", firstZeta, secondZeta, "ZETA-SENZA-DATA"],
            sorted.Select(order => order.QuoteNumber));
    }

    [Fact]
    public void SupplierWhitespaceAndCasingKeepEquivalentNamesTogether()
    {
        QuoteHistorySummary[] orders =
        [
            CreateSupplierOrder("ACME-VECCHIO", "  Acme\t S.r.l. \r\n", new DateTime(2026, 9, 1)),
            CreateSupplierOrder("BETA", "Beta", new DateTime(2026, 9, 5)),
            CreateSupplierOrder("ACME-RECENTE", "ACME S.r.l.", new DateTime(2026, 9, 9))
        ];

        IReadOnlyList<QuoteHistorySummary> sorted = SupplierOrderSortService.SortBySupplier(
            orders,
            SupplierOrderSortMode.OrderDateDescending);

        Assert.Equal(["ACME-RECENTE", "ACME-VECCHIO", "BETA"], sorted.Select(order => order.QuoteNumber));
        Assert.Equal("Acme S.r.l.", orders[0].OrderSupplierGroup);
        Assert.Equal(2, sorted.GroupBy(order => order.OrderSupplierGroup, StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal("  Acme\t S.r.l. \r\n", orders[0].SupplierName);
    }

    [Fact]
    public void CustomerOrdersShareTheirOwnGroupEvenWhenSupplierFieldContainsCustomerName()
    {
        QuoteHistorySummary[] orders =
        [
            CreateSupplierOrder("CLIENTE-ALFA", "Cliente Alfa", new DateTime(2026, 9, 1), orderedByCustomer: true),
            CreateSupplierOrder("FORNITORE-ALFA", "Cliente Alfa", new DateTime(2026, 9, 2)),
            CreateSupplierOrder("CLIENTE-ZETA", "Cliente Zeta", new DateTime(2026, 9, 9), orderedByCustomer: true),
            CreateSupplierOrder("CLIENTE-SENZA-NOME", string.Empty, new DateTime(2026, 9, 5), orderedByCustomer: true)
        ];

        IReadOnlyList<QuoteHistorySummary> sorted = SupplierOrderSortService.SortBySupplier(
            orders,
            SupplierOrderSortMode.OrderDateDescending);

        Assert.Equal(
            ["FORNITORE-ALFA", "CLIENTE-ZETA", "CLIENTE-SENZA-NOME", "CLIENTE-ALFA"],
            sorted.Select(order => order.QuoteNumber));
        Assert.All(sorted.Skip(1), order => Assert.Equal("Ordinati dal cliente", order.OrderSupplierGroup));
    }

    [Fact]
    public void SupplierAssignmentChangesNotifyTheGroupingProperty()
    {
        QuoteHistorySummary order = CreateSupplierOrder("ORDINE", "Alfa", null);
        List<string?> changedProperties = [];
        order.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        order.SupplierName = "Beta";

        Assert.Contains(nameof(QuoteHistorySummary.OrderSupplierGroup), changedProperties);
        Assert.Equal("Beta", order.OrderSupplierGroup);
        changedProperties.Clear();

        order.MaterialsOrderedByCustomer = true;

        Assert.Contains(nameof(QuoteHistorySummary.OrderSupplierGroup), changedProperties);
        Assert.Equal("Ordinati dal cliente", order.OrderSupplierGroup);
        changedProperties.Clear();

        order.MaterialsOrderedByCustomer = false;

        Assert.Contains(nameof(QuoteHistorySummary.OrderSupplierGroup), changedProperties);
        Assert.Equal("Beta", order.OrderSupplierGroup);
    }

    private static QuoteHistorySummary CreateSupplierOrder(
        string quoteNumber,
        string supplierName,
        DateTime? orderDate,
        DateTime? expectedDeliveryDate = null,
        bool orderedByCustomer = false)
    {
        QuoteHistorySummary order = CreateOrder(quoteNumber, orderDate, expectedDeliveryDate);
        order.SupplierName = supplierName;
        order.MaterialsOrderedByCustomer = orderedByCustomer;
        return order;
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
