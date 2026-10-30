using System.Globalization;
using System.Windows.Data;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;

namespace EdilPaintPreventibiviGen.Converters;

public sealed class SupplierOrderQueueConverter : IValueConverter
{
    public bool GroupBySupplier { get; set; } = true;
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is QuoteHistorySummary quote
        ? parameter as string == "Status" ? SupplierOrderQueue.StatusDisplay(quote) : SupplierOrderQueue.GroupName(quote, GroupBySupplier)
        : "";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
