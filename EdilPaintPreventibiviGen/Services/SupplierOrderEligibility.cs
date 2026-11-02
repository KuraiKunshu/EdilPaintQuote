using System.Linq.Expressions;
using EdilPaintPreventibiviGen.Data.Entities;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

/// <summary>Explicit pending/registered orders shown by Orders and the calendar; existing confirmed quotes are not backfilled.</summary>
public static class SupplierOrderEligibility
{
    public static Expression<Func<QuoteEntity, bool>> ActiveOrderPredicate { get; } = quote =>
        !quote.IsDeleted &&
        quote.Status != QuoteStatus.Finito &&
        quote.Status != QuoteStatus.Rifiutato &&
        quote.Status != QuoteStatus.Archiviato &&
        (quote.MaterialsOrderedByCustomer || quote.Status == QuoteStatus.Confermato &&
            (quote.SupplierName.Trim() != "" || quote.MaterialOrderDate.HasValue ||
             quote.ExpectedDeliveryDate.HasValue || quote.MaterialStatus.Trim() != ""));

    private static readonly Func<QuoteEntity, bool> Matches = ActiveOrderPredicate.Compile();

    public static bool IsActive(QuoteEntity quote) => Matches(quote);
}
