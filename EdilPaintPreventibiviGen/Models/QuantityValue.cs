using System.Globalization;

namespace EdilPaintPreventibiviGen.Models;

public static class QuantityValue
{
    // 28 significant digits fit both System.Decimal and the SQL providers exactly.
    public const int Precision = 28;
    public const int Scale = 9;
    public const decimal Maximum = 9999999999999999999.999999999m;
    public const string NumberFormat = "0.#########";
    public static IReadOnlyList<string> Units { get; } = ["pz", "m", "m²", "m³", "kg", "l", "h", "giorni", "a corpo"];
    public static bool IsValid(decimal value) => value > 0 && value <= Maximum && decimal.Round(value, Scale) == value;
    public static bool TryParse(string? text, out decimal value)
    {
        string normalized = (text ?? "").Trim().Replace(',', '.');
        value = 0;
        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal parsed)
            || !IsValid(parsed))
            return false;

        // TryParse can silently round excess significant digits. Accept only exact input.
        normalized = normalized.TrimStart('0');
        if (normalized.Contains('.'))
            normalized = normalized.TrimEnd('0').TrimEnd('.');
        if (normalized.StartsWith('.'))
            normalized = "0" + normalized;
        if (normalized != parsed.ToString(NumberFormat, CultureInfo.InvariantCulture))
            return false;
        value = parsed;
        return true;
    }
    public static string NormalizeUnit(string? unit) => string.IsNullOrWhiteSpace(unit) ? "pz" : unit.Trim();
    public static string Format(decimal quantity) => quantity.ToString(NumberFormat, CultureInfo.GetCultureInfo("it-IT"));
    public static string Display(decimal quantity, string? unit) => $"{Format(quantity)} {NormalizeUnit(unit)}";
    public static string OrderDisplay(decimal quantity, string? unit) => NormalizeUnit(unit) == "pz"
        ? $"N.{Format(quantity)}" : Display(quantity, unit);
    public static int WindowCount(decimal quantity, string? unit) =>
        NormalizeUnit(unit) == "pz" && quantity > 0 && quantity <= int.MaxValue && decimal.Truncate(quantity) == quantity
            ? (int)quantity : 0;
}
