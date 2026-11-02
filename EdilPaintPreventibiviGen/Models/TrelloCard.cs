using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EdilPaintPreventibiviGen.Models;

/// <summary>
/// Read-only mirror of a Trello card. Customer and quote associations are intentionally absent.
/// </summary>
public sealed class TrelloCard
{
    private static readonly JsonSerializerOptions SnapshotOptions = new() { PropertyNameCaseInsensitive = true };

    public string Id { get; set; } = string.Empty;
    public string BoardId { get; set; } = string.Empty;
    public string BoardName { get; set; } = string.Empty;
    public string ListId { get; set; } = string.Empty;
    public string ListName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DueUtc { get; set; }
    public DateTime? LastActivityUtc { get; set; }
    public DateTime SyncedAtUtc { get; set; }
    public string PayloadJson { get; set; } = string.Empty;

    [NotMapped, JsonIgnore]
    public bool IsOfflineSnapshot { get; set; }

    public string SerializeSnapshot() => JsonSerializer.Serialize(this, SnapshotOptions);

    public static TrelloCard? FromSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var card = JsonSerializer.Deserialize<TrelloCard>(json, SnapshotOptions);
            return string.IsNullOrWhiteSpace(card?.Id) ? null : card;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string GetSafeUrl() =>
        Uri.TryCreate(Url, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
        (uri.Host.Equals("trello.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("www.trello.com", StringComparison.OrdinalIgnoreCase)) &&
        uri.AbsolutePath.StartsWith("/c/", StringComparison.Ordinal)
            ? uri.AbsoluteUri
            : string.Empty;
}
