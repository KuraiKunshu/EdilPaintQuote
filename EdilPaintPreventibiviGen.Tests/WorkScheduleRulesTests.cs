using EdilPaintPreventibiviGen.Data;
using EdilPaintPreventibiviGen.Data.Entities;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class WorkScheduleRulesTests
{
    private static readonly Guid Mario = Guid.Parse("4789511c-333a-455c-a38d-7b1b4fcfdd2a");
    private static readonly Guid Anna = Guid.Parse("93590e6e-aeb6-4feb-b008-ab966234e5e6");
    private static readonly DateTime Today = new(2026, 10, 6);

    private static WorkScheduleEntry Entry(int start = 480, int end = 720,
        Guid? employee = null, DateTime? date = null) => new()
    {
        QuoteNumber = "2026/001", SiteName = "Cantiere Rossi", Revision = 3, SettingsRevision = 1,
        SlotKind = WorkScheduleSlotKind.Morning, StartMinutes = start, EndMinutes = end,
        Date = date ?? Today, EmployeeIds = [employee ?? Mario],
        Employees = [new() { Id = Mario, FirstName = "Mario", LastName = "Rossi" },
            new() { Id = Anna, FirstName = "Anna", LastName = "Bianchi" }]
    };

    [Fact]
    public void AdjacentInterventionsForSameEmployeeDoNotOverlap()
    {
        WorkScheduleRules.ValidateNoConflicts([Entry(480, 720), Entry(720, 1020)]);
    }

    [Fact]
    public void DifferentPeopleAndDifferentDatesCanShareTimes()
    {
        WorkScheduleRules.ValidateNoConflicts([Entry(), Entry(employee: Anna), Entry(date: Today.AddDays(1))]);
    }

    [Fact]
    public void ConflictsIdentifyEmployeeDayAndBothSites()
    {
        var first = Entry();
        var second = Entry(600, 780);
        second.SiteName = "Cantiere Verdi";
        var error = Assert.Throws<InvalidOperationException>(() => WorkScheduleRules.ValidateNoConflicts([first, second]));
        Assert.Contains("Mario Rossi", error.Message);
        Assert.Contains("06/10/2026", error.Message);
        Assert.Contains("Cantiere Rossi", error.Message);
        Assert.Contains("Cantiere Verdi", error.Message);
        Assert.Contains("nessuna modifica", error.Message);
    }

    [Fact]
    public void AbsenceConflictsWithWorkAndWithOtherAbsences()
    {
        var absence = Entry(600, 780);
        absence.Kind = WorkScheduleEntryKind.Absence;
        absence.AbsenceReason = "Ferie";
        var jobError = Assert.Throws<InvalidOperationException>(() => WorkScheduleRules.ValidateNoConflicts([Entry(), absence]));
        Assert.Contains("Ferie", jobError.Message);
        var otherAbsence = Entry();
        otherAbsence.Kind = WorkScheduleEntryKind.Absence;
        Assert.Throws<InvalidOperationException>(() => WorkScheduleRules.ValidateNoConflicts([absence, otherAbsence]));
    }

    [Fact]
    public void WholeTeamIsCheckedIncludingSomeoneOtherThanFirstSelectedPerson()
    {
        var team = Entry();
        team.EmployeeIds.Add(Anna);
        Assert.Throws<InvalidOperationException>(() => WorkScheduleRules.ValidateNoConflicts([team, Entry(employee: Anna)]));
    }

    [Fact]
    public void ALongInterventionConflictsWithLaterNestedIntervals()
    {
        Assert.Throws<InvalidOperationException>(() => WorkScheduleRules.ValidateNoConflicts([
            Entry(480, 1020), Entry(600, 620), Entry(780, 800)]));
    }

    [Theory]
    [InlineData(WorkScheduleSlotKind.FullDay, 450, 1050)]
    [InlineData(WorkScheduleSlotKind.Morning, 450, 750)]
    [InlineData(WorkScheduleSlotKind.Afternoon, 810, 1050)]
    public void PresetChangesRescheduleUpcomingJobsAndAbsences(WorkScheduleSlotKind kind, int start, int end)
    {
        var settings = ChangedSettings();
        var job = Entry();
        job.SlotKind = kind;
        var absence = Entry(employee: Anna, date: Today.AddDays(2));
        absence.Kind = WorkScheduleEntryKind.Absence;
        absence.QuoteNumber = "";
        absence.SlotKind = kind;
        var changed = WorkScheduleRules.RescheduleUpcomingPresets([job, absence], settings, Today);
        Assert.Equal(2, changed.Count);
        Assert.All(changed, x =>
        {
            Assert.Equal(start, x.StartMinutes);
            Assert.Equal(end, x.EndMinutes);
            Assert.Equal(4, x.Revision);
            Assert.Equal(settings.Revision, x.SettingsRevision);
        });
        Assert.Equal(480, job.StartMinutes);
        Assert.Equal(3, job.Revision);
    }

    [Fact]
    public void PastCompletedAndCustomTimesSurvivePresetChanges()
    {
        var past = Entry(date: Today.AddDays(-1));
        var completed = Entry();
        completed.Status = WorkScheduleEntryStatus.Completed;
        var custom = Entry();
        custom.SlotKind = WorkScheduleSlotKind.Custom;
        Assert.Empty(WorkScheduleRules.RescheduleUpcomingPresets([past, completed, custom], ChangedSettings(), Today));
        Assert.Equal(3, past.Revision);
        Assert.Equal(3, completed.Revision);
        Assert.Equal(3, custom.Revision);
    }

    [Fact]
    public void ADisplayChangeOrUnchangedSlotDoesNotTouchEntryRevision()
    {
        var settings = new WorkScheduleSettings { Revision = 2, UseEmployeeAbbreviations = false,
            AfternoonEndMinutes = 1000 };
        Assert.Empty(WorkScheduleRules.RescheduleUpcomingPresets([Entry()], settings, Today));
    }

    [Fact]
    public void ExtendingPresetsRejectsCollisionsWithUnchangedCustomEntriesBeforeMutatingOriginals()
    {
        var morning = Entry();
        var custom = Entry(720, 780);
        custom.SlotKind = WorkScheduleSlotKind.Custom;
        var changes = WorkScheduleRules.RescheduleUpcomingPresets([morning, custom], ChangedSettings(), Today);
        var replacements = changes.ToDictionary(x => x.Id);
        Assert.Throws<InvalidOperationException>(() => WorkScheduleRules.ValidateNoConflicts(
            new[] { morning, custom }.Select(x => replacements.GetValueOrDefault(x.Id, x))));
        Assert.Equal(720, morning.EndMinutes);
        Assert.Equal(3, morning.Revision);
        Assert.Equal(720, custom.StartMinutes);
    }

    [Fact]
    public void ASettingsChangeCannotCollideWithAnAlreadyCompletedAppointment()
    {
        var upcoming = Entry();
        var completed = Entry(720, 780);
        completed.Status = WorkScheduleEntryStatus.Completed;
        var changed = Assert.Single(WorkScheduleRules.RescheduleUpcomingPresets([upcoming, completed], ChangedSettings(), Today));
        Assert.Throws<InvalidOperationException>(() => WorkScheduleRules.ValidateNoConflicts([changed, completed]));
    }

    [Fact]
    public void NewPresetRequiresCurrentSettingsRevisionAndCorrectTimes()
    {
        var requested = Entry();
        var settings = new WorkScheduleSettings { Revision = 2 };
        Assert.Throws<InvalidOperationException>(() => WorkScheduleRules.ValidatePresetTimes(requested, null, settings));
        requested.SettingsRevision = 2;
        requested.EndMinutes = 721;
        Assert.Throws<InvalidOperationException>(() => WorkScheduleRules.ValidatePresetTimes(requested, null, settings));
        requested.EndMinutes = 720;
        WorkScheduleRules.ValidatePresetTimes(requested, null, settings);
    }

    [Fact]
    public void MovingAnOldPresetToAnotherDayRequiresCurrentSettings()
    {
        var stored = Entry(date: Today.AddDays(-1));
        var requested = stored.CreateValidatedCopy();
        requested.Date = Today;
        Assert.Throws<InvalidOperationException>(() => WorkScheduleRules.ValidatePresetTimes(requested, stored,
            new WorkScheduleSettings { Revision = 2 }));
    }

    [Fact]
    public void EditingNotesOrCompletingHistoricalInterventionsKeepsAgreedHours()
    {
        var stored = Entry(date: Today.AddDays(-1));
        var requested = stored.CreateValidatedCopy();
        requested.Notes = "Lavoro concluso";
        requested.Status = WorkScheduleEntryStatus.Completed;
        WorkScheduleRules.ValidatePresetTimes(requested, stored, ChangedSettings());
    }

    [Fact]
    public void CustomTimeDoesNotDependOnPresetRevision()
    {
        var custom = Entry(645, 765);
        custom.SlotKind = WorkScheduleSlotKind.Custom;
        WorkScheduleRules.ValidatePresetTimes(custom, null, ChangedSettings());
    }

    [Fact]
    public void StaleRevisionOrChangingOrderCannotOverwriteExistingIntervention()
    {
        var stored = Entry();
        var stale = stored.CreateValidatedCopy();
        stale.Revision--;
        Assert.Throws<InvalidOperationException>(() => WorkScheduleRules.ValidateRevision(stale, stored));
        var moved = stored.CreateValidatedCopy();
        moved.QuoteNumber = "2026/002";
        Assert.Throws<InvalidOperationException>(() => WorkScheduleRules.ValidateRevision(moved, stored));
        WorkScheduleRules.ValidateRevision(stored.CreateValidatedCopy(), stored);
    }

    [Fact]
    public void AnUnknownIdWithExistingRevisionIsNotInsertedAsNew()
    {
        Assert.Throws<InvalidOperationException>(() => WorkScheduleRules.ValidateRevision(Entry(), null));
        var newEntry = Entry();
        newEntry.Revision = 0;
        WorkScheduleRules.ValidateRevision(newEntry, null);
    }

    [Theory]
    [InlineData(QuoteStatus.Finito, false)]
    [InlineData(QuoteStatus.Rifiutato, false)]
    [InlineData(QuoteStatus.Archiviato, false)]
    [InlineData(QuoteStatus.Confermato, true)]
    public void ClosedOrDeletedOrdersRejectFutureScheduleChanges(QuoteStatus status, bool deleted)
    {
        var stored = Entry();
        var requested = stored.CreateValidatedCopy();
        requested.OrderStatus = status;
        requested.IsOrderDeleted = deleted;
        requested.Date = Today.AddDays(1);
        var error = Assert.Throws<InvalidOperationException>(() =>
            WorkScheduleRules.ValidateOrderAvailabilityForEdit(requested, stored, Today));
        Assert.Contains(stored.QuoteNumber, error.Message);
        Assert.Contains("note", error.Message);
        Assert.Contains("completare", error.Message);
    }

    [Theory]
    [InlineData("hours")]
    [InlineData("team")]
    [InlineData("slot")]
    public void ClosedOrdersAlsoProtectFutureHoursAndTeam(string change)
    {
        var stored = Entry();
        var requested = stored.CreateValidatedCopy();
        requested.OrderStatus = QuoteStatus.Finito;
        if (change == "hours") requested.EndMinutes++;
        if (change == "team") requested.EmployeeIds.Add(Anna);
        if (change == "slot") requested.SlotKind = WorkScheduleSlotKind.Custom;
        Assert.Throws<InvalidOperationException>(() =>
            WorkScheduleRules.ValidateOrderAvailabilityForEdit(requested, stored, Today));
    }

    [Fact]
    public void ClosedOrderAllowsNotesCompletionAndHistoricalCorrections()
    {
        var stored = Entry();
        var notes = stored.CreateValidatedCopy();
        notes.OrderStatus = QuoteStatus.Archiviato;
        notes.Notes = "Visita da verificare";
        WorkScheduleRules.ValidateOrderAvailabilityForEdit(notes, stored, Today);
        notes.Status = WorkScheduleEntryStatus.Completed;
        WorkScheduleRules.ValidateOrderAvailabilityForEdit(notes, stored, Today);
        var past = Entry(date: Today.AddDays(-3));
        var historicalCorrection = past.CreateValidatedCopy();
        historicalCorrection.OrderStatus = QuoteStatus.Finito;
        historicalCorrection.Date = Today.AddDays(-2);
        historicalCorrection.StartMinutes++;
        historicalCorrection.EmployeeIds.Add(Anna);
        WorkScheduleRules.ValidateOrderAvailabilityForEdit(historicalCorrection, past, Today);
    }

    [Fact]
    public void CompletingVisitCannotAlsoMoveAnUnavailableFutureOrder()
    {
        var stored = Entry();
        var requested = stored.CreateValidatedCopy();
        requested.OrderStatus = QuoteStatus.Rifiutato;
        requested.Status = WorkScheduleEntryStatus.Completed;
        requested.Date = Today.AddDays(1);
        Assert.Throws<InvalidOperationException>(() =>
            WorkScheduleRules.ValidateOrderAvailabilityForEdit(requested, stored, Today));
    }

    [Fact]
    public void ClosedHistoricalOrderCannotBeMovedIntoFutureOrReactivated()
    {
        var past = Entry(date: Today.AddDays(-1));
        var moved = past.CreateValidatedCopy();
        moved.OrderStatus = QuoteStatus.Finito;
        moved.Date = Today;
        Assert.Throws<InvalidOperationException>(() =>
            WorkScheduleRules.ValidateOrderAvailabilityForEdit(moved, past, Today));
        var completed = Entry();
        completed.Status = WorkScheduleEntryStatus.Completed;
        var reopened = completed.CreateValidatedCopy();
        reopened.Status = WorkScheduleEntryStatus.Planned;
        reopened.OrderStatus = QuoteStatus.Finito;
        Assert.Throws<InvalidOperationException>(() =>
            WorkScheduleRules.ValidateOrderAvailabilityForEdit(reopened, completed, Today));
    }

    [Fact]
    public void ConfirmedOrdersAndAbsencesRemainEditable()
    {
        var stored = Entry();
        var requested = stored.CreateValidatedCopy();
        requested.OrderStatus = QuoteStatus.Confermato;
        requested.Date = Today.AddDays(1);
        requested.EmployeeIds.Add(Anna);
        WorkScheduleRules.ValidateOrderAvailabilityForEdit(requested, stored, Today);
        requested.Kind = WorkScheduleEntryKind.Absence;
        requested.OrderStatus = QuoteStatus.Finito;
        WorkScheduleRules.ValidateOrderAvailabilityForEdit(requested, stored, Today);
    }

    [Fact]
    public void SettingsBulkChangePreservesAnInactiveFutureOrder()
    {
        var stored = Entry();
        stored.OrderStatus = QuoteStatus.Archiviato;
        stored.IsOrderActive = false;
        Assert.Empty(WorkScheduleRules.RescheduleUpcomingPresets([stored], ChangedSettings(), Today));
        Assert.Equal(720, stored.EndMinutes);
        Assert.Equal(3, stored.Revision);
    }

    [Fact]
    public void CustomerOrderedJobsRemainEditableAndReservedEvenWhenNotConfirmed()
    {
        var stored = Entry();
        stored.OrderStatus = QuoteStatus.Finito;
        stored.IsOrderActive = true;
        var requested = stored.CreateValidatedCopy();
        requested.Date = Today.AddDays(1);
        WorkScheduleRules.ValidateOrderAvailabilityForEdit(requested, stored, Today);
        Assert.True(stored.ReservesEmployees(Today));
        Assert.Single(WorkScheduleRules.RescheduleUpcomingPresets([stored], ChangedSettings(), Today));
    }

    [Fact]
    public void AConfirmedJobOutsideOrdersCannotBeRescheduledAsAnActiveOrder()
    {
        var stored = Entry();
        stored.OrderStatus = QuoteStatus.Confermato;
        stored.IsOrderActive = false;
        var requested = stored.CreateValidatedCopy();
        requested.Date = Today.AddDays(1);
        Assert.Throws<InvalidOperationException>(() =>
            WorkScheduleRules.ValidateOrderAvailabilityForEdit(requested, stored, Today));
    }

    [Fact]
    public void FutureInactivePlansDoNotReservePeopleOrConflictWithActiveWork()
    {
        var inactive = Entry();
        inactive.IsOrderActive = false;
        var active = Entry();
        active.IsOrderActive = true;
        Assert.False(inactive.ReservesEmployees(Today));
        WorkScheduleRules.ValidateNoConflicts([inactive, active], Today);
        inactive.Date = Today.AddDays(1);
        active.Date = Today.AddDays(1);
        WorkScheduleRules.ValidateNoConflicts([inactive, active], Today);
    }

    [Fact]
    public void CompletedAndHistoricalInactiveJobsStillReservePeople()
    {
        var completed = Entry();
        completed.IsOrderActive = false;
        completed.Status = WorkScheduleEntryStatus.Completed;
        Assert.True(completed.ReservesEmployees(Today));
        Assert.Throws<InvalidOperationException>(() =>
            WorkScheduleRules.ValidateNoConflicts([completed, Entry()], Today));
        var historical = Entry(date: Today.AddDays(-1));
        historical.IsOrderActive = false;
        Assert.True(historical.ReservesEmployees(Today));
        Assert.Throws<InvalidOperationException>(() =>
            WorkScheduleRules.ValidateNoConflicts([historical, Entry(date: Today.AddDays(-1))], Today));
    }

    [Fact]
    public void AnAbsenceAlwaysReservesPeopleEvenWithoutOrderEligibility()
    {
        var absence = Entry();
        absence.Kind = WorkScheduleEntryKind.Absence;
        absence.IsOrderActive = false;
        Assert.True(absence.ReservesEmployees(Today));
        Assert.Throws<InvalidOperationException>(() =>
            WorkScheduleRules.ValidateNoConflicts([absence, Entry()], Today));
    }

    [Theory]
    [InlineData(QuoteStatus.Finalizzato)]
    [InlineData(QuoteStatus.Spedito)]
    [InlineData(QuoteStatus.Confermato)]
    [InlineData(QuoteStatus.Finito)]
    [InlineData(QuoteStatus.Rifiutato)]
    [InlineData(QuoteStatus.Bozza)]
    [InlineData(QuoteStatus.DaInviare)]
    [InlineData(QuoteStatus.DaSollecitare)]
    [InlineData(QuoteStatus.Archiviato)]
    public void OrdersCustomerFlagKeepsEveryExistingStatusEligible(QuoteStatus status)
    {
        var quote = new QuoteEntity { Status = status, MaterialsOrderedByCustomer = true };
        Assert.True(SupplierOrderEligibility.IsActive(quote));
        Assert.True(SupplierOrderEligibility.ActiveOrderPredicate.Compile()(quote));
        quote.IsDeleted = true;
        Assert.False(SupplierOrderEligibility.IsActive(quote));
        Assert.False(SupplierOrderEligibility.ActiveOrderPredicate.Compile()(quote));
    }

    [Theory]
    [InlineData("supplier")]
    [InlineData("ordered")]
    [InlineData("delivery")]
    [InlineData("materials")]
    public void ConfirmedOrdersNeedAtLeastOneSupplierOrderDetail(string detail)
    {
        var quote = new QuoteEntity { Status = QuoteStatus.Confermato };
        Assert.False(SupplierOrderEligibility.IsActive(quote));
        if (detail == "supplier") quote.SupplierName = "Fornitore";
        if (detail == "ordered") quote.MaterialOrderDate = Today;
        if (detail == "delivery") quote.ExpectedDeliveryDate = Today;
        if (detail == "materials") quote.MaterialStatus = "Da ordinare";
        Assert.True(SupplierOrderEligibility.IsActive(quote));
        Assert.True(SupplierOrderEligibility.ActiveOrderPredicate.Compile()(quote));
        quote.Status = QuoteStatus.Finito;
        Assert.False(SupplierOrderEligibility.IsActive(quote));
        Assert.False(SupplierOrderEligibility.ActiveOrderPredicate.Compile()(quote));
    }

    [Fact]
    public void MapperUsesOrdersEligibilityRatherThanOnlyConfirmedStatus()
    {
        var entity = new WorkScheduleEntryEntity
        {
            Id = Guid.NewGuid(), Date = Today,
            Quote = new QuoteEntity { QuoteNumber = "2026/001", Status = QuoteStatus.Rifiutato,
                MaterialsOrderedByCustomer = true }
        };
        var active = SqlDataService.ScheduleEntryToModel(entity);
        Assert.True(active.IsOrderActive);
        Assert.True(active.IsActiveOrder);
        Assert.False(active.HasOrderWarning);
        entity.Quote.Status = QuoteStatus.Confermato;
        entity.Quote.MaterialsOrderedByCustomer = false;
        var inactive = SqlDataService.ScheduleEntryToModel(entity);
        Assert.False(inactive.IsOrderActive);
        Assert.False(inactive.IsActiveOrder);
        Assert.True(inactive.HasOrderWarning);
    }

    [Fact]
    public void SnapshotRetainsAssignedArchivedEmployeeNames()
    {
        var archived = new EmployeeEntity { Id = Mario, FirstName = "Mario", LastName = "Rossi", IsDeleted = true };
        var entity = new WorkScheduleEntryEntity { Id = Guid.NewGuid(), Date = Today,
            Quote = new QuoteEntity { QuoteNumber = "2026/001", SiteName = "Cantiere Rossi",
                Status = QuoteStatus.Archiviato, IsDeleted = true } };
        entity.Assignments.Add(new WorkScheduleAssignmentEntity { EntryId = entity.Id, EmployeeId = Mario, Employee = archived });
        var snapshot = SqlDataService.ScheduleEntryToModel(entity);
        Assert.Equal("Mario", Assert.Single(snapshot.Employees).FirstName);
        Assert.Equal(Mario, Assert.Single(snapshot.EmployeeIds));
        Assert.Equal("Cantiere Rossi", snapshot.SiteName);
        Assert.Equal(QuoteStatus.Archiviato, snapshot.OrderStatus);
        Assert.True(snapshot.IsOrderDeleted);
        Assert.False(snapshot.IsOrderActive);
        Assert.True(snapshot.HasOrderWarning);
        snapshot.Status = WorkScheduleEntryStatus.Completed;
        Assert.False(snapshot.HasOrderWarning);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProvidersMapDateRevisionsAndRestrictEmployeeAndOrderDeletion(bool postgres)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        if (postgres) builder.UseNpgsql("Host=localhost;Database=unused");
        else builder.UseSqlServer("Server=localhost;Database=unused;Integrated Security=True");
        using var db = new AppDbContext(builder.Options);
        var entries = db.Model.FindEntityType(typeof(WorkScheduleEntryEntity))!;
        Assert.Equal("date", entries.FindProperty(nameof(WorkScheduleEntryEntity.Date))!.GetColumnType());
        Assert.True(entries.FindProperty(nameof(WorkScheduleEntryEntity.Revision))!.IsConcurrencyToken);
        Assert.Equal(DeleteBehavior.Restrict, Assert.Single(entries.GetForeignKeys()).DeleteBehavior);
        var assignments = db.Model.FindEntityType(typeof(WorkScheduleAssignmentEntity))!;
        Assert.Equal(2, assignments.FindPrimaryKey()!.Properties.Count);
        Assert.Equal(DeleteBehavior.Restrict, assignments.GetForeignKeys().Single(x =>
            x.PrincipalEntityType.ClrType == typeof(EmployeeEntity)).DeleteBehavior);
        var settings = db.Model.FindEntityType(typeof(WorkScheduleSettingsEntity))!;
        Assert.True(settings.FindProperty(nameof(WorkScheduleSettingsEntity.Revision))!.IsConcurrencyToken);
        string script = db.Database.GenerateCreateScript();
        Assert.Contains("WorkScheduleAssignments", script);
        Assert.Contains("SettingsRevision", script);
        string orderQuery = db.Quotes.Where(SupplierOrderEligibility.ActiveOrderPredicate).ToQueryString();
        Assert.Contains("MaterialsOrderedByCustomer", orderQuery);
        Assert.Contains("MaterialOrderDate", orderQuery);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingDatabaseSchemaInitializationIsLockedAndIdempotent(bool postgres)
    {
        string schema = SqlDataService.BuildWorkScheduleSchemaSql(postgres);
        Assert.Contains(postgres ? "pg_advisory_xact_lock" : "sp_getapplock", schema);
        Assert.Contains("EdilPaint.WorkScheduleSchema", schema);
        Assert.Contains(postgres ? "CREATE TABLE IF NOT EXISTS" : "IF OBJECT_ID", schema);
        Assert.Contains(postgres ? "ON CONFLICT" : "IF NOT EXISTS (SELECT 1 FROM [dbo].[WorkScheduleSettings]", schema);
        Assert.Contains("WorkScheduleSettings", schema);
        Assert.Contains("WorkScheduleEntries", schema);
        Assert.Contains("WorkScheduleAssignments", schema);
        Assert.Equal("EdilPaint.SharedStaffPlanning", WorkScheduleDatabaseLock.Resource);
    }

    private static WorkScheduleSettings ChangedSettings() => new()
    {
        Revision = 2, FullDayStartMinutes = 450, FullDayEndMinutes = 1050,
        MorningStartMinutes = 450, MorningEndMinutes = 750,
        AfternoonStartMinutes = 810, AfternoonEndMinutes = 1050
    };
}
