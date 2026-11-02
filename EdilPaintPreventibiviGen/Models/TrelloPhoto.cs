using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EdilPaintPreventibiviGen.Models;

/// <summary>Image bytes copied by the NAS for a manually linked card.</summary>
public sealed class TrelloPhoto
{
    public string CardId { get; set; } = string.Empty;
    public string AttachmentId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string ContentBase64 { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public DateTime SyncedAtUtc { get; set; }
}

public sealed class TrelloPhotoRequest
{
    public string CardId { get; set; } = string.Empty;
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string Status { get; set; } = "pending";
    public string Error { get; set; } = string.Empty;
    public string ExpectedAttachmentIdsJson { get; set; } = "[]";
}

public sealed class TrelloPhotoBatch
{
    public string CardId { get; set; } = string.Empty;
    public string Status { get; set; } = "none";
    public string Error { get; set; } = string.Empty;
    public DateTime? RequestedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string ExpectedAttachmentIdsJson { get; set; } = "[]";
    public IReadOnlyList<TrelloPhoto> Photos { get; set; } = [];

    [JsonIgnore]
    public bool IsOfflineSnapshot { get; set; }

    public string[] GetExpectedAttachmentIds()
    {
        try
        {
            var ids = JsonSerializer.Deserialize<string[]>(ExpectedAttachmentIdsJson);
            if (ids == null || ids.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 64))
                throw new InvalidDataException("Elenco delle fotografie Trello non valido.");
            return ids.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Elenco delle fotografie Trello non leggibile.", ex);
        }
    }

    [JsonIgnore]
    public bool IsComplete => Status == "completed" && string.IsNullOrEmpty(Error) &&
        GetExpectedAttachmentIds().All(id => Photos.Any(photo =>
            photo.CardId == CardId && photo.AttachmentId == id && !string.IsNullOrWhiteSpace(photo.ContentBase64)));
}
