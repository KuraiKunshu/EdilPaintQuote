using System.Text.Json;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class CalendarLaborCalculatorTests
{
    private const string QuoteNumber = "2026/0042";
    private static readonly DateTime FirstDay = new(2026, 10, 7);
    private static readonly Guid Mario = Guid.Parse("0bf41460-e6a0-4f5d-a056-142963878783");
    private static readonly Guid Anna = Guid.Parse("c59a742d-0dd2-48b5-a1b9-42479f38b323");
    private static readonly Guid Luca = Guid.Parse("b6b7f676-cf83-4928-a4b8-0d6297224f36");

    [Theory]
    [InlineData(WorkScheduleSlotKind.FullDay, 480, 1020, 8)]
    [InlineData(WorkScheduleSlotKind.Morning, 480, 720, 4)]
    [InlineData(WorkScheduleSlotKind.Afternoon, 780, 1020, 4)]
    [InlineData(WorkScheduleSlotKind.Custom, 480, 1020, 9)]
    public void CalendarSlotsUseActualWorkingHours(WorkScheduleSlotKind slot, int start, int end,
        double expectedHours)
    {
        var snapshot = Build(Entry(slot, start, end, Mario, Anna));

        var row = Assert.Single(snapshot.Rows);
        Assert.Equal(expectedHours, row.WorkHours);
        Assert.Equal(2, row.WorkerCount);
        Assert.Equal(expectedHours * 2, snapshot.TotalPersonHours);
        Assert.Equal(expectedHours * 2 * 35, Cost(snapshot).LaborCost);
    }

    [Fact]
    public void FullDayUsesBreakFromSharedSettings()
    {
        var settings = new WorkScheduleSettings { AfternoonStartMinutes = 750 };

        var snapshot = CalendarLaborCalculator.Build(QuoteNumber,
            [Entry(WorkScheduleSlotKind.FullDay, 480, 1020, Mario)], settings)!;

        Assert.Equal(8.5, Assert.Single(snapshot.Rows).WorkHours);
        Assert.Equal(8.5 * 35, Cost(snapshot).LaborCost);
    }

    [Theory]
    [InlineData(690, 750, 0.5)]
    [InlineData(750, 810, 0.5)]
    [InlineData(720, 780, 0)]
    [InlineData(480, 720, 4)]
    [InlineData(780, 1020, 4)]
    public void FullDayOnlySubtractsBreakInsideStoredTimeRange(int start, int end, double hours)
    {
        var snapshot = Build(Entry(WorkScheduleSlotKind.FullDay, start, end, Mario));

        Assert.Equal(hours, Assert.Single(snapshot.Rows).WorkHours);
    }

    [Fact]
    public void IncludesPlannedAndFinishedInterventionsAcrossWeeksWithTheirActualTeams()
    {
        var first = Entry(WorkScheduleSlotKind.FullDay, 480, 1020, Mario, Anna);
        first.Status = WorkScheduleEntryStatus.Completed;
        var afternoon = Entry(WorkScheduleSlotKind.Afternoon, 780, 1020, Luca);
        var later = Entry(WorkScheduleSlotKind.Morning, 480, 720, Anna, Luca);
        later.Date = FirstDay.AddDays(14);

        var snapshot = Build(later, afternoon, first);

        Assert.Equal(3, snapshot.Rows.Count);
        Assert.Equal(28, snapshot.TotalPersonHours);
        Assert.Equal(3, snapshot.DistinctWorkers);
        Assert.Equal(2, snapshot.Days);
        Assert.Equal(first.Id, snapshot.Rows[0].EntryId);
        Assert.True(snapshot.Rows[0].IsCompleted);
        Assert.False(snapshot.Rows[1].IsCompleted);
        Assert.Equal(later.Id, snapshot.Rows[2].EntryId);
        Assert.Equal(980, Cost(snapshot).LaborCost);
    }

    [Fact]
    public void ExcludesAbsencesAndVisitsForOtherOrders()
    {
        var absence = Entry(WorkScheduleSlotKind.FullDay, 480, 1020, Mario, Anna);
        absence.Kind = WorkScheduleEntryKind.Absence;
        absence.AbsenceReason = "Ferie";
        var anotherOrder = Entry(WorkScheduleSlotKind.FullDay, 480, 1020, Mario, Anna);
        anotherOrder.QuoteNumber = "2026/0099";
        var work = Entry(WorkScheduleSlotKind.Morning, 480, 720, Mario);

        var snapshot = Build(absence, anotherOrder, work);

        Assert.Equal(work.Id, Assert.Single(snapshot.Rows).EntryId);
        Assert.Equal(4, snapshot.TotalPersonHours);
        Assert.Equal(1, snapshot.DistinctWorkers);
    }

    [Fact]
    public void NoInterventionsForOrderKeepsManualCalculationAvailable()
    {
        var other = Entry(WorkScheduleSlotKind.Morning, 480, 720, Mario);
        other.QuoteNumber = "2026/0099";

        Assert.Null(CalendarLaborCalculator.Build(QuoteNumber, [other], new WorkScheduleSettings()));
        Assert.Null(CalendarLaborCalculator.Build(QuoteNumber, [], new WorkScheduleSettings()));
        var result = RealProfitCalculator.Calculate(new RealProfitInput
        {
            Workers = 3, Days = 2.5, HoursPerDay = 8, HourlyCost = 35
        });
        Assert.Equal(2100, result.LaborCost);
    }

    [Fact]
    public void CountsAssignedIdsWhenEmployeeNamesAreMissingOrExtraNamesArePresent()
    {
        var entry = Entry(WorkScheduleSlotKind.Morning, 480, 720, Mario, Anna);
        entry.Employees =
        [
            new() { Id = Mario, FirstName = "Mario", LastName = "Rossi", Abbreviation = "MR" },
            new() { Id = Luca, FirstName = "Luca", LastName = "Verdi" }
        ];

        var snapshot = Build(entry);

        var row = Assert.Single(snapshot.Rows);
        Assert.Equal(new[] { Mario, Anna }, row.Employees.Select(employee => employee.Id));
        Assert.Equal("Mario Rossi", row.Employees[0].Name);
        Assert.Equal("MR", row.Employees[0].Abbreviation);
        Assert.Equal("Dipendente assegnato", row.Employees[1].Name);
        Assert.Equal(2, row.WorkerCount);
        Assert.Equal(8, snapshot.TotalPersonHours);
    }

    [Fact]
    public void UnassignedVisitWarnsAndNeverFallsBackToManualTeamValues()
    {
        var snapshot = Build(Entry(WorkScheduleSlotKind.FullDay, 480, 1020));

        Assert.True(snapshot.HasUnassignedInterventions);
        Assert.Equal(0, snapshot.TotalPersonHours);
        Assert.Equal(0, snapshot.DistinctWorkers);
        Assert.Equal(1, snapshot.Days);
        var result = RealProfitCalculator.Calculate(new RealProfitInput
        {
            CalendarLabor = snapshot, Workers = 100, Days = 30, HoursPerDay = 8, HourlyCost = 35
        });
        Assert.Equal(0, result.LaborCost);
    }

    [Fact]
    public void UnassignedVisitsDoNotPreventAssignedVisitsFromContributingTheirCosts()
    {
        var unassigned = Entry(WorkScheduleSlotKind.FullDay, 480, 1020);
        unassigned.Date = FirstDay.AddDays(1);
        var snapshot = Build(Entry(WorkScheduleSlotKind.Morning, 480, 720, Mario), unassigned);

        Assert.True(snapshot.HasUnassignedInterventions);
        Assert.Equal(4, snapshot.TotalPersonHours);
        Assert.Equal(140, Cost(snapshot).LaborCost);
    }

    [Fact]
    public void SnapshotAndSavedCopyKeepTheirOwnEmployeeAndInterventionData()
    {
        var entry = Entry(WorkScheduleSlotKind.FullDay, 480, 1020, Mario);
        entry.Employees = [new() { Id = Mario, FirstName = "Mario", Abbreviation = "MR" }];
        var snapshot = Build(entry);
        var saved = snapshot.CreateCopy();

        entry.EmployeeIds.Clear();
        entry.Employees[0].FirstName = "Nome cambiato";
        entry.StartMinutes = 600;
        snapshot.Rows[0].Employees[0].Name = "Nuovo nome";
        snapshot.Rows[0].Employees[0].Abbreviation = "NN";
        snapshot.Rows[0].WorkHours = 2;
        snapshot.Rows.Clear();

        var savedRow = Assert.Single(saved.Rows);
        Assert.Equal("Mario", Assert.Single(savedRow.Employees).Name);
        Assert.Equal("MR", savedRow.Employees[0].Abbreviation);
        Assert.Equal(480, savedRow.StartMinutes);
        Assert.Equal(entry.Id, savedRow.EntryId);
        Assert.Equal(3, savedRow.Revision);
        Assert.Equal(8, saved.TotalPersonHours);
        Assert.Equal(280, Cost(saved).LaborCost);
    }

    [Fact]
    public void SavedRealProfitSnapshotRoundTripsCalendarTeamAndRecalculatesSameCosts()
    {
        var entry = Entry(WorkScheduleSlotKind.FullDay, 480, 1020, Mario, Anna);
        entry.Status = WorkScheduleEntryStatus.Completed;
        var input = new RealProfitInput
        {
            CalendarLabor = Build(entry), Workers = 100, Days = 100, HoursPerDay = 24,
            HourlyCost = 35, QuoteRevenue = 3000
        };
        var original = new RealProfitSnapshot { Input = input, Result = RealProfitCalculator.Calculate(input) };

        var saved = JsonSerializer.Deserialize<RealProfitSnapshot>(JsonSerializer.Serialize(original))!;

        Assert.NotSame(original.Input.CalendarLabor, saved.Input.CalendarLabor);
        var savedRow = Assert.Single(saved.Input.CalendarLabor!.Rows);
        Assert.Equal(entry.Id, savedRow.EntryId);
        Assert.Equal(3, savedRow.Revision);
        Assert.True(savedRow.IsCompleted);
        Assert.Equal(FirstDay, savedRow.Date);
        Assert.Equal(new[] { Mario, Anna }, savedRow.Employees.Select(employee => employee.Id));
        Assert.Equal(16, saved.Input.CalendarLabor.TotalPersonHours);
        Assert.Equal(560, saved.Result.LaborCost);
        Assert.Equal(560, RealProfitCalculator.Calculate(saved.Input).LaborCost);
        Assert.Equal(original.Result.Profit, RealProfitCalculator.Calculate(saved.Input).Profit);
    }

    private static CalendarLaborSnapshot Build(params WorkScheduleEntry[] entries) =>
        CalendarLaborCalculator.Build(QuoteNumber, entries, new WorkScheduleSettings())!;

    private static RealProfitResult Cost(CalendarLaborSnapshot snapshot) =>
        RealProfitCalculator.Calculate(new RealProfitInput { CalendarLabor = snapshot, HourlyCost = 35 });

    private static WorkScheduleEntry Entry(WorkScheduleSlotKind slot, int start, int end,
        params Guid[] employees) => new()
    {
        QuoteNumber = QuoteNumber, Date = FirstDay, Revision = 3,
        Kind = WorkScheduleEntryKind.Job, SlotKind = slot,
        StartMinutes = start, EndMinutes = end, EmployeeIds = [.. employees],
        Employees = employees.Select(id => new EmployeeSettingsModel
        {
            Id = id, FirstName = id == Mario ? "Mario" : id == Anna ? "Anna" : "Luca"
        }).ToList()
    };
}
