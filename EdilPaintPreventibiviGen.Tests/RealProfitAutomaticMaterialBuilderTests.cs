using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class RealProfitAutomaticMaterialBuilderTests
{
    [Fact]
    public void RebindsStaleRuleAndQuoteIdsToCurrentUniqueCatalogNamesAndPrice()
    {
        var quote = Quote(laborId: 12, materialId: 5, existingQuantity: 2);
        var result = Build(quote, Rule(laborId: 12, materialId: 5));
        var material = Assert.Single(result.Materials);
        Assert.Equal(4, material.Quantity);
        Assert.Equal(17.5, material.UnitCost);
        Assert.Equal("Automatico", material.Source);
        Assert.False(result.HasWarnings);
        Assert.Equal(12, quote.Labors[0].PersistentId);
        Assert.Equal(5, quote.Materials[0].PersistentId);
    }

    [Fact]
    public void ValidIdPreservesCatalogRenameAndExistingIdentity()
    {
        var quote = Quote(120, 50, 1); quote.Labors[0].Name = "Nome vecchio";
        var rule = Rule(120, 50); rule.LaborName = "Nome vecchio"; rule.MaterialName = "Materiale vecchio";
        var result = Build(quote, rule);
        Assert.Equal("Stucco", Assert.Single(result.Materials).Name);
        Assert.Equal(5, Assert.Single(result.Materials).Quantity);
        Assert.False(result.HasWarnings);
    }

    [Fact]
    public void DifferentDatabaseDoesNotTrustCollidingIds()
    {
        var labors = new[] { Item(12, "Altro lavoro"), Item(120, "Finitura") };
        var materials = new[] { Item(5, "Altro materiale", company: true), Item(50, "Stucco", 17.5, true) };
        var result = RealProfitAutomaticMaterialBuilder.Build(Quote(120, 50), Settings(Rule(12, 5)),
            labors, materials, trustCatalogIds: false);
        Assert.Equal("Stucco", Assert.Single(result.Materials).Name);
        Assert.Equal(17.5, Assert.Single(result.Materials).UnitCost);
    }

    [Fact]
    public void AmbiguousLaborNameDoesNotChooseCostAutomatically()
    {
        var result = RealProfitAutomaticMaterialBuilder.Build(Quote(), Settings(Rule()),
            [Item(120, "Finitura"), Item(121, "Finitura")], [Item(50, "Stucco", 17.5, true)]);
        Assert.Empty(result.Materials);
        Assert.True(result.HasWarnings);
        Assert.Contains(result.Notices, x => x.Contains("non è riconosciuta in modo univoco"));
    }

    [Fact]
    public void AmbiguousMaterialNameNeverCreatesZeroCostRow()
    {
        var result = RealProfitAutomaticMaterialBuilder.Build(Quote(), Settings(Rule()),
            [Item(120, "Finitura")], [Item(50, "Stucco", 17.5, true), Item(51, "Stucco", 28, true)]);
        Assert.Empty(result.Materials);
        Assert.True(result.HasWarnings);
    }

    [Fact]
    public void MissingMaterialDoesNotProduceZeroCost()
    {
        var result = RealProfitAutomaticMaterialBuilder.Build(Quote(), Settings(Rule()), [Item(120, "Finitura")], []);
        Assert.Empty(result.Materials); Assert.True(result.HasWarnings);
    }

    [Fact]
    public void KnownDifferentLaborIdDoesNotMatchAnEqualName()
    {
        var result = RealProfitAutomaticMaterialBuilder.Build(Quote(121), Settings(Rule(120, 50)),
            [Item(120, "Finitura"), Item(121, "Finitura")], [Item(50, "Stucco", 17.5, true)]);
        Assert.Empty(result.Materials);
        Assert.False(result.HasWarnings);
    }

    [Fact]
    public void GenericRulesRemainActiveWhenWindowAutomationsDisabled()
    {
        var generic = Rule(120, 50);
        var window = Rule(120, 51); window.IsWindowAutomation = true; window.MaterialName = "Perline";
        var result = RealProfitAutomaticMaterialBuilder.Build(Quote(120), Settings(generic, window),
            [Item(120, "Finitura")], [Item(50, "Stucco", 17.5, true), Item(51, "Perline", 5, true)],
            enableWindowAutomations: false);
        Assert.Single(result.Materials); Assert.Equal("Stucco", result.Materials[0].Name);
        Assert.False(result.HasWarnings);
    }

    [Fact]
    public void CurrentPriceIsUsedWhenGeneratingOnSecondComputer()
    {
        var settings = Settings(Rule(120, 50));
        var quote = Quote(120);
        var first = RealProfitAutomaticMaterialBuilder.Build(quote, settings,
            [Item(120, "Finitura")], [Item(50, "Stucco", 10, true)]);
        var second = RealProfitAutomaticMaterialBuilder.Build(quote, settings,
            [Item(120, "Finitura")], [Item(50, "Stucco", 19, true)]);
        Assert.Equal(60, first.Materials[0].Total);
        Assert.Equal(114, second.Materials[0].Total);
    }

    [Fact]
    public void NonCompanyMaterialIsNotAcceptedAsAutomaticCompanyCost()
    {
        var result = RealProfitAutomaticMaterialBuilder.Build(Quote(), Settings(Rule()),
            [Item(120, "Finitura")], [Item(50, "Stucco", 17.5)]);
        Assert.Empty(result.Materials); Assert.True(result.HasWarnings);
    }

    [Fact]
    public void RebindingSettingsClonesWithoutChangingSharedRules()
    {
        var original = Settings(Rule(12, 5));
        var rebound = RealProfitAutomaticMaterialBuilder.RebindSettings(original,
            [Item(120, "Finitura")], [Item(50, "Stucco", 17.5, true)]);
        Assert.Equal(120, rebound.WindowMaterialRules[0].LaborCatalogId);
        Assert.Equal(50, rebound.WindowMaterialRules[0].MaterialCatalogId);
        Assert.Equal(12, original.WindowMaterialRules[0].LaborCatalogId);
    }

    [Fact]
    public void UnusedInvalidRuleDoesNotBlockUnrelatedQuote()
    {
        var quote = new QuoteHistoryEntry { Labors = [new Item { Name = "Pittura", PersistentId = 80, Quantity = 10 }] };
        var settings = Settings(Rule(12, 5));
        settings.ResolutionWarnings.Add("Regola disattivata durante la migrazione: finitura e stucco mancanti.");
        var result = RealProfitAutomaticMaterialBuilder.Build(quote, settings, [Item(80, "Pittura")], []);
        Assert.Empty(result.Materials); Assert.Empty(result.Notices); Assert.False(result.HasWarnings);
    }

    [Fact]
    public void QuoteWithCollidingIdUsesVerifiableUniqueName()
    {
        var result = RealProfitAutomaticMaterialBuilder.Build(Quote(12, 5, 2), Settings(Rule(120, 50)),
            [Item(12, "Altra lavorazione"), Item(120, "Finitura")],
            [Item(5, "Altro materiale", 100, true), Item(50, "Stucco", 17.5, true)]);
        Assert.Equal("Stucco", Assert.Single(result.Materials).Name);
        Assert.Equal(4, result.Materials[0].Quantity);
        Assert.False(result.HasWarnings);
    }

    [Fact]
    public void LegacyQuoteWithAmbiguousLaborIsNotAppliedToBothModernRules()
    {
        var result = RealProfitAutomaticMaterialBuilder.Build(Quote(), Settings(Rule(120, 50)),
            [Item(120, "Finitura"), Item(121, "Finitura")], [Item(50, "Stucco", 17.5, true)]);
        Assert.Empty(result.Materials); Assert.True(result.HasWarnings);
    }

    private static AutomaticMaterialBuildResult Build(QuoteHistoryEntry quote, WindowMaterialRuleSettingsModel rule) =>
        RealProfitAutomaticMaterialBuilder.Build(quote, Settings(rule), [Item(120, "Finitura")],
            [Item(50, "Stucco", 17.5, true)]);
    private static Item Item(int id, string name, double price = 0, bool company = false) =>
        new() { PersistentId = id, Name = name, UnitPrice = price, IsCompanyMaterial = company };
    private static QuoteHistoryEntry Quote(int laborId = 0, int materialId = 0, decimal existingQuantity = 0) => new()
    {
        Labors = [new Item { PersistentId = laborId, Name = "Finitura", Quantity = 3 }],
        Materials = existingQuantity > 0
            ? [new Item { PersistentId = materialId, Name = "Stucco", Quantity = existingQuantity }] : []
    };
    private static WindowMaterialRuleSettingsModel Rule(int laborId = 0, int materialId = 0) => new()
    {
        LaborCatalogId = laborId, LaborName = "Finitura", MaterialCatalogId = materialId, MaterialName = "Stucco",
        IsWindowAutomation = false, CalculationMode = "FixedPerWindow", QuantityParameter = 2
    };
    private static AutomaticMaterialSettings Settings(params WindowMaterialRuleSettingsModel[] rules) =>
        new() { WindowMaterialRules = rules.ToList(), WindowProductPrefixes = ["GGL"] };
}
