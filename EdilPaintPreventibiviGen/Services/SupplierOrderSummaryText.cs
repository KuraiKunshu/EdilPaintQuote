using System.Globalization;
using System.Text.RegularExpressions;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

internal sealed record InterventionMessageField(string Label, string Text);

internal static class SupplierOrderSummaryText
{
    private const string MaterialRowsTemplate = "{#Materials}{QuantityDisplay} — {Name}\n{/Materials}";
    private const string LaborRowsTemplate = "{#Labors}{QuantityDisplay} — {Name}\n{#Description}  {DescriptionIndented}\n{/Description}{/Labors}";

    private static readonly IReadOnlyDictionary<string, string> ExpandedBlocks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ContactDetails"] = "Cliente: {CustomerName}\n{#ReferenceName}Riferimento: {ReferenceName}\n{/ReferenceName}" +
            "{#Address}{AddressLabel}: {Address}\n{/Address}{#Phone}Telefono cliente: {Phone}{/Phone}",
        ["InterventionDetails"] = "Intervento: {InterventionWeekday} {InterventionDateShort} · {StartTime}–{EndTime}\nSquadra: {Crew}",
        ["MaterialDetails"] = "Stato materiale: {MaterialStatus}\nMateriali:\n" + MaterialRowsTemplate +
            "{#NoMaterials}Nessun materiale nel preventivo.{/NoMaterials}",
        ["LaborDetails"] = "{#HasLabors}Lavorazioni:\n" + LaborRowsTemplate + "{/HasLabors}",
        ["Notes"] = "{#CustomerNotes}Note:\n{CustomerNotes}{/CustomerNotes}",
        ["InternalNotes"] = "{#InternalNotesText}Note interne:\n{InternalNotesText}{/InternalNotesText}",
        ["InterventionNotes"] = "{#InterventionNotesText}Note intervento:\n{InterventionNotesText}{/InterventionNotesText}",
        ["Materials"] = MaterialRowsTemplate + "{#NoMaterials}Nessun materiale nel preventivo.{/NoMaterials}",
        ["Labors"] = LaborRowsTemplate
    };

    internal static IReadOnlyList<InterventionMessageField> TemplateFields { get; } =
    [
        new("Cliente · Nome", "{CustomerName}"),
        new("Riferimento · Nome", "{ReferenceName}"),
        new("Indirizzo · Cantiere o cliente", "{Address}"),
        new("Indirizzo · Solo cantiere", "{SiteAddress}"),
        new("Indirizzo · Solo cliente", "{CustomerAddress}"),
        new("Indirizzo · Riferimento", "{ReferenceAddress}"),
        new("Telefono · Cliente", "{Phone}"),
        new("Telefono · Riferimento", "{ReferencePhone}"),
        new("Intervento · Data completa", "{InterventionDate}"),
        new("Intervento · Data breve", "{InterventionDateShort}"),
        new("Intervento · Giorno della settimana", "{InterventionWeekday}"),
        new("Intervento · Ora iniziale", "{StartTime}"),
        new("Intervento · Ora finale", "{EndTime}"),
        new("Intervento · Fascia oraria", "{Slot}"),
        new("Squadra · Nomi separati da virgole", "{Crew}"),
        new("Squadra · Sigle", "{CrewAbbreviations}"),
        new("Squadra · Una persona per riga", "{#CrewMembers}{FullName}\n{/CrewMembers}"),
        new("Materiali · Stato", "{MaterialStatus}"),
        new("Materiali · Righe modificabili", MaterialRowsTemplate),
        new("Lavorazioni · Righe modificabili", LaborRowsTemplate.Replace("DescriptionIndented", "Description")),
        new("Preventivo · Numero", "{QuoteNumber}"),
        new("Note · Solo testo", "{CustomerNotes}"),
        new("Note interne · Solo testo", "{InternalNotesText}"),
        new("Note intervento · Solo testo", "{InterventionNotesText}"),
        new("Sezione facoltativa · Riferimento", "{#ReferenceName}Riferimento: {ReferenceName}\n{/ReferenceName}"),
        new("Sezione facoltativa · Note intervento", "{#InterventionNotesText}Note intervento:\n{InterventionNotesText}\n{/InterventionNotesText}")
    ];

