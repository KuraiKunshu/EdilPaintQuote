namespace EdilPaintPreventibiviGen.Models;

public sealed record WindowMaterialProductLine(
    string Name,
    string Description,
    int Quantity);

public sealed record WindowMaterialLaborLine(
    string Name,
    string Description,
    int Quantity = 1);

public sealed class WindowMaterialCalculationOptions
{
    public IReadOnlyCollection<string> WindowPrefixes { get; init; } = Array.Empty<string>();
    public string RequiredLaborKeyword { get; init; } = string.Empty;
}

public readonly record struct WindowSize(int WidthCentimeters, int HeightCentimeters)
{
    public override string ToString() => $"{WidthCentimeters}x{HeightCentimeters}";
}

public sealed record WindowMaterialMeasureDetail(
    WindowSize Size,
    int WindowQuantity,
    int LinearMetersPerWindow)
{
    public int TotalLinearMeters => WindowQuantity * LinearMetersPerWindow;
}

public sealed record UnrecognizedWindowProduct(
    string Name,
    int Quantity);

public sealed class WindowMaterialCalculationResult
{
    public required bool RequiredLaborFound { get; init; }
    public required IReadOnlyList<WindowMaterialMeasureDetail> Details { get; init; }
    public required IReadOnlyList<UnrecognizedWindowProduct> UnrecognizedProducts { get; init; }

    public int TotalWindowQuantity => Details.Sum(detail => detail.WindowQuantity);
    public int TotalLinearMeters => Details.Sum(detail => detail.TotalLinearMeters);
}
