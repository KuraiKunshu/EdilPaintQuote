using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class MaterialOrderRegisterTests
{
    [Fact]
    public void RegisterIncludesFinishedAndArchivedJobsButNotQuotesWithoutOrderInformation()
    {
        var rows = MaterialOrderRegister.Filter([
            new() { QuoteNumber = "1", Status = QuoteStatus.Finito, MaterialOrderDate = new(2026, 9, 12), SupplierName = "Alfa" },
            new() { QuoteNumber = "2", Status = QuoteStatus.Archiviato, SupplierName = "Beta" },
            new() { QuoteNumber = "3", Status = QuoteStatus.Bozza },
            new() { QuoteNumber = "4", MaterialsOrderedByCustomer = true }]);
        Assert.Equal(new[] { "1", "2", "4" }, rows.Select(row => row.Number));
        Assert.Equal("Finito", rows[0].JobStatus);
        Assert.Equal("Non indicata", rows[1].OrderDateText);
        Assert.Equal("Ordinati dal cliente", rows[2].Supplier);
    }

    [Fact]
    public void DateRangeUsesTheMaterialOrderDateAndRetainsTheWholeFinalDay()
    {
        QuoteHistorySummary[] quotes = [new() { QuoteNumber = "1", Date = DateTimeOffset.Now, MaterialOrderDate = new(2026, 10, 3, 23, 59, 0) },
            new() { QuoteNumber = "2", MaterialOrderDate = new(2026, 10, 4) }, new() { QuoteNumber = "3", SupplierName = "Senza data" }];
        var rows = MaterialOrderRegister.Filter(quotes, from: new(2026, 10, 1), to: new(2026, 10, 3));
        Assert.Equal("1", Assert.Single(rows).Number);
        Assert.Throws<ArgumentException>(() => MaterialOrderRegister.Filter(quotes, from: new(2026, 10, 4), to: new(2026, 10, 1)));
    }

    [Fact]
    public void SearchAndSupplierCombineAcrossCustomerReferenceSiteAndOrderWithoutChangingTheSource()
    {
        var quote = new QuoteHistorySummary { QuoteNumber = "42", CustomerName = "Rossi", ReferenceName = "Libertà",
            SiteName = "Via Roma", SupplierName = "Alfa", MaterialOrderDate = new(2026, 10, 3), Status = QuoteStatus.Finito };
        Assert.Single(MaterialOrderRegister.Filter([quote], "ROSSI liberta Roma 42", "ALFA"));
        Assert.Empty(MaterialOrderRegister.Filter([quote], "Rossi", "Beta"));
        Assert.Equal(QuoteStatus.Finito, quote.Status);
        Assert.Equal(new DateTime(2026, 10, 3), quote.MaterialOrderDate);
    }
}
