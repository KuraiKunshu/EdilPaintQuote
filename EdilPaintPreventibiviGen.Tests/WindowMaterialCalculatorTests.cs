using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class WindowMaterialCalculatorTests
{
    [Fact]
    public void CalculateAggregatesExamplePerSizeAndRoundsEveryWindowUp()
    {
        WindowMaterialProductLine[] products =
        [
            new("GGL MK04 2070(78x98)", "", 7),
            new("GGL MK04 207021A(78x98)", "", 5),
            new("EDW MK04 2000S(78x98)", "", 12),
            new("DKL MK04 1025SG", "", 5),
            new("GGL MK08 2070(78x140)", "", 2),
            new("EDW MK08 2000S(78x140)", "", 2),
            new("DKL MK08 1025SG", "", 2),
            new("GGL PK04 2070(94x98)", "", 1),
            new("EDW PK04 2000S(94x98)", "", 1)
        ];

        WindowMaterialCalculationResult result = WindowMaterialCalculator.Calculate(
            products,
            [new WindowMaterialLaborLine("Finitura interna", "")],
            new WindowMaterialCalculationOptions
            {
                WindowPrefixes = ["GGL", "GGU", "Q4", "R8"],
                RequiredLaborKeyword = "finitura interna"
            });

        Assert.True(result.RequiredLaborFound);
        Assert.Equal(15, result.TotalWindowQuantity);
        Assert.Equal(62, result.TotalLinearMeters);

        Assert.Collection(
            result.Details,
            detail =>
            {
                Assert.Equal(new WindowSize(78, 98), detail.Size);
                Assert.Equal(12, detail.WindowQuantity);
                Assert.Equal(4, detail.LinearMetersPerWindow);
                Assert.Equal(48, detail.TotalLinearMeters);
            },
            detail =>
            {
                Assert.Equal(new WindowSize(78, 140), detail.Size);
                Assert.Equal(2, detail.WindowQuantity);
                Assert.Equal(5, detail.LinearMetersPerWindow);
                Assert.Equal(10, detail.TotalLinearMeters);
            },
            detail =>
            {
                Assert.Equal(new WindowSize(94, 98), detail.Size);
                Assert.Equal(1, detail.WindowQuantity);
                Assert.Equal(4, detail.LinearMetersPerWindow);
                Assert.Equal(4, detail.TotalLinearMeters);
            });
    }

    [Theory]
    [InlineData("BK", 47)]
    [InlineData("CK", 55)]
    [InlineData("FK", 66)]
    [InlineData("MK", 78)]
    [InlineData("PK", 94)]
    [InlineData("SK", 114)]
    [InlineData("UK", 134)]
    public void TryGetWindowSizeMapsEveryVeluxWidth(string code, int expectedWidth)
    {
        bool recognized = WindowMaterialCalculator.TryGetWindowSize(
            $"GGL {code}04 2070",
            ["ggl"],
            out WindowSize size);

        Assert.True(recognized);
        Assert.Equal(new WindowSize(expectedWidth, 98), size);
    }

    [Theory]
    [InlineData("25", 55)]
    [InlineData("01", 70)]
    [InlineData("02", 78)]
    [InlineData("04", 98)]
    [InlineData("06", 118)]
    [InlineData("08", 140)]
    [InlineData("10", 160)]
    [InlineData("12", 180)]
    public void TryGetWindowSizeMapsEveryVeluxHeight(string code, int expectedHeight)
    {
        bool recognized = WindowMaterialCalculator.TryGetWindowSize(
            $"GGU MK{code} 006621",
            ["GGU"],
            out WindowSize size);

        Assert.True(recognized);
        Assert.Equal(new WindowSize(78, expectedHeight), size);
    }

    [Theory]
    [InlineData("Q42C 078/118 K200", "q4", 78, 118)]
    [InlineData("R89P 114/140 K2EF", "R8", 114, 140)]
    [InlineData("GGL prodotto speciale (94X98)", "GGL", 94, 98)]
    [InlineData("GGU prodotto speciale (55×98)", "ggu", 55, 98)]
    public void TryGetWindowSizeSupportsRotoAndExplicitMeasures(
        string productName,
        string prefix,
        int expectedWidth,
        int expectedHeight)
    {
        bool recognized = WindowMaterialCalculator.TryGetWindowSize(
            productName,
            [prefix],
            out WindowSize size);

        Assert.True(recognized);
        Assert.Equal(new WindowSize(expectedWidth, expectedHeight), size);
    }

    [Theory]
    [InlineData("EDW MK04 2000S(78x98)")]
    [InlineData("DKL MK04 1025SG")]
    [InlineData("Accessorio GGL MK04 (78x98)")]
    public void TryGetWindowSizeRejectsProductsThatDoNotStartWithAllowedPrefix(string productName)
    {
        bool recognized = WindowMaterialCalculator.TryGetWindowSize(
            productName,
            ["GGL", "GGU"],
            out _);

        Assert.False(recognized);
    }

    [Theory]
    [InlineData("FINITURA INTERNA")]
    [InlineData("  Finitura interna  ")]
    public void RequiredLaborNameMatchesIgnoringCaseAndOuterSpaces(string name)
    {
        WindowMaterialCalculationResult result = WindowMaterialCalculator.Calculate(
            [new WindowMaterialProductLine("GGL CK04 2070", "", 2)],
            [new WindowMaterialLaborLine(name, "")],
            new WindowMaterialCalculationOptions
            {
                WindowPrefixes = ["GGL"],
                RequiredLaborKeyword = "finitura interna"
            });

        Assert.True(result.RequiredLaborFound);
        Assert.Equal(2, result.TotalWindowQuantity);
        Assert.Equal(8, result.TotalLinearMeters);
    }

    [Theory]
    [InlineData("Finitura interna esclusa", "")]
    [InlineData("Posa", "Finitura interna")]
    public void SimilarOrDescriptiveLaborTextDoesNotActivateRule(string name, string description)
    {
        Assert.False(WindowMaterialCalculator.ContainsRequiredLabor(
            [new WindowMaterialLaborLine(name, description)],
            "Finitura interna"));
    }

    [Fact]
    public void MissingRequiredLaborDoesNotGenerateMaterial()
    {
        WindowMaterialCalculationResult result = WindowMaterialCalculator.Calculate(
            [new WindowMaterialProductLine("GGL MK04 2070", "", 3)],
            [new WindowMaterialLaborLine("Installazione finestra", "")],
            new WindowMaterialCalculationOptions
            {
                WindowPrefixes = ["GGL"],
                RequiredLaborKeyword = "finitura interna"
            });

        Assert.False(result.RequiredLaborFound);
        Assert.Empty(result.Details);
        Assert.Equal(0, result.TotalLinearMeters);
    }

    [Fact]
    public void BlankKeywordNeverActivatesCalculation()
    {
        Assert.False(WindowMaterialCalculator.ContainsRequiredLabor(
            [new WindowMaterialLaborLine("Qualunque lavoro", "")],
            "  "));
    }

    [Fact]
    public void LaborWithNonPositiveQuantityDoesNotActivateCalculation()
    {
        Assert.False(WindowMaterialCalculator.ContainsRequiredLabor(
            [new WindowMaterialLaborLine("Finitura interna", "", 0)],
            "finitura interna"));
    }

    [Fact]
    public void ValidPrefixWithMissingMeasureIsReportedInsteadOfSilentlyIgnored()
    {
        WindowMaterialCalculationResult result = WindowMaterialCalculator.Calculate(
            [new WindowMaterialProductLine("GGL prodotto senza misura", "", 2)],
            [new WindowMaterialLaborLine("Finitura interna", "")],
            new WindowMaterialCalculationOptions
            {
                WindowPrefixes = ["GGL"],
                RequiredLaborKeyword = "Finitura interna"
            });

        Assert.Equal(0, result.TotalLinearMeters);
        UnrecognizedWindowProduct product = Assert.Single(result.UnrecognizedProducts);
        Assert.Equal("GGL prodotto senza misura", product.Name);
        Assert.Equal(2, product.Quantity);
    }

    [Fact]
    public void ConflictingExplicitAndVeluxMeasuresAreReportedAsUnrecognized()
    {
        WindowMaterialCalculationResult result = WindowMaterialCalculator.Calculate(
            [new WindowMaterialProductLine("GGL MK04 2070(94x98)", "", 1)],
            [new WindowMaterialLaborLine("Finitura interna", "")],
            new WindowMaterialCalculationOptions
            {
                WindowPrefixes = ["GGL"],
                RequiredLaborKeyword = "Finitura interna"
            });

        Assert.Equal(0, result.TotalLinearMeters);
        Assert.Single(result.UnrecognizedProducts);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveProductQuantityIsIgnoredWithoutWarning(int quantity)
    {
        WindowMaterialCalculationResult result = WindowMaterialCalculator.Calculate(
            [new WindowMaterialProductLine("GGL MK04 2070", "", quantity)],
            [new WindowMaterialLaborLine("Finitura interna", "")],
            new WindowMaterialCalculationOptions
            {
                WindowPrefixes = ["GGL"],
                RequiredLaborKeyword = "Finitura interna"
            });

        Assert.Equal(0, result.TotalLinearMeters);
        Assert.Empty(result.UnrecognizedProducts);
    }
}
