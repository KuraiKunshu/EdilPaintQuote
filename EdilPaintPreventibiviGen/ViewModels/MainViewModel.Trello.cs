using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;

namespace EdilPaintPreventibiviGen.ViewModels;

public partial class MainViewModel
{
    private string _trelloCardId = string.Empty;
    private string _trelloCardSnapshotJson = string.Empty;
    private TrelloCard? _associatedTrelloCard;

    public string TrelloCardId => _trelloCardId;
    public string TrelloCardSnapshotJson => _trelloCardSnapshotJson;
    public bool HasTrelloCardAssociation => !string.IsNullOrWhiteSpace(_trelloCardId);
    public string TrelloCardTitle => !HasTrelloCardAssociation
        ? "Nessuna scheda Trello associata"
        : string.IsNullOrWhiteSpace(_associatedTrelloCard?.Name)
            ? $"Scheda Trello: {_trelloCardId}"
            : _associatedTrelloCard.Name;
    public string TrelloCardAssociationDetails => !HasTrelloCardAssociation
        ? "Scegli la scheda da collegare al preventivo. Il cliente va scelto separatamente."
        : $"{_associatedTrelloCard?.BoardName} / {_associatedTrelloCard?.ListName} · " +
          (_associatedTrelloCard?.SyncedAtUtc is { } synced && synced != default
              ? $"Dati sincronizzati il {synced.ToLocalTime():dd/MM/yyyy HH:mm}"
              : "Copia conservata nel preventivo");
    public string TrelloCardUrl => _associatedTrelloCard?.Url ?? string.Empty;

    public async Task SetManualTrelloAssociationAsync(TrelloCard? card, CancellationToken token = default)
    {
        if (card != null && (string.IsNullOrWhiteSpace(card.Id) || card.IsDeleted))
            throw new ArgumentException("Seleziona una scheda Trello disponibile.", nameof(card));
        await _draftSaveLock.WaitAsync(token);
        try
        {
            if (!_isEditingExistingQuote || !_hasPersistedCurrentQuote)
            {
                if (card == null) ClearTrelloCardAssociation();
                else AssociateTrelloCard(card);
                return;
            }

            string quoteNumber = QuoteNumber;
            long expectedRevision = _loadedQuoteBaseRevision;
            string id = card?.Id ?? string.Empty;
            string snapshot = card?.SerializeSnapshot() ?? string.Empty;
            var metadata = await _dataService.UpdateQuoteTrelloAssociationAsync(
                quoteNumber, id, snapshot, expectedRevision, token);
            ApplyPersistedTrelloAssociation(quoteNumber, id, snapshot, expectedRevision, metadata);
        }
        finally { _draftSaveLock.Release(); }
    }

    public void ApplyPersistedTrelloAssociation(string quoteNumber, string cardId, string snapshotJson,
        long expectedRevision, QuoteMetadata metadata)
    {
        if (_isEditingExistingQuote && _hasPersistedCurrentQuote &&
            string.Equals(QuoteNumber, quoteNumber, StringComparison.Ordinal))
        {
            bool editorWasUnchanged = string.Equals(_lastSharedDraftContentHash,
                ComputeSharedDraftContentHash(CreateDraftEntry()), StringComparison.Ordinal);
            RestoreTrelloCardAssociation(cardId, snapshotJson);
            // Metadata comes from the atomic patch, never from a later read that could
            // silently adopt another operator's commercial changes.
            if (_loadedQuoteBaseRevision == expectedRevision)
            {
                _loadedQuoteBaseRevision = metadata.Revision;
                _loadedQuoteBaseVersionUtc = metadata.LastModifiedUtc;
                if (editorWasUnchanged)
                    _lastSharedDraftContentHash = ComputeSharedDraftContentHash(CreateDraftEntry());
            }
        }

        var history = History.FirstOrDefault(quote => quote.QuoteNumber == quoteNumber);
        if (history != null)
        {
            history.TrelloCardId = cardId;
            history.TrelloCardSnapshotJson = snapshotJson;
            history.Revision = history.BaseRevision = metadata.Revision;
            history.LastModifiedUtc = history.BaseVersionUtc = metadata.LastModifiedUtc;
            history.SyncHash = metadata.SyncHash;
        }
    }

    /// <summary>Called only after the operator explicitly confirms a selected card.</summary>
    public void AssociateTrelloCard(TrelloCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (string.IsNullOrWhiteSpace(card.Id) || card.IsDeleted)
            throw new ArgumentException("Seleziona una scheda Trello disponibile.", nameof(card));

        RestoreTrelloCardAssociation(card.Id, card.SerializeSnapshot());
    }

    public void ClearTrelloCardAssociation() => RestoreTrelloCardAssociation(string.Empty, string.Empty);

    private void RestoreTrelloCardAssociation(string? cardId, string? snapshotJson)
    {
        _trelloCardId = cardId?.Trim() ?? string.Empty;
        _trelloCardSnapshotJson = HasTrelloCardAssociation ? snapshotJson ?? string.Empty : string.Empty;
        _associatedTrelloCard = null;
        if (!string.IsNullOrWhiteSpace(_trelloCardSnapshotJson))
        {
            var snapshot = TrelloCard.FromSnapshot(_trelloCardSnapshotJson);
            if (string.Equals(snapshot?.Id, _trelloCardId, StringComparison.Ordinal))
                _associatedTrelloCard = snapshot;
        }

        OnPropertyChanged(nameof(TrelloCardId));
        OnPropertyChanged(nameof(TrelloCardSnapshotJson));
        OnPropertyChanged(nameof(HasTrelloCardAssociation));
        OnPropertyChanged(nameof(TrelloCardTitle));
        OnPropertyChanged(nameof(TrelloCardAssociationDetails));
        OnPropertyChanged(nameof(TrelloCardUrl));
    }
}
