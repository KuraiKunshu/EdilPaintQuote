namespace EdilPaintPreventibiviGen.Models;

public sealed record CatalogMaterialOption(Item Item)
{
    public string Label => $"{Item.Name} — {Item.UnitPrice:N2} €/{Item.UnitOfMeasure}";
    public string Value => Item.Name;
}
