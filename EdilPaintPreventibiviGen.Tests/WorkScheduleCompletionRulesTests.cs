using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class WorkScheduleCompletionRulesTests
{
    private static WorkScheduleEntry Entry() => new()
    {
        Revision = 4, Date = new DateTime(2026, 10, 12), QuoteNumber = "2026/001",
        SlotKind = WorkScheduleSlotKind.Morning, SettingsRevision = 2,
        StartMinutes = 8 * 60 + 15, EndMinutes = 12 * 60 + 30,
        CustomerName = "Cliente Rossi", SiteName = "Via Roma", ReferenceName = "Referente",
        Notes = "Accesso dal cancello laterale", AbsenceReason = "Indisponibilità",
        MaterialStatus = "Disponibile", ExpectedDeliveryDate = new DateTime(2026, 10, 11),
        OrderStatus = QuoteStatus.Confermato, IsOrderActive = true,
        Employees = [new() { FirstName = "Mario", LastName = "Rossi", Abbreviation = "MR", Revision = 3 }]
    };

    [Fact]
    public void CompletionPreservesSavedVisitAndDoesNotMutateOriginal()
    {
        var stored = Entry();
        stored.EmployeeIds = stored.Employees.Select(employee => employee.Id).ToList();
        var completed = WorkScheduleRules.CreateCompletedCopy(stored, stored.Id, stored.Revision);

        Assert.Equal(WorkScheduleEntryStatus.Completed, completed.Status);
        Assert.Equal(5, completed.Revision);
        Assert.Equal(WorkScheduleEntryStatus.Planned, stored.Status);
        Assert.Equal(4, stored.Revision);
        Assert.Equal(stored.Id, completed.Id);
        Assert.Equal(stored.Date, completed.Date);
        Assert.Equal(stored.Kind, completed.Kind);
        Assert.Equal(stored.QuoteNumber, completed.QuoteNumber);
        Assert.Equal(stored.SlotKind, completed.SlotKind);
        Assert.Equal(stored.SettingsRevision, completed.SettingsRevision);
        Assert.Equal(stored.StartMinutes, completed.StartMinutes);
        Assert.Equal(stored.EndMinutes, completed.EndMinutes);
        Assert.Equal(stored.EmployeeIds, completed.EmployeeIds);
        Assert.NotSame(stored.EmployeeIds, completed.EmployeeIds);
        Assert.Equal(stored.Notes, completed.Notes);
        Assert.Equal(stored.AbsenceReason, completed.AbsenceReason);
        Assert.Equal(stored.CustomerName, completed.CustomerName);
        Assert.Equal(stored.SiteName, completed.SiteName);
        Assert.Equal(stored.ReferenceName, completed.ReferenceName);
        Assert.Equal(stored.MaterialStatus, completed.MaterialStatus);
        Assert.Equal(stored.ExpectedDeliveryDate, completed.ExpectedDeliveryDate);
        Assert.Equal(stored.OrderStatus, completed.OrderStatus);
        Assert.Equal(stored.IsOrderActive, completed.IsOrderActive);
        Assert.Equal(stored.IsOrderDeleted, completed.IsOrderDeleted);
        var employee = Assert.Single(completed.Employees);
        Assert.NotSame(stored.Employees[0], employee);
        Assert.Equal(stored.Employees[0].Id, employee.Id);
        Assert.Equal(stored.Employees[0].Revision, employee.Revision);
        Assert.Equal(stored.Employees[0].FirstName, employee.FirstName);
        Assert.Equal(stored.Employees[0].LastName, employee.LastName);
        Assert.Equal(stored.Employees[0].Abbreviation, employee.Abbreviation);
    }

    [Fact]
    public void AlreadyCompletedVisitIsIdempotentOnlyForTheCurrentRevision()
    {
        var stored = Entry();
        stored.Status = WorkScheduleEntryStatus.Completed;
        var completed = WorkScheduleRules.CreateCompletedCopy(stored, stored.Id, stored.Revision);
        Assert.Equal(stored.Revision, completed.Revision);
        Assert.Equal(WorkScheduleEntryStatus.Completed, completed.Status);
        Assert.Throws<InvalidOperationException>(() =>
            WorkScheduleRules.CreateCompletedCopy(stored, stored.Id, stored.Revision - 1));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("deleted")]
    [InlineData("wrong-id")]
    [InlineData("empty-id")]
    [InlineData("stale")]
    [InlineData("unsaved")]
    public void InvalidOrOutdatedSelectionCannotBeCompleted(string condition)
    {
        var stored = Entry();
        var id = condition == "empty-id" ? Guid.Empty : condition == "wrong-id" ? Guid.NewGuid() : stored.Id;
        var revision = condition == "stale" ? stored.Revision - 1 : condition == "unsaved" ? 0 : stored.Revision;
        Assert.Throws<InvalidOperationException>(() => WorkScheduleRules.CreateCompletedCopy(
            condition == "missing" ? null : stored, id, revision, condition == "deleted"));
        Assert.Equal(WorkScheduleEntryStatus.Planned, stored.Status);
        Assert.Equal(4, stored.Revision);
    }

    [Fact]
    public void AbsenceCannotBeCompletedAsAJob()
    {
        var stored = Entry();
        stored.Kind = WorkScheduleEntryKind.Absence;
        Assert.Throws<InvalidOperationException>(() =>
            WorkScheduleRules.CreateCompletedCopy(stored, stored.Id, stored.Revision));
    }

    [Fact]
    public void ExistingOverlapDoesNotPreventRecordingCompletion()
    {
        var stored = Entry();
        stored.EmployeeIds = stored.Employees.Select(employee => employee.Id).ToList();
        var overlap = stored.CreateValidatedCopy();
        overlap.Id = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => WorkScheduleRules.ValidateNoConflicts([stored, overlap]));
        var completed = WorkScheduleRules.CreateCompletedCopy(stored, stored.Id, stored.Revision);
        Assert.Equal(WorkScheduleEntryStatus.Completed, completed.Status);
    }

    [Fact]
    public void InactiveOrderAndOldPresetCanStillBeCompleted()
    {
        var stored = Entry();
        stored.IsOrderActive = false;
        stored.IsOrderDeleted = true;
        stored.OrderStatus = QuoteStatus.Finito;
        var completed = WorkScheduleRules.CreateCompletedCopy(stored, stored.Id, stored.Revision);
        Assert.Equal(WorkScheduleEntryStatus.Completed, completed.Status);
        Assert.Equal(stored.SettingsRevision, completed.SettingsRevision);
        Assert.Equal(stored.StartMinutes, completed.StartMinutes);
        Assert.Equal(stored.EndMinutes, completed.EndMinutes);
        Assert.Equal(QuoteStatus.Finito, completed.OrderStatus);
    }

    [Fact]
    public void RevisionOverflowDoesNotChangeTheSavedVisit()
    {
        var stored = Entry();
        stored.Revision = long.MaxValue;
        Assert.Throws<OverflowException>(() =>
            WorkScheduleRules.CreateCompletedCopy(stored, stored.Id, stored.Revision));
        Assert.Equal(WorkScheduleEntryStatus.Planned, stored.Status);
        Assert.Equal(long.MaxValue, stored.Revision);
    }
}
