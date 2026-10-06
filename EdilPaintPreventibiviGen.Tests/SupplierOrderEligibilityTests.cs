using EdilPaintPreventibiviGen.Data.Entities;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class SupplierOrderEligibilityTests
{
    [Fact]
    public void ConfirmedQuoteWithoutOrderInformationDoesNotAppearInEitherOrderList()
        => Assert.False(SupplierOrderEligibility.IsActive(new() { Status = QuoteStatus.Confermato }));

    [Fact]
    public void EveryRegisteredOrderFieldCanIdentifyAConfirmedOrder()
    {
        foreach (var quote in new QuoteEntity[]
        {
            new() { Status = QuoteStatus.Confermato, SupplierName = "Fornitore" },
            new() { Status = QuoteStatus.Confermato, MaterialOrderDate = new DateTime(2026,10,6) },
            new() { Status = QuoteStatus.Confermato, ExpectedDeliveryDate = new DateTime(2026,10,9) },
            new() { Status = QuoteStatus.Confermato, MaterialStatus = "Da ordinare" }
        }) Assert.True(SupplierOrderEligibility.IsActive(quote));
    }

    [Theory]
    [InlineData(QuoteStatus.Finalizzato)]
    [InlineData(QuoteStatus.Spedito)]
    [InlineData(QuoteStatus.Confermato)]
    [InlineData(QuoteStatus.Finito)]
    [InlineData(QuoteStatus.Rifiutato)]
    [InlineData(QuoteStatus.Archiviato)]
    public void CustomerOrderedMaterialsKeepTheExistingOrdersWindowCriteria(QuoteStatus status)
        => Assert.True(SupplierOrderEligibility.IsActive(new() { Status = status, MaterialsOrderedByCustomer = true }));

    [Theory]
    [InlineData(QuoteStatus.Finalizzato)]
    [InlineData(QuoteStatus.Finito)]
    [InlineData(QuoteStatus.Rifiutato)]
    [InlineData(QuoteStatus.Archiviato)]
    public void OtherStatesDoNotQualifyThroughSupplierInformation(QuoteStatus status)
        => Assert.False(SupplierOrderEligibility.IsActive(new() { Status = status, SupplierName = "Fornitore" }));

    [Fact]
    public void DeletedOrdersAreExcludedEvenIfMaterialsAreOrderedByTheCustomer()
        => Assert.False(SupplierOrderEligibility.IsActive(new() { IsDeleted = true, MaterialsOrderedByCustomer = true }));

    [Fact]
    public void FuturePlanReleasesPeopleWhenItsOrderLeavesTheOrdersListWhileActualHistoryIsKept()
    {
        var today = new DateTime(2026,10,6);
        var entry = new WorkScheduleEntry { Date = today, QuoteNumber = "2026/001", IsOrderActive = false };
        Assert.False(entry.ReservesEmployees(today));
        entry.Date = today.AddDays(-1);
        Assert.True(entry.ReservesEmployees(today));
        entry.Date = today;
        entry.Status = WorkScheduleEntryStatus.Completed;
        Assert.True(entry.ReservesEmployees(today));
        entry.Status = WorkScheduleEntryStatus.Planned;
        entry.Kind = WorkScheduleEntryKind.Absence;
        Assert.True(entry.ReservesEmployees(today));
    }
}
