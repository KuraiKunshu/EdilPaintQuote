using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class RegressionTests
{
    [Fact]
    public void ReverseChargeSplitsSignificantGoodsAndAddsVat()
    {
        var calculator = new QuoteCalculator();
        var materials = new[] { new Item { UnitPrice = 200, Quantity = 1, IsSignificant = true } };
        var labors = new[] { new Item { UnitPrice = 100, Quantity = 1, IsSignificant = true } };

        var totals = calculator.Calculate(materials, labors, 0, 0, "RC 10%+22%");

        Assert.Equal(200d, totals.Imponibile10);
        Assert.Equal(100d, totals.Imponibile22);
        Assert.Equal(42d, totals.IvaTotale);
        Assert.Equal(342d, totals.TotaleGenerale);
    }

    [Fact]
    public void ReverseChargeAcceptsSpacingVariants()
    {
        var calculator = new QuoteCalculator();
        var materials = new[] { new Item { UnitPrice = 100, Quantity = 1 } };

        var totals = calculator.Calculate(materials, [], 0, 0, " RC 10% + 22% ");

        Assert.Equal(10d, totals.IvaTotale);
        Assert.Equal(110d, totals.TotaleGenerale);
    }

    [Fact]
    public void Vat22AppliesToEverything()
    {
        var calculator = new QuoteCalculator();
        var materials = new[] { new Item { UnitPrice = 100, Quantity = 1 } };
        var labors = new[] { new Item { UnitPrice = 200, Quantity = 1 } };

        var totals = calculator.Calculate(materials, labors, 0, 0, "22%");

        Assert.Equal(0d, totals.Imponibile10);
        Assert.Equal(300d, totals.Imponibile22);
        Assert.Equal(0d, totals.Iva10);
        Assert.Equal(66d, totals.Iva22);
        Assert.Equal(366d, totals.TotaleGenerale);
    }

    [Fact]
    public void ExcludedVatDoesNotAddTax()
    {
        var calculator = new QuoteCalculator();
        var materials = new[] { new Item { UnitPrice = 100, Quantity = 1 } };

        var totals = calculator.Calculate(materials, [], 0, 0, "esclusa");

        Assert.Equal(0d, totals.IvaTotale);
        Assert.Equal(100d, totals.TotaleGenerale);
    }

    [Fact]
    public async Task TombstoneKeepsOfflineDeletions()
    {
        string temporaryPath = CreateTemporaryTestPath();
        try
        {
            var service = new LocalDeletionOutboxService(temporaryPath);
            Guid customerId = Guid.NewGuid();

            await service.AddQuoteAsync("PREV/DEL");
            await service.AddCustomerAsync(customerId, "Cliente eliminato");
            var state = await service.LoadAsync();
            Assert.Single(state.Quotes);
            Assert.Single(state.Customers);

            await service.RemoveQuoteAsync("PREV/DEL");
            await service.RemoveCustomerAsync(customerId, "Cliente eliminato");
            state = await service.LoadAsync();
            Assert.Empty(state.Quotes);
            Assert.Empty(state.Customers);
        }
        finally
        {
            DeleteTemporaryTestPath(temporaryPath);
        }
    }

    [Fact]
    public async Task QuotePatchOutboxMergesNotesAndStatus()
    {
        string temporaryPath = CreateTemporaryTestPath();
        try
        {
            var service = new LocalQuotePatchOutboxService(temporaryPath);
            await service.StoreNotesAsync("PREV/PATCH", "nota");
            await service.StoreStatusAsync("PREV/PATCH", QuoteStatus.Spedito);
            await service.StoreSendInfoAsync("PREV/PATCH", new QuoteSendInfo
            {
                Method = "Email",
                Recipient = "cliente@example.com",
                SentAtUtc = DateTime.UtcNow,
                DeviceName = "PC test"
            });

            var patch = Assert.Single(await service.LoadAllAsync());
            Assert.Equal("nota", patch.Notes);
            Assert.Equal(QuoteStatus.Spedito, patch.Status);
            Assert.Equal("Email", patch.SendInfo?.Method);

            await service.RemoveAppliedAsync("PREV/PATCH", p => p.Notes = null);
            patch = Assert.Single(await service.LoadAllAsync());
            Assert.Null(patch.Notes);
            Assert.Equal(QuoteStatus.Spedito, patch.Status);
            Assert.Equal("Email", patch.SendInfo?.Method);

            await service.RemoveAsync("PREV/PATCH");
            Assert.Empty(await service.LoadAllAsync());
        }
        finally
        {
            DeleteTemporaryTestPath(temporaryPath);
        }
    }

    [Fact]
    public async Task LocalJsonStoreRecoversValidBackup()
    {
        string temporaryPath = CreateTemporaryTestPath();
        try
        {
            var store = new LocalJsonStoreService(temporaryPath);
            var quote = new QuoteHistoryEntry { QuoteNumber = "PREV/BACKUP", Date = DateTime.Today };
            await store.SaveOrUpdateQuoteAsync(quote);
            await store.SaveOrUpdateQuoteAsync(quote);

            await File.WriteAllTextAsync(Path.Combine(temporaryPath, "history.json"), "{");
            var restored = await store.LoadHistoryAsync();
            var restoredQuote = Assert.Single(restored);
            Assert.Equal("PREV/BACKUP", restoredQuote.QuoteNumber);
        }
        finally
        {
            DeleteTemporaryTestPath(temporaryPath);
        }
    }

    [Fact]
    public async Task CustomerRenameKeepsStableIdentity()
    {
        string temporaryPath = CreateTemporaryTestPath();
        try
        {
            var store = new LocalJsonStoreService(temporaryPath);
            Guid syncId = Guid.NewGuid();
            var customer = new Customer { SyncId = syncId, BusinessName = "Cliente vecchio" };
            await store.SaveOrUpdateCustomerAsync(customer);

            customer.BusinessName = "Cliente nuovo";
            await store.UpdateCustomerAsync("Cliente vecchio", customer);

            var restored = await store.LoadCustomersAsync();
            var restoredCustomer = Assert.Single(restored);
            Assert.Equal(syncId, restoredCustomer.SyncId);
            Assert.Equal("Cliente nuovo", restoredCustomer.BusinessName);
        }
        finally
        {
            DeleteTemporaryTestPath(temporaryPath);
        }
    }

    [Fact]
    public void PdfLookupExcludesCostsAndSimilarNumbers()
    {
        Assert.True(QuoteHistoryService.IsPdfForQuote("Cliente_Preventivo_140257-V1_20260602.pdf", "140257-V1", false));
        Assert.False(QuoteHistoryService.IsPdfForQuote("Cliente_Preventivo_140257-V1_COSTI.pdf", "140257-V1", false));
        Assert.False(QuoteHistoryService.IsPdfForQuote("Cliente_Preventivo_140257-V10_20260602.pdf", "140257-V1", false));
    }

    [Fact]
    public void EmailParserSplitsFirstRecipientAndCopies()
    {
        var split = EmailAddressParser.SplitPrimaryAndCopies(
            " info@example.com / amministrazione@example.it, tecnico@example.it non valida / info@example.com ");

        Assert.Equal("info@example.com", split.PrimaryRecipient);
        Assert.Equal(["amministrazione@example.it", "tecnico@example.it"], split.CopyRecipients);
    }

    [Fact]
    public void CollaborationMarginUsesTaxableAmount()
    {
        var context = new CostsPdfContext
        {
            Imponibile = 100,
            Total = 122,
            OurCosts = [new CostAllocationItem { Amount = 40 }],
            PartnerCosts = [new CostAllocationItem { Amount = 10 }],
            AdditionalCosts = [new CostAllocationItem { Amount = 5 }]
        };

        Assert.Equal(45d, PdfService.CalculateEstimatedMargin(context));
    }

    [Fact]
    public void DpapiEncryptsAndDecryptsVeluxSession()
    {
        const string json = """{"cookies":[{"name":"session","value":"secret"}]}""";

        string protectedValue = SecretProtectionService.Protect(json, "VeluxSession.v1");

        Assert.StartsWith("dpapi:", protectedValue, StringComparison.Ordinal);
        Assert.NotEqual(json, protectedValue);
        Assert.Equal(json, SecretProtectionService.Unprotect(protectedValue, "VeluxSession.v1"));
    }

    [Fact]
    public async Task VeluxSearchRespectsCancellation()
    {
        using var service = new VeluxService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await service.SearchProductsAsync("test", cts.Token));
    }

    private static string CreateTemporaryTestPath() =>
        Path.Combine(Path.GetTempPath(), "EdilPaintPreventivi.Tests", Guid.NewGuid().ToString("N"));

    private static void DeleteTemporaryTestPath(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
