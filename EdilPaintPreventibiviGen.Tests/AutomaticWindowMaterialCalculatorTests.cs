using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class AutomaticWindowMaterialCalculatorTests
{
    [Fact]
    public void PerimeterRuleRoundsEveryWindowAndReproducesSixtyTwoMeters()
    {
        AutomaticWindowMaterialCalculationResult result = Calculate(
            products:
            [
                new("GGL MK04 2070(78x98)", 7),
                new("GGL MK04 207021A(78x98)", 5),
                new("EDW MK04 2000S(78x98)", 12),
                new("GGL MK08 2070(78x140)", 2),
                new("GGL PK04 2070(94x98)", 1)
            ],
            labors: [new(20, "Finitura interna", 15)],
            rules: [Rule("finish", 20, "Finitura interna", 100, "Perline")],
            catalog: [new(100, "Perline")]);

        AutomaticWindowMaterialPlanLine material = Assert.Single(result.Materials);
        Assert.Equal(15L, result.TotalRecognizedWindowQuantity);
        Assert.Equal(62, material.GrossRequiredQuantity);
        Assert.Equal(62, material.QuantityToAdd);
        Assert.Equal(100, material.MaterialCatalogItemId);
        Assert.Equal(AutomaticMaterialResolutionStatus.ResolvedById, material.MaterialResolution);
        Assert.DoesNotContain(result.Issues, issue =>
            issue.Code == AutomaticWindowMaterialIssueCode.UnrecognizedWindowProduct);
    }

    [Fact]
    public void AttachedQuoteReproducesAutomaticMaterialQuantitiesAndCost()
    {
        AutomaticWindowMaterialCalculationResult result = Calculate(
            products:
            [
                new("GGL MK04 2070(78x98)", 7),
                new("GGL MK04 207021A(78x98)", 5),
                new("GGL MK08 2070(78x140)", 2),
                new("GGL PK04 2070(94x98)", 1)
            ],
            labors:
            [
                new(20, "Finiture Interne", 15),
                new(30, "Installazione finestra", 15)
            ],
            rules:
            [
                Rule("angles", 20, "Finiture Interne", 100, "Angolari al ML"),
                Rule("beading", 20, "Finiture Interne", 101, "Perlina al metro lineare"),
                Rule("tape", 30, "Installazione finestra", 102, "Nastro RIWEGA", parameter: 2m)
            ],
            catalog:
            [
                new(100, "Angolari al ML"),
                new(101, "Perlina al metro lineare"),
                new(102, "Nastro RIWEGA")
            ]);

        Dictionary<int, long> quantities = result.Materials.ToDictionary(
            material => material.MaterialCatalogItemId,
            material => material.GrossRequiredQuantity);
        Assert.Equal(62, quantities[100]);
        Assert.Equal(62, quantities[101]);
        Assert.Equal(122, quantities[102]);

        decimal totalCost = quantities[100] * 9.64m +
                            quantities[101] * 2.03m +
                            quantities[102] * 1.60m;
        Assert.Equal(918.74m, totalCost);
    }

    [Fact]
    public void WindowQuantityOverflowDiscardsAllRecognizedAndCalculatedResults()
    {
        AutomaticWindowMaterialCalculationResult result = Calculate(
            products:
            [
                new("GGL MK08", 2),
                new("GGL MK04", 1),
                new("GGL MK04", int.MaxValue)
            ],
            labors: [new(20, "Finitura interna")],
            rules: [Rule("finish", 20, "Finitura interna", 100, "Perline")],
            catalog: [new(100, "Perline")]);

        Assert.Empty(result.RecognizedWindows);
        Assert.Empty(result.RuleCalculations);
        Assert.Empty(result.Materials);
        Assert.Equal(0L, result.TotalRecognizedWindowQuantity);
        Assert.Contains(result.Issues, issue =>
            issue.Code == AutomaticWindowMaterialIssueCode.QuantityOverflow);
    }

    [Fact]
    public void RuleOverflowDiscardsCalculationsFromOtherwiseValidRules()
    {
        AutomaticWindowMaterialCalculationResult result = Calculate(
            products: [new("GGL MK04", 1)],
            labors:
            [
                new(20, "Finitura interna"),
                new(30, "Supporto corretta quota")
            ],
            rules:
            [
                Rule(
                    "valid",
                    20,
                    "Finitura interna",
                    100,
                    "Perline",
                    AutomaticWindowMaterialModes.FixedPerWindow,
                    1m),
                Rule(
                    "overflow",
                    30,
                    "Supporto corretta quota",
                    100,
                    "Perline",
                    AutomaticWindowMaterialModes.FixedPerWindow,
                    decimal.MaxValue)
            ],
            catalog: [new(100, "Perline")]);

        Assert.Single(result.RecognizedWindows);
        Assert.Empty(result.RuleCalculations);
        Assert.Empty(result.Materials);
        Assert.Contains(result.Issues, issue =>
            issue.Code == AutomaticWindowMaterialIssueCode.QuantityOverflow &&
            issue.RuleId == "overflow");
    }

    [Fact]
    public void AggregateOverflowDiscardsEveryMaterialPlan()
    {
        AutomaticWindowMaterialCalculationResult result = Calculate(
            products: [new("GGL MK04", 1)],
            labors:
            [
                new(20, "Lavoro A"),
                new(30, "Lavoro B"),
                new(40, "Lavoro C")
            ],
            rules:
            [
                Rule(
                    "max",
                    20,
                    "Lavoro A",
                    100,
                    "Perline",
                    AutomaticWindowMaterialModes.FixedPerWindow,
                    long.MaxValue),
                Rule(
                    "overflow",
                    30,
                    "Lavoro B",
                    100,
                    "Perline",
                    AutomaticWindowMaterialModes.FixedPerWindow,
                    1m),
                Rule(
                    "otherwise-valid",
                    40,
                    "Lavoro C",
                    200,
                    "Viti",
                    AutomaticWindowMaterialModes.FixedPerWindow,
                    2m)
            ],
            catalog:
            [
                new(100, "Perline"),
                new(200, "Viti")
            ]);

        Assert.Empty(result.Materials);
        Assert.Contains(result.Issues, issue =>
            issue.Code == AutomaticWindowMaterialIssueCode.QuantityOverflow &&
            issue.ItemName == "Perline");
    }

    [Fact]
    public void GeneratedRuleIdIsReservedAgainstLaterExplicitCollision()
    {
        AutomaticWindowMaterialCalculationResult result = Calculate(
            products: [new("GGL MK04", 1)],
            labors: [new(20, "Finitura interna")],
            rules:
            [
                Rule(
                    "",
                    20,
                    "Finitura interna",
                    100,
                    "Perline",
                    AutomaticWindowMaterialModes.FixedPerWindow,
                    1m),
                Rule(
                    "regola-1",
                    20,
                    "Finitura interna",
                    101,
                    "Listoni",
                    AutomaticWindowMaterialModes.FixedPerWindow,
                    1m)
            ],
            catalog:
            [
                new(100, "Perline"),
                new(101, "Listoni")
            ]);

        AutomaticWindowMaterialPlanLine material = Assert.Single(result.Materials);
        Assert.Equal(100, material.MaterialCatalogItemId);
        Assert.Equal("regola-1", Assert.Single(result.RuleCalculations).RuleId);
        Assert.Contains(result.Issues, issue =>
            issue.Code == AutomaticWindowMaterialIssueCode.DuplicateRule &&
            issue.RuleId == "regola-1");
    }

    [Fact]
    public void TotalRecognizedWindowQuantityUsesLongAcrossDifferentSizes()
    {
        AutomaticWindowMaterialCalculationResult result = Calculate(
            products:
            [
                new("GGL MK04", int.MaxValue),
                new("GGL MK08", 1)
            ],
            labors:
            [
                new(20, "Finitura interna", int.MaxValue),
                new(20, "Finitura interna", 1)
            ],
            rules:
            [
                Rule(
                    "finish",
                    20,
                    "Finitura interna",
                    100,
                    "Perline",
                    AutomaticWindowMaterialModes.FixedPerWindow,
                    1m)
            ],
            catalog: [new(100, "Perline")]);

        long expected = (long)int.MaxValue + 1;
        Assert.Equal(expected, result.TotalRecognizedWindowQuantity);
        Assert.Equal(expected, Assert.Single(result.Materials).GrossRequiredQuantity);
        Assert.DoesNotContain(result.Issues, issue =>
            issue.Code == AutomaticWindowMaterialIssueCode.QuantityOverflow);
    }

    [Fact]
    public void FixedRuleRoundsDecimalQuantityUpForEachWindow()
    {
        AutomaticWindowMaterialCalculationResult result = Calculate(
            products:
            [
                new("GGL MK04 2070", 2),
                new("GGL MK08 2070", 1)
            ],
            labors: [new(30, "Supporto corretta quota", 3)],
            rules:
            [
                Rule(
                    "supports",
                    30,
                    "Supporto corretta quota",
                    200,
                    "Listoni",
                    AutomaticWindowMaterialModes.FixedPerWindow,
                    2.2m)
            ],
            catalog: [new(200, "Listoni")]);

        AutomaticWindowMaterialPlanLine material = Assert.Single(result.Materials);
        Assert.Equal(9, material.GrossRequiredQuantity);
        Assert.All(material.Details, detail => Assert.Equal(3, detail.RoundedQuantityPerWindow));
    }

    [Fact]
    public void ModernLaborIdsDoNotFallBackToEqualNames()
    {
        AutomaticWindowMaterialCalculationResult result = Calculate(
            products: [new("GGL MK04", 1)],
            labors: [new(99, "Finitura interna")],
            rules: [Rule("finish", 20, "Finitura interna", 100, "Perline")],
            catalog: [new(100, "Perline")]);

        Assert.Empty(result.RecognizedWindows);
        Assert.Empty(result.RuleCalculations);
        Assert.Empty(result.Materials);
    }

    [Fact]
    public void LegacyLaborLineFallsBackToExactSnapshotName()
    {
        AutomaticWindowMaterialCalculationResult result = Calculate(
            products: [new("GGL MK04", 1)],
            labors: [new(0, "  FINITURA INTERNA  ")],
            rules: [Rule("finish", 20, "Finitura interna", 100, "Perline")],
            catalog: [new(100, "Perline")]);

        Assert.Equal(4, Assert.Single(result.Materials).QuantityToAdd);
    }

    [Fact]
    public void LegacyMaterialRuleUsesUniqueCatalogNameAndSubtractsByResolvedId()
    {
        AutomaticWindowMaterialCalculationResult result = Calculate(
            products: [new("GGL MK04", 2)],
            labors: [new(20, "Finitura interna", 2)],
            existing: [new(100, "Nome non rilevante", 3)],
            rules: [Rule("finish", 20, "Finitura interna", 0, " perline ")],
            catalog: [new(100, "Perline")]);

        AutomaticWindowMaterialPlanLine material = Assert.Single(result.Materials);
        Assert.True(material.MaterialKey.HasCatalogItemId);
        Assert.Equal(100, material.MaterialCatalogItemId);
        Assert.Equal(8, material.GrossRequiredQuantity);
        Assert.Equal(3, material.AlreadyQuotedQuantity);
        Assert.Equal(5, material.QuantityToAdd);
        Assert.Equal(AutomaticMaterialResolutionStatus.ResolvedByUniqueName, material.MaterialResolution);
    }

    [Fact]
    public void TwoRulesForSameMaterialAreAggregatedBeforeExistingQuantityIsSubtracted()
    {
        AutomaticWindowMaterialCalculationResult result = Calculate(
            products: [new("GGL MK04", 1)],
            labors:
            [
                new(20, "Finitura interna"),
                new(30, "Supporto corretta quota")
            ],
            existing: [new(100, "Perline", 1)],
            rules:
            [
                Rule("finish", 20, "Finitura interna", 100, "Perline"),
                Rule(
                    "support",
                    30,
                    "Supporto corretta quota",
                    100,
                    "Perline",
                    AutomaticWindowMaterialModes.FixedPerWindow,
                    2m)
            ],
            catalog: [new(100, "Perline")]);

        AutomaticWindowMaterialPlanLine material = Assert.Single(result.Materials);
        Assert.Equal(6, material.GrossRequiredQuantity);
        Assert.Equal(1, material.AlreadyQuotedQuantity);
        Assert.Equal(5, material.QuantityToAdd);
        Assert.Equal(2, material.ContributingRuleIds.Count);
    }

    [Fact]
    public void MaterialsWithSameNameAndDifferentIdsRemainSeparate()
    {
        AutomaticWindowMaterialCalculationResult result = Calculate(
            products: [new("GGL MK04", 1)],
            labors:
            [
                new(20, "Lavoro A"),
                new(30, "Lavoro B")
            ],
            existing: [new(0, "Materiale omonimo", 2)],
            rules:
            [
                Rule("a", 20, "Lavoro A", 100, "Materiale omonimo"),
                Rule("b", 30, "Lavoro B", 101, "Materiale omonimo")
            ],
            catalog:
            [
                new(100, "Materiale omonimo"),
                new(101, "Materiale omonimo")
            ]);

        Assert.Equal(2, result.Materials.Count);
        Assert.All(result.Materials, material =>
        {
            Assert.Equal(4, material.GrossRequiredQuantity);
            Assert.Equal(0, material.AlreadyQuotedQuantity);
            Assert.Equal(4, material.QuantityToAdd);
        });
        Assert.Contains(result.Issues, issue =>
            issue.Code == AutomaticWindowMaterialIssueCode.AmbiguousExistingMaterial);
    }

    [Fact]
    public void AmbiguousLegacyMaterialDoesNotChooseCatalogItemOrSubtractLegacyQuantity()
    {
        AutomaticWindowMaterialCalculationResult result = Calculate(
            products: [new("GGL MK04", 1)],
            labors: [new(20, "Finitura interna")],
            existing: [new(0, "Perline", 2)],
            rules: [Rule("finish", 20, "Finitura interna", 0, "Perline")],
            catalog:
            [
                new(100, "Perline"),
                new(101, "Perline")
            ]);

        AutomaticWindowMaterialPlanLine material = Assert.Single(result.Materials);
        Assert.False(material.MaterialKey.HasCatalogItemId);
        Assert.Equal(AutomaticMaterialResolutionStatus.AmbiguousName, material.MaterialResolution);
        Assert.Equal(0, material.AlreadyQuotedQuantity);
        Assert.Equal(4, material.QuantityToAdd);
        Assert.Contains(result.Issues, issue =>
            issue.Code == AutomaticWindowMaterialIssueCode.AmbiguousCatalogMaterial);
        Assert.Contains(result.Issues, issue =>
            issue.Code == AutomaticWindowMaterialIssueCode.AmbiguousExistingMaterial);
    }

    [Fact]
    public void MissingCatalogItemKeepsConfiguredIdAndCanSubtractSameId()
    {
        AutomaticWindowMaterialCalculationResult result = Calculate(
            products: [new("GGL MK04", 1)],
            labors: [new(20, "Finitura interna")],
            existing: [new(999, "Vecchie perline", 1)],
            rules: [Rule("finish", 20, "Finitura interna", 999, "Perline eliminate")]);

        AutomaticWindowMaterialPlanLine material = Assert.Single(result.Materials);
        Assert.Equal(999, material.MaterialCatalogItemId);
        Assert.Equal(AutomaticMaterialResolutionStatus.MissingFromCatalog, material.MaterialResolution);
        Assert.Equal(1, material.AlreadyQuotedQuantity);
        Assert.Equal(3, material.QuantityToAdd);
        Assert.Contains(result.Issues, issue =>
            issue.Code == AutomaticWindowMaterialIssueCode.MissingCatalogMaterial);
    }

    [Fact]
    public void CatalogRenameRetainsSnapshotAsAliasForLegacyQuoteLine()
    {
        AutomaticWindowMaterialCalculationResult result = Calculate(
            products: [new("GGL MK04", 1)],
            labors: [new(20, "Finitura interna")],
            existing: [new(0, "Vecchie perline", 1)],
            rules: [Rule("finish", 20, "Finitura interna", 100, "Vecchie perline")],
            catalog: [new(100, "Perline abete")]);

        AutomaticWindowMaterialPlanLine material = Assert.Single(result.Materials);
        Assert.Equal("Perline abete", material.MaterialName);
        Assert.Equal(1, material.AlreadyQuotedQuantity);
        Assert.Equal(3, material.QuantityToAdd);
    }

    [Fact]
    public void DuplicateRuleIsIgnoredInsteadOfDoublingRequirement()
    {
        AutomaticWindowMaterialCalculationResult result = Calculate(
            products: [new("GGL MK04", 1)],
            labors: [new(20, "Finitura interna")],
            rules:
            [
                Rule("first", 20, "Finitura interna", 100, "Perline"),
                Rule("second", 20, "Finitura interna", 100, "Perline")
            ],
            catalog: [new(100, "Perline")]);

        AutomaticWindowMaterialPlanLine material = Assert.Single(result.Materials);
        Assert.Equal(4, material.GrossRequiredQuantity);
        Assert.Single(material.ContributingRuleIds);
        Assert.Contains(result.Issues, issue =>
            issue.Code == AutomaticWindowMaterialIssueCode.DuplicateRule);
    }

    [Fact]
    public void ExistingMaterialWithDifferentModernIdDoesNotMatchByName()
    {
        AutomaticWindowMaterialCalculationResult result = Calculate(
            products: [new("GGL MK04", 1)],
            labors: [new(20, "Finitura interna")],
            existing: [new(101, "Perline", 4)],
            rules: [Rule("finish", 20, "Finitura interna", 100, "Perline")],
            catalog:
            [
                new(100, "Perline"),
                new(101, "Perline")
            ]);

        AutomaticWindowMaterialPlanLine material = Assert.Single(result.Materials);
        Assert.Equal(0, material.AlreadyQuotedQuantity);
        Assert.Equal(4, material.QuantityToAdd);
    }

    [Fact]
    public void ExactAndExcessExistingQuantitiesNeverProduceNegativeAddition()
    {
        AutomaticWindowMaterialCalculationResult exact = Calculate(
            products: [new("GGL MK04", 1)],
            labors: [new(20, "Finitura interna")],
            existing: [new(100, "Perline", 4)],
            rules: [Rule("finish", 20, "Finitura interna", 100, "Perline")],
            catalog: [new(100, "Perline")]);
        AutomaticWindowMaterialCalculationResult excess = Calculate(
            products: [new("GGL MK04", 1)],
            labors: [new(20, "Finitura interna")],
            existing: [new(100, "Perline", 10)],
            rules: [Rule("finish", 20, "Finitura interna", 100, "Perline")],
            catalog: [new(100, "Perline")]);

        Assert.Equal(0, Assert.Single(exact.Materials).QuantityToAdd);
        Assert.Equal(0, Assert.Single(excess.Materials).QuantityToAdd);
    }

    [Fact]
    public void InvalidModeAndParameterAreReportedAndSkipped()
    {
        AutomaticWindowMaterialRule badMode = Rule(
            "mode",
            20,
            "Finitura interna",
            100,
            "Perline",
            "Area",
            1m);
        AutomaticWindowMaterialRule badParameter = Rule(
            "parameter",
            20,
            "Finitura interna",
            100,
            "Perline",
            AutomaticWindowMaterialModes.Perimeter,
            0m);

        AutomaticWindowMaterialCalculationResult result = Calculate(
            products: [new("GGL MK04", 1)],
            labors: [new(20, "Finitura interna")],
            rules: [badMode, badParameter],
            catalog: [new(100, "Perline")]);

        Assert.Empty(result.Materials);
        Assert.Contains(result.Issues, issue =>
            issue.Code == AutomaticWindowMaterialIssueCode.UnsupportedMode);
        Assert.Contains(result.Issues, issue =>
            issue.Code == AutomaticWindowMaterialIssueCode.InvalidRule);
    }

    [Fact]
    public void PrefixMatchedProductWithoutMeasureIsReportedOnce()
    {
        AutomaticWindowMaterialCalculationResult result = Calculate(
            products:
            [
                new("GGL prodotto senza misura", 2),
                new("EDW MK04", 10)
            ],
            labors: [new(20, "Finitura interna")],
            rules: [Rule("finish", 20, "Finitura interna", 100, "Perline")],
            catalog: [new(100, "Perline")]);

        Assert.Empty(result.Materials);
        Assert.Single(result.Issues, issue =>
            issue.Code == AutomaticWindowMaterialIssueCode.UnrecognizedWindowProduct);
        Assert.Single(result.Issues, issue =>
            issue.Code == AutomaticWindowMaterialIssueCode.NoRecognizedWindows);
    }

    [Fact]
    public void WindowRuleUsesLaborQuantityInsteadOfEveryRecognizedWindow()
    {
        AutomaticWindowMaterialCalculationResult result = Calculate(
            products: [new("GGL MK04", 3)],
            labors: [new(20, "Finitura interna", 2)],
            rules: [Rule("finish", 20, "Finitura interna", 100, "Perline")],
            catalog: [new(100, "Perline")]);

        AutomaticWindowMaterialPlanLine material = Assert.Single(result.Materials);
        AutomaticWindowMaterialRuleCalculation calculation = Assert.Single(result.RuleCalculations);
        Assert.Equal(3, result.TotalRecognizedWindowQuantity);
        Assert.Equal(2, calculation.LaborQuantity);
        Assert.Equal(8, material.GrossRequiredQuantity);
        Assert.Equal(2, Assert.Single(material.Details).WindowQuantity);
    }

    [Fact]
    public void GenericRuleMultipliesParameterByLaborQuantityWithoutWindows()
    {
        AutomaticWindowMaterialCalculationResult result = Calculate(
            products: [],
            labors: [new(20, "Finitura interna", 3)],
            rules:
            [
                Rule(
                    "finish",
                    20,
                    "Finitura interna",
                    100,
                    "Stucco",
                    parameter: 1.5m,
                    isWindowAutomation: false)
            ],
            catalog: [new(100, "Stucco")]);

        AutomaticWindowMaterialPlanLine material = Assert.Single(result.Materials);
        AutomaticWindowMaterialRuleCalculation calculation = Assert.Single(result.RuleCalculations);
        Assert.False(calculation.IsWindowAutomation);
        Assert.Equal(3, calculation.LaborQuantity);
        Assert.Equal(5, material.GrossRequiredQuantity);
        Assert.Empty(material.Details);
        Assert.DoesNotContain(result.Issues, issue =>
            issue.Code == AutomaticWindowMaterialIssueCode.NoRecognizedWindows);
    }

    [Fact]
    public void WindowRuleWarnsWhenLaborQuantityExceedsRecognizedWindows()
    {
        AutomaticWindowMaterialCalculationResult result = Calculate(
            products: [new("GGL MK04", 2)],
            labors: [new(20, "Finitura interna", 3)],
            rules: [Rule("finish", 20, "Finitura interna", 100, "Perline")],
            catalog: [new(100, "Perline")]);

        Assert.Equal(8, Assert.Single(result.Materials).GrossRequiredQuantity);
        Assert.Contains(result.Issues, issue =>
            issue.Code == AutomaticWindowMaterialIssueCode.InsufficientRecognizedWindows);
    }

    private static AutomaticWindowMaterialCalculationResult Calculate(
        IReadOnlyCollection<AutomaticWindowProductLine> products,
        IReadOnlyCollection<AutomaticWindowLaborLine> labors,
        IReadOnlyCollection<AutomaticQuoteMaterialLine>? existing = null,
        IReadOnlyCollection<AutomaticWindowMaterialRule>? rules = null,
        IReadOnlyCollection<AutomaticMaterialCatalogItem>? catalog = null) =>
        AutomaticWindowMaterialCalculator.Calculate(new AutomaticWindowMaterialCalculationInput
        {
            WindowProducts = products,
            Labors = labors,
            ExistingQuoteMaterials = existing ?? Array.Empty<AutomaticQuoteMaterialLine>(),
            Rules = rules ?? Array.Empty<AutomaticWindowMaterialRule>(),
            WindowPrefixes = ["GGL", "GGU", "Q4", "R8"],
            MaterialCatalog = catalog ?? Array.Empty<AutomaticMaterialCatalogItem>()
        });

    private static AutomaticWindowMaterialRule Rule(
        string id,
        int laborId,
        string laborName,
        int materialId,
        string materialName,
        string mode = AutomaticWindowMaterialModes.Perimeter,
        decimal parameter = 1m,
        bool isWindowAutomation = true) =>
        new()
        {
            RuleId = id,
            IsWindowAutomation = isWindowAutomation,
            LaborCatalogItemId = laborId,
            LaborNameSnapshot = laborName,
            MaterialCatalogItemId = materialId,
            MaterialNameSnapshot = materialName,
            Mode = mode,
            Parameter = parameter
        };
}
