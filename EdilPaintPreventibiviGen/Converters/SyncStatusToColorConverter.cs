using System;
using System.Globalization;
using System.Windows.Data;
using EdilPaintPreventibiviGen.Helpers;
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
				SyncStatus.LocalOnly => ThemeResources.GetBrush("SyncLocalOnlyBrush"),
				SyncStatus.OnlineOnly => ThemeResources.GetBrush("SyncOnlineOnlyBrush"),
				SyncStatus.Synced => ThemeResources.GetBrush("SyncSyncedBrush"),
				_ => ThemeResources.GetBrush("SyncUnknownBrush")
			};
		}

		return ThemeResources.GetBrush("SyncUnknownBrush");
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		return Binding.DoNothing;
	}
}
