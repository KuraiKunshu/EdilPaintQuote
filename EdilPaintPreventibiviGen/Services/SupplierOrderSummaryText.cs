using System.Globalization;
using System.Text.RegularExpressions;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

internal static class SupplierOrderSummaryText
{
    public static IEnumerable<Item> GetMaterials(QuoteHistoryEntry quote) => quote.Materials
        .Where(material => !string.IsNullOrWhiteSpace(material.Name))
        .OrderBy(material => material.SortOrder);

    public static string Format(WorkScheduleEntry intervention, QuoteHistoryEntry quote, IEnumerable<Customer> customers,
        string? template = null)
    {
        var fields = CreateFields(new QuoteHistorySummary
        {
            CustomerName = intervention.CustomerName,
            ReferenceName = intervention.ReferenceName,
            Status = quote.Status,
            MaterialStatus = quote.MaterialStatus,
            MaterialsOrderedByCustomer = quote.MaterialsOrderedByCustomer
        }, quote, customers);
        fields["InterventionDate"] = intervention.Date.ToString("dddd dd/MM/yyyy", CultureInfo.GetCultureInfo("it-IT"));
        fields["InterventionTime"] = intervention.TimeDisplay;
        var crew = intervention.Employees.Select(employee => $"{employee.FirstName} {employee.LastName}".Trim())
            .Where(name => name.Length > 0);
        fields["Crew"] = string.Join(", ", crew.DefaultIfEmpty("Da assegnare"));
        fields["InterventionDetails"] = $"Intervento: {fields["InterventionDate"]} · {fields["InterventionTime"]}" +
            Environment.NewLine + $"Squadra: {fields["Crew"]}";
        fields["InterventionNotesText"] = intervention.Notes?.Trim() ?? "";
        fields["InterventionNotes"] = NotesBlock("Note intervento:", intervention.Notes);

        template = string.IsNullOrWhiteSpace(template) ? WorkScheduleSettings.DefaultInterventionMessageTemplate : template;
        // Replace once so braces inside customer data or notes remain literal text.
        string message = Regex.Replace(template, @"\{([A-Za-z]+)\}", match =>
            fields.GetValueOrDefault(match.Groups[1].Value, match.Value));
        message = Regex.Replace(message.ReplaceLineEndings("\n"), @"\n(?:[ \t]*\n){2,}", "\n\n");
        return message.Trim().ReplaceLineEndings();
    }

    public static string Format(QuoteHistorySummary order, QuoteHistoryEntry quote, IEnumerable<Customer> customers)
    {
        var fields = CreateFields(order, quote, customers);
        return string.Join(Environment.NewLine + Environment.NewLine,
            new[] { "ContactDetails", "MaterialDetails", "Notes", "InternalNotes" }
                .Select(field => fields[field]).Where(value => value.Length > 0));
    }

    private static Dictionary<string, string> CreateFields(QuoteHistorySummary order, QuoteHistoryEntry quote,
        IEnumerable<Customer> customers)
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

        string status = SupplierOrderQueue.StatusDisplay(order);
        status = status == "-" ? "Da impostare" : status;
        string materials = string.Join(Environment.NewLine, GetMaterials(quote)
            .Select(material => $"{material.QuantityDisplay} — {material.Name.Trim()}")
            .DefaultIfEmpty("Nessun materiale nel preventivo."));
        string labors = string.Join(Environment.NewLine, quote.Labors
            .Where(labor => !string.IsNullOrWhiteSpace(labor.Name)).OrderBy(labor => labor.SortOrder)
            .Select(FormatLabor));

        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["QuoteNumber"] = quote.QuoteNumber,
            ["CustomerName"] = string.IsNullOrWhiteSpace(customerName) ? "Non indicato" : customerName.Trim(),
            ["ReferenceName"] = referenceName?.Trim() ?? "",
            ["Address"] = string.IsNullOrWhiteSpace(quote.SiteName) ? customer?.Address?.Trim() ?? "" : quote.SiteName.Trim(),
            ["Phone"] = customer?.Phone?.Trim() ?? "",
            ["ContactDetails"] = string.Join(Environment.NewLine, lines),
            ["MaterialStatus"] = status,
            ["Materials"] = materials,
            ["MaterialDetails"] = $"Stato materiale: {status}{Environment.NewLine}Materiali:{Environment.NewLine}{materials}",
            ["Labors"] = labors,
            ["LaborDetails"] = labors.Length > 0 ? $"Lavorazioni:{Environment.NewLine}{labors}" : "",
            ["CustomerNotes"] = quote.CustomerNotes?.Trim() ?? "",
            ["InternalNotesText"] = quote.Notes?.Trim() ?? "",
            ["Notes"] = NotesBlock("Note:", quote.CustomerNotes),
            ["InternalNotes"] = NotesBlock("Note interne:", quote.Notes)
        };
    }

    private static string FormatLabor(Item labor)
    {
        string line = $"{labor.QuantityDisplay} — {labor.Name.Trim()}";
        if (!string.IsNullOrWhiteSpace(labor.Description))
            line += Environment.NewLine + "  " + labor.Description.Trim().ReplaceLineEndings(Environment.NewLine + "  ");
        return line;
    }

    private static string NotesBlock(string heading, string? notes) => string.IsNullOrWhiteSpace(notes)
        ? "" : heading + Environment.NewLine + notes.Trim();

    internal static string CreatePreview(string? template) => Format(new WorkScheduleEntry
    {
        Date = new DateTime(2026, 11, 9), StartMinutes = 8 * 60, EndMinutes = 12 * 60,
        Employees = [new() { FirstName = "Anna", LastName = "Verdi" }, new() { FirstName = "Luca", LastName = "Bianchi" }],
        Notes = "Accesso dal cancello laterale"
    }, new QuoteHistoryEntry
    {
        QuoteNumber = "1234", CustomerName = "Impresa Rossi", ReferenceName = "Condominio Verdi",
        SiteName = "Via Roma 12, Brescia", MaterialStatus = "In magazzino",
        Materials = [new Item { Name = "Finestra", Quantity = 2 }],
        Labors = [new Item { Name = "Posa finestre", Quantity = 2, Description = "Rimozione esistente e sigillatura" }],
        CustomerNotes = "Avvisare prima dell’arrivo", Notes = "Verificare la consegna dei materiali"
    }, [new Customer { BusinessName = "Impresa Rossi", Phone = "030 123456" }], template);

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
