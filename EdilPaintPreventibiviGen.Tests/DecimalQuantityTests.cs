using System.Globalization;
using System.Text.Json;
using EdilPaintPreventibiviGen.Data;
using EdilPaintPreventibiviGen.Data.Entities;
using EdilPaintPreventibiviGen.Data.Mappers;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class DecimalQuantityTests
{
    [Theory]
    [InlineData("2,5", "2.5")]
    [InlineData("2.5", "2.5")]
    [InlineData("0,125", "0.125")]
    [InlineData("1", "1")]
    [InlineData("10000", "10000")]
    [InlineData("1000000000000000", "1000000000000000")]
    [InlineData("10000,123456789", "10000.123456789")]
    [InlineData(" 00010000,123456789000 ", "10000.123456789")]
    [InlineData("9999999999999999999,999999999", "9999999999999999999.999999999")]
    [InlineData("0,000000001", "0.000000001")]
    public void ParsePreservesFractions(string text, string expected)
    {
        Assert.True(QuantityValue.TryParse(text, out decimal quantity));
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), quantity);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("1,2345678912")]
    [InlineData("1,2,3")]
    [InlineData("NaN")]
    [InlineData("10000000000000000000")]
    [InlineData("9999999999999999999,9999999991")]
    [InlineData("1,00000000000000000000000000001")]
    public void InvalidQuantityIsRejectedWithoutRounding(string value) => Assert.False(QuantityValue.TryParse(value, out _));

    [Fact]
    public void LargeQuantityKeepsPrecisionThroughFormattingSqlAndTotals()
    {
        const decimal quantity = 10000.123456789m;
        Assert.Equal("10000,123456789", QuantityValue.Format(quantity));
        var sqlValue = System.Data.SqlTypes.SqlDecimal.ConvertToPrecScale(
            new System.Data.SqlTypes.SqlDecimal(QuantityValue.Maximum), QuantityValue.Precision, QuantityValue.Scale);
        Assert.Equal(QuantityValue.Maximum, sqlValue.Value);
        var item = new Item { Quantity = 10000m, UnitOfMeasure = "m²", UnitPrice = 2.5 };
        var totals = new QuoteCalculator().Calculate([item], [], 0, 0, "22%");
        Assert.Equal(25000, totals.Imponibile);
        Assert.Equal(30500, totals.TotaleGenerale);
    }

    [Fact]
    public void SyncDetectsChangesBeyondTheThirdDecimalForBothLineTypes()
    {
        var quote = new QuoteHistoryEntry
        {
            Materials = [new Item { Quantity = 10000.123456781m }],
            Labors = [new Item { Quantity = 10000.123456781m }]
        };
        string before = QuoteSyncHashService.Compute(quote);
        quote.Materials[0].Quantity = 10000.123456782m;
        string changedMaterial = QuoteSyncHashService.Compute(quote);
        Assert.NotEqual(before, changedMaterial);
        quote.Labors[0].Quantity = 10000.123456782m;
        Assert.NotEqual(changedMaterial, QuoteSyncHashService.Compute(quote));
    }

    [Fact]
    public void LegacyJsonKeepsIntegerQuantityAndDefaultsToPieces()
    {
        var item = JsonSerializer.Deserialize<Item>("""{"Name":"Vecchia voce","Quantity":2,"UnitPrice":10} """)!;
        Assert.Equal(2m, item.Quantity);
        Assert.Equal("pz", item.UnitOfMeasure);
        Assert.Equal(20, item.TotalPrice);
    }

    [Fact]
    public void FractionalHoursHaveConsistentRoundedVatAndGrandTotal()
    {
        var totals = new QuoteCalculator().Calculate(
            [new Item { Quantity = 2.5m, UnitPrice = 40 }],
            [new Item { Quantity = 1.125m, UnitPrice = 50 }], 0, 0, "22%");
        Assert.Equal(156.25, totals.Imponibile, 8);
        Assert.Equal(34.38, totals.IvaTotale, 8);
        Assert.Equal(190.63, totals.TotaleGenerale, 8);
    }

    [Fact]
    public void FractionalQuantityFlowsThroughVatProfitAndWorkSheet()
    {
        var material = new Item { Name = "Rivestimento", Quantity = 2.5m, UnitOfMeasure = "m²", UnitPrice = 40, Discount = 10 };
        Assert.Equal(90, material.TotalPrice);
        var totals = new QuoteCalculator().Calculate([material], [], 0, 0, "22%");
        Assert.Equal(19.8, totals.IvaTotale, 8);
        var profit = RealProfitCalculator.Calculate(new RealProfitInput
        {
            QuoteRevenue = 90, SupplierDiscount = 25,
            Materials = [new ProfitMaterialCost { Quantity = 2.5m, UnitOfMeasure = "m²", CustomerUnitPrice = 40, CustomerDiscount = 10 }],
            CompanyMaterials = [new CompanyMaterialCost { Quantity = 1.5m, UnitOfMeasure = "h", UnitCost = 4 }]
        });
        Assert.Equal(75, profit.SupplierMaterialCost);
        Assert.Equal(6, profit.CompanyMaterialCost);
        Assert.Equal(9, profit.Profit);
        var sheet = WorkSheetService.CreateContext(new QuoteHistoryEntry { Materials = [material] }, [], []);
        Assert.Equal(2.5m, Assert.Single(sheet.Materials).Quantity);
        Assert.Equal("m²", sheet.Materials[0].UnitOfMeasure);
    }

    [Fact]
    public async Task CatalogHistoryAndDraftRoundTripPreservesUnitsAndFractions()
    {
        string root = Path.Combine(Path.GetTempPath(), "quantity-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var item = new Item { Name = "Posa", Quantity = 10000.123456789m, UnitOfMeasure = "h", UnitPrice = 50 };
            var store = new LocalJsonStoreService(root);
            await store.SaveLaborCatalogAsync([item]);
            Assert.Equal("h", Assert.Single(await store.LoadLaborCatalogAsync()).UnitOfMeasure);
            var quote = new QuoteHistoryEntry { QuoteNumber = "42", Labors = [item] };
            await store.BulkUpdateQuotesAsync([quote]);
            var stored = Assert.Single((await store.LoadHistoryAsync()).Single().Labors);
            Assert.Equal(10000.123456789m, stored.Quantity);
            Assert.Equal("h", stored.UnitOfMeasure);
            var drafts = new LocalDraftService(root);
            Assert.True(await drafts.SaveIfChangedAsync(quote));
            item.UnitOfMeasure = "m";
            Assert.True(await drafts.SaveIfChangedAsync(quote));
            var restored = Assert.Single((await drafts.LoadAsync())!.Labors);
            Assert.Equal(10000.123456789m, restored.Quantity);
            Assert.Equal("m", restored.UnitOfMeasure);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void HashDetectsUnitChangesAndDoesNotDependOnDecimalCulture()
    {
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            var quote = new QuoteHistoryEntry { Materials = [new Item { Quantity = 2.125m, UnitOfMeasure = "m²" }] };
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("it-IT");
            string hash = QuoteSyncHashService.Compute(quote);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal(hash, QuoteSyncHashService.Compute(quote));
            quote.Materials[0].UnitOfMeasure = "m";
            Assert.NotEqual(hash, QuoteSyncHashService.Compute(quote));
        }
        finally { CultureInfo.CurrentCulture = oldCulture; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DatabaseModelsAndUpgradeUseExactDecimalQuantity(bool postgres)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        if (postgres) builder.UseNpgsql("Host=localhost;Database=unused");
        else builder.UseSqlServer("Server=localhost;Database=unused;Integrated Security=True");
        using var db = new AppDbContext(builder.Options);
        foreach (Type type in new[] { typeof(QuoteMaterialEntity), typeof(QuoteLaborEntity) })
        {
            var entity = db.Model.FindEntityType(type)!;
            Assert.Equal(28, entity.FindProperty("Quantity")!.GetPrecision());
            Assert.Equal(9, entity.FindProperty("Quantity")!.GetScale());
            Assert.Equal("pz", entity.FindProperty("UnitOfMeasure")!.GetDefaultValue());
        }
        string sql = SqlDataService.BuildQuantitySchemaSql("QuoteMaterials", postgres, true);
        Assert.Contains(postgres ? "numeric(28,9)" : "decimal(28,9)", sql);
        Assert.Contains(postgres ? "numeric_precision = 18 AND numeric_scale = 3" : "c.precision = 18 AND c.scale = 3", sql);
        Assert.Contains("UnitOfMeasure", sql);
        var model = new QuoteEntity { Materials = [new QuoteMaterialEntity { Quantity = 2.125m, UnitOfMeasure = "m²" }] }.ToModel();
        Assert.Equal(2.125m, model.Materials[0].Quantity);
        Assert.Equal("m²", model.Materials[0].UnitOfMeasure);
    }

    [Fact]
    public void AutomaticMaterialsSubtractFractionalQuotedQuantity()
    {
        var result = AutomaticWindowMaterialCalculator.Calculate(new AutomaticWindowMaterialCalculationInput
        {
            Labors = [new(1, "Posa", 1.5m, "h")],
            MaterialCatalog = [new(2, "Nastro", "m")],
            ExistingQuoteMaterials = [new(2, "Nastro", 0.5m, "m")],
            Rules = [new() { Enabled = true, IsWindowAutomation = false, LaborCatalogItemId = 1, MaterialCatalogItemId = 2, Parameter = 2, RuleId = "r" }]
        });
        Assert.Equal(2.5m, Assert.Single(result.Materials).QuantityToAdd);
    }

    [Fact]
    public void GenericAutomaticMaterialsSupportQuantitiesBeyondIntegerRange()
    {
        var result = AutomaticWindowMaterialCalculator.Calculate(new AutomaticWindowMaterialCalculationInput
        {
            Labors = [new(1, "Posa", 3000000000m, "m²")],
            MaterialCatalog = [new(2, "Vernice", "l")],
            Rules = [new() { Enabled = true, IsWindowAutomation = false, LaborCatalogItemId = 1, MaterialCatalogItemId = 2, Parameter = 1, RuleId = "r" }]
        });
        decimal quantity = Assert.Single(result.Materials).QuantityToAdd;
        Assert.Equal(3000000000m, quantity);
        Assert.True(QuantityValue.IsValid(quantity));
    }

    [Fact]
    public void WindowAutomationRejectsFractionalPiecesInsteadOfTruncating()
    {
        var result = AutomaticWindowMaterialCalculator.Calculate(new AutomaticWindowMaterialCalculationInput
        {
            WindowProducts = [new("GGL MK04 78x98", 1.5m)], WindowPrefixes = ["GGL"],
            Labors = [new(1, "Posa", 1.5m)], MaterialCatalog = [new(2, "Nastro")],
            Rules = [new() { Enabled = true, IsWindowAutomation = true, LaborCatalogItemId = 1, MaterialCatalogItemId = 2, Parameter = 1, RuleId = "r", Mode = AutomaticWindowMaterialModes.Perimeter }]
        });
        Assert.Empty(result.Materials);
        Assert.Contains(result.Issues, issue => issue.Code == AutomaticWindowMaterialIssueCode.InvalidRule);
    }
}
