using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;

var tests = new (string Name, Func<Task> Run)[]
{
    ("IVA RC suddivide i beni significativi e aggiunge l'imposta", TestReverseChargeAsync),
    ("IVA RC accetta varianti di spaziatura", TestReverseChargeSpacingAsync),
    ("IVA esclusa non aggiunge imposta", TestExcludedVatAsync),
    ("Outbox PDF conserva e rimuove il documento pendente", TestPdfOutboxAsync),
    ("Outbox allegati conserva file e snapshot vuoti", TestAttachmentOutboxAsync),
    ("Outbox PDF costi conserva il documento pendente", TestCostsPdfOutboxAsync),
    ("Tombstone conserva eliminazioni offline", TestDeletionOutboxAsync),
    ("Patch preventivo unisce note e stato", TestQuotePatchOutboxAsync),
    ("JSON locale recupera automaticamente il backup", TestLocalJsonBackupRecoveryAsync),
    ("Rinomina cliente conserva identita stabile", TestCustomerStableIdentityAsync),
    ("Ricerca PDF esclude costi e numeri simili", TestPdfLookupMatchingAsync),
    ("Margine collaborazione usa imponibile e non totale IVA inclusa", TestCostsMarginAsync),
    ("DPAPI cifra e decifra la sessione Velux", TestDpapiAsync),
    ("Ricerca Velux rispetta la cancellazione", TestVeluxCancellationAsync)
};

int failed = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS: {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FAIL: {test.Name}");
        Console.WriteLine(ex.Message);
    }
}

Console.WriteLine($"{tests.Length - failed}/{tests.Length} test superati.");
return failed == 0 ? 0 : 1;

static Task TestReverseChargeAsync()
{
    var calculator = new QuoteCalculator();
    var materials = new[] { new Item { UnitPrice = 200, Quantity = 1, IsSignificant = true } };
    var labors = new[] { new Item { UnitPrice = 100, Quantity = 1, IsSignificant = true } };

    var totals = calculator.Calculate(materials, labors, 0, 0, "RC 10%+22%");

    Equal(200d, totals.Imponibile10);
    Equal(100d, totals.Imponibile22);
    Equal(42d, totals.IvaTotale);
    Equal(342d, totals.TotaleGenerale);
    return Task.CompletedTask;
}

static Task TestReverseChargeSpacingAsync()
{
    var calculator = new QuoteCalculator();
    var materials = new[] { new Item { UnitPrice = 100, Quantity = 1 } };

    var totals = calculator.Calculate(materials, [], 0, 0, " RC 10% + 22% ");

    Equal(10d, totals.IvaTotale);
    Equal(110d, totals.TotaleGenerale);
    return Task.CompletedTask;
}

static Task TestExcludedVatAsync()
{
    var calculator = new QuoteCalculator();
    var materials = new[] { new Item { UnitPrice = 100, Quantity = 1 } };

    var totals = calculator.Calculate(materials, [], 0, 0, "esclusa");

    Equal(0d, totals.IvaTotale);
    Equal(100d, totals.TotaleGenerale);
    return Task.CompletedTask;
}

static async Task TestPdfOutboxAsync()
{
    string temporaryPath = Path.Combine(Path.GetTempPath(), "EdilPaintPreventivi.Tests", Guid.NewGuid().ToString("N"));
    try
    {
        var service = new LocalPdfOutboxService(temporaryPath);
        byte[] expected = [1, 2, 3, 4];

        await service.StoreAsync("PREV/42", expected);
        SequenceEqual(expected, await service.TryReadAsync("PREV/42"));

        await service.RemoveAsync("PREV/42");
        Equal<byte[]?>(null, await service.TryReadAsync("PREV/42"));
    }
    finally
    {
        if (Directory.Exists(temporaryPath))
            Directory.Delete(temporaryPath, recursive: true);
    }
}

