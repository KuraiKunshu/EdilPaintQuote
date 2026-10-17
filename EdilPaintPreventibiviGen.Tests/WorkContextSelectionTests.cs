using EdilPaintPreventibiviGen.Models;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class WorkContextSelectionTests
{
    [Fact]
    public void SwitchingViewsOnTheSameQuoteRetainsTheSelectedIntervention()
    {
        var entry = Job("160765");
        var selection = new WorkContextSelection();
        selection.Select(entry.QuoteNumber, entry.Title, entry);
        selection.Select("160765", "Cantiere dallo storico");
        Assert.Same(entry, selection.Entry);
        Assert.Equal("160765", selection.QuoteNumber);
    }

    [Fact]
    public void AnotherQuoteCannotInheritThePreviousCrewOrCompletionAction()
    {
        var entry = Job("160765");
        var selection = new WorkContextSelection();
        selection.Select(entry.QuoteNumber, entry.Title, entry);
        selection.Select("1607651", "Altro cantiere");
        Assert.Null(selection.Entry);
        Assert.Equal("1607651", selection.QuoteNumber);
    }

    [Fact]
    public void AnAbsenceNeverExposesThePreviousQuotesDocumentActions()
    {
        var selection = new WorkContextSelection();
        selection.Select("123", "Cantiere", Job("123"));
        var absence = new WorkScheduleEntry { Kind = WorkScheduleEntryKind.Absence, AbsenceReason = "Ferie" };
        selection.Select("123", absence.Title, absence);
        Assert.Equal("", selection.QuoteNumber);
        Assert.Same(absence, selection.Entry);
        selection.Select("456", "Altro cantiere");
        Assert.Null(selection.Entry);
    }

    [Fact]
    public void CompletionKeepsTheInterventionAndUpdatesItsRevision()
    {
        var old = Job("123");
        var selection = new WorkContextSelection();
        selection.Select(old.QuoteNumber, old.Title, old);
        var completed = Job("123");
        completed.Id = old.Id;
        completed.Revision = 2;
        completed.Status = WorkScheduleEntryStatus.Completed;
        selection.UpdateEntries([completed]);
        Assert.Same(completed, selection.Entry);
        Assert.Equal(WorkScheduleEntryStatus.Completed, selection.Entry!.Status);
        Assert.Equal(2, selection.Entry.Revision);
    }

    [Fact]
    public void ARemovedOrDifferentWeekInterventionCannotRemainAnActionTarget()
    {
        var entry = Job("123");
        var selection = new WorkContextSelection();
        selection.Select(entry.QuoteNumber, entry.Title, entry);
        selection.UpdateEntries([Job("123")]);
        Assert.Null(selection.Entry);
        Assert.Equal("123", selection.QuoteNumber);
    }

    [Fact]
    public void ReassigningAnInterventionOnAnotherPcUpdatesItsQuoteContext()
    {
        var entry = Job("123");
        var selection = new WorkContextSelection();
        selection.Select(entry.QuoteNumber, entry.Title, entry);
        var updated = Job("456"); updated.Id = entry.Id; updated.SiteName = "Nuovo cantiere";
        selection.UpdateEntries([updated]);
        Assert.Equal("456", selection.QuoteNumber);
        Assert.Equal("Cantiere: Nuovo cantiere", selection.Title);
    }

    [Fact]
    public void AnEntryFromAnotherQuoteIsNotAcceptedAsTheSelectedIntervention()
    {
        var selection = new WorkContextSelection();
        selection.Select("123", "Cantiere", Job("456"));
        Assert.Null(selection.Entry);
    }

    private static WorkScheduleEntry Job(string number) => new()
    {
        QuoteNumber = number, Revision = 1, Kind = WorkScheduleEntryKind.Job, SiteName = "Via Roma 10"
    };
}
