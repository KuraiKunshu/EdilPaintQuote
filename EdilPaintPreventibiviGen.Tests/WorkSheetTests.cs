using System.Reflection;
using System.Text;
using System.Text.Json;
using EdilPaintPreventibiviGen.Data;
using EdilPaintPreventibiviGen.Data.Entities;
using EdilPaintPreventibiviGen.Data.Mappers;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using EdilPaintPreventibiviGen.ViewModels;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class WorkSheetTests
{
    [Fact]
    public void CatalogExclusionsApplyToOldQuotesAndRenamedLaborsWithoutChangingQuote()
    {
        var quote = new QuoteHistoryEntry { Labors =
        [
            new() { PersistentId = 12, Name = "Vecchio nome", UnitPrice = 75 },
            new() { Name = "  DIRITTO FISSO DI USCITA  ", UnitPrice = 50 },
            new() { Name = "Posa", Quantity = 3, UnitPrice = 120 }
        ] };
        var catalog = new[] { new Item { PersistentId = 12, Name = "Nuovo nome", ExcludeFromWorkSheet = true },
            new Item { Name = "Diritto fisso di uscita", ExcludeFromWorkSheet = true } };
        string original = JsonSerializer.Serialize(quote);
        WorkSheetContext result = WorkSheetService.CreateContext(quote, catalog, []);
        Assert.Equal("Posa", Assert.Single(result.Labors).Name);
        Assert.Equal(3, result.Labors[0].Quantity);
        Assert.Equal(original, JsonSerializer.Serialize(quote));
    }

    [Fact]
    public void ExplicitIncludedIdWinsOverNamesAndCustomLaborsArePreservedInQuoteOrder()
    {
        var quote = new QuoteHistoryEntry { Labors =
        [
            new() { PersistentId = 8, Name = "Uscita", SortOrder = 2 },
            new() { Name = "Lavoro su misura", SortOrder = 1 },
            new() { Name = "Quantita zero", Quantity = 0 }, new() { Name = "" }
        ] };
        var catalog = new[] { new Item { PersistentId = 8, Name = "Uscita tecnica" },
            new Item { PersistentId = 9, Name = "Uscita", ExcludeFromWorkSheet = true } };
        Assert.Equal(new[] { "Lavoro su misura", "Uscita" }, WorkSheetService.CreateContext(quote, catalog, []).Labors.Select(x => x.Name));
    }

    [Fact]
    public void MissingExclusionDoesNotHideChargesAutomatically()
    {
        var quote = new QuoteHistoryEntry { Labors = [new() { Name = "Diritto fisso di uscita" }] };
        Assert.Single(WorkSheetService.CreateContext(quote, [], []).Labors);
    }

    [Fact]
    public void ContextContainsNoFinancialOrPrivateFields()
    {
        var quote = SampleQuote();
        quote.Notes = "NOTA_INTERNA_RISERVATA";
        quote.CustomerNotes = "NOTA_COMMERCIALE_RISERVATA";
        quote.PaymentTerms = "CONDIZIONI_PAGAMENTO";
        quote.Labors[0].UnitPrice = 12345.67;
        string json = JsonSerializer.Serialize(WorkSheetService.CreateContext(quote, [], []));
        foreach (string prohibited in new[] { "UnitPrice", "Discount", "Total", "RealProfit", "RISERVATA", "CONDIZIONI_PAGAMENTO", "12345" })
            Assert.DoesNotContain(prohibited, json);
    }

    [Fact]
    public void CustomerOrderedMaterialsKeepStatusAndDoNotChangeSupplierAnagraphics()
    {
        var quote = SampleQuote();
        quote.MaterialsOrderedByCustomer = true;
        var customer = new Customer { BusinessName = quote.CustomerName, Phone = "030 000000", Address = "Indirizzo cliente" };
        var context = WorkSheetService.CreateContext(quote, [], [customer]);
        Assert.Equal(quote.CustomerName, context.SupplierName);
        Assert.True(context.MaterialsOrderedByCustomer);
        Assert.Equal("IN MAGAZZINO", context.MaterialStatus);
        Assert.Equal(quote.ExpectedDeliveryDate, context.ExpectedDeliveryDate);
        Assert.False(customer.IsSupplier);
        Assert.Equal("Fornitore di prova", quote.SupplierName);
    }

    [Fact]
    public void ReferenceContactIsResolvedByStableIdAndSiteRemainsExplicit()
    {
        var quote = SampleQuote();
        quote.ReferenceCustomerSyncId = Guid.NewGuid();
        var reference = new Customer { SyncId = quote.ReferenceCustomerSyncId, BusinessName = "Nome aggiornato", Address = "Via riferimento", Phone = "030 111111" };
        var context = WorkSheetService.CreateContext(quote, [], [reference]);
        Assert.Equal(quote.SiteName, context.WorkSite);
        Assert.Equal(reference.Phone, context.ContactPhone);
        quote.SiteName = "";
        Assert.Equal(reference.Address, WorkSheetService.CreateContext(quote, [], [reference]).WorkSite);
    }

    [Fact]
    public void ExclusionSurvivesEntityMappingAndCatalogSnapshot()
    {
        var source = new Item { PersistentId = 7, Name = "Uscita", ExcludeFromWorkSheet = true };
        Assert.True(source.ToLaborCatalogEntity().ToModel().ExcludeFromWorkSheet);
        var cloneMethod = typeof(MainViewModel).GetMethod("CloneCatalogItem", BindingFlags.NonPublic | BindingFlags.Static)!;
        var clone = Assert.IsType<Item>(cloneMethod.Invoke(null, [source]));
        Assert.True(clone.ExcludeFromWorkSheet);
        var equality = typeof(FallbackDataService).GetMethod("CatalogCollectionsHaveSameContent", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.False((bool)equality.Invoke(null, [new[] { source }, new[] { new Item { PersistentId = 7, Name = "Uscita" } }])!);
    }

    [Fact]
    public void DatabaseDefaultAllowsUnchangedAndroidInserts()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
        var property = db.Model.FindEntityType(typeof(LaborCatalogEntity))!.FindProperty(nameof(LaborCatalogEntity.ExcludeFromWorkSheet))!;
        Assert.Equal(false, property.GetDefaultValue());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CatalogCacheRoundTripPreservesExclusion(bool excluded)
    {
        string folder = Path.Combine(Path.GetTempPath(), "EdilPaintWorkSheetTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalJsonStoreService(folder);
            await store.SaveLaborCatalogAsync([new Item { PersistentId = 4, Name = "Uscita", ExcludeFromWorkSheet = excluded }]);
            Assert.Equal(excluded, Assert.Single(await store.LoadLaborCatalogAsync()).ExcludeFromWorkSheet);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task LegacyCatalogWithoutExclusionRemainsIncluded()
    {
        string folder = Path.Combine(Path.GetTempPath(), "EdilPaintWorkSheetTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalJsonStoreService(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, "dati_lavori.json"), """{"lavori":[{"nome":"Posa","valore":50}]}""");
            Assert.False(Assert.Single(await store.LoadLaborCatalogAsync()).ExcludeFromWorkSheet);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Theory]
    [InlineData("standard", 3, true)]
    [InlineData("multipagina", 45, true)]
    [InlineData("solo-lavorazioni", 3, false)]
    [InlineData("nessuna-lavorazione", 0, false)]
    public void WorkSheetPdfHandlesLongAndEmptyQuotes(string name, int laborCount, bool hasMaterials)
    {
        string? requestedFolder = Environment.GetEnvironmentVariable("EDILPAINT_WORKSHEET_QA_DIR");
        string folder = requestedFolder ?? Path.Combine(Path.GetTempPath(), "EdilPaintWorkSheetPdf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var quote = SampleQuote();
            if (!hasMaterials) quote.Materials.Clear();
            quote.Labors = Enumerable.Range(1, laborCount).Select(index => new Item
            {
                Name = $"Posa e finitura finestra - intervento {index}", Quantity = index == 1 ? 12 : 2, SortOrder = index,
                Description = "Smontaggio del serramento esistente, preparazione del vano, posa con isolamento e sigillatura. Controllo apertura e pulizia finale dell'area di lavoro."
            }).ToList();
            var context = WorkSheetService.CreateContext(quote, [], []);
            context.SelectedLogo = "Edilpaint.png";
            context.IsOfflineSnapshot = !hasMaterials;
            string path = Path.Combine(folder, name + ".pdf");
            new PdfService().GenerateWorkSheet(context, new Company { Nome = "EdilPaint" }, path);
            Assert.True(new FileInfo(path).Length > 4000);
            Assert.Equal("%PDF-", Encoding.ASCII.GetString(File.ReadAllBytes(path)[..5]));
        }
        finally { if (requestedFolder == null) Directory.Delete(folder, recursive: true); }
    }

    private static QuoteHistoryEntry SampleQuote() => new()
    {
        QuoteNumber = "PROVA-2026", Date = new DateTime(2026, 9, 11), CustomerName = "Cliente dimostrativo",
        ReferenceName = "Residenza di prova", SiteName = "Via del Cantiere 12, Brescia",
        SupplierName = "Fornitore di prova", MaterialStatus = "In magazzino",
        MaterialOrderDate = new DateTime(2026, 9, 1), ExpectedDeliveryDate = new DateTime(2026, 9, 15),
        Materials = [new() { Name = "Finestra GGL MK04", Description = "Finitura bianca, vetrocamera isolante", Quantity = 12 }],
        Labors = [new() { Name = "Posa finestra", Quantity = 12 }]
    };
}
