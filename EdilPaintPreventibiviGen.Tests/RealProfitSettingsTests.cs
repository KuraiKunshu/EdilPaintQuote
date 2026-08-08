using EdilPaintPreventibiviGen.Services;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Views;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class RealProfitSettingsTests
{
    [Fact]
    public void NormalizeAddsAutomaticMaterialDefaultsForLegacySettings()
    {
        var settings = new RealProfitSettingsModel();

        settings.Normalize();

        Assert.Equal(0, settings.ProfitReductionPercentage);
        Assert.Equal(["GGL", "GGU", "GPL", "GPU", "Q4", "R8"], settings.WindowProductPrefixes);
        Assert.Equal("Finitura interna", settings.InternalFinishLaborKeyword);
        Assert.Equal("Perline", settings.InternalFinishMaterialName);
        Assert.Equal(RealProfitSettingsModel.CurrentWindowMaterialRulesSchemaVersion, settings.WindowMaterialRulesSchemaVersion);
        WindowMaterialRuleSettingsModel rule = Assert.Single(settings.WindowMaterialRules);
        Assert.True(rule.Enabled);
        Assert.True(rule.IsWindowAutomation);
        Assert.Null(rule.LaborCatalogId);
        Assert.Equal("Finitura interna", rule.LaborName);
        Assert.Null(rule.MaterialCatalogId);
        Assert.Equal("Perline", rule.MaterialName);
        Assert.Equal(WindowMaterialRuleSettingsModel.PerimeterCalculationMode, rule.CalculationMode);
        Assert.Equal(1m, rule.QuantityParameter);
    }

    [Fact]
    public void NormalizeCleansConfiguredPrefixesAndClampsReduction()
    {
        var settings = new RealProfitSettingsModel
        {
            ProfitReductionPercentage = 140,
            WindowProductPrefixes = [" ggl ", "GGL", " q4", "", "R8 "],
            InternalFinishLaborKeyword = "  finitura interna  ",
            InternalFinishMaterialName = "  Perline abete  "
        };

        settings.Normalize();

        Assert.Equal(100, settings.ProfitReductionPercentage);
        Assert.Equal(["GGL", "Q4", "R8"], settings.WindowProductPrefixes);
        Assert.Equal("finitura interna", settings.InternalFinishLaborKeyword);
        Assert.Equal("Perline abete", settings.InternalFinishMaterialName);
        WindowMaterialRuleSettingsModel rule = Assert.Single(settings.WindowMaterialRules);
        Assert.Equal("finitura interna", rule.LaborName);
        Assert.Equal("Perline abete", rule.MaterialName);
    }

    [Fact]
    public void CustomBoundPrefixesDoNotIncludeDefaultPrefixes()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RealProfit:Workers"] = "3",
                ["RealProfit:WindowProductPrefixes:0"] = "CUSTOM",
                ["RealProfit:WindowProductPrefixes:1"] = "r9"
            })
            .Build();

        RealProfitSettingsModel settings =
            configuration.GetSection("RealProfit").Get<RealProfitSettingsModel>()!;
        settings.Normalize();

        Assert.Equal(3, settings.Workers);
        Assert.Equal(["CUSTOM", "R9"], settings.WindowProductPrefixes);
    }

    [Fact]
    public void NonFiniteReductionFallsBackToZero()
    {
        var settings = new RealProfitSettingsModel
        {
            ProfitReductionPercentage = double.NaN,
            WindowProductPrefixes = null!,
            InternalFinishMaterialName = " "
        };

        settings.Normalize();

        Assert.Equal(0, settings.ProfitReductionPercentage);
        Assert.Equal(RealProfitSettingsModel.CreateDefaultWindowProductPrefixes(), settings.WindowProductPrefixes);
        Assert.Equal(RealProfitSettingsModel.DefaultInternalFinishMaterialName, settings.InternalFinishMaterialName);
    }

    [Fact]
    public void LegacyPairMigratesToPerimeterRule()
    {
        var settings = new RealProfitSettingsModel
        {
            WindowMaterialRulesSchemaVersion = 0,
            WindowMaterialRules = null!,
            InternalFinishLaborKeyword = "  Creazione supporto  ",
            InternalFinishMaterialName = "  Listoni  "
        };

        settings.Normalize();

        Assert.Equal(RealProfitSettingsModel.CurrentWindowMaterialRulesSchemaVersion, settings.WindowMaterialRulesSchemaVersion);
        WindowMaterialRuleSettingsModel rule = Assert.Single(settings.WindowMaterialRules);
        Assert.True(rule.IsWindowAutomation);
        Assert.Equal("Creazione supporto", rule.LaborName);
        Assert.Equal("Listoni", rule.MaterialName);
        Assert.Equal(WindowMaterialRuleSettingsModel.PerimeterCalculationMode, rule.CalculationMode);
        Assert.Equal(1m, rule.QuantityParameter);
    }

    [Fact]
    public void EmptyVersionedRuleListRemainsEmpty()
    {
        var settings = new RealProfitSettingsModel
        {
            WindowMaterialRulesSchemaVersion = RealProfitSettingsModel.CurrentWindowMaterialRulesSchemaVersion,
            WindowMaterialRules = [],
            InternalFinishLaborKeyword = "Finitura interna",
            InternalFinishMaterialName = "Perline"
        };

        settings.Normalize();

        Assert.Empty(settings.WindowMaterialRules);
        Assert.Equal(string.Empty, settings.InternalFinishLaborKeyword);
        Assert.Equal(string.Empty, settings.InternalFinishMaterialName);
    }

    [Fact]
    public void VersionedRulesNormalizeCatalogIdsModeAndQuantity()
    {
        var settings = new RealProfitSettingsModel
        {
            WindowMaterialRulesSchemaVersion = RealProfitSettingsModel.CurrentWindowMaterialRulesSchemaVersion,
            WindowMaterialRules =
            [
                new WindowMaterialRuleSettingsModel
                {
                    LaborCatalogId = 0,
                    LaborName = "  Finitura interna  ",
                    MaterialCatalogId = -4,
                    MaterialName = "  Perline  ",
                    CalculationMode = "unknown",
                    QuantityParameter = 0
                },
                new WindowMaterialRuleSettingsModel
                {
                    IsWindowAutomation = false,
                    LaborCatalogId = 12,
                    LaborName = "Creazione supporto",
                    MaterialCatalogId = 34,
                    MaterialName = "Listoni",
                    CalculationMode = "fixedperwindow",
                    QuantityParameter = 6.5m
                }
            ]
        };

        settings.Normalize();

        Assert.Null(settings.WindowMaterialRules[0].LaborCatalogId);
        Assert.Null(settings.WindowMaterialRules[0].MaterialCatalogId);
        Assert.Equal("Finitura interna", settings.WindowMaterialRules[0].LaborName);
        Assert.Equal("Perline", settings.WindowMaterialRules[0].MaterialName);
        Assert.Equal(WindowMaterialRuleSettingsModel.PerimeterCalculationMode, settings.WindowMaterialRules[0].CalculationMode);
        Assert.Equal(1m, settings.WindowMaterialRules[0].QuantityParameter);
        Assert.Equal(12, settings.WindowMaterialRules[1].LaborCatalogId);
        Assert.Equal(34, settings.WindowMaterialRules[1].MaterialCatalogId);
        Assert.False(settings.WindowMaterialRules[1].IsWindowAutomation);
        Assert.Equal(WindowMaterialRuleSettingsModel.FixedPerWindowCalculationMode, settings.WindowMaterialRules[1].CalculationMode);
        Assert.Equal(6.5m, settings.WindowMaterialRules[1].QuantityParameter);
    }

    [Fact]
    public void ConfigurationBinderLoadsMultipleVersionedRulesWithoutLegacyDefault()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RealProfit:WindowMaterialRulesSchemaVersion"] = "2",
                ["RealProfit:WindowMaterialRules:0:Enabled"] = "true",
                ["RealProfit:WindowMaterialRules:0:IsWindowAutomation"] = "true",
                ["RealProfit:WindowMaterialRules:0:LaborCatalogId"] = "10",
                ["RealProfit:WindowMaterialRules:0:LaborName"] = "Finitura interna",
                ["RealProfit:WindowMaterialRules:0:MaterialCatalogId"] = "20",
                ["RealProfit:WindowMaterialRules:0:MaterialName"] = "Perline",
                ["RealProfit:WindowMaterialRules:0:CalculationMode"] = "Perimeter",
                ["RealProfit:WindowMaterialRules:0:QuantityParameter"] = "1",
                ["RealProfit:WindowMaterialRules:1:Enabled"] = "false",
                ["RealProfit:WindowMaterialRules:1:IsWindowAutomation"] = "false",
                ["RealProfit:WindowMaterialRules:1:LaborCatalogId"] = "11",
                ["RealProfit:WindowMaterialRules:1:LaborName"] = "Creazione supporto",
                ["RealProfit:WindowMaterialRules:1:MaterialCatalogId"] = "21",
                ["RealProfit:WindowMaterialRules:1:MaterialName"] = "Listoni",
                ["RealProfit:WindowMaterialRules:1:CalculationMode"] = "FixedPerWindow",
                ["RealProfit:WindowMaterialRules:1:QuantityParameter"] = "4"
            })
            .Build();

        RealProfitSettingsModel settings =
            configuration.GetSection("RealProfit").Get<RealProfitSettingsModel>()!;
        settings.Normalize();

        Assert.Equal(2, settings.WindowMaterialRules.Count);
        Assert.Equal("Finitura interna", settings.WindowMaterialRules[0].LaborName);
        Assert.Equal("Creazione supporto", settings.WindowMaterialRules[1].LaborName);
        Assert.Equal(4m, settings.WindowMaterialRules[1].QuantityParameter);
        Assert.True(settings.WindowMaterialRules[0].IsWindowAutomation);
        Assert.False(settings.WindowMaterialRules[1].IsWindowAutomation);
        Assert.False(settings.WindowMaterialRules[1].Enabled);
    }

    [Fact]
    public void CatalogIdentityIsStableAndDoesNotIncludeCredentials()
    {
        var first = new DatabaseSettingsModel
        {
            Provider = "  Postgres  ",
            Server = "  Db.Example.Com ",
            Port = 5432,
            Database = " EdilPaint ",
            Username = "first-user",
            Password = "first-password"
        };
        var second = new DatabaseSettingsModel
        {
            Provider = "PostgreSQL",
            Server = "db.example.com",
            Port = 5432,
            Database = "EdilPaint",
            Username = "different-user",
            Password = "different-password"
        };

        string identity = first.GetCatalogIdentity();

        Assert.StartsWith("sha256:", identity, StringComparison.Ordinal);
        Assert.Equal(identity, second.GetCatalogIdentity());
        Assert.DoesNotContain("first-user", identity, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("first-password", identity, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PostgreSqlCatalogIdentityPreservesDatabaseCaseAndNormalizesDefaultPort()
    {
        var baseline = new DatabaseSettingsModel
        {
            Provider = DatabaseSettingsModel.PostgreSqlProvider,
            Server = "db.example.com",
            Database = "Prod"
        };

        Assert.Equal(baseline.GetCatalogIdentity(), new DatabaseSettingsModel
        {
            Provider = DatabaseSettingsModel.PostgreSqlProvider,
            Server = "DB.EXAMPLE.COM",
            Port = 5432,
            Database = "Prod"
        }.GetCatalogIdentity());
        Assert.NotEqual(baseline.GetCatalogIdentity(), new DatabaseSettingsModel
        {
            Provider = DatabaseSettingsModel.PostgreSqlProvider,
            Port = 5432,
            Server = "db.example.com",
            Database = "prod"
        }.GetCatalogIdentity());
    }

    [Fact]
    public void CatalogIdentityChangesWithCatalogCoordinatesAndIsEmptyWithoutThem()
    {
        var baseline = new DatabaseSettingsModel
        {
            Provider = DatabaseSettingsModel.SqlServerProvider,
            Server = "server",
            Port = 1433,
            Database = "quotes"
        };

        Assert.Equal(string.Empty, new DatabaseSettingsModel().GetCatalogIdentity());
        Assert.Equal(string.Empty, new DatabaseSettingsModel
        {
            Provider = DatabaseSettingsModel.PostgreSqlProvider
        }.GetCatalogIdentity());
        Assert.NotEqual(baseline.GetCatalogIdentity(), new DatabaseSettingsModel
        {
            Provider = DatabaseSettingsModel.PostgreSqlProvider,
            Server = "server",
            Port = 1433,
            Database = "quotes"
        }.GetCatalogIdentity());
        Assert.NotEqual(baseline.GetCatalogIdentity(), new DatabaseSettingsModel
        {
            Provider = DatabaseSettingsModel.SqlServerProvider,
            Server = "other-server",
            Port = 1433,
            Database = "quotes"
        }.GetCatalogIdentity());
        Assert.NotEqual(baseline.GetCatalogIdentity(), new DatabaseSettingsModel
        {
            Provider = DatabaseSettingsModel.SqlServerProvider,
            Server = "server",
            Port = 1444,
            Database = "quotes"
        }.GetCatalogIdentity());
        Assert.NotEqual(baseline.GetCatalogIdentity(), new DatabaseSettingsModel
        {
            Provider = DatabaseSettingsModel.SqlServerProvider,
            Server = "server",
            Port = 1433,
            Database = "other-quotes"
        }.GetCatalogIdentity());
    }

    [Fact]
    public void PositiveCatalogIdNeverFallsBackToMatchingSnapshotName()
    {
        var labor = new Item { PersistentId = 10, Name = "Finitura interna" };
        var material = new Item
        {
            PersistentId = 20,
            Name = "Perline",
            IsCompanyMaterial = true
        };
        var rule = new WindowMaterialRuleSettingsModel
        {
            Enabled = false,
            IsWindowAutomation = false,
            LaborCatalogId = 999,
            LaborName = labor.Name,
            MaterialCatalogId = 998,
            MaterialName = material.Name
        };

        WindowMaterialRuleEditor editor = WindowMaterialRuleEditor.FromSettings(
            rule,
            [labor],
            [material]);

        Assert.Null(editor.SelectedLabor);
        Assert.Null(editor.SelectedMaterial);
        WindowMaterialRuleSettingsModel saved = editor.CreateSettingsRule(1m);
        Assert.False(saved.IsWindowAutomation);
        Assert.Equal(999, saved.LaborCatalogId);
        Assert.Equal("Finitura interna", saved.LaborName);
        Assert.Equal(998, saved.MaterialCatalogId);
        Assert.Equal("Perline", saved.MaterialName);
    }

    [Fact]
    public void IncompatibleCatalogRequiresExplicitReselection()
    {
        var labor = new Item { PersistentId = 10, Name = "Finitura interna" };
        var material = new Item
        {
            PersistentId = 20,
            Name = "Perline",
            IsCompanyMaterial = true
        };
        var rule = new WindowMaterialRuleSettingsModel
        {
            LaborCatalogId = 999,
            LaborName = labor.Name,
            MaterialCatalogId = 998,
            MaterialName = material.Name
        };

        WindowMaterialRuleEditor editor = WindowMaterialRuleEditor.FromSettings(
            rule,
            [labor],
            [material],
            useCatalogIds: false);

        Assert.Null(editor.SelectedLabor);
        Assert.Null(editor.SelectedMaterial);
        WindowMaterialRuleSettingsModel unresolved = editor.CreateSettingsRule(
            1m,
            discardUnresolvedCatalogIds: true);
        Assert.Null(unresolved.LaborCatalogId);
        Assert.Null(unresolved.MaterialCatalogId);
        Assert.Equal("Finitura interna", unresolved.LaborName);
        Assert.Equal("Perline", unresolved.MaterialName);

        editor.SelectedLabor = labor;
        editor.SelectedMaterial = material;
        WindowMaterialRuleSettingsModel saved = editor.CreateSettingsRule(
            1m,
            discardUnresolvedCatalogIds: true);
        Assert.Equal(10, saved.LaborCatalogId);
        Assert.Equal(20, saved.MaterialCatalogId);
    }
}
