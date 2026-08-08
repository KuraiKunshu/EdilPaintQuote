using System.Globalization;
using System.Text.RegularExpressions;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public static partial class WindowMaterialCalculator
{
    private static readonly IReadOnlyDictionary<string, int> VeluxWidths =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["BK"] = 47,
            ["CK"] = 55,
            ["FK"] = 66,
            ["MK"] = 78,
            ["PK"] = 94,
            ["SK"] = 114,
            ["UK"] = 134
        };

    private static readonly IReadOnlyDictionary<string, int> VeluxHeights =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["25"] = 55,
            ["01"] = 70,
            ["02"] = 78,
            ["04"] = 98,
            ["06"] = 118,
            ["08"] = 140,
            ["10"] = 160,
            ["12"] = 180
        };

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

        var detectedSizes = new List<WindowSize>(3);
        if (TryParseExplicitSize(name, out WindowSize explicitSize))
            detectedSizes.Add(explicitSize);
        if (TryParseRotoSize(name, out WindowSize rotoSize))
            detectedSizes.Add(rotoSize);
        if (TryParseVeluxSize(name, out WindowSize veluxSize))
            detectedSizes.Add(veluxSize);

        if (detectedSizes.Count == 0 || detectedSizes.Any(candidate => candidate != detectedSizes[0]))
            return false;

        size = detectedSizes[0];
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

    private static bool TryParseExplicitSize(string productName, out WindowSize size) =>
        TryCreateSize(ExplicitSizeRegex().Match(productName), out size);

    private static bool TryParseRotoSize(string productName, out WindowSize size) =>
        TryCreateSize(RotoSizeRegex().Match(productName), out size);

    private static bool TryParseVeluxSize(string productName, out WindowSize size)
    {
        size = default;
        Match match = VeluxSizeRegex().Match(productName);
        if (!match.Success ||
            !VeluxWidths.TryGetValue(match.Groups["width"].Value, out int width) ||
            !VeluxHeights.TryGetValue(match.Groups["height"].Value, out int height))
        {
            return false;
        }

        size = new WindowSize(width, height);
        return true;
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

    [GeneratedRegex(@"\(\s*(?<width>\d{2,3})\s*(?:[xX]|\u00D7)\s*(?<height>\d{2,3})\s*\)", RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitSizeRegex();

    [GeneratedRegex(@"(?<!\d)(?<width>\d{3})\s*/\s*(?<height>\d{3})(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex RotoSizeRegex();

    [GeneratedRegex(@"(?<![A-Z0-9])(?<width>BK|CK|FK|MK|PK|SK|UK)(?<height>25|01|02|04|06|08|10|12)(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VeluxSizeRegex();
}
