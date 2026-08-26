using System.Globalization;

namespace EdilPaintPreventibiviGen.Android.Models;

public enum QuoteLineKind
{
    Material,
    Labor
}

public sealed class CatalogItem
{
    private static readonly CultureInfo ItalianCulture = CultureInfo.GetCultureInfo("it-IT");

    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public double UnitPrice { get; init; }
    public bool IsSignificant { get; init; }

    public string PriceDisplay => UnitPrice.ToString("C", ItalianCulture);
    public string DescriptionDisplay => string.IsNullOrWhiteSpace(Description) ? "Nessuna descrizione" : Description.Trim();
}
