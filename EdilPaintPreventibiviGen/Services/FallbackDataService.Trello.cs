using System.Diagnostics;
using System.IO;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public partial class FallbackDataService
{
    public async Task UpdateQuoteTrelloAssociationAsync(string quoteNumber, string trelloCardId,
        string trelloCardSnapshotJson, CancellationToken cancellationToken = default)
    {
        SqlDataService.ValidateTrelloAssociation(trelloCardId, trelloCardSnapshotJson);
        await EnsureDatabaseRequiredAsync($"Associazione manuale Trello al preventivo {quoteNumber}", cancellationToken);
        try
        {
            await _sqlService.UpdateQuoteTrelloAssociationAsync(quoteNumber, trelloCardId,
                trelloCardSnapshotJson, cancellationToken).ConfigureAwait(false);
            InvalidateQuoteNumbersCaches();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            HandleDatabaseException(nameof(UpdateQuoteTrelloAssociationAsync), ex);
            throw;
        }
    }

    public async Task<QuoteMetadata> UpdateQuoteTrelloAssociationAsync(string quoteNumber, string trelloCardId,
        string trelloCardSnapshotJson, long expectedRevision, CancellationToken cancellationToken = default)
    {
        SqlDataService.ValidateTrelloAssociation(trelloCardId, trelloCardSnapshotJson);
        await EnsureDatabaseRequiredAsync($"Associazione manuale Trello al preventivo {quoteNumber}", cancellationToken);
        try
        {
            var metadata = await _sqlService.UpdateQuoteTrelloAssociationAsync(quoteNumber, trelloCardId,
                trelloCardSnapshotJson, expectedRevision, cancellationToken).ConfigureAwait(false);
            InvalidateQuoteNumbersCaches();
            return metadata;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            HandleDatabaseException(nameof(UpdateQuoteTrelloAssociationAsync), ex);
            throw;
        }
    }

    public async Task<List<TrelloCard>> GetTrelloCardsAsync(CancellationToken cancellationToken = default)
    {
        if (IsDatabaseAvailable())
        {
            try
            {
                var cards = await _sqlService.GetTrelloCardsAsync(cancellationToken).ConfigureAwait(false);
                await TryCacheTrelloAsync(() => _localStore.SaveTrelloCardsAsync(cards, cancellationToken)).ConfigureAwait(false);
                return cards;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (IsDatabaseConnectivityException(ex))
            {
                HandleDatabaseException(nameof(GetTrelloCardsAsync), ex);
            }
        }

        var cached = await _localStore.LoadTrelloCardsAsync(cancellationToken).ConfigureAwait(false);
        if (cached == null)
            throw new InvalidOperationException("Schede Trello non disponibili: il database è offline e su questo PC manca una copia completa. Riprova con il database raggiungibile.");
        foreach (var card in cached) card.IsOfflineSnapshot = true;
        return cached;
    }

    public async Task<TrelloCard?> GetTrelloCardAsync(string cardId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cardId)) return null;
        if (IsDatabaseAvailable())
        {
            try
            {
                var card = await _sqlService.GetTrelloCardAsync(cardId, cancellationToken).ConfigureAwait(false);
                if (card != null)
                    await TryCacheTrelloAsync(() => _localStore.CacheTrelloCardAsync(card, cancellationToken)).ConfigureAwait(false);
                return card;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (IsDatabaseConnectivityException(ex))
            {
                HandleDatabaseException(nameof(GetTrelloCardAsync), ex);
            }
        }

        var cached = await _localStore.LoadTrelloCardAsync(cardId, cancellationToken).ConfigureAwait(false);
        if (cached == null)
            throw new InvalidOperationException("Scheda Trello non disponibile: il database è offline e questa scheda non è nella copia locale.");
        cached.IsOfflineSnapshot = true;
        return cached;
    }

    private static async Task TryCacheTrelloAsync(Func<Task> save)
    {
        try { await save().ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Debug.WriteLine($"[Trello] Copia locale non aggiornata: {ex.Message}");
        }
    }
}
