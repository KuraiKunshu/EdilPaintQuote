using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class WorkScheduleCrewGroupingTests
{
    [Fact]
    public void GroupsJobsByTheExactCrewAcrossDaysRegardlessOfAssignmentOrder()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var other = Guid.NewGuid();
        var monday = new DateTime(2026, 11, 2);
        var crewOnMonday = new WorkScheduleEntry { Date = monday, EmployeeIds = [first, second] };
        var soloOnMonday = new WorkScheduleEntry { Date = monday, EmployeeIds = [first] };
        var differentCrew = new WorkScheduleEntry { Date = monday, EmployeeIds = [first, other] };
        var crewOnThursday = new WorkScheduleEntry
        {
            Date = monday.AddDays(3),
            EmployeeIds = [second, first],
            Status = WorkScheduleEntryStatus.Completed
        };

        var groups = WorkScheduleCrewGrouping.Group([crewOnMonday, soloOnMonday, differentCrew, crewOnThursday]);

        Assert.Collection(groups,
            crew => Assert.Equal(new[] { crewOnMonday, crewOnThursday }, crew.ToArray()),
            crew => Assert.Same(soloOnMonday, Assert.Single(crew)),
            crew => Assert.Same(differentCrew, Assert.Single(crew)));
        Assert.Equal(new[] { second, first }, crewOnThursday.EmployeeIds);
    }

    [Fact]
    public void KeepsUnassignedJobsTogetherAndLeavesAbsencesOutsideTheJobGroups()
    {
        var employee = Guid.NewGuid();
        var absence = new WorkScheduleEntry { Kind = WorkScheduleEntryKind.Absence, EmployeeIds = [employee] };
        var assigned = new WorkScheduleEntry { EmployeeIds = [employee] };
        var firstUnassigned = new WorkScheduleEntry();
        var secondUnassigned = new WorkScheduleEntry();

        var groups = WorkScheduleCrewGrouping.Group([absence, firstUnassigned, assigned, secondUnassigned]);

        Assert.Collection(groups,
            crew => Assert.Equal(new[] { firstUnassigned, secondUnassigned }, crew.ToArray()),
            crew => Assert.Same(assigned, Assert.Single(crew)));
    }
}
