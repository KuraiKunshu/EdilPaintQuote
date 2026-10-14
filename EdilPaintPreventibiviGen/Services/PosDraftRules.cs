using System.Text.Json;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public static class PosDraftRules
{
    public static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

    public static PosWorkItem ValidateTemplate(PosWorkItem template)
    {
        var copy = Copy(template);
        copy.Name = copy.Name.Trim();
        if (copy.Id == Guid.Empty || copy.Revision < 0 || copy.Name.Length is 0 or > 250)
            throw new InvalidOperationException("Indica il nome della scheda POS (massimo 250 caratteri).");
        ValidateSheet(copy);
        copy.Verified = false;
        return copy;
    }

    public static PosDraft ValidateDraft(PosDraft draft)
    {
        var copy = Copy(draft);
        copy.QuoteNumber = copy.QuoteNumber.Trim();
        if (copy.Id == Guid.Empty || copy.Revision < 0 || copy.QuoteNumber.Length is 0 or > 250)
            throw new InvalidOperationException("La bozza POS deve essere collegata a un lavoro salvato.");
        if (copy.Works == null || copy.EquipmentSheets == null || copy.Employees == null || copy.Works.Any(x => x == null || x.Id == Guid.Empty || x.Kind != PosSheetKind.Work) ||
            copy.EquipmentSheets.Any(x => x == null || x.Id == Guid.Empty || x.Kind != PosSheetKind.Equipment) ||
            copy.Employees.Any(x => x == null || x.EmployeeId == Guid.Empty) ||
            copy.Works.Select(x => x.Id).Distinct().Count() != copy.Works.Count ||
            copy.EquipmentSheets.Select(x => x.Id).Distinct().Count() != copy.EquipmentSheets.Count ||
            copy.Works.Select(x => x.Id).Intersect(copy.EquipmentSheets.Select(x => x.Id)).Any() ||
            copy.Employees.Select(x => x.EmployeeId).Distinct().Count() != copy.Employees.Count)
            throw new InvalidOperationException("Le schede o i dipendenti della bozza POS non sono validi.");
        foreach (var sheet in copy.Works.Concat(copy.EquipmentSheets)) ValidateSheet(sheet);
        if (copy.Works.SelectMany(x => x.EquipmentIds).Any(id => copy.EquipmentSheets.All(x => x.Id != id)))
            throw new InvalidOperationException("Una lavorazione fa riferimento a un’attrezzatura non inclusa nel POS.");
        // Incomplete dates and sections may be saved: this is a draft, not a completed safety document.
        return copy;
    }

    public static void RequireRevision(long expected, long? stored)
    {
        if ((stored == null && expected != 0) || (stored != null && stored.Value != expected)) throw Conflict();
    }

    public static InvalidOperationException Conflict(Exception? inner = null) => new(
        "Questo POS o testo standard è stato modificato da un altro PC. Le tue modifiche non sono state salvate. " +
        "Conserva i testi che ti servono e riapri la finestra per caricare la versione aggiornata.", inner);

    public static PosDraft Create(QuoteHistoryEntry quote, Company company, IReadOnlyList<Customer> customers)
    {
        var contact = customers.FirstOrDefault(x => quote.ReferenceCustomerSyncId != Guid.Empty && x.SyncId == quote.ReferenceCustomerSyncId)
            ?? customers.FirstOrDefault(x => x.BusinessName == quote.ReferenceName);
        return new PosDraft
        {
            QuoteNumber = quote.QuoteNumber,
            SiteName = !string.IsNullOrWhiteSpace(quote.SiteName) ? quote.SiteName :
                !string.IsNullOrWhiteSpace(quote.ReferenceName) ? quote.ReferenceName : quote.CustomerName,
            SiteAddress = contact?.Address ?? "", Client = quote.CustomerName,
            CompanyName = company.Nome, CompanyAddress = company.Indirizzo, CompanyVat = company.Piva
            // No mapping from quote.Labors; POS work sections are always selected manually.
        };
    }

    public static List<PosWorkChoice> WorkChoices(IEnumerable<PosWorkItem> catalog, PosDraft draft)
        => SheetChoices(catalog, draft.Works, PosSheetKind.Work);

    public static List<PosWorkChoice> EquipmentChoices(IEnumerable<PosWorkItem> catalog, PosDraft draft)
        => SheetChoices(catalog, draft.EquipmentSheets, PosSheetKind.Equipment);

    private static List<PosWorkChoice> SheetChoices(IEnumerable<PosWorkItem> catalog, List<PosWorkItem> saved, PosSheetKind kind)
    {
        var choices = catalog.Where(x => !x.IsArchived && x.Kind == kind).Select(x => new PosWorkChoice
        {
            Sheet = Copy(saved.FirstOrDefault(s => s.Id == x.Id) ?? x),
            IsSelected = saved.Any(s => s.Id == x.Id)
        }).ToList();
        choices.AddRange(saved.Where(s => choices.All(x => x.Sheet.Id != s.Id)).Select(s => new PosWorkChoice
        {
            Sheet = Copy(s), IsSelected = true, Notice = "Scheda conservata nel POS · modello non più attivo"
        }));
        return choices.OrderBy(x => x.Sheet.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static void ValidateSheet(PosWorkItem sheet)
    {
        if (sheet.Id == Guid.Empty || sheet.Revision < 0 || !Enum.IsDefined(sheet.Kind) || sheet.EquipmentIds.Any(id => id == Guid.Empty) ||
            sheet.EquipmentIds.Distinct().Count() != sheet.EquipmentIds.Count ||
            (sheet.Kind == PosSheetKind.Equipment && sheet.EquipmentIds.Count != 0))
            throw new InvalidOperationException("La categoria o i collegamenti delle schede POS non sono validi.");
    }

    public static List<PosEmployeeChoice> EmployeeChoices(IEnumerable<EmployeeSettingsModel> employees, PosDraft draft)
    {
        var choices = employees.Select(x => new PosEmployeeChoice
        {
            Employee = Copy(draft.Employees.FirstOrDefault(e => e.EmployeeId == x.Id) ?? new PosEmployee
            { EmployeeId = x.Id, Name = $"{x.FirstName} {x.LastName}".Trim() }),
            IsSelected = draft.Employees.Any(e => e.EmployeeId == x.Id)
        }).ToList();
        choices.AddRange(draft.Employees.Where(e => choices.All(x => x.Employee.EmployeeId != e.EmployeeId)).Select(e => new PosEmployeeChoice
        { Employee = Copy(e), IsSelected = true, Notice = "Persona conservata nel POS · non più nell’elenco attivo" }));
        return choices.OrderBy(x => x.Employee.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static List<string> Pending(PosDraft draft)
    {
        var pending = new List<string>();
        void Field(string? text, string label) { if (string.IsNullOrWhiteSpace(text)) pending.Add(label); }
        Field(draft.SiteName, "Nome del cantiere"); Field(draft.SiteAddress, "Indirizzo del cantiere");
        Field(draft.Client, "Committente"); Field(draft.CompanyName, "Impresa esecutrice");
        Field(draft.CompanyAddress, "Indirizzo impresa"); Field(draft.CompanyVat, "Partita IVA impresa");
        Field(draft.Description, "Descrizione complessiva del lavoro");
        Field(draft.Access, "Accessi e condizioni del sito"); Field(draft.Employer, "Datore di lavoro");
        Field(draft.Supervisor, "Preposto del cantiere"); Field(draft.Rspp, "RSPP");
        Field(draft.Rls, "RLS"); Field(draft.Doctor, "Medico competente");
        if (draft.StartDate == null || draft.EndDate == null) pending.Add("Periodo dei lavori");
        else if (draft.EndDate < draft.StartDate) pending.Add("Fine lavori precedente all’inizio");
        if (draft.Works.Count == 0) pending.Add("Lavorazioni POS");
        if (draft.Employees.Count == 0) pending.Add("Squadra del cantiere");
        foreach (var person in draft.Employees)
            if (string.IsNullOrWhiteSpace(person.Qualification)) pending.Add($"Qualifica da indicare: {person.Name}");
        foreach (var work in draft.Works)
        {
            if (!work.Verified) pending.Add($"Scheda da verificare: {work.Name}");
            if (new[] { work.Description, work.Risks, work.Dpi }.Any(string.IsNullOrWhiteSpace) ||
                (string.IsNullOrWhiteSpace(work.Measures) && string.IsNullOrWhiteSpace(work.Procedures)))
                pending.Add($"Testi da completare: {work.Name}");
            if (work.EquipmentIds.Count == 0 && string.IsNullOrWhiteSpace(work.Equipment)) pending.Add($"Attrezzature da indicare: {work.Name}");
            if (work.EquipmentIds.Any(id => draft.EquipmentSheets.All(x => x.Id != id))) pending.Add($"Schede attrezzature mancanti: {work.Name}");
        }
        foreach (var equipment in draft.EquipmentSheets)
        {
            if (!equipment.Verified) pending.Add($"Attrezzatura da verificare: {equipment.Name}");
            if (new[] { equipment.Description, equipment.Risks, equipment.Procedures, equipment.Dpi }.Any(string.IsNullOrWhiteSpace))
                pending.Add($"Scheda attrezzatura da completare: {equipment.Name}");
        }
        return pending;
    }
}
