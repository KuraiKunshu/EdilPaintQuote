using System.IO;
using System.Text.Json;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public partial class LocalJsonStoreService
{
    public async Task SaveTrelloCardsAsync(IEnumerable<TrelloCard> cards, CancellationToken cancellationToken = default)
    {
        await _trelloCardsSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteJsonWithBackupAsync(_trelloCardsPath,
                new TrelloCache { IsComplete = true, Cards = cards.ToList() }, cancellationToken).ConfigureAwait(false);
        }
        finally { _trelloCardsSemaphore.Release(); }
    }

    /// <summary>Null means no complete cache exists; an empty list means a verified empty mirror.</summary>
    public async Task<List<TrelloCard>?> LoadTrelloCardsAsync(CancellationToken cancellationToken = default)
    {
        await _trelloCardsSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cache = await ReadTrelloCacheAsync(cancellationToken).ConfigureAwait(false);
            return cache?.IsComplete == true ? cache.Cards : null;
        }
        finally { _trelloCardsSemaphore.Release(); }
    }

    public async Task CacheTrelloCardAsync(TrelloCard card, CancellationToken cancellationToken = default)
    {
        await _trelloCardsSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cache = await ReadTrelloCacheAsync(cancellationToken).ConfigureAwait(false) ?? new TrelloCache();
            cache.Cards.RemoveAll(c => c.Id == card.Id);
            cache.Cards.Add(card);
            await WriteJsonWithBackupAsync(_trelloCardsPath, cache, cancellationToken).ConfigureAwait(false);
        }
        finally { _trelloCardsSemaphore.Release(); }
    }

    public async Task<TrelloCard?> LoadTrelloCardAsync(string cardId, CancellationToken cancellationToken = default)
    {
        await _trelloCardsSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cache = await ReadTrelloCacheAsync(cancellationToken).ConfigureAwait(false);
            return cache?.Cards.SingleOrDefault(c => c.Id == cardId);
        }
        finally { _trelloCardsSemaphore.Release(); }
    }

    private async Task<TrelloCache?> ReadTrelloCacheAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_trelloCardsPath)) return null;
        string json = await ReadTextWithBackupAsync(_trelloCardsPath, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<TrelloCache>(json, JsonOptions)
            ?? throw new InvalidDataException("La copia locale delle schede Trello non è valida.");
    }

    private sealed class TrelloCache
    {
        public bool IsComplete { get; set; }
        public List<TrelloCard> Cards { get; set; } = [];
    }
}
