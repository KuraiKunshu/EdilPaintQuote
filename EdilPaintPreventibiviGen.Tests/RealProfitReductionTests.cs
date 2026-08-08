using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class RealProfitReductionTests
{
    [Fact]
    public void AppliesReductionToPositiveProfitAndReportsEveryStage()
    {
        var result = RealProfitCalculator.Calculate(new RealProfitInput
        {
            QuoteRevenue = 1000,
            Workers = 1,
            Days = 1,
            HoursPerDay = 5,
            HourlyCost = 40,
            ProfitReductionPercentage = 25
        });

        Assert.Equal(800, result.ProfitBeforeReduction);
        Assert.Equal(200, result.ProfitReductionAmount);
        Assert.Equal(600, result.ProfitAfterReduction);
        Assert.Equal(600, result.Profit);
        Assert.Equal(60, result.ProfitPercentage);
    }

    [Theory]
    [InlineData(-10, 800)]
    [InlineData(0, 800)]
    [InlineData(100, 0)]
    [InlineData(150, 0)]
    [InlineData(double.NaN, 800)]
    public void ClampsReductionPercentageBetweenZeroAndOneHundred(
        double reductionPercentage,
        double expectedProfit)
    {
        var result = RealProfitCalculator.Calculate(new RealProfitInput
        {
            QuoteRevenue = 1000,
            Workers = 1,
            Days = 1,
            HoursPerDay = 5,
            HourlyCost = 40,
            ProfitReductionPercentage = reductionPercentage
        });

        Assert.Equal(expectedProfit, result.Profit);
        Assert.Equal(800 - expectedProfit, result.ProfitReductionAmount);
    }

    [Fact]
    public void DoesNotReduceLosses()
    {
        var result = RealProfitCalculator.Calculate(new RealProfitInput
        {
            QuoteRevenue = 500,
            Workers = 2,
            Days = 1,
            HoursPerDay = 10,
            HourlyCost = 40,
            ProfitReductionPercentage = 50
        });

        Assert.Equal(-300, result.ProfitBeforeReduction);
        Assert.Equal(0, result.ProfitReductionAmount);
        Assert.Equal(-300, result.ProfitAfterReduction);
        Assert.Equal(-300, result.Profit);
        Assert.Equal(-60, result.ProfitPercentage);
    }

    [Fact]
    public void DoesNotReduceBreakEvenResult()
    {
        var result = RealProfitCalculator.Calculate(new RealProfitInput
        {
            QuoteRevenue = 800,
            Workers = 2,
            Days = 1,
            HoursPerDay = 10,
            HourlyCost = 40,
            ProfitReductionPercentage = 50
        });

        Assert.Equal(0, result.ProfitBeforeReduction);
        Assert.Equal(0, result.ProfitReductionAmount);
        Assert.Equal(0, result.Profit);
    }

    [Fact]
    public void DefaultPercentageKeepsExistingCalculationBehavior()
    {
        var result = RealProfitCalculator.Calculate(new RealProfitInput
        {
            QuoteRevenue = 1000,
            Workers = 1,
            Days = 1,
            HoursPerDay = 5,
            HourlyCost = 40
        });

        Assert.Equal(800, result.ProfitBeforeReduction);
        Assert.Equal(0, result.ProfitReductionAmount);
        Assert.Equal(800, result.ProfitAfterReduction);
        Assert.Equal(800, result.Profit);
        Assert.Equal(80, result.ProfitPercentage);
    }

    [Fact]
    public void AttachedQuoteReproducesDisplayedProfit()
    {
        RealProfitResult result = RealProfitCalculator.Calculate(new RealProfitInput
        {
            QuoteRevenue = 22288.50,
            SupplierDiscount = 0,
            Workers = 4,
            Days = 2,
            HoursPerDay = 10,
            HourlyCost = 40,
            Materials =
            [
                new ProfitMaterialCost
                {
                    Name = "Materiali preventivo",
                    Quantity = 1,
                    CustomerUnitPrice = 13268,
                    CustomerDiscount = 25
                }
            ],
            CompanyMaterials =
            [
                new CompanyMaterialCost { Name = "Angolari", Quantity = 62, UnitCost = 9.64 },
                new CompanyMaterialCost { Name = "Nastro", Quantity = 122, UnitCost = 1.60 },
                new CompanyMaterialCost { Name = "Perlina", Quantity = 62, UnitCost = 2.03 }
            ]
        });

        Assert.Equal(9951, result.CustomerMaterialRevenue, 2);
        Assert.Equal(13268, result.SupplierMaterialCost, 2);
        Assert.Equal(918.74, result.CompanyMaterialCost, 2);
        Assert.Equal(3200, result.LaborCost, 2);
        Assert.Equal(4901.76, result.Profit, 2);
        Assert.Equal(21.9923278821, result.ProfitPercentage, 8);
    }

    [Fact]
    public void SupplierDiscountBelowCustomerDiscountProducesNegativeMaterialMargin()
    {
        RealProfitResult result = RealProfitCalculator.Calculate(new RealProfitInput
        {
            SupplierDiscount = 22,
            Materials =
            [
                new ProfitMaterialCost
                {
                    Name = "Materiali preventivo",
                    Quantity = 1,
                    CustomerUnitPrice = 13268,
                    CustomerDiscount = 25
                }
            ]
        });

        Assert.Equal(9951, result.CustomerMaterialRevenue, 2);
        Assert.Equal(10349.04, result.SupplierMaterialCost, 2);
        Assert.Equal(-398.04, result.MaterialMargin, 2);
    }
}
