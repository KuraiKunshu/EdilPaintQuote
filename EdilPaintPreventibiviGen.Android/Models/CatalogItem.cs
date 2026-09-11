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

    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double UnitPrice { get; set; }
    public bool IsSignificant { get; set; }
    public bool IsCompanyMaterial { get; set; }

    public string PriceDisplay => UnitPrice.ToString("C", ItalianCulture);
    public string DescriptionDisplay => string.IsNullOrWhiteSpace(Description) ? "Nessuna descrizione" : Description.Trim();
    public override string ToString() => Name;

    public CatalogItem Clone() => new()
    {
        Id = Id,
        Name = Name,
        Description = Description,
        UnitPrice = UnitPrice,
        IsSignificant = IsSignificant,
        IsCompanyMaterial = IsCompanyMaterial
    };
}
