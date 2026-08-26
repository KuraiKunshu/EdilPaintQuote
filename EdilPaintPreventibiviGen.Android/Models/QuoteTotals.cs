namespace EdilPaintPreventibiviGen.Android.Models;

public sealed record QuoteTotals(double Imponibile, double Iva, double Total);

public static class QuoteTotalsCalculator
{
    public static QuoteTotals Calculate(
        IEnumerable<QuoteLine> materials,
        IEnumerable<QuoteLine> labors,
        double materialDiscount,
        double laborDiscount,
        string? ivaType)
    {
        double materialFactor = 1 - Math.Clamp(materialDiscount, 0, 100) / 100.0;
        double laborFactor = 1 - Math.Clamp(laborDiscount, 0, 100) / 100.0;
        double netMaterials = materials.Sum(line => line.Total) * materialFactor;
        double netLabors = labors.Sum(line => line.Total) * laborFactor;
        double taxable10 = 0;
        double taxable22 = 0;

        switch (NormalizeIvaType(ivaType))
        {
            case "RC 10%+22%":
                double significantMaterials = materials
                    .Where(line => line.IsSignificant)
                    .Sum(line => line.Total) * materialFactor;
                double regularMaterials = materials
                    .Where(line => !line.IsSignificant)
                    .Sum(line => line.Total) * materialFactor;
                double significantLabors = labors
                    .Where(line => line.IsSignificant)
                    .Sum(line => line.Total) * laborFactor;
                double significantAt10 = Math.Min(significantMaterials, significantLabors);
                taxable10 = netLabors + regularMaterials + significantAt10;
                taxable22 = Math.Max(0, significantMaterials - significantLabors);
                break;
            case "10%":
                taxable10 = netMaterials + netLabors;
                break;
            case "22%":
                taxable22 = netMaterials + netLabors;
                break;
            default:
                taxable10 = netMaterials + netLabors;
                break;
        }

        double taxable = taxable10 + taxable22;
        double vat = NormalizeIvaType(ivaType) == "esclusa"
            ? 0
            : taxable10 * 0.10 + taxable22 * 0.22;
        return new QuoteTotals(taxable, vat, taxable + vat);
    }

    public static string NormalizeIvaType(string? ivaType)
    {
        string normalized = string.Concat((ivaType ?? string.Empty).Where(character => !char.IsWhiteSpace(character)));
        return normalized.ToUpperInvariant() switch
        {
            "RC10%+22%" or "10%+22%" => "RC 10%+22%",
            "10%" => "10%",
            "22%" => "22%",
            _ => "esclusa"
        };
    }
}
