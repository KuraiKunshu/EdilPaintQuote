using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class SupplierOrderQueueTests
{
    [Fact]
    public void ConfirmedJobWithoutAnyOrderDetailsIsShownAsPendingWithoutWritingInventedMetadata()
    {
        var quote = new QuoteHistorySummary { Status = QuoteStatus.Confermato };
        Assert.True(SupplierOrderQueue.NeedsOrdering(quote));
        Assert.Equal("Da ordinare", SupplierOrderQueue.StatusDisplay(quote));
        Assert.Equal("Da ordinare", SupplierOrderQueue.GroupName(quote, true));
        Assert.Equal("Da ordinare", SupplierOrderQueue.GroupName(quote, false));
        Assert.Equal("", quote.MaterialStatus);
        Assert.Equal("", quote.SupplierName);
        Assert.Null(quote.MaterialOrderDate);
    }

    [Fact]
    public void ChoosingASupplierAloneDoesNotMarkMaterialsAsAlreadyOrdered()
    {
        var quote = new QuoteHistorySummary { Status = QuoteStatus.Confermato, SupplierName = "Alfa" };
        Assert.True(SupplierOrderQueue.NeedsOrdering(quote));
        quote.MaterialStatus = "Ordinato";
        Assert.False(SupplierOrderQueue.NeedsOrdering(quote));
        Assert.Equal("Alfa", SupplierOrderQueue.GroupName(quote, true));
        Assert.Equal("Ordini registrati", SupplierOrderQueue.GroupName(quote, false));
        Assert.Equal("Ordinato", SupplierOrderQueue.StatusDisplay(quote));
    }

    [Theory]
    [InlineData("Ordinato")]
    [InlineData("Da ritirare")]
    [InlineData("In magazzino")]
    [InlineData("Consegnato")]
    public void ActualMaterialStatusIsNeverReclassifiedAsPending(string status)
        => Assert.False(SupplierOrderQueue.NeedsOrdering(new() { Status = QuoteStatus.Confermato, MaterialStatus = status }));

    [Fact]
    public void AnOrderDateIsEvidenceOfAnOrderButAnExplicitPendingStatusTakesPriority()
    {
        var quote = new QuoteHistorySummary { Status = QuoteStatus.Confermato, MaterialOrderDate = new(2026, 10, 30) };
        Assert.False(SupplierOrderQueue.NeedsOrdering(quote));
        quote.MaterialStatus = " Da ordinare ";
        Assert.True(SupplierOrderQueue.NeedsOrdering(quote));
        quote.MaterialsOrderedByCustomer = true;
        Assert.False(SupplierOrderQueue.NeedsOrdering(quote));
        Assert.Equal("Ordinati dal cliente", SupplierOrderQueue.GroupName(quote, true));
    }

    [Theory]
    [InlineData(QuoteStatus.Finalizzato)]
    [InlineData(QuoteStatus.Spedito)]
    [InlineData(QuoteStatus.Finito)]
    [InlineData(QuoteStatus.Archiviato)]
    [InlineData(QuoteStatus.Rifiutato)]
    public void OtherStatesDoNotCreateNewPendingJobs(QuoteStatus status)
        => Assert.False(SupplierOrderQueue.NeedsOrdering(new() { Status = status }));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PendingSectionComesFirstAndRespectsTheSelectedSortRegardlessOfSupplierGrouping(bool grouped)
    {
        var ordered = new QuoteHistorySummary { QuoteNumber = "O", Status = QuoteStatus.Confermato, SupplierName = "Alfa", MaterialStatus = "Ordinato" };
        var pendingZ = new QuoteHistorySummary { QuoteNumber = "PZ", Status = QuoteStatus.Confermato, CustomerName = "Zeta", SupplierName = "A" };
        var pendingA = new QuoteHistorySummary { QuoteNumber = "PA", Status = QuoteStatus.Confermato, CustomerName = "Alfa", SupplierName = "Z" };
        var rows = SupplierOrderQueue.Sort([ordered, pendingZ, pendingA], SupplierOrderSortMode.CustomerAscending, grouped);
        Assert.Equal(new[] { "PA", "PZ", "O" }, rows.Select(row => row.QuoteNumber));
    }
}