    internal static string ExpandBlocks(string? template) => Regex.Replace(
        string.IsNullOrWhiteSpace(template) ? WorkScheduleSettings.DefaultInterventionMessageTemplate : template,
        @"\{([A-Za-z]+)\}", match => ExpandedBlocks.GetValueOrDefault(match.Groups[1].Value, match.Value));

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
        var culture = CultureInfo.GetCultureInfo("it-IT");
        fields["InterventionDate"] = intervention.Date.ToString("dddd dd/MM/yyyy", culture);
        fields["InterventionDateShort"] = intervention.Date.ToString("dd/MM/yyyy", culture);
        fields["InterventionWeekday"] = intervention.Date.ToString("dddd", culture);
        fields["InterventionTime"] = intervention.TimeDisplay;
        fields["StartTime"] = WorkScheduleSettings.FormatTime(intervention.StartMinutes);
        fields["EndTime"] = WorkScheduleSettings.FormatTime(intervention.EndMinutes);
        fields["Slot"] = intervention.SlotDisplay;
        var crew = intervention.Employees.Select(employee => $"{employee.FirstName} {employee.LastName}".Trim())
            .Where(name => name.Length > 0);
        fields["Crew"] = string.Join(", ", crew.DefaultIfEmpty("Da assegnare"));
        fields["CrewAbbreviations"] = string.Join(", ", intervention.Employees.Select(employee =>
            string.IsNullOrWhiteSpace(employee.Abbreviation) ? $"{employee.FirstName} {employee.LastName}".Trim() : employee.Abbreviation.Trim())
            .Where(name => name.Length > 0));
        fields["InterventionDetails"] = $"Intervento: {fields["InterventionDate"]} · {fields["InterventionTime"]}" +
            Environment.NewLine + $"Squadra: {fields["Crew"]}";
        fields["InterventionNotesText"] = intervention.Notes?.Trim() ?? "";
        fields["InterventionNotes"] = NotesBlock("Note intervento:", intervention.Notes);

        var rows = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Materials"] = CreateItemRows(GetMaterials(quote)),
            ["Labors"] = CreateItemRows(quote.Labors.Where(labor => !string.IsNullOrWhiteSpace(labor.Name)).OrderBy(labor => labor.SortOrder)),
            ["CrewMembers"] = intervention.Employees.Select((employee, index) =>
                (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["FullName"] = $"{employee.FirstName} {employee.LastName}".Trim(),
                    ["FirstName"] = employee.FirstName?.Trim() ?? "",
                    ["LastName"] = employee.LastName?.Trim() ?? "",
                    ["Abbreviation"] = employee.Abbreviation?.Trim() ?? "",
                    ["RowNumber"] = (index + 1).ToString(culture)
                }).ToList()
        };
        fields["NoMaterials"] = rows["Materials"].Count == 0 ? "1" : "";
        fields["HasLabors"] = rows["Labors"].Count > 0 ? "1" : "";
        template = string.IsNullOrWhiteSpace(template) ? WorkScheduleSettings.DefaultInterventionMessageTemplate : template;
        string message = InterventionMessageTemplate.Render(template, fields, rows);
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
        Customer? reference = FindCustomer(quote.ReferenceCustomerSyncId, referenceName, customers);
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
            ["AddressLabel"] = string.IsNullOrWhiteSpace(quote.SiteName) ? "Indirizzo cliente" : "Indirizzo cantiere",
            ["SiteAddress"] = quote.SiteName?.Trim() ?? "",
            ["CustomerAddress"] = customer?.Address?.Trim() ?? "",
            ["ReferenceAddress"] = reference?.Address?.Trim() ?? "",
            ["Phone"] = customer?.Phone?.Trim() ?? "",
            ["ReferencePhone"] = reference?.Phone?.Trim() ?? "",
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

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> CreateItemRows(IEnumerable<Item> items) => items
        .Select((item, index) => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["RowNumber"] = (index + 1).ToString(CultureInfo.GetCultureInfo("it-IT")),
            ["Quantity"] = QuantityValue.Format(item.Quantity),
            ["Unit"] = QuantityValue.NormalizeUnit(item.UnitOfMeasure),
            ["QuantityDisplay"] = item.QuantityDisplay,
            ["Name"] = item.Name.Trim(),
            ["Description"] = item.Description?.Trim().ReplaceLineEndings() ?? "",
            ["DescriptionIndented"] = item.Description?.Trim().ReplaceLineEndings(Environment.NewLine + "  ") ?? ""
        }).ToList();

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
