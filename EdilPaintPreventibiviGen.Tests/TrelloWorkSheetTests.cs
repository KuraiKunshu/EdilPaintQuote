using System.Reflection;
using System.Text.Json;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class TrelloWorkSheetTests
{
    private const string OperationalMarker = "TRELLO_OPERATIVO_RISERVATO";
    // A small, valid RGB PNG. No user photograph or network resource is used.
    private const string PhotoPng = "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAGCAIAAABxZ0isAAAAFUlEQVR4nGO4JCcHRxpRJ+CIYSAlAFxmOEFZLA6UAAAAAElFTkSuQmCC";

    private static TrelloCard Card(string name = OperationalMarker) => new()
    {
        Id = "64a111111111111111111111", Name = name, BoardName = "BACHECA_DA_NON_STAMPARE", ListName = "LISTA_DA_NON_STAMPARE",
        Url = "https://trello.com/c/AbCd1234/sopralluogo", Description = OperationalMarker + " - proteggere l'accesso.",
        SyncedAtUtc = new DateTime(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc),
        DueUtc = new DateTime(2026, 11, 4, 9, 30, 0, DateTimeKind.Utc),
        PayloadJson = """
            {"address":"INDIRIZZO_TRELLO_DA_NON_STAMPARE","locationName":"LUOGO_TRELLO_DA_NON_STAMPARE",
             "customFields":[{"id":"phone","name":"Telefono referente","type":"text"},
                             {"id":"type","name":"Intervento","type":"list","options":[{"id":"roof","value":{"text":"TIPO_DA_NON_STAMPARE"}}]}],
             "customFieldItems":[{"idCustomField":"phone","value":{"text":"030 123456"}},{"idCustomField":"type","idValue":"roof"}],
             "checklists":[{"name":"CHECKLIST_DA_NON_STAMPARE","checkItems":[{"name":"VOCE_DA_NON_STAMPARE","state":"incomplete","pos":2},
                                                                         {"name":"VERIFICA_DA_NON_STAMPARE","state":"complete","pos":1}]}],
             "actions":[{"type":"commentCard","data":{"text":"COMMENTO_DA_NON_STAMPARE"}}],"attachments":[]}
            """
    };

    private static QuoteHistoryEntry Quote(TrelloCard? card = null)
    {
        card ??= Card();
        return new()
        {
            QuoteNumber = "PROVA-2026", Date = new DateTime(2026, 11, 2),
            CustomerName = "Cliente scelto dall'operatore", CustomerSyncId = Guid.NewGuid(),
            SiteName = "Cantiere confermato nel preventivo", CustomerNotes = "Nota commerciale confermata",
            TrelloCardId = card.Id, TrelloCardSnapshotJson = card.SerializeSnapshot(),
            Labors = [new() { Name = "Tinteggiatura", Quantity = 25, UnitOfMeasure = "m²", UnitPrice = 12 }]
        };
    }

    [Fact]
    public void NoExplicitIdMeansNoAssociationEvenWithMatchingCustomerOrSavedSnapshot()
    {
        var card = Card();
        var quote = Quote(card);
        quote.CustomerName = card.Name;
        quote.TrelloCardId = string.Empty;
        Assert.Null(WorkSheetService.CreateContext(quote, [], [], trelloCard: card).Trello);
    }

    [Fact]
    public void LinkedCardSuppliesOnlyItsDueDateWithoutChangingCustomerQuoteOrWorkSchedule()
    {
        var card = Card();
        var quote = Quote(card);
        string before = JsonSerializer.Serialize(quote);
        var context = WorkSheetService.CreateContext(quote, [], [], new WorkSheetOptions
        { InterventionDate = new DateTime(2026, 11, 9), InterventionTime = "08:00" }, card);
        var details = Assert.IsType<TrelloWorkSheetDetails>(context.Trello);

        Assert.Equal(quote.CustomerName, context.CustomerName);
        Assert.Equal(quote.SiteName, context.WorkSite);
        Assert.Equal(new DateTime(2026, 11, 9), context.InterventionDate);
        Assert.Equal("08:00", context.InterventionTime);
        Assert.Equal(card.DueUtc, details.DueUtc);
        Assert.Empty(details.Photos);
        string json = JsonSerializer.Serialize(context);
        foreach (string forbidden in ForbiddenCardData(card)) Assert.DoesNotContain(forbidden, json);
        Assert.Equal(before, JsonSerializer.Serialize(quote));
    }

    [Fact]
    public void CurrentMatchingIdWinsOverSnapshotAndWrongIdIsNeverUsed()
    {
        var quote = Quote();
        var originalDue = TrelloCard.FromSnapshot(quote.TrelloCardSnapshotJson)!.DueUtc;
        var current = Card("Altro titolo non stampabile");
        current.DueUtc = originalDue!.Value.AddDays(2);
        var latest = WorkSheetService.CreateContext(quote, [], [], trelloCard: current).Trello!;
        Assert.Equal(current.DueUtc, latest.DueUtc);
        Assert.Empty(latest.Warning);

        current.Id = "another-card";
        var fallback = WorkSheetService.CreateContext(quote, [], [], trelloCard: current).Trello!;
        Assert.Equal(originalDue, fallback.DueUtc);
        Assert.NotEmpty(fallback.Warning);
    }

    [Fact]
    public void ARemovedDueDateDoesNotReappearFromTheOldSnapshotOrQuoteDate()
    {
        var quote = Quote();
        var current = Card();
        current.DueUtc = null;
        var context = WorkSheetService.CreateContext(quote, [], [], trelloCard: current);
        Assert.Null(context.Trello?.DueUtc);
        Assert.Equal(quote.Date, context.QuoteDate);
    }

    [Fact]
    public void CardsWithoutADueDateDoNotInventASurveyDate()
    {
        var card = Card();
        card.DueUtc = null;
        var context = WorkSheetService.CreateContext(Quote(card), [], [], trelloCard: card);
        Assert.Null(context.Trello?.DueUtc);
        Assert.Null(context.InterventionDate);
    }

    [Fact]
    public void MissingOrMismatchedSnapshotsCannotSupplyAnotherCardsDateOrPhotos()
    {
        var quote = Quote();
        quote.TrelloCardId = "unavailable-id";
        var details = WorkSheetService.CreateContext(quote, [], []).Trello!;
        Assert.Equal(quote.TrelloCardId, details.CardId);
        Assert.NotEmpty(details.Warning);
        Assert.Null(details.DueUtc);
        Assert.Empty(details.Photos);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"customFieldItems\":[null,\"invalid\"],\"checklists\":[{\"checkItems\":[{\"name\":\"Rilievo\",\"pos\":\"invalid\"}]}]}")]
    public void MalformedOptionalPayloadDoesNotPreventUsingTheExplicitDueDate(string payload)
    {
        var card = Card();
        card.PayloadJson = payload;
        var details = WorkSheetService.CreateContext(Quote(card), [], [], trelloCard: card).Trello!;
        Assert.Equal(card.DueUtc, details.DueUtc);
        Assert.Empty(details.Photos);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://trello.com.attacker.test/c/abc")]
    [InlineData("https://trello.com/c/abc")]
    public void NeitherUnsafeNorValidCardLinksEnterTheWorkSheetPrintData(string url)
    {
        var card = Card(); card.Url = url;
        var context = WorkSheetService.CreateContext(Quote(card), [], [], trelloCard: card);
        Assert.DoesNotContain(url, JsonSerializer.Serialize(context));
        Assert.DoesNotContain(typeof(TrelloWorkSheetDetails).GetProperties(), property => property.Name is "Url" or "Name" or "Description" or "BoardName" or "ListName" or "Checklists" or "Fields" or "SyncedAtUtc");
    }

    [Fact]
    public async Task LookupUsesOnlyTheExplicitIdAndFallsBackWithoutMutatingQuote()
    {
        var data = DispatchProxy.Create<IDataService, TrelloLookupProxy>();
        var fake = (TrelloLookupProxy)data;
        var quote = Quote();
        string before = JsonSerializer.Serialize(quote);
        fake.Fail = true;
        var context = await TrelloWorkSheetService.CreateContextAsync(data, quote, [], []);
        Assert.Equal(quote.TrelloCardId, Assert.Single(fake.RequestedIds));
        Assert.NotEmpty(context.Trello!.Warning);
        Assert.Equal(before, JsonSerializer.Serialize(quote));
        quote.TrelloCardId = string.Empty;
        Assert.Null((await TrelloWorkSheetService.CreateContextAsync(data, quote, [], [])).Trello);
        Assert.Single(fake.RequestedIds);
        Assert.Equal(nameof(IDataService.GetTrelloCardAsync), Assert.Single(fake.Calls));
    }

    [Fact]
    public async Task ACardWithoutPhotoAttachmentsDoesNotEnqueueOrReadPhotoJobs()
    {
        var data = DispatchProxy.Create<IDataService, TrelloLookupProxy>();
        var fake = (TrelloLookupProxy)data;
        var context = await TrelloWorkSheetService.CreateContextAsync(data, Quote(), [], []);
        Assert.Empty(context.Trello!.Photos);
        Assert.Equal(nameof(IDataService.GetTrelloCardAsync), Assert.Single(fake.Calls));
    }

    [Fact]
    public async Task CancellationIsNotHiddenBySnapshotFallback()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var data = DispatchProxy.Create<IDataService, TrelloLookupProxy>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            TrelloWorkSheetService.CreateContextAsync(data, Quote(), [], [], cancellationToken: cancellation.Token));
    }

    [Fact]
    public void CommercialPdfContextExcludesAllTrelloFieldsFromSavedQuote()
    {
        var quote = Quote();
        var context = QuotePdfDocumentService.CreateContext(quote, new(), [], "", new());
        string json = JsonSerializer.Serialize(context);
        Assert.Equal(quote.QuoteNumber, context.QuoteNumber);
        Assert.Equal(quote.CustomerNotes, context.CustomerNotes);
        foreach (string forbidden in new[] { "Trello", "trello.com", OperationalMarker, quote.TrelloCardId, "030 123456" })
            Assert.DoesNotContain(forbidden, json);
        Assert.DoesNotContain(typeof(PdfGenerationContext).GetProperties(), property => property.Name.Contains("Trello", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(true, 0, false, 1)]
    [InlineData(true, 1, false, 1)]
    [InlineData(false, 2, false, 1)]
    [InlineData(true, 5, true, 1)]
    [InlineData(false, 2, false, 2)]
    [InlineData(true, 5, true, 2)]
    public void RendersOnlySurveyDateAndEmbeddedPhotographs(bool hasDueDate, int photoCount, bool longFileName, int photosPerPage)
    {
        string? requestedFolder = Environment.GetEnvironmentVariable("EDILPAINT_TRELLO_QA_DIR");
        string folder = requestedFolder ?? Path.Combine(Path.GetTempPath(), "EdilPaintTrelloPdf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string name = longFileName ? new string('f', 350) + ".png" : "fotografia.png";
            var photos = Enumerable.Range(0, photoCount).Select(_ =>
                new TrelloWorkSheetPhoto(name, Convert.FromBase64String(PhotoPng))).ToArray();
            var context = new WorkSheetContext
            {
                QuoteNumber = "PROVA-2026", QuoteDate = new DateTime(2026, 11, 2), CustomerName = "Cliente dimostrativo",
                PhotosPerPage = photosPerPage,
                Trello = new TrelloWorkSheetDetails
                {
                    CardId = "ID_INTERNO_DA_NON_STAMPARE", DueUtc = hasDueDate ? Card().DueUtc : null,
                    Warning = "AVVISO_INTERNO_DA_NON_STAMPARE", Photos = photos
                }
            };
            string path = Path.Combine(folder, $"scheda-foto-{photoCount}-data-{hasDueDate}-per-pagina-{photosPerPage}.pdf");
            new PdfService().GenerateWorkSheet(context, new Company { Nome = "EdilPaint" }, path);
            Assert.True(new FileInfo(path).Length > 1000);
        }
        finally { if (requestedFolder == null) Directory.Delete(folder, recursive: true); }
    }

    private static IEnumerable<string> ForbiddenCardData(TrelloCard card) =>
    [
        card.Name, card.Description, card.BoardName, card.ListName, card.Url,
        "INDIRIZZO_TRELLO_DA_NON_STAMPARE", "LUOGO_TRELLO_DA_NON_STAMPARE", "CHECKLIST_DA_NON_STAMPARE",
        "VOCE_DA_NON_STAMPARE", "VERIFICA_DA_NON_STAMPARE", "COMMENTO_DA_NON_STAMPARE", "TIPO_DA_NON_STAMPARE", "030 123456"
    ];

    public class TrelloLookupProxy : DispatchProxy
    {
        public bool Fail { get; set; }
        public List<string> RequestedIds { get; } = [];
        public List<string> Calls { get; } = [];
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            Calls.Add(method?.Name ?? string.Empty);
            if (method?.Name != nameof(IDataService.GetTrelloCardAsync)) throw new InvalidOperationException("Unexpected data operation");
            RequestedIds.Add((string)arguments![0]!);
            return Fail ? Task.FromException<TrelloCard?>(new IOException("Unavailable")) : Task.FromResult<TrelloCard?>(Card());
        }
    }
}
