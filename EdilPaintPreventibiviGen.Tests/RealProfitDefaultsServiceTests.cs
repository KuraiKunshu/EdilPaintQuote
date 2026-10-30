using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class RealProfitDefaultsServiceTests
{
    [Fact]
    public void LinkedSupplierIsUsedInsteadOfTheCustomerAndMatchingToleratesCaseAndWhitespace()
    {
        var quote = new QuoteHistoryEntry { CustomerName = "Cliente", SupplierName = "  FORNITORE  " };
        Customer[] customers = [new() { BusinessName = "Cliente", IsSupplier = true, SupplierDiscount = 75 },
            new() { BusinessName = "Fornitore", IsSupplier = true, SupplierDiscount = 35 }];
        Assert.Equal(35, RealProfitDefaultsService.FindSupplier(quote, customers)!.SupplierDiscount);
        quote.SupplierName = "Sconosciuto";
        Assert.Null(RealProfitDefaultsService.FindSupplier(quote, customers));
    }

    [Fact]
    public void LegacyZerosUseConfiguredPercentagesButManualZeroRemainsZero()
    {
        var saved = new RealProfitInput();
        var defaults = RealProfitDefaultsService.Resolve(saved, 40, 15);
        Assert.Equal(40, defaults.SupplierDiscount);
        Assert.Equal(15, defaults.ProfitReduction);
        saved.UseSupplierDiscount = saved.UseDefaultProfitReduction = false;
        var manual = RealProfitDefaultsService.Resolve(saved, 40, 15);
        Assert.Equal(0, manual.SupplierDiscount);
        Assert.Equal(0, manual.ProfitReduction);
    }

    [Fact]
    public void LinkedPercentagesFollowCurrentSourcesAndOldNonzeroManualValuesArePreserved()
    {
        var saved = new RealProfitInput { SupplierDiscount = 25, ProfitReductionPercentage = 8 };
        var original = RealProfitDefaultsService.Resolve(saved, 40, 15);
        Assert.Equal(25, original.SupplierDiscount);
        Assert.Equal(8, original.ProfitReduction);
        saved.UseSupplierDiscount = saved.UseDefaultProfitReduction = true;
        var current = RealProfitDefaultsService.Resolve(saved, 40, 15);
        Assert.Equal(40, current.SupplierDiscount);
        Assert.Equal(15, current.ProfitReduction);
        Assert.Equal(25, saved.SupplierDiscount);
        Assert.Equal(8, saved.ProfitReductionPercentage);
    }

    [Fact]
    public void InvalidPercentageSourcesCannotProduceInvalidCostResults()
    {
        var values = RealProfitDefaultsService.Resolve(null, double.NaN, double.PositiveInfinity);
        Assert.Equal(0, values.SupplierDiscount);
        Assert.Equal(0, values.ProfitReduction);
        Assert.Equal(100, RealProfitDefaultsService.Resolve(null, 150, 130).SupplierDiscount);
    }
}
