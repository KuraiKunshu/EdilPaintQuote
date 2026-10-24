using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EdilPaintPreventibiviGen.Converters;

public sealed class OrderSelectionConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length == 2 && values[0] is string number && number.Length > 0 && Equals(number, values[1]);
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => targetTypes.Select(_ => DependencyProperty.UnsetValue).ToArray();
}
