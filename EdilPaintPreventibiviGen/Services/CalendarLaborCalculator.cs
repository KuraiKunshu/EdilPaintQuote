using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public static class CalendarLaborCalculator
{
    public static CalendarLaborSnapshot? Build(string quoteNumber, IEnumerable<WorkScheduleEntry> entries,
        WorkScheduleSettings settings)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var validSettings = settings.CreateValidatedCopy();
        var rows = entries.Where(entry => entry.Kind == WorkScheduleEntryKind.Job &&
                string.Equals(entry.QuoteNumber, quoteNumber, StringComparison.Ordinal))
            .Select(entry => entry.CreateValidatedCopy()).OrderBy(entry => entry.Date)
            .ThenBy(entry => entry.StartMinutes).ThenBy(entry => entry.Id).Select(entry =>
            {
                // Full-day reservations span lunch; other slots represent an explicit
                // continuous range. Only subtract the part of lunch inside this visit.
                int minutes = entry.EndMinutes - entry.StartMinutes;
                if (entry.SlotKind == WorkScheduleSlotKind.FullDay)
                    minutes -= Math.Max(0, Math.Min(entry.EndMinutes, validSettings.AfternoonStartMinutes) -
                        Math.Max(entry.StartMinutes, validSettings.MorningEndMinutes));
                var byId = entry.Employees.ToDictionary(employee => employee.Id);
                return new CalendarLaborRow
                {
                    EntryId = entry.Id, Revision = entry.Revision, Date = entry.Date,
                    StartMinutes = entry.StartMinutes, EndMinutes = entry.EndMinutes,
                    SlotLabel = entry.SlotDisplay, IsCompleted = entry.Status == WorkScheduleEntryStatus.Completed,
                    WorkHours = Math.Max(0, minutes) / 60d,
                    Employees = entry.EmployeeIds.Order().Select(id =>
                    {
                        byId.TryGetValue(id, out var employee);
                        return new CalendarLaborEmployee
                        {
                            Id = id, Name = employee == null ? "Dipendente assegnato" :
                                $"{employee.FirstName} {employee.LastName}".Trim(),
                            Abbreviation = employee?.Abbreviation ?? string.Empty
                        };
                    }).ToList()
                };
            }).ToList();
        return rows.Count == 0 ? null : new CalendarLaborSnapshot { Rows = rows };
    }
}
