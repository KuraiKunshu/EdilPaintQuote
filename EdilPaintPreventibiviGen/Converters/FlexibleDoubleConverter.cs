using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EdilPaintPreventibiviGen.Converters;

/// <summary>
/// Visualizza gli importi nel formato italiano e accetta sia la virgola sia il
/// punto come separatore decimale durante la modifica.
/// </summary>
public sealed class FlexibleDoubleConverter : IValueConverter
{
    private static readonly CultureInfo ItalianCulture = CultureInfo.GetCultureInfo("it-IT");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is double number && double.IsFinite(number)
            ? number.ToString("N2", ItalianCulture)
            : string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string text || !TryParse(text, out double number))
            return DependencyProperty.UnsetValue;

        return number;
    }

    public static bool TryParse(string? text, out double value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string normalized = text.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        int lastComma = normalized.LastIndexOf(',');
        int lastDot = normalized.LastIndexOf('.');

        if (lastComma >= 0 && lastDot >= 0)
        {
            char decimalSeparator = lastComma > lastDot ? ',' : '.';
            char thousandsSeparator = decimalSeparator == ',' ? '.' : ',';
            normalized = normalized.Replace(thousandsSeparator.ToString(), string.Empty, StringComparison.Ordinal);
            if (decimalSeparator == ',')
                normalized = normalized.Replace(',', '.');
        }
        else
        {
            normalized = normalized.Replace(',', '.');
        }

        return double.TryParse(
                   normalized,
                   NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                   CultureInfo.InvariantCulture,
                   out value) &&
               double.IsFinite(value);
    }
}
