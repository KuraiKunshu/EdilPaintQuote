using System.Data;
using EdilPaintPreventibiviGen.Data;
using EdilPaintPreventibiviGen.Data.Entities;
using EdilPaintPreventibiviGen.Models;
using Microsoft.EntityFrameworkCore;

namespace EdilPaintPreventibiviGen.Services;

public partial class SqlDataService
{
    public Task<WorkScheduleSnapshot> LoadWorkScheduleAsync(DateTime from, DateTime to, CancellationToken token = default)
    {
        from = DateTime.SpecifyKind(from.Date, DateTimeKind.Unspecified);
        to = DateTime.SpecifyKind(to.Date, DateTimeKind.Unspecified);
        if (from == default || to <= from)
            throw new InvalidOperationException("Seleziona un intervallo valido per il calendario.");
        return ExecuteWorkScheduleTransactionAsync(async (db, ct) =>
        {
            var settings = await db.WorkScheduleSettings.AsNoTracking().SingleAsync(x => x.Id == 1, ct).ConfigureAwait(false);
            var entries = await ScheduleEntriesQuery(db).AsNoTracking()
                .Where(x => !x.IsDeleted && x.Date >= from && x.Date < to)
                .OrderBy(x => x.Date).ThenBy(x => x.StartMinutes).ToListAsync(ct).ConfigureAwait(false);
            var employees = await db.Employees.AsNoTracking().Where(x => !x.IsDeleted)
                .OrderBy(x => x.FirstName).ThenBy(x => x.LastName).ToListAsync(ct).ConfigureAwait(false);
            var orders = await db.Quotes.AsNoTracking().Where(SupplierOrderEligibility.ActiveOrderPredicate)
                .Select(x => new { x.Id, Order = new WorkScheduleOrder
                {
                    QuoteNumber = x.QuoteNumber, SiteName = x.SiteName,
                    CustomerName = x.Customer != null ? x.Customer.BusinessName : string.Empty,
                    ReferenceName = x.ReferenceCustomer != null ? x.ReferenceCustomer.BusinessName : string.Empty,
                    MaterialStatus = x.MaterialStatus, ExpectedDeliveryDate = x.ExpectedDeliveryDate
                } }).ToListAsync(ct).ConfigureAwait(false);
            var today = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified);
            var counts = await db.WorkScheduleEntries.AsNoTracking()
                .Where(x => !x.IsDeleted && x.Kind == WorkScheduleEntryKind.Job &&
                    x.Status == WorkScheduleEntryStatus.Planned && x.Date >= today && x.QuoteId != null)
                .GroupBy(x => x.QuoteId!.Value).Select(x => new { Id = x.Key, Count = x.Count() })
                .ToDictionaryAsync(x => x.Id, x => x.Count, ct).ConfigureAwait(false);
            foreach (var order in orders) order.Order.PlannedInterventions = counts.GetValueOrDefault(order.Id);
            return new WorkScheduleSnapshot
            {
                From = from, To = to, Settings = ScheduleSettingsToModel(settings),
                Entries = entries.Select(ScheduleEntryToModel).ToList(),
                Employees = employees.Select(EmployeeChanges.ToModel).ToList(),
                Orders = orders.Select(x => x.Order).OrderBy(x => x.Title).ThenBy(x => x.QuoteNumber).ToList(),
                IsCurrent = true, HasCachedData = true, UpdatedAtUtc = DateTime.UtcNow
            };
        }, token);
    }

    public Task<WorkScheduleSettings> GetWorkScheduleSettingsAsync(CancellationToken token = default) =>
        ExecuteWorkScheduleTransactionAsync(async (db, ct) => ScheduleSettingsToModel(
            await db.WorkScheduleSettings.AsNoTracking().SingleAsync(x => x.Id == 1, ct).ConfigureAwait(false)), token);

    public async Task<WorkScheduleSettings> SaveWorkScheduleSettingsAsync(WorkScheduleSettings settings,
        CancellationToken token = default)
    {
        var requested = settings.CreateValidatedCopy();
        try
        {
            return await ExecuteWorkScheduleTransactionAsync(async (db, ct) =>
            {
                var stored = await db.WorkScheduleSettings.SingleAsync(x => x.Id == 1, ct).ConfigureAwait(false);
                if (stored.Revision != requested.Revision)
                    throw ScheduleSettingsConflict();
                var before = ScheduleSettingsToModel(stored);
                bool timesChanged = ScheduleTimesChanged(before, requested);
                if (!timesChanged && before.UseEmployeeAbbreviations == requested.UseEmployeeAbbreviations)
                    return before;
                var updated = requested.CreateValidatedCopy();
                updated.Revision = checked(stored.Revision + 1);
                if (timesChanged)
                {
                    var today = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified);
                    var upcoming = await ScheduleEntriesQuery(db).Where(x => !x.IsDeleted && x.Date >= today)
                        .ToListAsync(ct).ConfigureAwait(false);
                    await LockScheduleQuotesAsync(db, upcoming.Where(x => x.QuoteId.HasValue)
                        .Select(x => x.QuoteId!.Value), ct).ConfigureAwait(false);
                    var originals = upcoming.Select(ScheduleEntryToModel).ToList();
                    var changed = WorkScheduleRules.RescheduleUpcomingPresets(originals, updated, today);
                    var originalById = originals.ToDictionary(x => x.Id);
                    foreach (var changedEntry in changed)
                        WorkScheduleRules.ValidateOrderAvailabilityForEdit(changedEntry, originalById[changedEntry.Id], today);
                    var replacements = changed.ToDictionary(x => x.Id);
                    WorkScheduleRules.ValidateNoConflicts(originals.Select(x => replacements.GetValueOrDefault(x.Id, x)), today);
                    foreach (var entry in upcoming)
                    {
                        if (!replacements.TryGetValue(entry.Id, out var replacement)) continue;
                        entry.StartMinutes = replacement.StartMinutes;
                        entry.EndMinutes = replacement.EndMinutes;
                        entry.SettingsRevision = replacement.SettingsRevision;
                        entry.Revision = replacement.Revision;
                    }
                }
                CopyScheduleSettings(updated, stored);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return ScheduleSettingsToModel(stored);
            }, token).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex) { throw ScheduleSettingsConflict(ex); }
    }

    public async Task<List<WorkScheduleEntry>> SaveWorkScheduleEntriesAsync(IReadOnlyList<WorkScheduleEntry> entries,
        CancellationToken token = default)
    {
        var requested = entries.Select(x => x.CreateValidatedCopy()).ToList();
        if (requested.Select(x => x.Id).Distinct().Count() != requested.Count)
            throw new InvalidOperationException("Lo stesso intervento è presente più volte nella selezione.");
        if (requested.Count == 0) return [];
        try
        {
            return await ExecuteWorkScheduleTransactionAsync(async (db, ct) =>
            {
                var settings = ScheduleSettingsToModel(await db.WorkScheduleSettings.AsNoTracking()
                    .SingleAsync(x => x.Id == 1, ct).ConfigureAwait(false));
                var today = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified);
                var ids = requested.Select(x => x.Id).ToArray();
                var stored = await ScheduleEntriesQuery(db).Where(x => ids.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id, ct).ConfigureAwait(false);
                var quoteNumbers = requested.Where(x => x.Kind == WorkScheduleEntryKind.Job)
                    .Select(x => x.QuoteNumber).Distinct().ToArray();
                var quotes = await db.Quotes.IgnoreQueryFilters().Include(x => x.Customer).Include(x => x.ReferenceCustomer)
                    .Where(x => quoteNumbers.Contains(x.QuoteNumber)).ToDictionaryAsync(x => x.QuoteNumber, ct).ConfigureAwait(false);
                await LockScheduleQuotesAsync(db, quotes.Values.Select(x => x.Id), ct).ConfigureAwait(false);
                var employeeIds = requested.SelectMany(x => x.EmployeeIds).Distinct().ToArray();
                var employees = await db.Employees.Where(x => employeeIds.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id, ct).ConfigureAwait(false);
                foreach (var entry in requested)
                {
                    stored.TryGetValue(entry.Id, out var previous);
                    if (previous?.IsDeleted == true) throw WorkScheduleRules.Conflict();
                    var before = previous == null ? null : ScheduleEntryToModel(previous);
                    WorkScheduleRules.ValidateRevision(entry, before);
                    WorkScheduleRules.ValidatePresetTimes(entry, before, settings);
                    if (entry.Kind == WorkScheduleEntryKind.Absence && !string.IsNullOrWhiteSpace(entry.QuoteNumber))
                        throw new InvalidOperationException("Un'assenza non può essere collegata a un ordine.");
                    if (entry.Kind == WorkScheduleEntryKind.Job)
                    {
                        if (!quotes.TryGetValue(entry.QuoteNumber, out var quote) ||
                            (previous == null && !SupplierOrderEligibility.IsActive(quote)))
                            throw new InvalidOperationException($"L'ordine {entry.QuoteNumber} non è più presente nell'elenco Ordini o disponibile. Ricarica il calendario.");
                        FillScheduleQuoteDetails(entry, quote);
                        if (before != null)
                            WorkScheduleRules.ValidateOrderAvailabilityForEdit(entry, before, today);
                    }
                    foreach (var employeeId in entry.EmployeeIds)
                    {
                        if (!employees.TryGetValue(employeeId, out var employee) ||
                            employee.IsDeleted && (previous == null || !previous.Assignments.Any(x => x.EmployeeId == employeeId)))
                            throw new InvalidOperationException("Uno dei dipendenti selezionati è stato rimosso da un altro PC. Ricarica il calendario e aggiorna la squadra.");
                    }
                    entry.Employees = entry.EmployeeIds.Select(x => EmployeeChanges.ToModel(employees[x])).ToList();
                }
                var days = requested.Select(x => x.Date).Distinct().ToArray();
                var otherEntries = await ScheduleEntriesQuery(db).AsNoTracking()
                    .Where(x => !x.IsDeleted && days.Contains(x.Date) && !ids.Contains(x.Id))
                    .ToListAsync(ct).ConfigureAwait(false);
                WorkScheduleRules.ValidateNoConflicts(otherEntries.Select(ScheduleEntryToModel).Concat(requested), today);
                var saved = new List<WorkScheduleEntryEntity>();
                foreach (var entry in requested)
                {
                    if (!stored.TryGetValue(entry.Id, out var entity))
                    {
                        entity = new WorkScheduleEntryEntity { Id = entry.Id };
                        db.WorkScheduleEntries.Add(entity);
                    }
                    else entity.Revision = checked(entity.Revision + 1);
                    entity.QuoteId = entry.Kind == WorkScheduleEntryKind.Job ? quotes[entry.QuoteNumber].Id : null;
                    entity.Quote = entry.Kind == WorkScheduleEntryKind.Job ? quotes[entry.QuoteNumber] : null;
                    entity.Date = entry.Date;
                    entity.Kind = entry.Kind;
                    entity.SlotKind = entry.SlotKind;
                    entity.SettingsRevision = entry.SettingsRevision;
                    entity.StartMinutes = entry.StartMinutes;
                    entity.EndMinutes = entry.EndMinutes;
                    entity.AbsenceReason = entry.AbsenceReason;
                    entity.Notes = entry.Notes;
                    entity.Status = entry.Status;
                    foreach (var assignment in entity.Assignments.Where(x => !entry.EmployeeIds.Contains(x.EmployeeId)).ToList())
                    {
                        entity.Assignments.Remove(assignment);
                        db.WorkScheduleAssignments.Remove(assignment);
                    }
                    foreach (var employeeId in entry.EmployeeIds.Where(x => !entity.Assignments.Any(a => a.EmployeeId == x)))
                        entity.Assignments.Add(new WorkScheduleAssignmentEntity
                        {
                            EntryId = entity.Id, Entry = entity, EmployeeId = employeeId, Employee = employees[employeeId]
                        });
                    saved.Add(entity);
                }
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return saved.Select(ScheduleEntryToModel).ToList();
            }, token).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex) { throw WorkScheduleRules.Conflict(ex); }
    }

    public async Task<WorkScheduleEntry> CompleteWorkScheduleEntryAsync(Guid id, long revision,
        CancellationToken token = default)
    {
        if (id == Guid.Empty || revision <= 0) throw WorkScheduleRules.Conflict();
        try
        {
            return await ExecuteWorkScheduleTransactionAsync(async (db, ct) =>
            {
                var stored = await ScheduleEntriesQuery(db).SingleOrDefaultAsync(entry => entry.Id == id, ct)
                    .ConfigureAwait(false);
                var completed = WorkScheduleRules.CreateCompletedCopy(
                    stored == null ? null : ScheduleEntryToModel(stored), id, revision, stored?.IsDeleted == true);
                if (stored!.Status != completed.Status)
                {
                    // Only the visit status changes; keep the original dates, crew and order.
                    stored.Status = completed.Status;
                    stored.Revision = completed.Revision;
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                }
                return ScheduleEntryToModel(stored);
            }, token).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex) { throw WorkScheduleRules.Conflict(ex); }
    }

    public async Task DeleteWorkScheduleEntryAsync(Guid id, long revision, CancellationToken token = default)
    {
        if (id == Guid.Empty || revision <= 0) throw WorkScheduleRules.Conflict();
        try
        {
            await ExecuteWorkScheduleTransactionAsync(async (db, ct) =>
            {
                var stored = await db.WorkScheduleEntries.SingleOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
                if (stored == null || stored.IsDeleted || stored.Revision != revision) throw WorkScheduleRules.Conflict();
                stored.IsDeleted = true;
                stored.Revision = checked(stored.Revision + 1);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return true;
            }, token).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex) { throw WorkScheduleRules.Conflict(ex); }
    }

    private async Task<T> ExecuteWorkScheduleTransactionAsync<T>(Func<AppDbContext, CancellationToken, Task<T>> operation,
        CancellationToken token)
    {
        await using var strategyContext = AppDbContextFactory.Create();
        var strategy = strategyContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            await using var db = AppDbContextFactory.Create();
            await EnsureWorkScheduleSchemaAsync(db, token).ConfigureAwait(false);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
            await WorkScheduleDatabaseLock.AcquireAsync(db, token).ConfigureAwait(false);
            var result = await operation(db, token).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return result;
        }).ConfigureAwait(false);
    }

    private static IQueryable<WorkScheduleEntryEntity> ScheduleEntriesQuery(AppDbContext db) =>
        db.WorkScheduleEntries.IgnoreQueryFilters().Include(x => x.Quote).ThenInclude(x => x!.Customer)
            .Include(x => x.Quote).ThenInclude(x => x!.ReferenceCustomer)
            .Include(x => x.Assignments).ThenInclude(x => x.Employee).AsSplitQuery();

    private static Task LockScheduleQuotesAsync(AppDbContext db, IEnumerable<int> quoteIds, CancellationToken token)
    {
        // SQL Server's serializable reads already retain shared row locks.
        // PostgreSQL serializable snapshots also need a row lock to ensure the
        // quote cannot close between availability validation and the commit.
        var ids = quoteIds.Distinct().ToArray();
        if (!db.Database.IsNpgsql() || ids.Length == 0) return Task.CompletedTask;
        return db.Database.ExecuteSqlRawAsync("SELECT \"Id\" FROM \"Quotes\" WHERE \"Id\" = ANY ({0}) FOR SHARE;",
            new object[] { ids }, token);
    }

    internal static WorkScheduleEntry ScheduleEntryToModel(WorkScheduleEntryEntity entity)
    {
        var entry = new WorkScheduleEntry
        {
            Id = entity.Id, Revision = entity.Revision, Date = entity.Date,
            Kind = entity.Kind, SlotKind = entity.SlotKind, SettingsRevision = entity.SettingsRevision,
            StartMinutes = entity.StartMinutes, EndMinutes = entity.EndMinutes,
            AbsenceReason = entity.AbsenceReason, Notes = entity.Notes, Status = entity.Status,
            EmployeeIds = entity.Assignments.Select(x => x.EmployeeId).ToList(),
            Employees = entity.Assignments.Select(x => EmployeeChanges.ToModel(x.Employee)).ToList()
        };
        if (entity.Quote != null) FillScheduleQuoteDetails(entry, entity.Quote);
        return entry;
    }

    private static void FillScheduleQuoteDetails(WorkScheduleEntry entry, QuoteEntity quote)
    {
        entry.QuoteNumber = quote.QuoteNumber;
        entry.CustomerName = quote.Customer?.BusinessName ?? string.Empty;
        entry.SiteName = quote.SiteName;
        entry.ReferenceName = quote.ReferenceCustomer?.BusinessName ?? string.Empty;
        entry.MaterialStatus = quote.MaterialStatus;
        entry.ExpectedDeliveryDate = quote.ExpectedDeliveryDate;
        entry.OrderStatus = quote.Status;
        entry.IsOrderDeleted = quote.IsDeleted;
        entry.IsOrderActive = SupplierOrderEligibility.IsActive(quote);
    }

    internal static WorkScheduleSettings ScheduleSettingsToModel(WorkScheduleSettingsEntity entity) => new()
    {
        Revision = entity.Revision,
        FullDayStartMinutes = entity.FullDayStartMinutes, FullDayEndMinutes = entity.FullDayEndMinutes,
        MorningStartMinutes = entity.MorningStartMinutes, MorningEndMinutes = entity.MorningEndMinutes,
        AfternoonStartMinutes = entity.AfternoonStartMinutes, AfternoonEndMinutes = entity.AfternoonEndMinutes,
        UseEmployeeAbbreviations = entity.UseEmployeeAbbreviations
    };

    private static void CopyScheduleSettings(WorkScheduleSettings settings, WorkScheduleSettingsEntity entity)
    {
        entity.Revision = settings.Revision;
        entity.FullDayStartMinutes = settings.FullDayStartMinutes;
        entity.FullDayEndMinutes = settings.FullDayEndMinutes;
        entity.MorningStartMinutes = settings.MorningStartMinutes;
        entity.MorningEndMinutes = settings.MorningEndMinutes;
        entity.AfternoonStartMinutes = settings.AfternoonStartMinutes;
        entity.AfternoonEndMinutes = settings.AfternoonEndMinutes;
        entity.UseEmployeeAbbreviations = settings.UseEmployeeAbbreviations;
    }

    private static bool ScheduleTimesChanged(WorkScheduleSettings before, WorkScheduleSettings after) =>
        before.FullDayStartMinutes != after.FullDayStartMinutes || before.FullDayEndMinutes != after.FullDayEndMinutes ||
        before.MorningStartMinutes != after.MorningStartMinutes || before.MorningEndMinutes != after.MorningEndMinutes ||
        before.AfternoonStartMinutes != after.AfternoonStartMinutes || before.AfternoonEndMinutes != after.AfternoonEndMinutes;

    private static InvalidOperationException ScheduleSettingsConflict(Exception? inner = null) => new(
        "Gli orari del calendario sono stati modificati da un altro PC. Ricarica le impostazioni prima di salvarle.", inner);
}
