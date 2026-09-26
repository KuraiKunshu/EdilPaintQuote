using QuantityValue = EdilPaintPreventibiviGen.Models.QuantityValue;
using EdilPaintPreventibiviGen.Android.Models;

namespace EdilPaintPreventibiviGen.Android.Services;

public static class MobileMailService
{
    public static string BuildMaterialsList(IEnumerable<QuoteLine> materials)
    {
        var lines = materials
            .Where(material => !string.IsNullOrWhiteSpace(material.Name) && material.Quantity > 0)
            .OrderBy(material => material.SortOrder)
            .Select(material => $"{QuantityValue.OrderDisplay(material.Quantity, material.UnitOfMeasure)} {material.Name.Trim()}")
            .ToArray();
        return lines.Length == 0 ? "- Nessun materiale presente nel preventivo." : string.Join(Environment.NewLine, lines);
    }
}
