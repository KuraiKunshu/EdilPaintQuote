using System.Reflection;
using System.Text.Json;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class TrelloWorkSheetTests
{
    private const string OperationalMarker = "TRELLO_OPERATIVO_RISERVATO";

    private static TrelloCard Card(string name = OperationalMarker) => new()
    {
        Id = "64a111111111111111111111", Name = name, BoardName = "Sopralluoghi", ListName = "Da preventivare",
        Url = "https://trello.com/c/AbCd1234/sopralluogo", Description = OperationalMarker + " - proteggere l'accesso.",
        SyncedAtUtc = new DateTime(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc),
        DueUtc = new DateTime(2026, 11, 4, 9, 30, 0, DateTimeKind.Utc),
        PayloadJson = """
            {"address":"Via del Cantiere 12, Brescia","locationName":"Ingresso dal cortile",
             "customFields":[{"id":"phone","name":"Telefono referente","type":"text"},
                             {"id":"type","name":"Intervento","type":"list","options":[{"id":"roof","value":{"text":"Tetto"}}]}],
             "customFieldItems":[{"idCustomField":"phone","value":{"text":"030 123456"}},{"idCustomField":"type","idValue":"roof"}],
             "checklists":[{"name":"Rilievo","checkItems":[{"name":"Misura altezza","state":"incomplete","pos":2},
                                                          {"name":"Verifica accesso","state":"complete","pos":1}]}],
             "actions":[{"type":"commentCard","data":{"text":"COMMENTO_DA_NON_STAMPARE"}}]}
            """
    };

    private static QuoteHistoryEntry Quote(TrelloCard? card = null)
    {
        card ??= Card();
        return new()
        {
            QuoteNumber = "PROVA-TRELLO-2026", Date = new DateTime(2026, 11, 2),
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
    public void LinkedCardSuppliesSeparateOperationalFieldsWithoutChangingCustomerQuoteOrSchedule()
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
        Assert.Contains(details.Fields, x => x.Name == "Telefono referente" && x.Value == "030 123456");
        Assert.Contains(details.Fields, x => x.Name == "Intervento" && x.Value == "Tetto");
        Assert.Equal("Verifica accesso", Assert.Single(details.Checklists).Items[0].Name);
        Assert.True(details.Checklists[0].Items[0].IsComplete);
        Assert.DoesNotContain("COMMENTO_DA_NON_STAMPARE", JsonSerializer.Serialize(context));
        Assert.Equal(before, JsonSerializer.Serialize(quote));
    }

    [Fact]
    public void CurrentMatchingIdWinsOverSnapshotAndWrongIdIsNeverUsed()
    {
        var quote = Quote();
        var current = Card("Titolo aggiornato");
        Assert.Equal(current.Name, WorkSheetService.CreateContext(quote, [], [], trelloCard: current).Trello!.Name);
        Assert.Empty(WorkSheetService.CreateContext(quote, [], [], trelloCard: current).Trello!.Warning);
        current.Id = "another-card";
        var context = WorkSheetService.CreateContext(quote, [], [], trelloCard: current);
        Assert.Equal(OperationalMarker, context.Trello!.Name);
        Assert.Contains("Copia", context.Trello.Warning);
    }

    [Fact]
    public void MissingOrMismatchedSnapshotsAreExplicitAndDoNotFindCardsByName()
    {
        var quote = Quote();
        quote.TrelloCardId = "unavailable-id";
        var context = WorkSheetService.CreateContext(quote, [], []);
        Assert.Equal("unavailable-id", context.Trello!.CardId);
        Assert.Contains("non disponibili", context.Trello.Warning);
        Assert.Empty(context.Trello.Description);
        Assert.Empty(context.Trello.Url);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"customFieldItems\":[null,\"invalid\"],\"checklists\":[{\"checkItems\":[{\"name\":\"Rilievo\",\"pos\":\"invalid\"}]}]}")]
    public void MalformedOptionalPayloadCannotPreventWorkSheetGeneration(string payload)
    {
        var card = Card();
        card.PayloadJson = payload;
        var result = WorkSheetService.CreateContext(Quote(card), [], [], trelloCard: card);
        Assert.Equal(card.Description, result.Trello!.Description);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://trello.com.attacker.test/c/abc")]
    [InlineData("http://trello.com/c/abc")]
    public void UnsafeCardLinksAreNeverPrintedAsHyperlinks(string url)
    {
        var card = Card(); card.Url = url;
        Assert.Empty(WorkSheetService.CreateContext(Quote(card), [], [], trelloCard: card).Trello!.Url);
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
        Assert.Contains("Copia", context.Trello!.Warning);
        Assert.Equal(before, JsonSerializer.Serialize(quote));
        quote.TrelloCardId = string.Empty;
        Assert.Null((await TrelloWorkSheetService.CreateContextAsync(data, quote, [], [])).Trello);
        Assert.Single(fake.RequestedIds);
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
        Assert.DoesNotContain(typeof(PdfGenerationContext).GetProperties(), p => p.Name.Contains("Trello", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RendersOperationalWorkSheetForVerification()
    {
        string? requestedFolder = Environment.GetEnvironmentVariable("EDILPAINT_TRELLO_QA_DIR");
        string folder = requestedFolder ?? Path.Combine(Path.GetTempPath(), "EdilPaintTrelloPdf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var card = Card();
            var quote = Quote(card);
            var company = new Company { Nome = "EdilPaint" };
            var pdf = new PdfService();
            pdf.GenerateWorkSheet(WorkSheetService.CreateContext(quote, [], [], trelloCard: card), company, Path.Combine(folder, "scheda-lavoro-trello.pdf"));
            Assert.All(Directory.GetFiles(folder, "*.pdf"), file => Assert.True(new FileInfo(file).Length > 1000));
        }
        finally { if (requestedFolder == null) Directory.Delete(folder, recursive: true); }
    }

    public class TrelloLookupProxy : DispatchProxy
    {
        public bool Fail { get; set; }
        public List<string> RequestedIds { get; } = [];
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name != nameof(IDataService.GetTrelloCardAsync)) throw new InvalidOperationException("Unexpected data operation");
            RequestedIds.Add((string)arguments![0]!);
            return Fail ? Task.FromException<TrelloCard?>(new IOException("Unavailable")) : Task.FromResult<TrelloCard?>(Card());
        }
    }
}
