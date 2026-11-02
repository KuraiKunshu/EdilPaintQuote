using System.Diagnostics;
using System.IO;
using System.Text.Json;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public static class TrelloWorkSheetService
{
    private static readonly TimeSpan PhotoWaitTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PhotoPollInterval = TimeSpan.FromSeconds(2);
    private const int MaxPhotoBytes = 25 * 1024 * 1024;
    private const long MaxRequestPhotoBytes = 200L * 1024 * 1024;

    public static async Task<WorkSheetContext> CreateContextAsync(
        IDataService dataService,
        QuoteHistoryEntry quote,
        IEnumerable<Item> laborCatalog,
        IEnumerable<Customer> customers,
        WorkSheetOptions? options = null,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        TrelloCard? card = null;
        bool lookupFailed = false;
        if (!string.IsNullOrWhiteSpace(quote.TrelloCardId))
        {
            try
            {
                card = await dataService.GetTrelloCardAsync(quote.TrelloCardId, cancellationToken);
                lookupFailed = card == null || card.IsOfflineSnapshot;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Trello] Scheda lavoro: lettura non disponibile ({ex.GetType().Name}).");
                lookupFailed = true;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        var selected = ResolveSelectedCard(quote, card);
        if (!string.IsNullOrWhiteSpace(quote.TrelloCardId) && selected == null)
            throw new InvalidOperationException("La scheda Trello associata non è disponibile. Ricaricala dal database prima di generare la scheda lavoro: non è possibile verificare se mancano fotografie.");
        IReadOnlyList<TrelloWorkSheetPhoto> photos = [];
        if (selected != null)
        {
            string[] ids = GetExpectedPhotoIds(selected);
            if (ids.Length > 0)
            {
                progress?.Report($"Caricamento delle fotografie del sopralluogo ({ids.Length})…");
                photos = await LoadPhotosAsync(dataService, selected, ids, cancellationToken);
            }
        }
        return WorkSheetService.CreateContext(quote, laborCatalog, customers, options, card, lookupFailed, photos);
    }

    public static TrelloWorkSheetDetails? CreateDetails(QuoteHistoryEntry quote,
        TrelloCard? currentCard = null, bool lookupFailed = false,
        IReadOnlyList<TrelloWorkSheetPhoto>? photos = null)
    {
        // This method never establishes an association, even if names or contacts match.
        if (string.IsNullOrWhiteSpace(quote.TrelloCardId)) return null;
        var card = ResolveSelectedCard(quote, currentCard);
        if (card == null)
            return new TrelloWorkSheetDetails
            {
                CardId = quote.TrelloCardId,
                Warning = "Dati della scheda associata non disponibili."
            };
        bool usingSnapshot = !ReferenceEquals(card, currentCard) || lookupFailed || card.IsOfflineSnapshot;
        return new TrelloWorkSheetDetails
        {
            CardId = card.Id,
            DueUtc = card.DueUtc,
            Photos = photos ?? [],
            Warning = usingSnapshot ? "Copia del sopralluogo conservata su questo PC." : string.Empty
        };
    }

    private static TrelloCard? ResolveSelectedCard(QuoteHistoryEntry quote, TrelloCard? currentCard)
    {
        if (string.IsNullOrWhiteSpace(quote.TrelloCardId)) return null;
        if (string.Equals(currentCard?.Id, quote.TrelloCardId, StringComparison.Ordinal)) return currentCard;
        var snapshot = TrelloCard.FromSnapshot(quote.TrelloCardSnapshotJson);
        return string.Equals(snapshot?.Id, quote.TrelloCardId, StringComparison.Ordinal) ? snapshot : null;
    }

    internal static string[] GetExpectedPhotoIds(TrelloCard card)
    {
        if (string.IsNullOrWhiteSpace(card.PayloadJson))
            throw new InvalidDataException("I riferimenti alle fotografie del sopralluogo non sono disponibili. Ricarica la scheda dal database.");
        try
        {
            using var document = JsonDocument.Parse(card.PayloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("I riferimenti alle fotografie del sopralluogo non sono leggibili.");
            if (!document.RootElement.TryGetProperty("attachments", out var attachments))
                throw new InvalidDataException("L'elenco degli allegati del sopralluogo non è disponibile. Ricarica la scheda dal database.");
            if (attachments.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("I riferimenti alle fotografie del sopralluogo non sono leggibili.");
            var photoSizes = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var attachment in attachments.EnumerateArray())
            {
                if (attachment.ValueKind != JsonValueKind.Object ||
                    !attachment.TryGetProperty("isUpload", out var upload) || upload.ValueKind != JsonValueKind.True) continue;
                string mime = Text(attachment, "mimeType").Split(';', 2)[0].Trim().ToLowerInvariant();
                if (mime == "image/jpg") mime = "image/jpeg";
                string fileName = PhotoFileName(attachment);
                string extension = Path.GetExtension(fileName).ToLowerInvariant();
                bool supported = mime is "image/jpeg" or "image/png" or "image/webp" or "image/gif" or "image/bmp" ||
                    (mime.Length == 0 && extension is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".bmp");
                if (!supported)
                {
                    if (mime.StartsWith("image/", StringComparison.Ordinal) || extension is ".heic" or ".heif" or ".tif" or ".tiff")
                        throw new InvalidOperationException($"La fotografia «{fileName}» richiede la conversione in JPEG o PNG prima di essere inserita nella scheda lavoro.");
                    continue;
                }
                string id = Text(attachment, "id");
                if (string.IsNullOrWhiteSpace(id) || id.Length > 64)
                    throw new InvalidDataException("Una fotografia del sopralluogo ha un riferimento non valido.");
                if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains('/') || fileName.Contains('\\') || fileName.Any(char.IsControl))
                    throw new InvalidDataException("Una fotografia del sopralluogo ha un nome del file non valido.");
                if (!attachment.TryGetProperty("bytes", out var size) || size.ValueKind != JsonValueKind.Number ||
                    !size.TryGetInt64(out long bytes) || bytes <= 0)
                    throw new InvalidDataException($"La dimensione della fotografia «{fileName}» non è disponibile. Ricarica la scheda dal database.");
                if (bytes > MaxPhotoBytes)
                    throw new InvalidOperationException($"La fotografia «{fileName}» supera 25 MB. Riducila prima di generare la scheda lavoro.");
                photoSizes[id] = bytes;
            }
            if (photoSizes.Count > 100) throw new InvalidOperationException("La scheda associata contiene più di 100 fotografie: riduci il numero di allegati prima di generare la scheda lavoro.");
            if (photoSizes.Values.Sum() > MaxRequestPhotoBytes)
                throw new InvalidOperationException("Le fotografie del sopralluogo superano complessivamente 200 MB. Riducile prima di generare la scheda lavoro.");
            return photoSizes.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("I riferimenti alle fotografie del sopralluogo non sono leggibili. Ricarica la scheda dal database.", ex);
        }
    }

    private static string PhotoFileName(JsonElement attachment)
    {
        string fileName = Text(attachment, "fileName");
        if (!string.IsNullOrWhiteSpace(fileName)) return fileName;
        if (Uri.TryCreate(Text(attachment, "url"), UriKind.Absolute, out var source))
        {
            fileName = Uri.UnescapeDataString(source.AbsolutePath.Split('/').LastOrDefault() ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(fileName)) return fileName;
        }
        return Text(attachment, "name");
    }

    internal static async Task<IReadOnlyList<TrelloWorkSheetPhoto>> LoadPhotosAsync(
        IDataService dataService, TrelloCard card, string[] expectedIds, CancellationToken token)
    {
        var batch = await dataService.GetTrelloPhotosAsync(card.Id, token);
        if (IsReady(batch, card.Id, expectedIds)) return DecodePhotos(batch, expectedIds);
        if (batch.IsOfflineSnapshot || card.IsOfflineSnapshot)
            throw new InvalidOperationException("Le fotografie complete del sopralluogo non sono disponibili su questo PC. Collega il database e riprova.");
        await dataService.RequestTrelloPhotosAsync(card.Id, expectedIds, token);
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < PhotoWaitTimeout)
        {
            token.ThrowIfCancellationRequested();
            batch = await dataService.GetTrelloPhotosAsync(card.Id, token);
            if (IsReady(batch, card.Id, expectedIds)) return DecodePhotos(batch, expectedIds);
            if (batch.Status == "failed")
                throw new InvalidOperationException("Impossibile caricare tutte le fotografie del sopralluogo. " + batch.Error);
            await Task.Delay(PhotoPollInterval, token);
        }
        throw new InvalidOperationException("Le fotografie del sopralluogo sono ancora in preparazione. Riprova a generare la scheda lavoro fra poco; nessun PDF incompleto è stato salvato.");
    }

    private static bool IsReady(TrelloPhotoBatch batch, string cardId, string[] ids) =>
        string.Equals(batch.CardId, cardId, StringComparison.Ordinal) && batch.IsComplete &&
        ids.SequenceEqual(batch.GetExpectedAttachmentIds(), StringComparer.Ordinal);

    private static IReadOnlyList<TrelloWorkSheetPhoto> DecodePhotos(TrelloPhotoBatch batch, string[] ids)
    {
        var photos = new List<TrelloWorkSheetPhoto>();
        foreach (string id in ids)
        {
            var photo = batch.Photos.Single(x => x.CardId == batch.CardId && x.AttachmentId == id);
            byte[] bytes;
            try
            {
                if (photo.ContentBase64.Length > (long)MaxPhotoBytes * 4 / 3 + 4)
                    throw new InvalidDataException("Fotografia troppo grande.");
                bytes = Convert.FromBase64String(photo.ContentBase64);
            }
            catch (FormatException ex) { throw new InvalidDataException($"La fotografia «{photo.FileName}» non è leggibile.", ex); }
            if (bytes.Length == 0 || bytes.Length > MaxPhotoBytes || !IsRaster(bytes))
                throw new InvalidDataException($"La fotografia «{photo.FileName}» non è un'immagine utilizzabile nella scheda lavoro.");
            photos.Add(new(photo.FileName, bytes));
        }
        return photos;
    }

    private static bool IsRaster(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }) ||
        bytes.StartsWith(new byte[] { 0xff, 0xd8, 0xff }) || bytes.StartsWith("GIF87a"u8) ||
        bytes.StartsWith("GIF89a"u8) || bytes.StartsWith("BM"u8) ||
        (bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8));

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : string.Empty;
}
