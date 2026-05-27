using System;
using System.Globalization;
using System.Windows.Data;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Converters;

public class SyncStatusToTooltipConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		if (value is SyncStatus status)
		{
			return status switch
			{
				SyncStatus.LocalOnly => "Solo in locale (non sincronizzato)",
				SyncStatus.OnlineOnly => "Solo nel database online",
				SyncStatus.Synced => "Sincronizzato (locale + online)",
				_ => "Stato sconosciuto"
			};
		}

		return "Stato sconosciuto";
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		throw new NotImplementedException();
	}
}