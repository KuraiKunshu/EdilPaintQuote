using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public static class WorkSheetService
{
    public static WorkSheetContext CreateContext(
        QuoteHistoryEntry quote,
        IEnumerable<Item> laborCatalog,
        IEnumerable<Customer> customers)
    {
        var catalog = laborCatalog.ToList();
        var contacts = customers.ToList();
        Customer? customer = FindCustomer(quote.CustomerSyncId, quote.CustomerName);
        Customer? reference = FindCustomer(quote.ReferenceCustomerSyncId, quote.ReferenceName);

        return new WorkSheetContext
        {
            QuoteNumber = quote.QuoteNumber,
            QuoteDate = quote.Date,
            CustomerName = quote.CustomerName,
            ReferenceName = quote.ReferenceName,
            WorkSite = FirstText(quote.SiteName, reference?.Address, customer?.Address),
            ContactPhone = FirstText(reference?.Phone, customer?.Phone),
            MaterialsOrderedByCustomer = quote.MaterialsOrderedByCustomer,
            SupplierName = quote.MaterialsOrderedByCustomer ? quote.CustomerName : quote.SupplierName,
            MaterialStatus = string.IsNullOrWhiteSpace(quote.MaterialStatus) ? "NON INDICATO" : quote.MaterialStatus.Trim().ToUpperInvariant(),
            MaterialOrderDate = quote.MaterialOrderDate,
            ExpectedDeliveryDate = quote.ExpectedDeliveryDate,
            Materials = Lines(quote.Materials).ToArray(),
            Labors = Lines(quote.Labors.Where(labor => !IsExcluded(labor))).ToArray()
        };

        Customer? FindCustomer(Guid syncId, string name) =>
            (syncId == Guid.Empty ? null : contacts.FirstOrDefault(c => c.SyncId == syncId)) ??
            (string.IsNullOrWhiteSpace(name) ? null : contacts.FirstOrDefault(c => SameName(c.BusinessName, name)));

        bool IsExcluded(Item labor)
        {
            // Catalog IDs survive renames; old quotes may only have the name.
            Item? match = labor.PersistentId > 0 ? catalog.FirstOrDefault(c => c.PersistentId == labor.PersistentId) : null;
            match ??= catalog.FirstOrDefault(c => SameName(c.Name, labor.Name));
            return match?.ExcludeFromWorkSheet == true;
        }
    }

    private static IEnumerable<WorkSheetLine> Lines(IEnumerable<Item> items) => items
        .Where(item => item.Quantity > 0 && !string.IsNullOrWhiteSpace(item.Name))
        .OrderBy(item => item.SortOrder)
        .Select(item => new WorkSheetLine(item.Name, item.Description ?? string.Empty, item.Quantity));

    private static bool SameName(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string FirstText(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