static async Task TestAttachmentOutboxAsync()
{
    string temporaryPath = Path.Combine(Path.GetTempPath(), "EdilPaintPreventivi.Tests", Guid.NewGuid().ToString("N"));
    try
    {
        var service = new LocalAttachmentOutboxService(temporaryPath);
        var attachment = new StoredFile
        {
            FileName = "foto.jpg",
            ContentType = "image/jpeg",
            Content = [9, 8, 7],
            ImportedAt = DateTime.UtcNow
        };

        await service.StoreAsync("PREV/ATT", [attachment]);
        var restored = await service.TryReadAsync("PREV/ATT");
        Equal(1, restored?.Count ?? 0);
        Equal("foto.jpg", restored![0].FileName);
        SequenceEqual(attachment.Content, restored[0].Content);

        await service.StoreAsync("PREV/ATT", []);
        restored = await service.TryReadAsync("PREV/ATT");
        Equal(0, restored?.Count ?? -1);
        True((await service.GetPendingQuoteNumbersAsync()).Contains("PREV/ATT"), "Lo snapshot vuoto non risulta pendente.");

        await service.RemoveAsync("PREV/ATT");
        Equal<List<StoredFile>?>(null, await service.TryReadAsync("PREV/ATT"));
    }
    finally
    {
        if (Directory.Exists(temporaryPath))
            Directory.Delete(temporaryPath, recursive: true);
    }
}

static async Task TestCostsPdfOutboxAsync()
{
    string temporaryPath = Path.Combine(Path.GetTempPath(), "EdilPaintPreventivi.Tests", Guid.NewGuid().ToString("N"));
    try
    {
        var service = new LocalCostsPdfOutboxService(temporaryPath);
        var expected = new StoredFile
        {
            FileName = "preventivo_COSTI.pdf",
            ContentType = "application/pdf",
            Content = [4, 3, 2, 1],
            ImportedAt = DateTime.UtcNow
        };

        await service.StoreAsync("PREV/COSTI", expected);
        var restored = await service.TryReadAsync("PREV/COSTI");
        Equal(expected.FileName, restored?.FileName);
        SequenceEqual(expected.Content, restored?.Content);
        True((await service.GetPendingQuoteNumbersAsync()).Contains("PREV/COSTI"), "Il PDF costi non risulta pendente.");

        await service.RemoveAsync("PREV/COSTI");
        Equal<StoredFile?>(null, await service.TryReadAsync("PREV/COSTI"));
    }
    finally
    {
        if (Directory.Exists(temporaryPath))
            Directory.Delete(temporaryPath, recursive: true);
    }
}

static async Task TestDeletionOutboxAsync()
{
    string temporaryPath = Path.Combine(Path.GetTempPath(), "EdilPaintPreventivi.Tests", Guid.NewGuid().ToString("N"));
    try
    {
        var service = new LocalDeletionOutboxService(temporaryPath);
        Guid customerId = Guid.NewGuid();

        await service.AddQuoteAsync("PREV/DEL");
        await service.AddCustomerAsync(customerId, "Cliente eliminato");
        var state = await service.LoadAsync();
        Equal(1, state.Quotes.Count);
        Equal(1, state.Customers.Count);

        await service.RemoveQuoteAsync("PREV/DEL");
        await service.RemoveCustomerAsync(customerId, "Cliente eliminato");
        state = await service.LoadAsync();
        Equal(0, state.Quotes.Count);
        Equal(0, state.Customers.Count);
    }
    finally
    {
        if (Directory.Exists(temporaryPath))
            Directory.Delete(temporaryPath, recursive: true);
    }
}

static async Task TestQuotePatchOutboxAsync()
{
    string temporaryPath = Path.Combine(Path.GetTempPath(), "EdilPaintPreventivi.Tests", Guid.NewGuid().ToString("N"));
    try
    {
        var service = new LocalQuotePatchOutboxService(temporaryPath);
        await service.StoreNotesAsync("PREV/PATCH", "nota");
        await service.StoreStatusAsync("PREV/PATCH", QuoteStatus.Spedito);

        var patch = (await service.LoadAllAsync()).Single();
        Equal("nota", patch.Notes);
        Equal(QuoteStatus.Spedito, patch.Status);

        await service.RemoveAsync("PREV/PATCH");
        Equal(0, (await service.LoadAllAsync()).Count);
    }
    finally
    {
        if (Directory.Exists(temporaryPath))
            Directory.Delete(temporaryPath, recursive: true);
    }
}

