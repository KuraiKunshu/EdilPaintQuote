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
            new("FINESTRA (78x98) 2070 (78x98)", "", 7),
            new("FINESTRA (78x98) 207021A (78x98)", "", 5),
            new("EDW (78x98) 2000S(78x98)", "", 12),
            new("DKL (78x98) 1025SG", "", 5),
            new("FINESTRA (78x140) 2070 (78x140)", "", 2),
            new("EDW (78x140) 2000S(78x140)", "", 2),
            new("DKL (78x140) 1025SG", "", 2),
            new("FINESTRA (94x98) 2070 (94x98)", "", 1),
            new("EDW (94x98) 2000S(94x98)", "", 1)
        ];

        WindowMaterialCalculationResult result = WindowMaterialCalculator.Calculate(
            products,
            [new WindowMaterialLaborLine("Finitura interna", "")],
            new WindowMaterialCalculationOptions
            {
                WindowPrefixes = ["FINESTRA", "SERRAMENTO", "Q4", "R8"],
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
    [InlineData("FINESTRA CODICE-A04")]
    [InlineData("FINESTRA 1234")]
    public void CatalogCodesWithoutExplicitDimensionsAreNotGuessed(string name)
    {
        Assert.False(WindowMaterialCalculator.TryGetWindowSize(name, ["FINESTRA"], out _));
    }

    [Theory]
    [InlineData("Q42C 078/118 K200", "q4", 78, 118)]
    [InlineData("R89P 114/140 K2EF", "R8", 114, 140)]
    [InlineData("FINESTRA prodotto speciale (94X98)", "FINESTRA", 94, 98)]
    [InlineData("SERRAMENTO prodotto speciale (55×98)", "serramento", 55, 98)]
    public void TryGetWindowSizeSupportsSlashAndExplicitMeasures(
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
    [InlineData("EDW (78x98) 2000S(78x98)")]
    [InlineData("DKL (78x98) 1025SG")]
    [InlineData("Accessorio FINESTRA (78x98) (78x98)")]
    public void TryGetWindowSizeRejectsProductsThatDoNotStartWithAllowedPrefix(string productName)
    {
        bool recognized = WindowMaterialCalculator.TryGetWindowSize(
            productName,
            ["FINESTRA", "SERRAMENTO"],
            out _);

        Assert.False(recognized);
    }

    [Theory]
    [InlineData("FINITURA INTERNA")]
    [InlineData("  Finitura interna  ")]
    public void RequiredLaborNameMatchesIgnoringCaseAndOuterSpaces(string name)
    {
        WindowMaterialCalculationResult result = WindowMaterialCalculator.Calculate(
            [new WindowMaterialProductLine("FINESTRA (55x98) 2070", "", 2)],
            [new WindowMaterialLaborLine(name, "")],
            new WindowMaterialCalculationOptions
            {
                WindowPrefixes = ["FINESTRA"],
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
            [new WindowMaterialProductLine("FINESTRA (78x98) 2070", "", 3)],
            [new WindowMaterialLaborLine("Installazione finestra", "")],
            new WindowMaterialCalculationOptions
            {
                WindowPrefixes = ["FINESTRA"],
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
            [new WindowMaterialProductLine("FINESTRA prodotto senza misura", "", 2)],
            [new WindowMaterialLaborLine("Finitura interna", "")],
            new WindowMaterialCalculationOptions
            {
                WindowPrefixes = ["FINESTRA"],
                RequiredLaborKeyword = "Finitura interna"
            });

        Assert.Equal(0, result.TotalLinearMeters);
        UnrecognizedWindowProduct product = Assert.Single(result.UnrecognizedProducts);
        Assert.Equal("FINESTRA prodotto senza misura", product.Name);
        Assert.Equal(2, product.Quantity);
    }

    [Fact]
    public void ConflictingExplicitMeasuresAreReportedAsUnrecognized()
    {
        WindowMaterialCalculationResult result = WindowMaterialCalculator.Calculate(
            [new WindowMaterialProductLine("FINESTRA (78x98) 2070 (94x98)", "", 1)],
            [new WindowMaterialLaborLine("Finitura interna", "")],
            new WindowMaterialCalculationOptions
            {
                WindowPrefixes = ["FINESTRA"],
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
            [new WindowMaterialProductLine("FINESTRA (78x98) 2070", "", quantity)],
            [new WindowMaterialLaborLine("Finitura interna", "")],
            new WindowMaterialCalculationOptions
            {
                WindowPrefixes = ["FINESTRA"],
                RequiredLaborKeyword = "Finitura interna"
            });

        Assert.Equal(0, result.TotalLinearMeters);
        Assert.Empty(result.UnrecognizedProducts);
    }
}
