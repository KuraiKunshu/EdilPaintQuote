using System.Reflection;
using System.Text.Json;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class TrelloPhotoLoadingTests
{
    private const string CardId = "64a111111111111111111111";
    private const string PhotoPng = "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAGCAIAAABxZ0isAAAAFUlEQVR4nGO4JCcHRxpRJ+CIYSAlAFxmOEFZLA6UAAAAAElFTkSuQmCC";
    private const int MaxPhotoBytes = 25 * 1024 * 1024;

    [Fact]
    public void ExpectedPhotosIncludeOnlyUploadedRasterImagesAndHaveCanonicalIds()
    {
        var attachments = new object[]
        {
            Attachment("photo-b", "image/jpeg", "immagine.dat"),
            Attachment("photo-a", "", "FOTOGRAFIA.PNG"),
            Attachment("photo-a", "image/png", "copia.png"),
            Attachment("photo-webp", "", "immagine.webp"),
            Attachment("photo-gif", "image/gif", "immagine.gif"),
            Attachment("photo-bmp", "image/bmp", "immagine.bmp"),
            Attachment("document", "application/pdf", "rilievo.pdf"),
            Attachment("external-photo", "image/png", "esterna.png", isUpload: false),
            new { id = "external-without-upload-flag", mimeType = "image/jpeg", name = "immagine.jpg", url = "https://outside.example.test/private.jpg" }
        };
        var card = Card(attachments);

        Assert.Equal(new[] { "photo-a", "photo-b", "photo-bmp", "photo-gif", "photo-webp" },
            TrelloWorkSheetService.GetExpectedPhotoIds(card));
    }

    [Theory]
    [InlineData("{\"attachments\":[]}")]
    public void CardsWithoutAttachmentsHaveNoExpectedPhotographs(string payload)
    {
        var card = new TrelloCard { Id = CardId, PayloadJson = payload };
        Assert.Empty(TrelloWorkSheetService.GetExpectedPhotoIds(card));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    public void MissingAttachmentMetadataCannotSilentlyOmitPhotographs(string payload)
    {
        var card = new TrelloCard { Id = CardId, PayloadJson = payload };
        Assert.Throws<InvalidDataException>(() => TrelloWorkSheetService.GetExpectedPhotoIds(card));
    }

    [Theory]
    [InlineData("image/jpg")]
    [InlineData("IMAGE/JPG; charset=binary")]
    [InlineData(" IMAGE/JPEG ; charset=binary")]
    public void JpegAliasesAndMimeParametersRemainPrintablePhotographs(string mime)
    {
        Assert.Single(TrelloWorkSheetService.GetExpectedPhotoIds(Card([Attachment("photo-a", mime, "foto.jpg")])));
    }

    [Fact]
    public void CanonicalDownloadFilenamePrecedesRenamedLabelsAndLegacyNames()
    {
        var card = Card([
            new { id = "canonical", isUpload = true, mimeType = "", fileName = "originale.JPG", name = "Nome rinominato", bytes = 100,
                url = "https://trello.com/1/cards/card/attachments/attachment/download/non-foto.pdf" },
            new { id = "from-url", isUpload = true, mimeType = "", fileName = "", name = "Nome rinominato senza estensione", bytes = 100,
                url = "https://trello.com/1/cards/card/attachments/attachment/download/foto%20originale.png" },
            new { id = "legacy", isUpload = true, mimeType = "", fileName = "", name = "vecchia.jpg", bytes = 100, url = "" }
        ]);
        Assert.Equal(new[] { "canonical", "from-url", "legacy" }, TrelloWorkSheetService.GetExpectedPhotoIds(card));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"attachments\":{}}")]
    [InlineData("{\"attachments\":null}")]
    public void MalformedAttachmentListsCannotBeTreatedAsACompleteEmptyPhotoSet(string payload)
    {
        var card = new TrelloCard { Id = CardId, PayloadJson = payload };
        Assert.Throws<InvalidDataException>(() => TrelloWorkSheetService.GetExpectedPhotoIds(card));
    }

    [Theory]
    [InlineData("image/heic", "immagine.heic")]
    [InlineData("", "immagine.heif")]
    [InlineData("image/tiff", "immagine.tiff")]
    [InlineData("image/svg+xml", "immagine.svg")]
    public void UnsupportedImageFormatsAreReportedInsteadOfSilentlyOmitted(string mime, string fileName)
    {
        var card = Card([Attachment("image-a", mime, fileName)]);
        var error = Assert.Throws<InvalidOperationException>(() => TrelloWorkSheetService.GetExpectedPhotoIds(card));
        Assert.Contains(fileName, error.Message);
        Assert.Contains("JPEG o PNG", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("12345678901234567890123456789012345678901234567890123456789012345")]
    public void UploadedPhotosWithoutAValidAttachmentIdAreRejected(string id)
    {
        var card = Card([Attachment(id, "image/png", "foto.png")]);
        Assert.Throws<InvalidDataException>(() => TrelloWorkSheetService.GetExpectedPhotoIds(card));
    }

    [Fact]
    public void PhotoSizeAndCountLimitsFailBeforeAnyBinaryDownload()
    {
        Assert.Single(TrelloWorkSheetService.GetExpectedPhotoIds(Card([
            Attachment("limit-image", "image/png", "foto.png", bytes: MaxPhotoBytes)
        ])));
        Assert.Throws<InvalidOperationException>(() => TrelloWorkSheetService.GetExpectedPhotoIds(Card([
            Attachment("oversized-image", "image/png", "foto.png", bytes: MaxPhotoBytes + 1L)
        ])));

        var hundred = Enumerable.Range(0, 100).Select(i => Attachment("image-" + i, "image/png", "foto.png")).ToArray();
        Assert.Equal(100, TrelloWorkSheetService.GetExpectedPhotoIds(Card(hundred)).Length);
        Assert.Throws<InvalidOperationException>(() => TrelloWorkSheetService.GetExpectedPhotoIds(Card(
            hundred.Append(Attachment("image-extra", "image/png", "foto.png")).ToArray())));
    }

    [Fact]
    public void TotalPhotoSizeCannotExceedTheWorkersTwoHundredMegabyteLimit()
    {
        var photos = Enumerable.Range(0, 8).Select(i => Attachment("photo-" + i, "image/jpeg", "foto.jpg", bytes: MaxPhotoBytes)).ToArray();
        Assert.Equal(8, TrelloWorkSheetService.GetExpectedPhotoIds(Card(photos)).Length);
        var error = Assert.Throws<InvalidOperationException>(() => TrelloWorkSheetService.GetExpectedPhotoIds(
            Card(photos.Append(Attachment("overflow", "image/jpeg", "foto.jpg", bytes: 1)))));
        Assert.Contains("200 MB", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnExplicitAssociationWithoutACorrespondingCardCannotSilentlyOmitItsPhotographs(bool mismatchedSnapshot)
    {
        var data = Fake(out var fake);
        fake.Card = new TrelloCard { Id = "another-card" };
        var quote = new QuoteHistoryEntry
        {
            TrelloCardId = CardId,
            TrelloCardSnapshotJson = mismatchedSnapshot ? fake.Card.SerializeSnapshot() : string.Empty
        };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            TrelloWorkSheetService.CreateContextAsync(data, quote, [], []));
        Assert.Contains("non è disponibile", error.Message);
        Assert.Equal(nameof(IDataService.GetTrelloCardAsync), Assert.Single(fake.Calls));
    }

    [Fact]
    public async Task ACompleteMatchingBatchIsDecodedWithoutEnqueuingAnotherRequest()
    {
        var data = Fake(out var fake, CompleteBatch("image-a", "image-b"));
        var result = await TrelloWorkSheetService.LoadPhotosAsync(data, Card(), ["image-a", "image-b"], default);

        Assert.Equal(2, result.Count);
        Assert.All(result, photo => Assert.Equal(Convert.FromBase64String(PhotoPng), photo.Content));
        Assert.Equal(new[] { "image-a.png", "image-b.png" }, result.Select(photo => photo.FileName));
        Assert.Equal(nameof(IDataService.GetTrelloPhotosAsync), Assert.Single(fake.Calls));
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task CompleteOfflinePhotoBytesCanBePrintedWithoutAnyRequest()
    {
        var batch = CompleteBatch("image-a"); batch.IsOfflineSnapshot = true;
        var card = Card(); card.IsOfflineSnapshot = true;
        var data = Fake(out var fake, batch);

        Assert.Single(await TrelloWorkSheetService.LoadPhotosAsync(data, card, ["image-a"], default));
        Assert.Empty(fake.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnotherCardOrAnotherManifestCannotSupplyTheSelectedCardsPhotographs(bool anotherCard)
    {
        var stale = CompleteBatch("old-image");
        if (anotherCard)
        {
            stale = CompleteBatch("image-a");
            stale.CardId = "another-card";
            stale.Photos = [Photo("image-a", cardId: stale.CardId)];
        }
        var data = Fake(out var fake, stale, CompleteBatch("image-a"));

        var photo = Assert.Single(await TrelloWorkSheetService.LoadPhotosAsync(data, Card(), ["image-a"], default));

        Assert.Equal("image-a.png", photo.FileName);
        var request = Assert.Single(fake.Requests);
        Assert.Equal(CardId, request.CardId);
        Assert.Equal(new[] { "image-a" }, request.Ids);
        Assert.Equal(new[] { nameof(IDataService.GetTrelloPhotosAsync), nameof(IDataService.RequestTrelloPhotosAsync),
            nameof(IDataService.GetTrelloPhotosAsync) }, fake.Calls);
    }

    [Fact]
    public async Task AFailedWorkerJobProducesAnExplicitErrorInsteadOfAPartialPhotoSet()
    {
        var data = Fake(out var fake,
            new TrelloPhotoBatch { CardId = CardId },
            new TrelloPhotoBatch { CardId = CardId, Status = "failed", Error = "Download foto negato da Trello" });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            TrelloWorkSheetService.LoadPhotosAsync(data, Card(), ["image-a"], default));

        Assert.Contains("tutte le fotografie", error.Message);
        Assert.Contains("Download foto negato da Trello", error.Message);
        Assert.Single(fake.Requests);
    }

    [Theory]
    [InlineData("%%%not-base64%%%")]
    [InlineData("PGh0bWw+PHRpdGxlPkZvcmJpZGRlbjwvdGl0bGU+PC9odG1sPg==")]
    public async Task CorruptBase64OrHtmlDisguisedAsAJpegCannotBecomeAPrintedPhotograph(string contentBase64)
    {
        var batch = CompleteBatch("image-a");
        var photo = Photo("image-a"); photo.ContentType = "image/jpeg"; photo.ContentBase64 = contentBase64;
        batch.Photos = [photo];
        var data = Fake(out var fake, batch);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            TrelloWorkSheetService.LoadPhotosAsync(data, Card(), ["image-a"], default));

        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task AnIncompleteOfflineBatchCannotBeQueuedOrPrintedAsIfComplete()
    {
        var batch = CompleteBatch("image-a", "image-b");
        batch.Photos = [Photo("image-a")];
        batch.IsOfflineSnapshot = true;
        var data = Fake(out var fake, batch);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            TrelloWorkSheetService.LoadPhotosAsync(data, Card(), ["image-a", "image-b"], default));

        Assert.Contains("non sono disponibili", error.Message);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task CompletedStatusWithAMissingPhotographNeverReturnsAnIncompleteResult()
    {
        var incomplete = CompleteBatch("image-a", "image-b");
        incomplete.Photos = [Photo("image-a")];
        var data = Fake(out var fake, incomplete);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            TrelloWorkSheetService.LoadPhotosAsync(data, Card(), ["image-a", "image-b"], cancellation.Token));

        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task CardsWithNoPhotographsDoNotReadOrEnqueuePhotoJobs()
    {
        var data = Fake(out var fake);
        fake.Card = Card([Attachment("pdf", "application/pdf", "rilievo.pdf")]);
        var quote = new QuoteHistoryEntry { TrelloCardId = CardId, TrelloCardSnapshotJson = fake.Card.SerializeSnapshot() };

        var result = await TrelloWorkSheetService.CreateContextAsync(data, quote, [], []);

        Assert.Empty(result.Trello!.Photos);
        Assert.Equal(nameof(IDataService.GetTrelloCardAsync), Assert.Single(fake.Calls));
        Assert.Empty(fake.Requests);
    }

    private static TrelloCard Card(IEnumerable<object>? attachments = null) => new()
    {
        Id = CardId,
        PayloadJson = JsonSerializer.Serialize(new { attachments = attachments ?? [] })
    };

    private static object Attachment(string id, string mimeType, string fileName, bool isUpload = true, long bytes = 128) =>
        new { id, mimeType, fileName, isUpload, bytes, url = "https://outside.example.test/do-not-download-directly" };

    private static TrelloPhoto Photo(string id, string cardId = CardId) => new()
    {
        CardId = cardId, AttachmentId = id, FileName = id + ".png", ContentType = "image/png", ContentBase64 = PhotoPng
    };

    private static TrelloPhotoBatch CompleteBatch(params string[] ids) => new()
    {
        CardId = CardId, Status = "completed", ExpectedAttachmentIdsJson = JsonSerializer.Serialize(ids),
        Photos = ids.Select(id => Photo(id)).ToArray()
    };

    private static IDataService Fake(out PhotoDataProxy proxy, params TrelloPhotoBatch[] batches)
    {
        var data = DispatchProxy.Create<IDataService, PhotoDataProxy>();
        proxy = (PhotoDataProxy)data;
        proxy.Batches = new Queue<TrelloPhotoBatch>(batches);
        return data;
    }

    public class PhotoDataProxy : DispatchProxy
    {
        public Queue<TrelloPhotoBatch> Batches { get; set; } = new();
        public TrelloPhotoBatch? LastBatch { get; private set; }
        public TrelloCard Card { get; set; } = TrelloPhotoLoadingTests.Card();
        public List<string> Calls { get; } = [];
        public List<(string CardId, string[] Ids)> Requests { get; } = [];

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Calls.Add(method!.Name);
            switch (method.Name)
            {
                case nameof(IDataService.GetTrelloCardAsync):
                    Assert.Equal(CardId, args![0]);
                    return Task.FromResult<TrelloCard?>(Card);
                case nameof(IDataService.GetTrelloPhotosAsync):
                    Assert.Equal(CardId, args![0]);
                    if (Batches.Count > 0) LastBatch = Batches.Dequeue();
                    return Task.FromResult(LastBatch ?? new TrelloPhotoBatch { CardId = CardId });
                case nameof(IDataService.RequestTrelloPhotosAsync):
                    Requests.Add(((string)args![0]!, ((IReadOnlyList<string>)args[1]!).ToArray()));
                    return Task.CompletedTask;
                default:
                    throw new InvalidOperationException("Unexpected data operation: " + method.Name);
            }
        }
    }
}
