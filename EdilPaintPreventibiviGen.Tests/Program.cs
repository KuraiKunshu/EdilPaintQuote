using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;

var tests = new (string Name, Func<Task> Run)[]
{
    ("IVA RC suddivide i beni significativi e aggiunge l'imposta", TestReverseChargeAsync),
    ("IVA RC accetta varianti di spaziatura", TestReverseChargeSpacingAsync),
    ("IVA esclusa non aggiunge imposta", TestExcludedVatAsync),
    ("Outbox PDF conserva e rimuove il documento pendente", TestPdfOutboxAsync),
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
