using System.Globalization;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

internal static class SupplierOrderSummaryText
{
    public static IEnumerable<Item> GetMaterials(QuoteHistoryEntry quote) => quote.Materials
        .Where(material => !string.IsNullOrWhiteSpace(material.Name))
        .OrderBy(material => material.SortOrder);

    public static string Format(WorkScheduleEntry intervention, QuoteHistoryEntry quote, IEnumerable<Customer> customers) =>
        Format(new QuoteHistorySummary
        {
            CustomerName = intervention.CustomerName,
            ReferenceName = intervention.ReferenceName,
            Status = quote.Status,
            MaterialStatus = quote.MaterialStatus,
            MaterialsOrderedByCustomer = quote.MaterialsOrderedByCustomer
        }, quote, customers, intervention);

    public static string Format(QuoteHistorySummary order, QuoteHistoryEntry quote, IEnumerable<Customer> customers,
        WorkScheduleEntry? intervention = null)
    {
        string customerName = string.IsNullOrWhiteSpace(quote.CustomerName) ? order.CustomerName : quote.CustomerName;
        string referenceName = string.IsNullOrWhiteSpace(quote.ReferenceName) ? order.ReferenceName : quote.ReferenceName;
        Customer? customer = FindCustomer(quote.CustomerSyncId, customerName, customers);
        var lines = new List<string>
        {
            $"Cliente: {(string.IsNullOrWhiteSpace(customerName) ? "Non indicato" : customerName.Trim())}"
        };
        if (!string.IsNullOrWhiteSpace(referenceName))
            lines.Add($"Riferimento: {referenceName.Trim()}");
        if (!string.IsNullOrWhiteSpace(quote.SiteName))
            lines.Add($"Indirizzo cantiere: {quote.SiteName.Trim()}");
        else if (!string.IsNullOrWhiteSpace(customer?.Address))
            lines.Add($"Indirizzo cliente: {customer.Address.Trim()}");
        if (!string.IsNullOrWhiteSpace(customer?.Phone))
            lines.Add($"Telefono cliente: {customer.Phone.Trim()}");

        if (intervention != null)
        {
            string date = intervention.Date.ToString("dddd dd/MM/yyyy", CultureInfo.GetCultureInfo("it-IT"));
            var crew = intervention.Employees.Select(employee => $"{employee.FirstName} {employee.LastName}".Trim())
                .Where(name => name.Length > 0);
            lines.Add("");
            lines.Add($"Intervento: {date} · {intervention.TimeDisplay}");
            lines.Add($"Squadra: {string.Join(", ", crew.DefaultIfEmpty("Da assegnare"))}");
        }

        string status = SupplierOrderQueue.StatusDisplay(order);
        var materials = GetMaterials(quote)
            .Select(material => $"{material.QuantityDisplay} — {material.Name.Trim()}");
        lines.Add("");
        lines.Add($"Stato materiale: {(status == "-" ? "Da impostare" : status)}");
        lines.Add("Materiali:");
        lines.AddRange(materials.DefaultIfEmpty("Nessun materiale nel preventivo."));

        AddNotes("Note:", quote.CustomerNotes);
        AddNotes("Note interne:", quote.Notes);
        AddNotes("Note intervento:", intervention?.Notes);
        return string.Join(Environment.NewLine, lines);

        void AddNotes(string heading, string? notes)
        {
            if (string.IsNullOrWhiteSpace(notes)) return;
            lines.Add("");
            lines.Add(heading);
            lines.Add(notes.Trim());
        }
    }

    private static Customer? FindCustomer(Guid syncId, string name, IEnumerable<Customer> customers)
    {
        if (syncId != Guid.Empty)
            return customers.FirstOrDefault(customer => customer.SyncId == syncId);
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var matches = customers.Where(customer =>
            string.Equals(customer.BusinessName?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
            .Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}
