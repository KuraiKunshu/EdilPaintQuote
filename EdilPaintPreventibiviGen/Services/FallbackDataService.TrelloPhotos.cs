using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public partial class FallbackDataService
{
    public async Task RequestTrelloPhotosAsync(string cardId, IReadOnlyList<string> expectedAttachmentIds,
        CancellationToken cancellationToken = default)
    {
        SqlDataService.ValidateTrelloPhotoCardId(cardId);
        _ = SqlDataService.SerializeTrelloPhotoIds(expectedAttachmentIds);
        await EnsureDatabaseRequiredAsync("Caricamento fotografie del sopralluogo", cancellationToken).ConfigureAwait(false);
        try
        {
            await _sqlService.RequestTrelloPhotosAsync(cardId, expectedAttachmentIds, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            HandleDatabaseException(nameof(RequestTrelloPhotosAsync), ex);
            throw;
        }
    }

    public async Task<TrelloPhotoBatch> GetTrelloPhotosAsync(string cardId, CancellationToken cancellationToken = default)
    {
        SqlDataService.ValidateTrelloPhotoCardId(cardId);
        if (IsDatabaseAvailable())
        {
            try
            {
                var batch = await _sqlService.GetTrelloPhotosAsync(cardId, cancellationToken).ConfigureAwait(false);
                if (batch.IsComplete)
                    await TryCacheTrelloAsync(() => _localStore.SaveTrelloPhotoBatchAsync(batch, cancellationToken)).ConfigureAwait(false);
                return batch;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (IsDatabaseConnectivityException(ex))
            {
                HandleDatabaseException(nameof(GetTrelloPhotosAsync), ex);
            }
        }
        var cached = await _localStore.LoadTrelloPhotoBatchAsync(cardId, cancellationToken).ConfigureAwait(false);
        if (cached == null)
            throw new InvalidOperationException("Fotografie del sopralluogo non disponibili: il database è offline e questo PC non dispone di una copia completa.");
        cached.IsOfflineSnapshot = true;
        return cached;
    }
}
