using System.Globalization;
using System.Text.RegularExpressions;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public static partial class WindowMaterialCalculator
{
    public static WindowMaterialCalculationResult Calculate(
        IEnumerable<WindowMaterialProductLine> products,
        IEnumerable<WindowMaterialLaborLine> labors,
        WindowMaterialCalculationOptions options)
    {
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(labors);
        ArgumentNullException.ThrowIfNull(options);

        bool requiredLaborFound = ContainsRequiredLabor(labors, options.RequiredLaborKeyword);
        if (!requiredLaborFound)
        {
            return new WindowMaterialCalculationResult
            {
                RequiredLaborFound = false,
                Details = Array.Empty<WindowMaterialMeasureDetail>(),
                UnrecognizedProducts = Array.Empty<UnrecognizedWindowProduct>()
            };
        }

        var quantitiesBySize = new Dictionary<WindowSize, int>();
        var unrecognizedProducts = new List<UnrecognizedWindowProduct>();
        foreach (WindowMaterialProductLine product in products)
        {
            if (product.Quantity <= 0)
            {
                continue;
            }

            string productName = product.Name?.TrimStart() ?? string.Empty;
            if (!StartsWithAllowedPrefix(productName, options.WindowPrefixes))
                continue;

            if (!TryGetWindowSize(productName, options.WindowPrefixes, out WindowSize size))
            {
                unrecognizedProducts.Add(new UnrecognizedWindowProduct(
                    product.Name ?? string.Empty,
                    product.Quantity));
                continue;
            }

            quantitiesBySize[size] = quantitiesBySize.GetValueOrDefault(size) + product.Quantity;
        }

        WindowMaterialMeasureDetail[] details = quantitiesBySize
            .OrderBy(pair => pair.Key.WidthCentimeters)
            .ThenBy(pair => pair.Key.HeightCentimeters)
            .Select(pair => new WindowMaterialMeasureDetail(
                pair.Key,
                pair.Value,
                CalculateLinearMetersPerWindow(pair.Key)))
            .ToArray();

        return new WindowMaterialCalculationResult
        {
            RequiredLaborFound = true,
            Details = details,
            UnrecognizedProducts = unrecognizedProducts
        };
    }

    public static bool ContainsRequiredLabor(
        IEnumerable<WindowMaterialLaborLine> labors,
        string? requiredKeyword)
    {
        ArgumentNullException.ThrowIfNull(labors);

        string keyword = requiredKeyword?.Trim() ?? string.Empty;
        if (keyword.Length == 0)
        {
            return false;
        }

        return labors.Any(labor =>
            labor.Quantity > 0 &&
            string.Equals(labor.Name?.Trim(), keyword, StringComparison.OrdinalIgnoreCase));
    }

    public static bool TryGetWindowSize(
        string? productName,
        IEnumerable<string> windowPrefixes,
        out WindowSize size)
    {
        ArgumentNullException.ThrowIfNull(windowPrefixes);
        size = default;

        string name = productName?.TrimStart() ?? string.Empty;
        if (name.Length == 0 || !StartsWithAllowedPrefix(name, windowPrefixes))
        {
            return false;
        }

        WindowSize? detected = null;
        foreach (Match match in ExplicitSizeRegex().Matches(name))
        {
            if (!TryCreateSize(match, out WindowSize candidate) || (detected.HasValue && candidate != detected.Value))
                return false;
            detected = candidate;
        }
        if (!detected.HasValue)
            return false;
        size = detected.Value;
        return true;
    }

    public static int CalculateLinearMetersPerWindow(WindowSize size)
    {
        if (size.WidthCentimeters <= 0 || size.HeightCentimeters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Le dimensioni devono essere maggiori di zero.");
        }

        double perimeterMeters = 2d * (size.WidthCentimeters + size.HeightCentimeters) / 100d;
        return checked((int)Math.Ceiling(perimeterMeters));
    }

    private static bool StartsWithAllowedPrefix(string productName, IEnumerable<string> prefixes)
    {
        foreach (string? configuredPrefix in prefixes)
        {
            string prefix = configuredPrefix?.Trim() ?? string.Empty;
            if (prefix.Length > 0 && productName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryCreateSize(Match match, out WindowSize size)
    {
        size = default;
        if (!match.Success ||
            !int.TryParse(match.Groups["width"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int width) ||
            !int.TryParse(match.Groups["height"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int height) ||
            width <= 0 ||
            height <= 0)
        {
            return false;
        }

        size = new WindowSize(width, height);
        return true;
    }

    [GeneratedRegex(@"(?<![\d.,])(?<width>\d{2,3})\s*(?:[xX/]|\u00D7)\s*(?<height>\d{2,3})(?![\d.,])", RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitSizeRegex();
}
