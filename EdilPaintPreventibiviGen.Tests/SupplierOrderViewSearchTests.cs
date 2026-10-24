using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Views;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class SupplierOrderViewSearchTests
{
    private static QuoteHistorySummary Order => new()
    {
        QuoteNumber = "160765", CustomerName = "Impresa Rossi", ReferenceName = "Condominio Libertà",
        SiteName = "Via Roma 12, Brescia", SupplierName = "Fornitore Bianchi"
    };

    [Theory]
    [InlineData("ROSSI libertA roma")]
    [InlineData("160765 bianchi")]
    [InlineData("Brescia condominio")]
    [InlineData("")]
    [InlineData("  ")]
    public void SearchCombinesTheJobIdentityAndSupplierIgnoringCaseAndAccents(string search)
        => Assert.True(SupplierOrdersView.MatchesSearch(Order, search));

    [Fact]
    public void EverySearchTermMustMatchAndClearingSearchRestoresTheOrder()
    {
        Assert.False(SupplierOrdersView.MatchesSearch(Order, "Rossi Milano"));
        Assert.True(SupplierOrdersView.MatchesSearch(Order, "Rossi"));
        Assert.True(SupplierOrdersView.MatchesSearch(Order, ""));
    }
}
