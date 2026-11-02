using System.Windows;
using EdilPaintPreventibiviGen.Views;

namespace EdilPaintPreventibiviGen.Services;

/// <summary>Explicit operator actions only; synchronization never calls this class.</summary>
public static class TrelloAssociationActions
{
    public static async Task ManageAsync(string quoteNumber, Window owner, CancellationToken token = default)
    {
        var quote = await App.DataService.GetQuoteByNumberAsync(quoteNumber, token, includeAttachments: false)
            ?? throw new InvalidOperationException("Preventivo non trovato nello storico.");
        token.ThrowIfCancellationRequested();
        var picker = new TrelloCardsWindow(quote.TrelloCardId, allowRemove: true)
        {
            Owner = owner,
            Title = $"Scheda Trello — preventivo {quoteNumber}"
        };
        if (picker.ShowDialog() != true) return;
        token.ThrowIfCancellationRequested();
        if (!picker.RemoveRequested && picker.SelectedResult == null) return;
        string cardId = picker.RemoveRequested ? string.Empty : picker.SelectedResult!.Id;
        string snapshot = picker.RemoveRequested ? string.Empty : picker.SelectedResult!.SerializeSnapshot();
        var metadata = await App.DataService.UpdateQuoteTrelloAssociationAsync(
            quoteNumber, cardId, snapshot, quote.Revision, token);
        App.MainVm?.ApplyPersistedTrelloAssociation(quoteNumber, cardId, snapshot, quote.Revision, metadata);
    }
}
