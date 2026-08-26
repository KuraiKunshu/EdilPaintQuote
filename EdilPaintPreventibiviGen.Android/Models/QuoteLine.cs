using System.Globalization;

namespace EdilPaintPreventibiviGen.Android.Models;

public sealed class QuoteLine
{
    private static readonly CultureInfo ItalianCulture = CultureInfo.GetCultureInfo("it-IT");

    public int CatalogItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double UnitPrice { get; set; }
    public int Quantity { get; set; } = 1;
    public double Discount { get; set; }
    public bool IsSignificant { get; set; }
    public int SortOrder { get; set; }

    public double Total => UnitPrice * Quantity * (1 - Math.Clamp(Discount, 0, 100) / 100);

    public string TotalDisplay => Total.ToString("C", ItalianCulture);
    public string QuantityDisplay => Quantity.ToString("N0", ItalianCulture);
    public string UnitPriceDisplay => UnitPrice.ToString("C", ItalianCulture);
    public string DiscountDisplay => Discount > 0 ? $"{Discount:0.#}%" : "Nessuno";
    public bool HasDiscount => Discount > 0;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public string DescriptionDisplay => string.IsNullOrWhiteSpace(Description) ? string.Empty : Description.Trim();
    public string DetailDisplay
    {
        get
        {
            string price = UnitPrice.ToString("C", ItalianCulture);
            string discount = Discount > 0 ? $" - sc. {Discount:0.#}%" : string.Empty;
            return $"{Quantity} x {price}{discount}";
        }
    }

    public QuoteLine Clone() => new()
    {
        CatalogItemId = CatalogItemId,
        Name = Name,
        Description = Description,
        UnitPrice = UnitPrice,
        Quantity = Quantity,
        Discount = Discount,
        IsSignificant = IsSignificant,
        SortOrder = SortOrder
    };
}
