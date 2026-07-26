using System.Globalization;

namespace EdilPaintPreventibiviGen.Android.Models;

public sealed class QuoteLine
{
    private static readonly CultureInfo ItalianCulture = CultureInfo.GetCultureInfo("it-IT");

    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public double UnitPrice { get; init; }
    public int Quantity { get; init; }
    public double Discount { get; init; }
    public double Total { get; init; }

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
}
