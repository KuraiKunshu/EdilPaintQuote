using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class SupplierOrderTransitionTests
{
    [Theory]
    [InlineData(QuoteStatus.Spedito)]
    [InlineData(QuoteStatus.Finalizzato)]
    [InlineData(QuoteStatus.Bozza)]
    [InlineData(QuoteStatus.DaInviare)]
    [InlineData(QuoteStatus.DaSollecitare)]
    public void ChangingToConfirmedCreatesAnExplicitPendingOrder(QuoteStatus previous)
        => Assert.Equal("Da ordinare", SupplierOrderQueue.MaterialStatusAfterStatusChange(
            previous, QuoteStatus.Confermato, false, null, null, ""));

    [Theory]
    [InlineData(QuoteStatus.Confermato, QuoteStatus.Confermato)]
    [InlineData(QuoteStatus.Spedito, QuoteStatus.Spedito)]
    [InlineData(QuoteStatus.Spedito, QuoteStatus.Finalizzato)]
    [InlineData(QuoteStatus.Spedito, QuoteStatus.Finito)]
    public void SameStateAndOtherStateChangesDoNotEnqueue(QuoteStatus previous, QuoteStatus next)
        => Assert.Equal("", SupplierOrderQueue.MaterialStatusAfterStatusChange(previous, next, false, null, null, ""));

    [Theory]
    [InlineData("Ordinato")]
    [InlineData("Da ordinare")]
    [InlineData("Da ritirare")]
    [InlineData("In magazzino")]
    [InlineData("Consegnato")]
    public void ConfirmationPreservesExistingMaterialStatus(string materialStatus)
        => Assert.Equal(materialStatus, SupplierOrderQueue.MaterialStatusAfterStatusChange(
            QuoteStatus.Spedito, QuoteStatus.Confermato, false, null, null, materialStatus));

    [Fact]
    public void CustomerOrdersAndKnownOrderOrDeliveryDatesAreNotReclassified()
    {
        var date = new DateTime(2026, 11, 2);
        Assert.Equal("", SupplierOrderQueue.MaterialStatusAfterStatusChange(QuoteStatus.Spedito, QuoteStatus.Confermato, true, null, null, ""));
        Assert.Equal("", SupplierOrderQueue.MaterialStatusAfterStatusChange(QuoteStatus.Spedito, QuoteStatus.Confermato, false, date, null, ""));
        Assert.Equal("", SupplierOrderQueue.MaterialStatusAfterStatusChange(QuoteStatus.Spedito, QuoteStatus.Confermato, false, null, date, ""));
    }

    [Fact]
    public async Task ImportAndRepeatedConfirmationLeaveLegacyQuotesOutUntilARealStatusChange()
    {
        string root = Path.Combine(Path.GetTempPath(), "EdilPaintOrderTransition_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalJsonStoreService(root);
            await store.BulkUpdateQuotesAsync([new() { QuoteNumber = "legacy", Status = QuoteStatus.Confermato }]);
            Assert.Equal("", (await store.GetQuoteByNumberAsync("legacy"))!.MaterialStatus);
            await store.UpdateQuoteStatusAsync("legacy", QuoteStatus.Confermato);
            Assert.Equal("", (await store.GetQuoteByNumberAsync("legacy"))!.MaterialStatus);
            await store.UpdateQuoteStatusAsync("legacy", QuoteStatus.Spedito);
            await store.UpdateQuoteStatusAsync("legacy", QuoteStatus.Confermato);
            var saved = (await new LocalJsonStoreService(root).GetQuoteByNumberAsync("legacy"))!;
            Assert.Equal(QuoteStatus.Confermato, saved.Status);
            Assert.Equal("Da ordinare", saved.MaterialStatus);
            Assert.Null(saved.MaterialOrderDate);
            Assert.Equal("", saved.SupplierName);
            Assert.True(SupplierOrderQueue.NeedsOrdering(new() { Status = saved.Status, MaterialStatus = saved.MaterialStatus }));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
