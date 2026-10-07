namespace EdilPaintPreventibiviGen.Models;

/// <summary>The scheduled work used for a cost calculation, preserved with its report.</summary>
public sealed class CalendarLaborSnapshot
{
    public List<CalendarLaborRow> Rows { get; set; } = [];
    public double TotalPersonHours => Rows.Sum(row => row.PersonHours);
    public int DistinctWorkers => Rows.SelectMany(row => row.Employees).Select(employee => employee.Id).Distinct().Count();
    public int Days => Rows.Select(row => row.Date.Date).Distinct().Count();
    public bool HasUnassignedInterventions => Rows.Any(row => row.WorkerCount == 0);

    public CalendarLaborSnapshot CreateCopy() => new() { Rows = Rows.Select(row => row.CreateCopy()).ToList() };
}

public sealed class CalendarLaborRow
{
    public Guid EntryId { get; set; }
    public long Revision { get; set; }
    public DateTime Date { get; set; }
    public int StartMinutes { get; set; }
    public int EndMinutes { get; set; }
    public string SlotLabel { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public double WorkHours { get; set; }
    public List<CalendarLaborEmployee> Employees { get; set; } = [];
    public int WorkerCount => Employees.Count;
    public double PersonHours => Math.Max(0, WorkHours) * WorkerCount;
    public string TimeDisplay => $"{StartMinutes / 60:00}:{StartMinutes % 60:00}–{EndMinutes / 60:00}:{EndMinutes % 60:00}";

    public CalendarLaborRow CreateCopy() => new()
    {
        EntryId = EntryId, Revision = Revision, Date = Date, StartMinutes = StartMinutes, EndMinutes = EndMinutes,
        SlotLabel = SlotLabel, IsCompleted = IsCompleted, WorkHours = WorkHours,
        Employees = Employees.Select(employee => new CalendarLaborEmployee
        { Id = employee.Id, Name = employee.Name, Abbreviation = employee.Abbreviation }).ToList()
    };
}

public sealed class CalendarLaborEmployee
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Abbreviation { get; set; } = string.Empty;
}
