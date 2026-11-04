namespace EdilPaintPreventibiviGen.Models;

internal sealed record InstallationCertificateRecipient(
    string Label, string Name, string Address, string ReferenceName, string WorkSiteFallback)
{
    public static IReadOnlyList<InstallationCertificateRecipient> CreateOptions(
        QuoteHistoryEntry quote, IEnumerable<Customer> customers)
    {
        string customerName = quote.CustomerName?.Trim() ?? string.Empty;
        string referenceName = quote.ReferenceName?.Trim() ?? string.Empty;
        var contacts = customers.ToList();
        string customerAddress = FindAddress(contacts, quote.CustomerSyncId, customerName);
        string referenceAddress = FindAddress(contacts, quote.ReferenceCustomerSyncId, referenceName);

        var options = new List<InstallationCertificateRecipient>();
        if (customerName.Length > 0 && referenceName.Length > 0)
        {
            options.Add(new($"Cliente e riferimento · {customerName} / {referenceName}",
                customerName, customerAddress, referenceName, referenceAddress));
        }
        if (customerName.Length > 0)
            options.Add(new($"Cliente · {customerName}", customerName, customerAddress, string.Empty, customerAddress));
        if (referenceName.Length > 0)
            options.Add(new($"Riferimento · {referenceName}", referenceName, referenceAddress, string.Empty, referenceAddress));
        if (options.Count == 0)
            options.Add(new("Intestatario non indicato", string.Empty, string.Empty, string.Empty, string.Empty));

        return options;
    }

    private static string FindAddress(IReadOnlyList<Customer> customers, Guid syncId, string name)
    {
        if (name.Length == 0)
            return string.Empty;
        if (syncId != Guid.Empty)
            return customers.FirstOrDefault(customer => customer.SyncId == syncId)?.Address?.Trim() ?? string.Empty;

        var matches = customers.Where(customer =>
            string.Equals(customer.BusinessName?.Trim(), name, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0].Address?.Trim() ?? string.Empty : string.Empty;
    }
}
