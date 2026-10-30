using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public sealed record RealProfitPercentages(double SupplierDiscount, double ProfitReduction,
    bool UseSupplierDiscount, bool UseDefaultReduction);

public static class RealProfitDefaultsService
{
    public static bool UsesSupplierDiscount(RealProfitInput? saved) => saved?.UseSupplierDiscount ?? (saved == null || saved.SupplierDiscount == 0);
    public static bool UsesDefaultReduction(RealProfitInput? saved) => saved?.UseDefaultProfitReduction ?? (saved == null || saved.ProfitReductionPercentage == 0);
    public static double Percentage(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;

    public static RealProfitPercentages Resolve(RealProfitInput? saved, double supplierDiscount, double defaultReduction) => new(
        Percentage(UsesSupplierDiscount(saved) ? supplierDiscount : saved!.SupplierDiscount),
        Percentage(UsesDefaultReduction(saved) ? defaultReduction : saved!.ProfitReductionPercentage),
        UsesSupplierDiscount(saved), UsesDefaultReduction(saved));

    public static Customer? FindSupplier(QuoteHistoryEntry quote, IEnumerable<Customer> customers) =>
        customers.FirstOrDefault(customer => customer.IsSupplier && !string.IsNullOrWhiteSpace(quote.SupplierName) &&
            string.Equals(customer.BusinessName?.Trim(), quote.SupplierName.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool CustomerIsSupplier(QuoteHistoryEntry quote, IEnumerable<Customer> customers) =>
        customers.Any(customer => customer.IsSupplier &&
            (quote.CustomerSyncId != Guid.Empty ? customer.SyncId == quote.CustomerSyncId :
                string.Equals(customer.BusinessName?.Trim(), quote.CustomerName?.Trim(), StringComparison.OrdinalIgnoreCase)));
}
