using System.Text.Json;

namespace EdilPaintPreventibiviGen.Services;

public sealed class AutomaticMaterialSettings
{
    public long Revision { get; set; }
    public string CatalogIdentity { get; set; } = string.Empty;
    public List<string> WindowProductPrefixes { get; set; } = [];
    public List<WindowMaterialRuleSettingsModel> WindowMaterialRules { get; set; } = [];
    public List<string> ResolutionWarnings { get; set; } = [];

    public static AutomaticMaterialSettings FromLocal(RealProfitSettingsModel local) => new()
    {
        CatalogIdentity = local.WindowMaterialCatalogIdentity,
        WindowProductPrefixes = [.. local.WindowProductPrefixes],
        WindowMaterialRules = CloneRules(local.WindowMaterialRules)
    };

    public AutomaticMaterialSettings CreateValidatedCopy()
    {
        if (Revision < 0 || WindowProductPrefixes == null || WindowMaterialRules == null ||
            WindowMaterialRules.Any(rule => rule == null || rule.QuantityParameter <= 0))
            throw new InvalidOperationException("Le regole dei materiali automatici non sono valide.");
        var prefixes = WindowProductPrefixes.Select(x => x?.Trim().ToUpperInvariant() ?? "")
            .Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (prefixes.Count == 0) prefixes = RealProfitSettingsModel.CreateDefaultWindowProductPrefixes();
        return new()
        {
            Revision = Revision, CatalogIdentity = CatalogIdentity?.Trim() ?? string.Empty,
            ResolutionWarnings = [.. ResolutionWarnings ?? []],
            WindowProductPrefixes = prefixes,
            WindowMaterialRules = CloneRules(WindowMaterialRules)
        };
    }

    public void ApplyTo(RealProfitSettingsModel local)
    {
        var copy = CreateValidatedCopy();
        local.WindowProductPrefixes = copy.WindowProductPrefixes;
        local.WindowMaterialRules = copy.WindowMaterialRules;
        local.WindowMaterialCatalogIdentity = copy.CatalogIdentity;
        local.WindowMaterialRulesSchemaVersion = RealProfitSettingsModel.CurrentWindowMaterialRulesSchemaVersion;
    }

    public static bool SameContent(AutomaticMaterialSettings left, AutomaticMaterialSettings right) =>
        JsonSerializer.Serialize(new { left.WindowProductPrefixes, left.WindowMaterialRules }) ==
        JsonSerializer.Serialize(new { right.WindowProductPrefixes, right.WindowMaterialRules });

    private static List<WindowMaterialRuleSettingsModel> CloneRules(IEnumerable<WindowMaterialRuleSettingsModel> rules) =>
        rules.Select(rule => new WindowMaterialRuleSettingsModel
        {
            Enabled = rule.Enabled, IsWindowAutomation = rule.IsWindowAutomation,
            LaborCatalogId = rule.LaborCatalogId, LaborName = rule.LaborName?.Trim() ?? "",
            MaterialCatalogId = rule.MaterialCatalogId, MaterialName = rule.MaterialName?.Trim() ?? "",
            CalculationMode = rule.CalculationMode, QuantityParameter = rule.QuantityParameter
        }).ToList();
}

public sealed record AutomaticMaterialSettingsSnapshot(AutomaticMaterialSettings Settings, bool IsCurrent);

public interface IAutomaticMaterialSettingsRepository
{
    Task<AutomaticMaterialSettings> LoadAutomaticMaterialSettingsAsync(AutomaticMaterialSettings? legacy,
        CancellationToken token = default);
    Task<AutomaticMaterialSettings> SaveAutomaticMaterialSettingsAsync(AutomaticMaterialSettings settings,
        CancellationToken token = default);
}
