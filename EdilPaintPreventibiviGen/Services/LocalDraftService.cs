using System.IO;
using System.Text.Json;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public sealed class LocalDraftService
{
    private readonly string _draftPath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public LocalDraftService(string dataPath)
    {
        string draftDirectory = Path.Combine(dataPath, "Drafts");
        Directory.CreateDirectory(draftDirectory);
        _draftPath = Path.Combine(draftDirectory, "current-draft.json");
    }

    public async Task<QuoteHistoryEntry?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_draftPath))
            return null;

        try
        {
            string json = await File.ReadAllTextAsync(_draftPath, cancellationToken);
            return JsonSerializer.Deserialize<QuoteHistoryEntry>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveAsync(QuoteHistoryEntry draft, CancellationToken cancellationToken = default)
    {
        draft.Status = QuoteStatus.Bozza;
        draft.LastModifiedUtc = DateTime.UtcNow;
        string temporaryPath = _draftPath + ".tmp";
        string json = JsonSerializer.Serialize(draft, JsonOptions);
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        File.Move(temporaryPath, _draftPath, overwrite: true);
    }

    public Task DeleteAsync()
    {
        if (File.Exists(_draftPath))
            File.Delete(_draftPath);

        return Task.CompletedTask;
    }
}
