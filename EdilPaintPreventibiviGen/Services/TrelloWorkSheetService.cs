using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public static class TrelloWorkSheetService
{
    public static async Task<WorkSheetContext> CreateContextAsync(
        IDataService dataService,
        QuoteHistoryEntry quote,
        IEnumerable<Item> laborCatalog,
        IEnumerable<Customer> customers,
        WorkSheetOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        TrelloCard? card = null;
        bool lookupFailed = false;
        if (!string.IsNullOrWhiteSpace(quote.TrelloCardId))
        {
            try
            {
                card = await dataService.GetTrelloCardAsync(quote.TrelloCardId, cancellationToken);
                lookupFailed = card == null || card.IsOfflineSnapshot;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Trello] Scheda lavoro: lettura non disponibile ({ex.GetType().Name}).");
                lookupFailed = true;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return WorkSheetService.CreateContext(quote, laborCatalog, customers, options, card, lookupFailed);
    }

    public static TrelloWorkSheetDetails? CreateDetails(
        QuoteHistoryEntry quote, TrelloCard? currentCard = null, bool lookupFailed = false)
    {
        // Only an explicit persisted ID establishes the association. Names and contacts are never matched.
        if (string.IsNullOrWhiteSpace(quote.TrelloCardId)) return null;
        bool currentMatches = currentCard != null && string.Equals(currentCard.Id, quote.TrelloCardId, StringComparison.Ordinal);
        var card = currentMatches ? currentCard : TrelloCard.FromSnapshot(quote.TrelloCardSnapshotJson);
        if (card == null || !string.Equals(card.Id, quote.TrelloCardId, StringComparison.Ordinal))
            return new TrelloWorkSheetDetails
            {
                CardId = quote.TrelloCardId,
                Name = "Scheda Trello collegata",
                Warning = "Dati della scheda Trello non disponibili. Verificare il collegamento nel programma prima dell'intervento."
            };

        var fields = new List<TrelloWorkSheetField>();
        var checklists = new List<TrelloWorkSheetChecklist>();
        bool payloadInvalid = false;
        if (!string.IsNullOrWhiteSpace(card.PayloadJson))
        {
            try
            {
                using var document = JsonDocument.Parse(card.PayloadJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object) payloadInvalid = true;
                else ReadOperationalData(document.RootElement, fields, checklists);
            }
            catch (JsonException) { payloadInvalid = true; }
        }
        var warnings = new List<string>();
        if (!currentMatches || lookupFailed || card.IsOfflineSnapshot)
            warnings.Add("Copia dei dati Trello disponibile al momento del salvataggio o dell'ultima sincronizzazione: verificare eventuali aggiornamenti.");
        if (card.IsDeleted) warnings.Add("La scheda risulta eliminata in Trello. Il collegamento storico è conservato.");
        else if (card.IsArchived) warnings.Add("La scheda risulta archiviata in Trello.");
        if (payloadInvalid) warnings.Add("Dettagli Trello non leggibili: checklist e campi aggiuntivi potrebbero essere incompleti.");

        return new TrelloWorkSheetDetails
        {
            CardId = card.Id,
            Name = card.Name,
            Url = card.GetSafeUrl(),
            BoardName = card.BoardName,
            ListName = card.ListName,
            Description = card.Description,
            DueUtc = card.DueUtc,
            SyncedAtUtc = card.SyncedAtUtc == default ? null : card.SyncedAtUtc,
            IsArchived = card.IsArchived,
            IsDeleted = card.IsDeleted,
            Warning = string.Join(" ", warnings),
            Fields = fields,
            Checklists = checklists
        };
    }

    private static void ReadOperationalData(JsonElement root, List<TrelloWorkSheetField> fields,
        List<TrelloWorkSheetChecklist> checklists)
    {
        string address = Text(root, "address");
        string location = Text(root, "locationName");
        if (!string.IsNullOrWhiteSpace(address)) fields.Add(new("Indirizzo in Trello", address));
        if (!string.IsNullOrWhiteSpace(location) && !string.Equals(address, location, StringComparison.Ordinal))
            fields.Add(new("Luogo in Trello", location));

        var definitions = Array(root, "customFields").Where(x => x.ValueKind == JsonValueKind.Object)
            .Where(x => Text(x, "id").Length > 0).GroupBy(x => Text(x, "id"))
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        foreach (var field in Array(root, "customFieldItems"))
        {
            if (field.ValueKind != JsonValueKind.Object) continue;
            if (!definitions.TryGetValue(Text(field, "idCustomField"), out var definition)) continue;
            string name = Text(definition, "name");
            string value = string.Empty;
            if (field.TryGetProperty("value", out var raw) && raw.ValueKind == JsonValueKind.Object)
            {
                value = Text(raw, "text");
                if (value.Length == 0) value = Text(raw, "number");
                if (value.Length == 0 && raw.TryGetProperty("checked", out var check))
                    value = check.ToString().Equals("true", StringComparison.OrdinalIgnoreCase) ? "Sì" : "No";
                if (value.Length == 0 && DateTimeOffset.TryParse(Text(raw, "date"), CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out var date))
                    value = date.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("it-IT"));
            }
            if (value.Length == 0)
            {
                string selectedId = Text(field, "idValue");
                foreach (var option in Array(definition, "options"))
                    if (option.ValueKind == JsonValueKind.Object && Text(option, "id") == selectedId && option.TryGetProperty("value", out var optionValue))
                    { value = Text(optionValue, "text"); break; }
            }
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(value)) fields.Add(new(name, value));
        }
        foreach (var checklist in Array(root, "checklists"))
        {
            var items = Array(checklist, "checkItems").OrderBy(x => Position(x))
                .Where(x => !string.IsNullOrWhiteSpace(Text(x, "name")))
                .Select(x => new TrelloWorkSheetCheckItem(Text(x, "name"), Text(x, "state") == "complete")).ToArray();
            if (items.Length > 0) checklists.Add(new(Text(checklist, "name"), items));
        }
        // Comments, attachment metadata and member data are preserved by sync, but are not printed as operational instructions.
    }

    private static IEnumerable<JsonElement> Array(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray() : [];

    private static string Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
        value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
            ? value.ToString() : string.Empty;

    private static decimal Position(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty("pos", out var position) &&
        position.ValueKind == JsonValueKind.Number && position.TryGetDecimal(out var value)
            ? value : decimal.MaxValue;
}
