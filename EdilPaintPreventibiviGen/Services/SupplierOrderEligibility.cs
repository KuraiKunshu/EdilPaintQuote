using System.Linq.Expressions;
using EdilPaintPreventibiviGen.Data.Entities;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

/// <summary>The registered orders shown by both Orders and the work calendar.</summary>
public static class SupplierOrderEligibility
{
    public static Expression<Func<QuoteEntity, bool>> ActiveOrderPredicate { get; } = quote =>
        !quote.IsDeleted &&
        (quote.MaterialsOrderedByCustomer ||
         quote.Status == QuoteStatus.Confermato &&
         (quote.SupplierName != string.Empty || quote.MaterialOrderDate.HasValue ||
          quote.ExpectedDeliveryDate.HasValue || quote.MaterialStatus != string.Empty));

    private static readonly Func<QuoteEntity, bool> Matches = ActiveOrderPredicate.Compile();

    public static bool IsActive(QuoteEntity quote) => Matches(quote);
}
