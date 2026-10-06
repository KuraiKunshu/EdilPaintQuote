using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class WorkScheduleConflictDetectorTests
{
    private readonly DateTime _today = new(2026,10,6);
    private readonly EmployeeSettingsModel _employee = new() { FirstName = "Mario", LastName = "Rossi" };

    private WorkScheduleEntry Entry(int from, int to) => new()
    {
        Date = _today, QuoteNumber = Guid.NewGuid().ToString(), SiteName = "Cantiere",
        StartMinutes = from, EndMinutes = to, IsOrderActive = true,
        Employees = [_employee], EmployeeIds = [_employee.Id]
    };

    [Fact]
    public void ReappearingOrderReportsBothOverlappingVisitsWithTheEmployeeAndTimes()
    {
        var first = Entry(480,720);
        var second = Entry(600,780);
        first.IsOrderActive = false;
        Assert.Empty(WorkScheduleConflictDetector.Find([first,second], _today));
        first.IsOrderActive = true;
        var conflict = Assert.Single(WorkScheduleConflictDetector.Find([first,second], _today));
        Assert.Equal(first.Id, conflict.FirstEntryId);
        Assert.Equal(second.Id, conflict.SecondEntryId);
        Assert.Equal(_employee.Id, conflict.EmployeeId);
        Assert.Contains("Mario Rossi", conflict.Message);
        Assert.Contains("08:00–12:00", conflict.Message);
        Assert.Contains("10:00–13:00", conflict.Message);
    }

    [Fact]
    public void ConsecutiveHalfDaysAndDifferentDaysDoNotConflict()
    {
        var first = Entry(480,720);
        var second = Entry(720,1020);
        Assert.Empty(WorkScheduleConflictDetector.Find([first,second], _today));
        second.StartMinutes = 480;
        second.Date = _today.AddDays(1);
        Assert.Empty(WorkScheduleConflictDetector.Find([first,second], _today));
    }

    [Fact]
    public void AbsencesAndRecordedWorkStillConflictRegardlessOfOrderVisibility()
    {
        var first = Entry(480,720);
        var second = Entry(480,1020);
        second.IsOrderActive = false;
        second.Kind = WorkScheduleEntryKind.Absence;
        Assert.Single(WorkScheduleConflictDetector.Find([first,second], _today));
        second.Kind = WorkScheduleEntryKind.Job;
        second.Status = WorkScheduleEntryStatus.Completed;
        Assert.Single(WorkScheduleConflictDetector.Find([first,second], _today));
        second.Status = WorkScheduleEntryStatus.Planned;
        first.Date = second.Date = _today.AddDays(-1);
        Assert.Single(WorkScheduleConflictDetector.Find([first,second], _today));
    }
}
