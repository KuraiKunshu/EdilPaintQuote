using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public static class SupplierOrderAssignmentService
{
    public static void ApplyCustomerOrderChoice(
        QuoteHistorySummary summary,
        bool orderedByCustomer)
    {
        ArgumentNullException.ThrowIfNull(summary);

        summary.MaterialsOrderedByCustomer = orderedByCustomer;
        if (orderedByCustomer)
        {
            summary.SupplierName = summary.CustomerName?.Trim() ?? string.Empty;
        }
        else if (string.Equals(
                     summary.SupplierName?.Trim(),
                     summary.CustomerName?.Trim(),
                     StringComparison.OrdinalIgnoreCase))
        {
            summary.SupplierName = string.Empty;
        }
    }
}
