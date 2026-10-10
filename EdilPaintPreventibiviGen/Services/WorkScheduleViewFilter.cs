using System.Globalization;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public enum WorkScheduleViewFilterKind { All, Planned, WithoutCrew, Warnings, Completed, Absences }

public static class WorkScheduleViewFilter
{
    private static readonly CompareInfo SearchComparison = CultureInfo.GetCultureInfo("it-IT").CompareInfo;
    private const CompareOptions SearchOptions = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    public static bool Matches(WorkScheduleEntry entry, string? search, Guid? employeeId,
        WorkScheduleViewFilterKind kind, bool showInactive, bool hasConflict = false)
    {
        ArgumentNullException.ThrowIfNull(entry);

        bool isJob = entry.Kind == WorkScheduleEntryKind.Job;
        bool isCompleted = isJob && entry.Status == WorkScheduleEntryStatus.Completed;
        bool isPlanned = isJob && entry.Status == WorkScheduleEntryStatus.Planned;
        if (entry.Kind != WorkScheduleEntryKind.Absence && !isCompleted && !entry.IsActiveOrder && !showInactive)
            return false;
        if (employeeId.HasValue && !entry.EmployeeIds.Contains(employeeId.Value))
            return false;

        bool matchesKind = kind switch
        {
            WorkScheduleViewFilterKind.All => true,
            WorkScheduleViewFilterKind.Planned => isPlanned,
            WorkScheduleViewFilterKind.WithoutCrew => isPlanned && entry.EmployeeIds.Count == 0,
            WorkScheduleViewFilterKind.Warnings => hasConflict || entry.HasMaterialWarning || entry.HasOrderWarning,
            WorkScheduleViewFilterKind.Completed => isCompleted,
            WorkScheduleViewFilterKind.Absences => entry.Kind == WorkScheduleEntryKind.Absence,
            _ => false
        };
        if (!matchesKind || string.IsNullOrWhiteSpace(search))
            return matchesKind;

        string searchableText = string.Join(" ", new[]
        {
            entry.Title, entry.ReferenceName, entry.CustomerName, entry.QuoteNumber, entry.Notes
        }.Concat(entry.Employees.SelectMany(employee => new[]
        {
            employee.FirstName, employee.LastName, employee.Abbreviation
        })));
        return search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .All(term => SearchComparison.IndexOf(searchableText, term, SearchOptions) >= 0);
    }
}