static async Task TestLocalJsonBackupRecoveryAsync()
{
    string temporaryPath = Path.Combine(Path.GetTempPath(), "EdilPaintPreventivi.Tests", Guid.NewGuid().ToString("N"));
    try
    {
        var store = new LocalJsonStoreService(temporaryPath);
        var quote = new QuoteHistoryEntry { QuoteNumber = "PREV/BACKUP", Date = DateTime.Today };
        await store.SaveOrUpdateQuoteAsync(quote);
        await store.SaveOrUpdateQuoteAsync(quote);

        await File.WriteAllTextAsync(Path.Combine(temporaryPath, "history.json"), "{");
        var restored = await store.LoadHistoryAsync();
        Equal(1, restored.Count);
        Equal("PREV/BACKUP", restored[0].QuoteNumber);
    }
    finally
    {
        if (Directory.Exists(temporaryPath))
            Directory.Delete(temporaryPath, recursive: true);
    }
}

static async Task TestCustomerStableIdentityAsync()
{
    string temporaryPath = Path.Combine(Path.GetTempPath(), "EdilPaintPreventivi.Tests", Guid.NewGuid().ToString("N"));
    try
    {
        var store = new LocalJsonStoreService(temporaryPath);
        Guid syncId = Guid.NewGuid();
        var customer = new Customer { SyncId = syncId, BusinessName = "Cliente vecchio" };
        await store.SaveOrUpdateCustomerAsync(customer);

        customer.BusinessName = "Cliente nuovo";
        await store.UpdateCustomerAsync("Cliente vecchio", customer);

        var restored = await store.LoadCustomersAsync();
        Equal(1, restored.Count);
        Equal(syncId, restored[0].SyncId);
        Equal("Cliente nuovo", restored[0].BusinessName);
    }
    finally
    {
        if (Directory.Exists(temporaryPath))
            Directory.Delete(temporaryPath, recursive: true);
    }
}

static Task TestPdfLookupMatchingAsync()
{
    True(QuoteHistoryService.IsPdfForQuote("Cliente_Preventivo_140257-V1_20260602.pdf", "140257-V1", false),
        "Il PDF ufficiale esatto non viene riconosciuto.");
    True(!QuoteHistoryService.IsPdfForQuote("Cliente_Preventivo_140257-V1_COSTI.pdf", "140257-V1", false),
        "Il PDF costi viene confuso con il PDF ufficiale.");
    True(!QuoteHistoryService.IsPdfForQuote("Cliente_Preventivo_140257-V10_20260602.pdf", "140257-V1", false),
        "Un numero preventivo simile viene confuso con quello richiesto.");
    return Task.CompletedTask;
}

static Task TestCostsMarginAsync()
{
    var context = new CostsPdfContext
    {
        Imponibile = 100,
        Total = 122,
        OurCosts = [new CostAllocationItem { Amount = 40 }],
        PartnerCosts = [new CostAllocationItem { Amount = 10 }],
        AdditionalCosts = [new CostAllocationItem { Amount = 5 }]
    };

    Equal(45d, PdfService.CalculateEstimatedMargin(context));
    return Task.CompletedTask;
}

static Task TestDpapiAsync()
{
    const string json = """{"cookies":[{"name":"session","value":"secret"}]}""";

    string protectedValue = SecretProtectionService.Protect(json, "VeluxSession.v1");

    True(protectedValue.StartsWith("dpapi:", StringComparison.Ordinal), "Manca il prefisso DPAPI.");
    True(protectedValue != json, "Il testo non e' stato cifrato.");
    Equal(json, SecretProtectionService.Unprotect(protectedValue, "VeluxSession.v1"));
    return Task.CompletedTask;
}

static async Task TestVeluxCancellationAsync()
{
    using var service = new VeluxService();
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    try
    {
        await service.SearchProductsAsync("test", cts.Token);
        throw new InvalidOperationException("La ricerca Velux non ha rispettato la cancellazione.");
    }
    catch (OperationCanceledException)
    {
    }
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Atteso: {expected}; ottenuto: {actual}.");
}

static void SequenceEqual(byte[] expected, byte[]? actual)
{
    if (actual == null || !expected.SequenceEqual(actual))
        throw new InvalidOperationException("Il contenuto binario non corrisponde.");
}

static void True(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
