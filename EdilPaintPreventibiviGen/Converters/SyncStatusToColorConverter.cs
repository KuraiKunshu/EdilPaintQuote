using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Converters;

public class SyncStatusToColorConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		if (value is SyncStatus status)
		{
			return status switch
			{
				SyncStatus.LocalOnly => new SolidColorBrush(Colors.Red),
				SyncStatus.OnlineOnly => new SolidColorBrush(Colors.Orange),
				SyncStatus.Synced => new SolidColorBrush(Colors.LimeGreen),
				_ => new SolidColorBrush(Colors.Gray)
			};
		}

		return new SolidColorBrush(Colors.Gray);
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		throw new NotImplementedException();
	}
}