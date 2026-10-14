using System.Text.Json;
using EdilPaintPreventibiviGen.Data;
using EdilPaintPreventibiviGen.Data.Entities;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class WorkScheduleQuoteCompletionTests
{
    private static WorkScheduleEntryEntity Entry() => new()
    {
        Id = Guid.NewGuid(), Revision = 4, Date = new(2026, 10, 14),
        Kind = WorkScheduleEntryKind.Job, Status = WorkScheduleEntryStatus.Planned,
        StartMinutes = 8 * 60, EndMinutes = 12 * 60, Notes = "Accesso dal cancello",
        QuoteId = 51, Quote = new()
        {
            Id = 51, QuoteNumber = "Q-51", Status = QuoteStatus.Confermato, Revision = 9,
            SiteName = "Cantiere Rossi", MaterialStatus = "In magazzino", SupplierName = "Fornitore",
            LastModifiedUtc = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
            Notes = "Note ordine", RealProfitJson = "{\"saved\":true}"
        },
        Assignments = [new() { EmployeeId = Guid.NewGuid(), Employee = new() { Id = Guid.NewGuid(), FirstName = "Mario" } }]
    };

    [Fact]
    public void FinishingVisitTracksBothStatusesAndPreservesCrewDatesNotesAndCosts()
    {
        var stored = Entry();
        var quote = stored.Quote!;
        stored.Assignments.Single().Employee.Id = stored.Assignments.Single().EmployeeId;
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost;Database=unused;Integrated Security=true;TrustServerCertificate=true").Options);
        db.Attach(stored); // Inspect tracking without opening any database connection.
        var originalModified = quote.LastModifiedUtc;
        var result = SqlDataService.ApplyWorkScheduleCompletion(stored, stored.Id, stored.Revision);
        db.ChangeTracker.DetectChanges();

        Assert.Equal(QuoteStatus.Finito, quote.Status);
        Assert.Equal(10, quote.Revision);
        Assert.True(quote.LastModifiedUtc > originalModified);
        Assert.NotEmpty(quote.LastModifiedByDevice);
        Assert.Equal(EntityState.Modified, db.Entry(quote).State);
        Assert.Equal(EntityState.Modified, db.Entry(stored).State);
        Assert.Equal(originalModified, db.Entry(quote).Property(x => x.LastModifiedUtc).OriginalValue);
        Assert.Equal(WorkScheduleEntryStatus.Completed, result.Status);
        Assert.Equal(QuoteStatus.Finito, result.OrderStatus);
        Assert.False(result.IsOrderActive);
        Assert.Equal(stored.Id, result.Id);
        Assert.Equal(5, result.Revision);
        Assert.Equal(new DateTime(2026, 10, 14), result.Date);
        Assert.Equal(8 * 60, result.StartMinutes);
        Assert.Equal(12 * 60, result.EndMinutes);
        Assert.Equal("Accesso dal cancello", result.Notes);
        Assert.Equal("Mario", Assert.Single(result.Employees).FirstName);
        Assert.Single(stored.Assignments);
        Assert.Equal("Note ordine", quote.Notes);
        Assert.Equal("{\"saved\":true}", quote.RealProfitJson);
        Assert.Equal("In magazzino", quote.MaterialStatus);
        var audit = Assert.Single(JsonSerializer.Deserialize<List<QuoteEventEntry>>(quote.EventsJson)!);
        Assert.Equal("stato", audit.EventType);
        Assert.Contains("Finito", audit.Description);
        Assert.Contains("calendario", audit.Description);
    }

    [Fact]
    public void RetryDoesNotDuplicateQuoteRevisionOrAuditAndDoesNotCloseReopenedQuote()
    {
        var stored = Entry();
        SqlDataService.ApplyWorkScheduleCompletion(stored, stored.Id, stored.Revision);
        var audit = stored.Quote!.EventsJson;
        SqlDataService.ApplyWorkScheduleCompletion(stored, stored.Id, stored.Revision);
        Assert.Equal(10, stored.Quote.Revision);
        Assert.Equal(audit, stored.Quote.EventsJson);
        stored.Quote.Status = QuoteStatus.Confermato;
        var result = SqlDataService.ApplyWorkScheduleCompletion(stored, stored.Id, stored.Revision);
        Assert.Equal(QuoteStatus.Confermato, result.OrderStatus);
        Assert.Equal(QuoteStatus.Confermato, stored.Quote.Status);
        Assert.Equal(10, stored.Quote.Revision);
        Assert.Equal(5, stored.Revision);
    }

    [Fact]
    public void AnotherVisitForFinishedOrderRemainsSavedWithoutAnotherStatusEvent()
    {
        var first = Entry();
        var second = Entry(); second.Quote = first.Quote;
        SqlDataService.ApplyWorkScheduleCompletion(first, first.Id, first.Revision);
        Assert.Equal(WorkScheduleEntryStatus.Planned, second.Status);
        Assert.Single(second.Assignments);
        SqlDataService.ApplyWorkScheduleCompletion(second, second.Id, second.Revision);
        Assert.Equal(WorkScheduleEntryStatus.Completed, second.Status);
        Assert.Equal(10, first.Quote!.Revision);
        Assert.Single(JsonSerializer.Deserialize<List<QuoteEventEntry>>(first.Quote.EventsJson)!);
    }

    [Fact]
    public void CompletingOldVisitDoesNotRestoreADeletedOrder()
    {
        var stored = Entry(); stored.Quote!.IsDeleted = true; stored.Quote.Status = QuoteStatus.Archiviato;
        var result = SqlDataService.ApplyWorkScheduleCompletion(stored, stored.Id, stored.Revision);
        Assert.Equal(WorkScheduleEntryStatus.Completed, result.Status);
        Assert.True(result.IsOrderDeleted);
        Assert.True(stored.Quote.IsDeleted);
        Assert.Equal(QuoteStatus.Archiviato, stored.Quote.Status);
        Assert.Equal(9, stored.Quote.Revision);
        Assert.Empty(stored.Quote.EventsJson);
    }

    [Fact]
    public void ConcurrentOrOverflowingCompletionChangesNeitherQuoteNorVisit()
    {
        var stored = Entry();
        Assert.Throws<InvalidOperationException>(() => SqlDataService.ApplyWorkScheduleCompletion(stored, stored.Id, stored.Revision - 1));
        Assert.Equal(WorkScheduleEntryStatus.Planned, stored.Status);
        Assert.Equal(QuoteStatus.Confermato, stored.Quote!.Status);
        stored.Quote.Revision = long.MaxValue;
        Assert.Throws<OverflowException>(() => SqlDataService.ApplyWorkScheduleCompletion(stored, stored.Id, stored.Revision));
        Assert.Equal(WorkScheduleEntryStatus.Planned, stored.Status);
        Assert.Equal(4, stored.Revision);
        Assert.Equal(QuoteStatus.Confermato, stored.Quote.Status);
        Assert.Empty(stored.Quote.EventsJson);
    }
}
