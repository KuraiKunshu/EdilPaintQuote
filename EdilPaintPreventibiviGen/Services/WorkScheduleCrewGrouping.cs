using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

internal static class WorkScheduleCrewGrouping
{
    public static IEnumerable<IGrouping<string, WorkScheduleEntry>> Group(IEnumerable<WorkScheduleEntry> entries) => entries
        .Where(entry => entry.Kind == WorkScheduleEntryKind.Job)
        .GroupBy(entry => string.Join(",", entry.EmployeeIds.Order()));
}
