using System.Globalization;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class WorkScheduleViewFilterTests
{
    private static readonly Guid EmployeeId = Guid.Parse("5702f511-a64e-47d0-9b58-3a4c413fae09");
    private static readonly Guid OtherEmployeeId = Guid.Parse("40b194a4-ed64-48cc-bc77-f690e8781b3a");

    [Theory]
    [InlineData(WorkScheduleEntryKind.Job, WorkScheduleEntryStatus.Planned, true, false, true)]
    [InlineData(WorkScheduleEntryKind.Job, WorkScheduleEntryStatus.Planned, false, false, false)]
    [InlineData(WorkScheduleEntryKind.Job, WorkScheduleEntryStatus.Planned, false, true, true)]
    [InlineData(WorkScheduleEntryKind.Job, WorkScheduleEntryStatus.Completed, false, false, true)]
    [InlineData(WorkScheduleEntryKind.Absence, WorkScheduleEntryStatus.Planned, false, false, true)]
    public void DefaultVisibilityRetainsCompletedVisitsAndAbsences(WorkScheduleEntryKind kind,
        WorkScheduleEntryStatus status, bool active, bool showInactive, bool expected)
    {
        var entry = Entry();
        entry.Kind = kind;
        entry.Status = status;
        entry.IsOrderActive = active;

        Assert.Equal(expected, Matches(entry, showInactive: showInactive));
    }

    [Theory]
    [InlineData(WorkScheduleViewFilterKind.All, true, true, true)]
    [InlineData(WorkScheduleViewFilterKind.Planned, true, false, false)]
    [InlineData(WorkScheduleViewFilterKind.Completed, false, true, false)]
    [InlineData(WorkScheduleViewFilterKind.Absences, false, false, true)]
    public void StatusFiltersDistinguishPlannedWorkCompletedWorkAndAbsences(WorkScheduleViewFilterKind filter,
        bool plannedMatches, bool completedMatches, bool absenceMatches)
    {
        var planned = Entry();
        var completed = Entry();
        completed.Status = WorkScheduleEntryStatus.Completed;
        var absence = Entry();
        absence.Kind = WorkScheduleEntryKind.Absence;
        absence.Status = WorkScheduleEntryStatus.Completed;

        Assert.Equal(plannedMatches, Matches(planned, kind: filter));
        Assert.Equal(completedMatches, Matches(completed, kind: filter));
        Assert.Equal(absenceMatches, Matches(absence, kind: filter));
    }

    [Fact]
    public void DeletedOrderRetainsCompletedVisitWithItsCrewButHidesPlannedVisitByDefault()
    {
        var entry = Entry();
        entry.IsOrderDeleted = true;
        entry.OrderStatus = QuoteStatus.Finito;
        entry.Status = WorkScheduleEntryStatus.Completed;
        entry.Date = new DateTime(2024, 1, 2);

        Assert.True(Matches(entry, employee: EmployeeId, kind: WorkScheduleViewFilterKind.Completed));
        entry.Status = WorkScheduleEntryStatus.Planned;
        Assert.False(Matches(entry, employee: EmployeeId, kind: WorkScheduleViewFilterKind.Planned));
        Assert.True(Matches(entry, employee: EmployeeId, kind: WorkScheduleViewFilterKind.Planned,
            showInactive: true));
    }

    [Fact]
    public void CrewAndEmployeeFiltersUseSavedIdsEvenWhenEmployeeDetailsAreUnavailable()
    {
        var entry = Entry();
        entry.Employees.Clear();

        Assert.True(Matches(entry, employee: EmployeeId));
        Assert.False(Matches(entry, employee: OtherEmployeeId));
        Assert.False(Matches(entry, kind: WorkScheduleViewFilterKind.WithoutCrew));

        entry.EmployeeIds.Clear();
        entry.Employees.Add(new() { Id = EmployeeId, FirstName = "Mario" });
        Assert.True(Matches(entry, kind: WorkScheduleViewFilterKind.WithoutCrew));
        Assert.False(Matches(entry, employee: EmployeeId));

        entry.Status = WorkScheduleEntryStatus.Completed;
        Assert.False(Matches(entry, kind: WorkScheduleViewFilterKind.WithoutCrew));
        entry.Kind = WorkScheduleEntryKind.Absence;
        entry.Status = WorkScheduleEntryStatus.Planned;
        Assert.False(Matches(entry, kind: WorkScheduleViewFilterKind.WithoutCrew));
    }

    [Theory]
    [InlineData("  RENE\tCAFFE\n00042  ", true)]
    [InlineData("rEnÉ bIaNcHi RB", true)]
    [InlineData("caffe ILLUMINAZIONE", true)]
    [InlineData("Maflan Graziano", true)]
    [InlineData("RENE sconosciuto", false)]
    [InlineData("via mancante", false)]
    [InlineData(null, true)]
    [InlineData(" \t\n ", true)]
    public void SearchRequiresEveryTermAcrossSiteReferenceCustomerOrderNotesAndCrew(string? query, bool expected)
    {
        Assert.Equal(expected, Matches(Entry(), search: query));
    }

    [Fact]
    public void SearchUsesItalianComparisonEvenUnderAnotherCurrentCulture()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.True(Matches(Entry(), search: "illuminazione BIANCHI rene"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void AbsenceReasonIsSearchableWithItsAssignedPerson()
    {
        var entry = Entry();
        entry.Kind = WorkScheduleEntryKind.Absence;
        entry.AbsenceReason = "Visita medica";

        Assert.True(Matches(entry, search: "medica rene", employee: EmployeeId,
            kind: WorkScheduleViewFilterKind.Absences));
        Assert.False(Matches(entry, search: "medica rene", employee: OtherEmployeeId,
            kind: WorkScheduleViewFilterKind.Absences));
    }

    [Fact]
    public void WarningFilterIncludesMaterialAvailabilityDeliveryOrderAndConflictWarnings()
    {
        var entry = Entry();
        Assert.False(Matches(entry, kind: WorkScheduleViewFilterKind.Warnings));
        Assert.True(Matches(entry, kind: WorkScheduleViewFilterKind.Warnings, hasConflict: true));

        entry.MaterialStatus = "Da ordinare";
        Assert.True(Matches(entry, kind: WorkScheduleViewFilterKind.Warnings));
        entry.MaterialStatus = "Non disponibile";
        Assert.True(Matches(entry, kind: WorkScheduleViewFilterKind.Warnings));
        entry.MaterialStatus = "Ordinato";
        entry.ExpectedDeliveryDate = entry.Date.AddDays(1);
        Assert.True(Matches(entry, kind: WorkScheduleViewFilterKind.Warnings));
        entry.ExpectedDeliveryDate = entry.Date;
        Assert.False(Matches(entry, kind: WorkScheduleViewFilterKind.Warnings));

        entry.IsOrderActive = false;
        Assert.False(Matches(entry, kind: WorkScheduleViewFilterKind.Warnings));
        Assert.True(Matches(entry, kind: WorkScheduleViewFilterKind.Warnings, showInactive: true));
    }

    [Fact]
    public void ConflictWarningCanIncludeAbsenceButDoesNotOverrideOtherFilters()
    {
        var entry = Entry();
        entry.Kind = WorkScheduleEntryKind.Absence;
        entry.AbsenceReason = "Ferie";
        Assert.True(Matches(entry, search: "Ferie", employee: EmployeeId,
            kind: WorkScheduleViewFilterKind.Warnings, hasConflict: true));
        Assert.False(Matches(entry, search: "Malattia", employee: EmployeeId,
            kind: WorkScheduleViewFilterKind.Warnings, hasConflict: true));
        Assert.False(Matches(entry, employee: OtherEmployeeId,
            kind: WorkScheduleViewFilterKind.Warnings, hasConflict: true));

        entry.Kind = WorkScheduleEntryKind.Job;
        entry.IsOrderActive = false;
        Assert.False(Matches(entry, kind: WorkScheduleViewFilterKind.Warnings, hasConflict: true));
        Assert.True(Matches(entry, kind: WorkScheduleViewFilterKind.Warnings, hasConflict: true,
            showInactive: true));
    }

    [Fact]
    public void FilteringKeepsSavedPlanningAndSourceCollectionsUntouched()
    {
        var entry = Entry();
        var ids = entry.EmployeeIds;
        var employees = entry.Employees;
        var person = Assert.Single(employees);
        var id = entry.Id;
        var date = entry.Date;
        var notes = entry.Notes;
        var source = new[] { entry };

        Assert.Single(source, value => Matches(value, search: "rene", employee: EmployeeId));
        Assert.Same(entry, source[0]);
        Assert.Same(ids, entry.EmployeeIds);
        Assert.Same(employees, entry.Employees);
        Assert.Same(person, Assert.Single(entry.Employees));
        Assert.Equal(new[] { EmployeeId }, entry.EmployeeIds);
        Assert.Equal(id, entry.Id);
        Assert.Equal(date, entry.Date);
        Assert.Equal(notes, entry.Notes);
        Assert.Equal(7, entry.Revision);
    }

    private static WorkScheduleEntry Entry() => new()
    {
        Revision = 7,
        Date = new DateTime(2026, 10, 8),
        Kind = WorkScheduleEntryKind.Job,
        Status = WorkScheduleEntryStatus.Planned,
        IsOrderActive = true,
        SiteName = "Via del Caffè 10",
        ReferenceName = "Zoccarato Graziano",
        CustomerName = "MAFLAN SRL",
        QuoteNumber = "2026/00042",
        Notes = "Controllare ILLUMINAZIONE",
        EmployeeIds = [EmployeeId],
        Employees = [new() { Id = EmployeeId, FirstName = "René", LastName = "Bianchi", Abbreviation = "RB" }],
        MaterialStatus = "In magazzino"
    };

    private static bool Matches(WorkScheduleEntry entry, string? search = null, Guid? employee = null,
        WorkScheduleViewFilterKind kind = WorkScheduleViewFilterKind.All, bool showInactive = false,
        bool hasConflict = false) => WorkScheduleViewFilter.Matches(entry, search, employee, kind, showInactive,
            hasConflict);
}
