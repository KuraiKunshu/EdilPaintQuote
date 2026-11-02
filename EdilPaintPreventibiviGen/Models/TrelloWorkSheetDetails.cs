using System.Text.Json.Serialization;

namespace EdilPaintPreventibiviGen.Models;

/// <summary>Only the visit date and photographs belong in a work sheet.</summary>
public sealed class TrelloWorkSheetDetails
{
    [JsonIgnore]
    public string CardId { get; init; } = string.Empty;
    public DateTime? DueUtc { get; init; }
    [JsonIgnore]
    public string Warning { get; init; } = string.Empty;
    public IReadOnlyList<TrelloWorkSheetPhoto> Photos { get; init; } = [];
}

public sealed record TrelloWorkSheetPhoto(string FileName, byte[] Content);
