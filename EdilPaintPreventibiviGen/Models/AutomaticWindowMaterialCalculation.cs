namespace EdilPaintPreventibiviGen.Models;

public static class AutomaticWindowMaterialModes
{
    public const string Perimeter = "Perimeter";
    public const string FixedPerWindow = "FixedPerWindow";
}

public sealed record AutomaticWindowProductLine(
    string Name,
    int Quantity);

public sealed record AutomaticWindowLaborLine(
    int CatalogItemId,
    string Name,
    int Quantity = 1);

public sealed record AutomaticQuoteMaterialLine(
    int CatalogItemId,
    string Name,
    int Quantity);

public sealed record AutomaticMaterialCatalogItem(
    int CatalogItemId,
    string Name);

public sealed class AutomaticWindowMaterialRule
{
    public string RuleId { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool IsWindowAutomation { get; set; } = true;
    public int LaborCatalogItemId { get; set; }
    public string LaborNameSnapshot { get; set; } = string.Empty;
    public int MaterialCatalogItemId { get; set; }
    public string MaterialNameSnapshot { get; set; } = string.Empty;
    public string Mode { get; set; } = AutomaticWindowMaterialModes.Perimeter;
    public decimal Parameter { get; set; } = 1m;
}

public sealed class AutomaticWindowMaterialCalculationInput
{
    public IReadOnlyCollection<AutomaticWindowProductLine> WindowProducts { get; init; } =
        Array.Empty<AutomaticWindowProductLine>();

    public IReadOnlyCollection<AutomaticWindowLaborLine> Labors { get; init; } =
        Array.Empty<AutomaticWindowLaborLine>();

    public IReadOnlyCollection<AutomaticQuoteMaterialLine> ExistingQuoteMaterials { get; init; } =
        Array.Empty<AutomaticQuoteMaterialLine>();

    public IReadOnlyCollection<AutomaticWindowMaterialRule> Rules { get; init; } =
        Array.Empty<AutomaticWindowMaterialRule>();

    public IReadOnlyCollection<string> WindowPrefixes { get; init; } =
        Array.Empty<string>();

    public IReadOnlyCollection<AutomaticMaterialCatalogItem> MaterialCatalog { get; init; } =
        Array.Empty<AutomaticMaterialCatalogItem>();
}

/// <summary>
/// Identifies a catalog material without using a non-transitive "ID or name" comparison.
/// Materials with an ID compare only by ID; legacy materials compare by normalized name.
/// </summary>
public readonly struct AutomaticMaterialKey : IEquatable<AutomaticMaterialKey>
{
    private AutomaticMaterialKey(int catalogItemId, string normalizedName)
    {
        CatalogItemId = catalogItemId > 0 ? catalogItemId : 0;
        NormalizedName = CatalogItemId > 0 ? string.Empty : normalizedName;
    }

    public int CatalogItemId { get; }
    public string NormalizedName { get; }
    public bool HasCatalogItemId => CatalogItemId > 0;

    public static AutomaticMaterialKey FromCatalogItemId(int catalogItemId)
    {
        if (catalogItemId <= 0)
            throw new ArgumentOutOfRangeException(nameof(catalogItemId));

        return new AutomaticMaterialKey(catalogItemId, string.Empty);
    }

    public static AutomaticMaterialKey FromLegacyName(string normalizedName)
    {
        if (string.IsNullOrWhiteSpace(normalizedName))
            throw new ArgumentException("Il nome normalizzato non può essere vuoto.", nameof(normalizedName));

        return new AutomaticMaterialKey(0, normalizedName.Trim().ToUpperInvariant());
    }

    public bool Equals(AutomaticMaterialKey other) =>
        HasCatalogItemId == other.HasCatalogItemId &&
        (HasCatalogItemId
            ? CatalogItemId == other.CatalogItemId
            : string.Equals(NormalizedName, other.NormalizedName, StringComparison.Ordinal));

    public override bool Equals(object? obj) => obj is AutomaticMaterialKey other && Equals(other);

    public override int GetHashCode() => HasCatalogItemId
        ? HashCode.Combine(1, CatalogItemId)
        : HashCode.Combine(0, NormalizedName);

    public override string ToString() => HasCatalogItemId
        ? $"id:{CatalogItemId}"
        : $"name:{NormalizedName}";

    public static bool operator ==(AutomaticMaterialKey left, AutomaticMaterialKey right) => left.Equals(right);
    public static bool operator !=(AutomaticMaterialKey left, AutomaticMaterialKey right) => !left.Equals(right);
}

public enum AutomaticMaterialResolutionStatus
{
    ResolvedById,
    ResolvedByUniqueName,
    MissingFromCatalog,
    AmbiguousName
}

public enum AutomaticWindowMaterialIssueCode
{
    InvalidRule,
    UnsupportedMode,
    DuplicateRule,
    UnrecognizedWindowProduct,
    NoRecognizedWindows,
    MissingCatalogMaterial,
    AmbiguousCatalogMaterial,
    AmbiguousExistingMaterial,
    InsufficientRecognizedWindows,
    QuantityOverflow
}

public sealed record AutomaticWindowMaterialIssue(
    AutomaticWindowMaterialIssueCode Code,
    string Message,
    string RuleId = "",
    string ItemName = "");

public sealed record AutomaticRecognizedWindowGroup(
    WindowSize Size,
    int WindowQuantity);

public sealed record AutomaticWindowRuleSizeCalculation(
    WindowSize Size,
    int WindowQuantity,
    decimal RawQuantityPerWindow,
    long RoundedQuantityPerWindow,
    long RequiredQuantity);

public sealed record AutomaticWindowMaterialRuleCalculation(
    string RuleId,
    int LaborCatalogItemId,
    string LaborName,
    bool IsWindowAutomation,
    long LaborQuantity,
    AutomaticMaterialKey MaterialKey,
    int MaterialCatalogItemId,
    string MaterialName,
    AutomaticMaterialResolutionStatus MaterialResolution,
    string Mode,
    decimal Parameter,
    long GrossRequiredQuantity,
    IReadOnlyList<AutomaticWindowRuleSizeCalculation> Details);

public sealed record AutomaticWindowMaterialPlanLine(
    AutomaticMaterialKey MaterialKey,
    int MaterialCatalogItemId,
    string MaterialName,
    AutomaticMaterialResolutionStatus MaterialResolution,
    long GrossRequiredQuantity,
    long AlreadyQuotedQuantity,
    long QuantityToAdd,
    IReadOnlyList<string> ContributingRuleIds,
    IReadOnlyList<AutomaticWindowRuleSizeCalculation> Details);

public sealed class AutomaticWindowMaterialCalculationResult
{
    public IReadOnlyList<AutomaticRecognizedWindowGroup> RecognizedWindows { get; init; } =
        Array.Empty<AutomaticRecognizedWindowGroup>();

    public IReadOnlyList<AutomaticWindowMaterialRuleCalculation> RuleCalculations { get; init; } =
        Array.Empty<AutomaticWindowMaterialRuleCalculation>();

    public IReadOnlyList<AutomaticWindowMaterialPlanLine> Materials { get; init; } =
        Array.Empty<AutomaticWindowMaterialPlanLine>();

    public IReadOnlyList<AutomaticWindowMaterialIssue> Issues { get; init; } =
        Array.Empty<AutomaticWindowMaterialIssue>();

    public long TotalRecognizedWindowQuantity =>
        RecognizedWindows.Sum(window => (long)window.WindowQuantity);
}
