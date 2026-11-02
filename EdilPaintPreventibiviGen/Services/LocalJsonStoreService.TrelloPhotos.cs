using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public partial class LocalJsonStoreService
{
    private readonly SemaphoreSlim _trelloPhotoCacheSemaphore = new(1, 1);

    public async Task SaveTrelloPhotoBatchAsync(TrelloPhotoBatch batch, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        SqlDataService.ValidateTrelloPhotoCardId(batch.CardId);
        if (!batch.IsComplete) throw new ArgumentException("La copia delle fotografie Trello non è completa.", nameof(batch));
        await _trelloPhotoCacheSemaphore.WaitAsync(token).ConfigureAwait(false);
        try
        {
            string path = GetTrelloPhotoCachePath(batch.CardId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await WriteJsonWithBackupAsync(path, batch, token).ConfigureAwait(false);
        }
        finally { _trelloPhotoCacheSemaphore.Release(); }
    }

    public async Task<TrelloPhotoBatch?> LoadTrelloPhotoBatchAsync(string cardId, CancellationToken token = default)
    {
        SqlDataService.ValidateTrelloPhotoCardId(cardId);
        await _trelloPhotoCacheSemaphore.WaitAsync(token).ConfigureAwait(false);
        try
        {
            string path = GetTrelloPhotoCachePath(cardId);
            if (!File.Exists(path)) return null;
            string json = await ReadTextWithBackupAsync(path, token).ConfigureAwait(false);
            var batch = JsonSerializer.Deserialize<TrelloPhotoBatch>(json, JsonOptions);
            if (batch == null || batch.CardId != cardId || !batch.IsComplete)
                throw new InvalidDataException("La copia locale delle fotografie Trello non è completa o non corrisponde alla scheda selezionata.");
            return batch;
        }
        finally { _trelloPhotoCacheSemaphore.Release(); }
    }

    private string GetTrelloPhotoCachePath(string cardId)
    {
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cardId)));
        return Path.Combine(Path.GetDirectoryName(_trelloCardsPath)!, "TrelloPhotos", key + ".json");
    }
}
