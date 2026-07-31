using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public static class RealProfitCalculator
{
    public static RealProfitResult Calculate(RealProfitInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        double supplierDiscount = Math.Clamp(input.SupplierDiscount, 0, 100);
        double customerMaterialRevenue = input.ExcludeMaterials
            ? 0
            : input.Materials.Sum(material => material.CustomerTotal);
        double supplierMaterialCost = input.ExcludeMaterials
            ? 0
            : input.Materials.Sum(material =>
                Math.Max(0, material.CustomerUnitPrice) *
                Math.Max(0, material.Quantity) *
                (1 - supplierDiscount / 100));
        double laborCost =
            Math.Max(0, input.Workers) *
            Math.Max(0, input.Days) *
            Math.Max(0, input.HoursPerDay) *
            Math.Max(0, input.HourlyCost);
        double companyMaterialCost = input.CompanyMaterials.Sum(cost => cost.Total);
        double totalCosts = supplierMaterialCost + laborCost + companyMaterialCost;
        double profit = input.QuoteRevenue - totalCosts;

        return new RealProfitResult
        {
            CustomerMaterialRevenue = customerMaterialRevenue,
            SupplierMaterialCost = supplierMaterialCost,
            MaterialMargin = customerMaterialRevenue - supplierMaterialCost,
            LaborCost = laborCost,
            CompanyMaterialCost = companyMaterialCost,
            TotalCosts = totalCosts,
            Profit = profit,
            ProfitPercentage = input.QuoteRevenue == 0 ? 0 : profit / input.QuoteRevenue * 100
        };
    }
}
