using System;
using System.Windows;
using System.Windows.Media;
using PdfColor = QuestPDF.Infrastructure.Color;

namespace EdilPaintPreventibiviGen.Helpers;

public static class ThemeResources
{
    public static Brush GetBrush(string resourceKey)
    {
        if (Application.Current?.TryFindResource(resourceKey) is Brush brush)
            return brush;

        throw new InvalidOperationException($"Risorsa colore '{resourceKey}' non trovata in Helpers/Colors.xaml.");
    }

    public static PdfColor GetPdfColor(string resourceKey)
    {
        if (Application.Current?.TryFindResource(resourceKey) is Color color)
            return PdfColor.FromHex(ToHex(color));

        if (Application.Current?.TryFindResource(resourceKey) is SolidColorBrush brush)
            return PdfColor.FromHex(ToHex(brush.Color));

        throw new InvalidOperationException($"Risorsa colore PDF '{resourceKey}' non trovata in Helpers/Colors.xaml.");
    }

    private static string ToHex(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
