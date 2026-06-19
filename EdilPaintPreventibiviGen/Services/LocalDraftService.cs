using System.IO;
using System.Diagnostics;
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
        catch (Exception ex)
        {
            Debug.WriteLine($"[Draft] Impossibile leggere '{_draftPath}': {ex}");
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

    public async Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        await DeleteIfExistsAsync(_draftPath, cancellationToken);
        await DeleteIfExistsAsync(_draftPath + ".tmp", cancellationToken);
    }

    private static async Task DeleteIfExistsAsync(string path, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(path))
                    File.Delete(path);

                return;
            }
            catch (IOException) when (attempt < 2)
            {
                await Task.Delay(120, cancellationToken);
            }
            catch (UnauthorizedAccessException) when (attempt < 2)
            {
                await Task.Delay(120, cancellationToken);
            }
        }
    }
}
