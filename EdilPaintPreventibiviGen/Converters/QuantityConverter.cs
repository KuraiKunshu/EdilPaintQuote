using System.Globalization;
using System.Windows;
using System.Windows.Data;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Converters;

public sealed class QuantityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is decimal quantity ? QuantityValue.Format(quantity) : string.Empty;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        QuantityValue.TryParse(value as string, out decimal quantity) ? quantity : DependencyProperty.UnsetValue;
}
