using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public sealed record WorkScheduleConflict(Guid EmployeeId, DateTime Date, Guid FirstEntryId, Guid SecondEntryId, string Message);

/// <summary>Shows conflicts introduced by order changes, including changes from another client.</summary>
public static class WorkScheduleConflictDetector
{
    public static List<WorkScheduleConflict> Find(IEnumerable<WorkScheduleEntry> entries, DateTime? today = null)
    {
        var conflicts = new List<WorkScheduleConflict>();
        var assignments = entries.Where(entry => entry.ReservesEmployees(today ?? DateTime.Today))
            .SelectMany(entry => entry.EmployeeIds.Select(employeeId => (employeeId, entry)))
            .GroupBy(assignment => (assignment.employeeId, assignment.entry.Date.Date));
        foreach (var group in assignments)
        {
            var visits = group.Select(assignment => assignment.entry).OrderBy(entry => entry.StartMinutes)
                .ThenBy(entry => entry.EndMinutes).ToList();
            for (int i = 0; i < visits.Count; i++)
            {
                var first = visits[i];
                for (int j = i + 1; j < visits.Count && visits[j].StartMinutes < first.EndMinutes; j++)
                {
                    var second = visits[j];
                    if (first.Id == second.Id) continue;
                    var employee = first.Employees.Concat(second.Employees).FirstOrDefault(person => person.Id == group.Key.employeeId);
                    var name = employee == null ? "Dipendente" : $"{employee.FirstName} {employee.LastName}".Trim();
                    conflicts.Add(new(group.Key.employeeId, first.Date.Date, first.Id, second.Id,
                        $"{name} · {first.Date:dd/MM/yyyy}: {first.Title} ({first.TimeDisplay}) e {second.Title} ({second.TimeDisplay})."));
                }
            }
        }
        return conflicts;
    }
}
