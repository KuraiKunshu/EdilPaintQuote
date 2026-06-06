using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EdilPaintPreventibiviGen.Helpers;

public class StringVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter?.ToString() == "FileName" && value is string path)
        {
            return System.IO.Path.GetFileName(path);
        }

        if (value is string str)
            return string.IsNullOrWhiteSpace(str) ? Visibility.Collapsed : Visibility.Visible;
            
        return value == null ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
