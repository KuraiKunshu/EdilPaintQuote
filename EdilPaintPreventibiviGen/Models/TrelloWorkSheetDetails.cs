namespace EdilPaintPreventibiviGen.Models;

/// <summary>Operational Trello data, deliberately separate from PdfGenerationContext.</summary>
public sealed class TrelloWorkSheetDetails
{
    public string CardId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string BoardName { get; init; } = string.Empty;
    public string ListName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public DateTime? DueUtc { get; init; }
    public DateTime? SyncedAtUtc { get; init; }
    public bool IsArchived { get; init; }
    public bool IsDeleted { get; init; }
    public string Warning { get; init; } = string.Empty;
    public IReadOnlyList<TrelloWorkSheetField> Fields { get; init; } = [];
    public IReadOnlyList<TrelloWorkSheetChecklist> Checklists { get; init; } = [];
}

public sealed record TrelloWorkSheetField(string Name, string Value);
public sealed record TrelloWorkSheetChecklist(string Name, IReadOnlyList<TrelloWorkSheetCheckItem> Items);
public sealed record TrelloWorkSheetCheckItem(string Name, bool IsComplete);
