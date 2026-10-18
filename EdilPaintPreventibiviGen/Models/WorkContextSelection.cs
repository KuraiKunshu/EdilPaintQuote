namespace EdilPaintPreventibiviGen.Models;

/// <summary>One selected job across the calendar, orders and history; an absence has no quote.</summary>
public sealed class WorkContextSelection
{
    public string QuoteNumber { get; private set; } = "";
    public string Title { get; private set; } = "Seleziona un cantiere";
    public WorkScheduleEntry? Entry { get; private set; }

    public void Select(string? quoteNumber, string? title, WorkScheduleEntry? entry = null)
    {
        string number = quoteNumber?.Trim() ?? "";
        Entry = entry?.Kind == WorkScheduleEntryKind.Absence ? entry :
            entry != null && string.Equals(entry.QuoteNumber, number, StringComparison.Ordinal) ? entry :
            Entry?.Kind == WorkScheduleEntryKind.Job && string.Equals(QuoteNumber, number, StringComparison.Ordinal) ? Entry : null;
        QuoteNumber = entry?.Kind == WorkScheduleEntryKind.Absence ? "" : number;
        Title = string.IsNullOrWhiteSpace(title) ? (number.Length == 0 ? "Seleziona un cantiere" : $"Ordine {number}") : title;
    }

    public void UpdateEntries(IEnumerable<WorkScheduleEntry> entries)
    {
        if (Entry == null) return;
        Entry = entries.FirstOrDefault(item => item.Id == Entry.Id);
        if (Entry != null)
        {
            QuoteNumber = Entry.Kind == WorkScheduleEntryKind.Job ? Entry.QuoteNumber : "";
            Title = Entry.Title;
        }
        else if (QuoteNumber.Length == 0) Title = "Seleziona un cantiere";
    }
}
