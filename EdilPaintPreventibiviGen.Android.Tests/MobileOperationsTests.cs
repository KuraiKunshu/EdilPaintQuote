using System.Text.Json;
using QuantityValue = EdilPaintPreventibiviGen.Models.QuantityValue;
using EdilPaintPreventibiviGen.Android.Models;
using EdilPaintPreventibiviGen.Android.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Android.Tests;

public class MobileOperationsTests
{
    [Fact]
    public void CustomerVatPreferenceSurvivesMobileEditingAndJson()
    {
        var customer = new CustomerRecord { BusinessName = "Cliente", PreferredVatType = "10%" };
        var copy = customer.Clone();
        Assert.Equal("10%", copy.PreferredVatType);
        copy.PreferredVatType = "22%";
        Assert.Equal("10%", customer.PreferredVatType);
        var restored = JsonSerializer.Deserialize<CustomerRecord>(JsonSerializer.Serialize(copy))!;
        Assert.Equal("22%", restored.PreferredVatType);
        Assert.Equal("", JsonSerializer.Deserialize<CustomerRecord>("{}")!.PreferredVatType);
        Assert.Equal("22%", EdilPaintPreventibiviGen.Models.CustomerVatPreference.Resolve(customer.PreferredVatType,
            restored.PreferredVatType, true, "esclusa"));
    }

    [Fact]
    public void LargeQuantitiesKeepAllSupportedDecimalsInMobileDocuments()
    {
        Assert.True(QuantityValue.TryParse("10000,123456789", out decimal quantity));
        var line = new QuoteLine { Name = "Materiale", Quantity = quantity, UnitOfMeasure = "m²" };
        var restored = JsonSerializer.Deserialize<QuoteLine>(JsonSerializer.Serialize(line))!;
        Assert.Equal(quantity, restored.Clone().Quantity);
        Assert.Equal("10000,123456789 m²", restored.QuantityDisplay);
        Assert.Contains("10000,123456789 m²", MobileMailService.BuildMaterialsList([restored]));
        var quote = new QuoteDetail { Materials = [restored] };
        Assert.Contains(MobileDocumentContent.Quote(quote, new CompanyContact("Azienda", "", "", "")), row => row.Text.Contains("10000,123456789 m²"));
        Assert.False(QuantityValue.TryParse("1,0000000001", out _));
    }

    [Fact]
    public void FractionalLinesKeepUnitsInClonesTotalsAndDocuments()
    {
        var line = new QuoteLine { Name = "Posa", Quantity = 1.125m, UnitOfMeasure = "h", UnitPrice = 40 };
        var clone = line.Clone();
        Assert.Equal(1.125m, clone.Quantity);
        Assert.Equal("h", clone.UnitOfMeasure);
        Assert.Equal(45, clone.Total);
        Assert.Equal("1,125 h", clone.QuantityDisplay);
        Assert.Contains("1,125 h", MobileMailService.BuildMaterialsList([clone]));
        var quote = new QuoteDetail { Materials = [clone] };
        Assert.Contains(MobileDocumentContent.Quote(quote, new CompanyContact("Azienda", "", "", "")), row => row.Text.Contains("1,125 h"));
        var totals = QuoteTotalsCalculator.Calculate([clone], [], 0, 0, "22%");
        Assert.Equal(54.9, totals.Total, 8);
    }

    [Fact]
    public void FractionalTotalsMatchRoundedSummary()
    {
        var totals = QuoteTotalsCalculator.Calculate(
            [new QuoteLine { Quantity = 2.5m, UnitPrice = 40 }],
            [new QuoteLine { Quantity = 1.125m, UnitPrice = 50 }], 0, 0, "22%");
        Assert.Equal(156.25, totals.Imponibile, 8);
        Assert.Equal(34.38, totals.Iva, 8);
        Assert.Equal(190.63, totals.Total, 8);
    }

    [Fact]
    public void LegacyMobileLineDefaultsToPiecesAndDecimalJsonRoundTrips()
    {
        var old = JsonSerializer.Deserialize<QuoteLine>("""{"Quantity":2,"UnitPrice":10}""")!;
        Assert.Equal("pz", old.UnitOfMeasure);
        old.Quantity = 0.125m;
        old.UnitOfMeasure = "kg";
        var restored = JsonSerializer.Deserialize<QuoteLine>(JsonSerializer.Serialize(old))!;
        Assert.Equal(0.125m, restored.Quantity);
        Assert.Equal("kg", restored.UnitOfMeasure);
        Assert.Equal("m²", new CatalogItem { UnitOfMeasure = "m²" }.Clone().UnitOfMeasure);
    }

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
