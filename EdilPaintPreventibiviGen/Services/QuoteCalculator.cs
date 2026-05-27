using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public sealed class QuoteCalculator
{
    public QuoteTotals Calculate(
    IEnumerable<Item> materials,
    IEnumerable<Item> labors,
    double materialDiscount,
    double laborDiscount,
    string ivaType)
{
    double materialDiscountFactor = 1 - materialDiscount / 100.0;
    double laborDiscountFactor = 1 - laborDiscount / 100.0;

    double netMat = materials.Sum(m => m.TotalPrice) * materialDiscountFactor;
    double netLab = labors.Sum(l => l.TotalPrice) * laborDiscountFactor;

    double imponibile10 = 0.0;
    double imponibile22 = 0.0;
    double iva10 = 0.0;
    double iva22 = 0.0;

    switch (ivaType)
    {
        case "RC 10%+22%":
        {
            double pSig = materials
                .Where(m => m.IsSignificant)
                .Sum(m => m.TotalPrice) * materialDiscountFactor;

            double pNonSig = materials
                .Where(m => !m.IsSignificant)
                .Sum(m => m.TotalPrice) * materialDiscountFactor;

            double netLabSig = labors
                .Where(l => l.IsSignificant)
                .Sum(l => l.TotalPrice) * laborDiscountFactor;

            double quotaSigAl10 = Math.Min(pSig, netLabSig);
            double quotaSigAl22 = Math.Max(0, pSig - netLabSig);

            // Al 10%:
            // - tutta la manodopera
            // - tutti i materiali non significativi
            // - la quota dei materiali significativi coperta dai lavori significativi
            imponibile10 = netLab + pNonSig + quotaSigAl10;

            // Al 22%:
            // - solo l'eccedenza dei materiali significativi
            imponibile22 = quotaSigAl22;

            iva10 = imponibile10 * 0.10;
            iva22 = imponibile22 * 0.22;
            break;
        }

        case "10%":
            imponibile10 = netMat + netLab;
            iva10 = imponibile10 * 0.10;
            break;

        case "22%":
            imponibile22 = netMat;
            imponibile10 = netLab;
            iva22 = imponibile22 * 0.22;
            iva10 = imponibile10 * 0.10;
            break;

        case "esclusa":
        default:
            imponibile10 = netMat + netLab;
            break;
    }

    return new QuoteTotals
    {
        Imponibile10 = imponibile10,
        Imponibile22 = imponibile22,
        Iva10 = iva10,
        Iva22 = iva22,
    };
}
}

public sealed class QuoteTotals
{
    public double Imponibile10 { get; set; }
    public double Imponibile22 { get; set; }
    public double Imponibile => Imponibile10 + Imponibile22;

    public double Iva10 { get; set; }
    public double Iva22 { get; set; }
    public double IvaTotale => Iva10 + Iva22;

    public double TotaleGenerale => Imponibile + IvaTotale;
}