using System.Text.Json;
using EdilPaintPreventibiviGen.Android.Models;
using EdilPaintPreventibiviGen.Android.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Android.Tests;

public class MobileOperationsTests
{
    [Theory]
    [InlineData("esclusa", 0)]
    [InlineData("10%", 28.2)]
    [InlineData("22%", 62.04)]
    [InlineData("RC 10%+22%", 33.24)]
    public void QuoteTotalsApplyLineAndGlobalDiscountsBeforeVat(string ivaType, double expectedVat)
    {
        var materials = new[] { new QuoteLine { Quantity = 2, UnitPrice = 100, Discount = 10, IsSignificant = true } };
        var labors = new[] { new QuoteLine { Quantity = 3, UnitPrice = 50, IsSignificant = true } };
        QuoteTotals totals = QuoteTotalsCalculator.Calculate(materials, labors, 10, 20, ivaType);
        Assert.Equal(282, totals.Imponibile, 6);
        Assert.Equal(expectedVat, totals.Iva, 6);
        Assert.Equal(282 + expectedVat, totals.Total, 6);
    }

    [Theory]
    [InlineData("1.5", 1.5)]
    [InlineData("1,5", 1.5)]
    [InlineData("12", 12)]
    public void DecimalInputDoesNotTreatDotAsThousands(string input, double expected)
    {
        Assert.True(MobileNumber.TryParse(input, out double value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1.234,56")]
    public void AmbiguousOrNonFiniteNumbersAreRejected(string input) => Assert.False(MobileNumber.TryParse(input, out _));

    [Fact]
    public void SentOpenFilterIncludesBoundaryDatesAndExcludesClosedQuotes()
    {
        var quotes = new[]
        {
            new QuoteSummary { QuoteNumber = "open", Date = new(2026, 9, 1), SentAtUtc = DateTime.UtcNow, Status = QuoteStatus.Spedito },
            new QuoteSummary { QuoteNumber = "closed", Date = new(2026, 9, 1), SentAtUtc = DateTime.UtcNow, Status = QuoteStatus.Finito },
            new QuoteSummary { QuoteNumber = "draft", Date = new(2026, 9, 1), Status = QuoteStatus.Bozza }
        };
        var filter = new QuoteListFilter { SentOpenOnly = true, From = new(2026, 9, 1), Until = new(2026, 9, 1) };
        Assert.Equal("open", Assert.Single(filter.Apply(quotes)).QuoteNumber);
    }

    [Fact]
    public void CustomerDocumentDoesNotLeakInternalData()
    {
        var quote = new QuoteDetail
        {
            QuoteNumber = "26-001", CustomerName = "Cliente", CustomerNotes = "Nota pubblica",
            Notes = "Segreto interno", SupplierName = "Fornitore segreto", PartnerCompanyName = "Partner segreto",
            OurCosts = [new() { Description = "Costo segreto", Amount = 1234 }],
            Materials = [new() { Name = "Finestra", Quantity = 3, UnitPrice = 100 }]
        };
        string text = string.Join("\n", MobileDocumentContent.Quote(quote, new("EdilPaint", "test@example.com")).Select(x => x.Text));
        Assert.Contains("Nota pubblica", text);
        Assert.Contains("N.3  Finestra", text);
        Assert.DoesNotContain("segreto", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GUADAGNO", text);
    }

    [Fact]
    public void SupplierMailUsesQuantitiesAndQuoteOrderWithoutPrices()
    {
        var lines = new[] { new QuoteLine { Name = "B", Quantity = 2, UnitPrice = 99, SortOrder = 2 },
            new QuoteLine { Name = "A", Quantity = 5, SortOrder = 1 }, new QuoteLine { Name = "Vuoto", Quantity = 0 } };
        Assert.Equal($"N.5 A{Environment.NewLine}N.2 B", MobileMailService.BuildMaterialsList(lines));
    }

    [Theory]
    [InlineData(SupplierOrderSortMode.OrderDateAscending, "old", "new", "none")]
    [InlineData(SupplierOrderSortMode.OrderDateDescending, "new", "old", "none")]
    public void UndatedOrdersAlwaysComeLast(SupplierOrderSortMode mode, string first, string second, string third)
    {
        var orders = new[] { new SupplierOrderSummary { QuoteNumber = "none" },
            new SupplierOrderSummary { QuoteNumber = "old", MaterialOrderDate = new(2026, 1, 1) },
            new SupplierOrderSummary { QuoteNumber = "new", MaterialOrderDate = new(2026, 8, 1) } };
        Assert.Equal(new[] { first, second, third }, SupplierOrderSortService.Sort(orders, mode).Select(x => x.QuoteNumber));
        Assert.Equal(SupplierOrderSortMode.OrderDateDescending, SupplierOrderSortService.Options[0].Mode);
    }

    [Fact]
    public void EditingCopiesLinesAndCollaborationCosts()
    {
        var original = new QuoteDetail { Revision = 8, IsJointVenture = true,
            Materials = [new() { Name = "Finestra", Quantity = 2 }], OurCosts = [new() { Description = "Costo", Amount = 12 }] };
        var draft = QuoteDraft.FromDetail(original);
        draft.Materials[0].Quantity = 10;
        draft.OurCosts[0].Amount = 100;
        Assert.Equal(2, original.Materials[0].Quantity);
        Assert.Equal(12, original.OurCosts[0].Amount);
        Assert.Equal(8, draft.Revision);
        Assert.True(draft.IsJointVenture);
    }

    [Fact]
    public void SavedProfitRoundTripsSharedDesktopSnapshot()
    {
        var input = new RealProfitInput { QuoteRevenue = 1000, Workers = 2, Days = 1, HoursPerDay = 8,
            HourlyCost = 20, ProfitReductionPercentage = 10, ExcludeMaterials = true };
        var snapshot = new RealProfitSnapshot { Input = input, Result = RealProfitCalculator.Calculate(input), CalculatedByDevice = "Android" };
        var loaded = JsonSerializer.Deserialize<RealProfitSnapshot>(JsonSerializer.Serialize(snapshot))!;
        Assert.Equal(snapshot.Result.Profit, loaded.Result.Profit);
        Assert.Equal(612, loaded.Result.Profit, 2);
        Assert.Equal("Android", loaded.CalculatedByDevice);
    }

    [Fact]
    public void CertificateContainsQuantitiesButNoPrices()
    {
        var quote = new QuoteDetail { Materials = [new() { Name = "Finestra", Quantity = 4, UnitPrice = 1234.56 }] };
        string text = string.Join("\n", MobileDocumentContent.InstallationCertificate(quote, new("EdilPaint", ""), "Cantiere", new(2026, 9, 1)).Select(x => x.Text));
        Assert.Contains("N.4 Finestra", text);
        Assert.DoesNotContain("1234", text);
        Assert.Contains("01/09/2026", text);
    }
}
