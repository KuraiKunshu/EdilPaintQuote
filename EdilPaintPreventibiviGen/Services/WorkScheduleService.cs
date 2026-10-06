using System.Diagnostics;
using System.IO;
using System.Text.Json;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

/// <summary>Database authority for planning, with scoped caches for offline consultation.</summary>
public sealed class WorkScheduleService
{
    private const int CurrentCacheVersion = 2;
    private readonly IWorkScheduleRepository _repository;
    private readonly string _cachePath;
    private readonly string _databaseIdentity;
    private readonly Func<string> _currentDatabaseIdentity;
    private readonly Func<CancellationToken, Task> _ensureReady;
    private readonly Func<CancellationToken, Task> _prepareEmployees;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _cacheLock = new();
    private ScheduleCache _cache = new() { Version = CurrentCacheVersion };
    private bool _employeesPrepared;

    public WorkScheduleService(IWorkScheduleRepository repository, string cachePath, string databaseIdentity,
        Func<CancellationToken, Task>? ensureReady = null, Func<string>? currentDatabaseIdentity = null,
        Func<CancellationToken, Task>? prepareEmployees = null)
    {
        _repository = repository;
        _cachePath = cachePath;
        _databaseIdentity = databaseIdentity;
        _currentDatabaseIdentity = currentDatabaseIdentity ?? (() => databaseIdentity);
        _ensureReady = ensureReady ?? (_ => Task.CompletedTask);
        _prepareEmployees = prepareEmployees ?? (_ => Task.CompletedTask);
        try
        {
            if (File.Exists(cachePath))
            {
                var cache = JsonSerializer.Deserialize<ScheduleCache>(File.ReadAllText(cachePath));
                if (cache?.DatabaseIdentity == databaseIdentity && cache.Version == CurrentCacheVersion)
                {
                    if (cache.Settings == null || cache.Pages == null || cache.Pages.Values.Any(page =>
                            page == null || page.Entries == null || page.Employees == null || page.Orders == null ||
                            page.Entries.Any(entry => entry == null || entry.EmployeeIds == null ||
                                entry.Employees == null || entry.Employees.Any(employee => employee == null)) ||
                            page.Employees.Any(employee => employee == null) || page.Orders.Any(order => order == null)))
                        throw new InvalidOperationException("La copia locale del calendario non è valida.");
                    cache.Settings.CreateValidatedCopy();
                    foreach (var page in cache.Pages.Values)
                    {
                        foreach (var entry in page.Entries) entry.CreateValidatedCopy();
                        foreach (var employee in page.Employees) employee.CreateValidatedCopy();
                    }
                    _cache = cache;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            Debug.WriteLine($"[Schedule] Cache unavailable: {ex.GetType().Name}");
        }
        _cache.DatabaseIdentity = databaseIdentity;
    }

    public WorkScheduleSnapshot CachedSnapshot(DateTime from, DateTime to)
    {
        EnsureIdentity();
        ValidateRange(from, to);
        lock (_cacheLock)
        {
            _cache.Pages.TryGetValue(PageKey(from, to), out var snapshot);
            var copy = snapshot == null ? new WorkScheduleSnapshot { From = from.Date, To = to.Date } : Clone(snapshot);
            copy.Settings = _cache.Settings.CreateValidatedCopy();
            copy.IsCurrent = false;
            copy.HasCachedData = snapshot != null;
            return copy;
        }
    }

    public async Task<WorkScheduleSnapshot> GetLatestAsync(DateTime from, DateTime to, CancellationToken token = default)
    {
        ValidateRange(from, to);
        try
        {
            return await RunDatabaseAsync(async operationToken =>
            {
                var snapshot = await _repository.LoadWorkScheduleAsync(from.Date, to.Date, operationToken).ConfigureAwait(false);
                snapshot.From = from.Date;
                snapshot.To = to.Date;
                snapshot.IsCurrent = true;
                snapshot.HasCachedData = true;
                snapshot.UpdatedAtUtc = DateTime.UtcNow;
                lock (_cacheLock)
                {
                    _cache.Settings = snapshot.Settings.CreateValidatedCopy();
                    _cache.SettingsUpdatedAtUtc = snapshot.UpdatedAtUtc;
                    _cache.Pages[PageKey(from, to)] = Clone(snapshot);
                    // Bound disk cache growth while retaining recently consulted weeks.
                    foreach (var key in _cache.Pages.OrderByDescending(x => x.Value.UpdatedAtUtc).Skip(26).Select(x => x.Key).ToArray())
                        _cache.Pages.Remove(key);
                }
                PersistCache();
                return snapshot;
            }, token, prepareEmployees: true).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Schedule] Read failed: {ex.GetType().Name}");
            return CachedSnapshot(from, to);
        }
    }

    public async Task<WorkScheduleSettingsSnapshot> GetSettingsAsync(CancellationToken token = default)
    {
        try
        {
            return await RunDatabaseAsync(async operationToken =>
            {
                var settings = await _repository.GetWorkScheduleSettingsAsync(operationToken).ConfigureAwait(false);
                var updated = DateTime.UtcNow;
                lock (_cacheLock)
                {
                    _cache.Settings = settings.CreateValidatedCopy();
                    _cache.SettingsUpdatedAtUtc = updated;
                }
                PersistCache();
                return new WorkScheduleSettingsSnapshot(settings, true, updated);
            }, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Schedule] Settings read failed: {ex.GetType().Name}");
            EnsureIdentity();
            lock (_cacheLock)
                return new(_cache.Settings.CreateValidatedCopy(), false, _cache.SettingsUpdatedAtUtc);
        }
    }

    public Task<WorkScheduleSettingsSnapshot> SaveSettingsAsync(WorkScheduleSettings settings, CancellationToken token = default)
    {
        var copy = settings.CreateValidatedCopy();
        return RunDatabaseAsync(async operationToken =>
        {
            var saved = await _repository.SaveWorkScheduleSettingsAsync(copy, operationToken).ConfigureAwait(false);
            var updated = DateTime.UtcNow;
            lock (_cacheLock)
            {
                _cache.Settings = saved.CreateValidatedCopy();
                _cache.SettingsUpdatedAtUtc = updated;
                foreach (var page in _cache.Pages.Values)
                {
                    page.Settings = saved.CreateValidatedCopy();
                    page.IsCurrent = false;
                    foreach (var entry in page.Entries.Where(x => x.Status == WorkScheduleEntryStatus.Planned &&
                                 x.Date.Date >= DateTime.Today && x.SlotKind != WorkScheduleSlotKind.Custom &&
                                 x.ReservesEmployees(DateTime.Today)))
                    {
                        var range = saved.GetRange(entry.SlotKind);
                        if (entry.StartMinutes == range.Start && entry.EndMinutes == range.End) continue;
                        entry.StartMinutes = range.Start;
                        entry.EndMinutes = range.End;
                        entry.SettingsRevision = saved.Revision;
                        entry.Revision++;
                    }
                }
            }
            PersistCache();
            return new WorkScheduleSettingsSnapshot(saved, true, updated);
        }, token);
    }

    public Task<List<WorkScheduleEntry>> SaveEntriesAsync(IReadOnlyList<WorkScheduleEntry> entries, CancellationToken token = default)
    {
        var copies = entries.Select(x => x.CreateValidatedCopy()).ToList();
        if (copies.Count == 0) throw new InvalidOperationException("Seleziona almeno una giornata da programmare.");
        return RunDatabaseAsync(async operationToken =>
        {
            var saved = await _repository.SaveWorkScheduleEntriesAsync(copies, operationToken).ConfigureAwait(false);
            lock (_cacheLock)
            {
                foreach (var page in _cache.Pages.Values)
                {
                    page.Entries.RemoveAll(x => saved.Any(y => y.Id == x.Id));
                    page.Entries.AddRange(saved.Where(x => x.Date >= page.From && x.Date < page.To).Select(x => x.CreateValidatedCopy()));
                    page.IsCurrent = false;
                }
            }
            PersistCache();
            return saved;
        }, token, prepareEmployees: true);
    }

    public Task<WorkScheduleEntry> CompleteEntryAsync(Guid id, long revision, CancellationToken token = default)
    {
        if (id == Guid.Empty || revision <= 0) throw WorkScheduleRules.Conflict();
        return RunDatabaseAsync(async operationToken =>
        {
            var completed = await _repository.CompleteWorkScheduleEntryAsync(id, revision, operationToken)
                .ConfigureAwait(false);
            lock (_cacheLock)
            {
                foreach (var page in _cache.Pages.Values)
                {
                    page.Entries.RemoveAll(entry => entry.Id == completed.Id);
                    if (completed.Date >= page.From && completed.Date < page.To)
                        page.Entries.Add(completed.CreateValidatedCopy());
                    page.IsCurrent = false;
                }
            }
            PersistCache();
            return completed;
        }, token);
    }

    public Task DeleteEntryAsync(Guid id, long revision, CancellationToken token = default) =>
        RunDatabaseAsync(async operationToken =>
        {
            await _repository.DeleteWorkScheduleEntryAsync(id, revision, operationToken).ConfigureAwait(false);
            lock (_cacheLock)
                foreach (var page in _cache.Pages.Values)
                {
                    page.Entries.RemoveAll(x => x.Id == id);
                    page.IsCurrent = false;
                }
            PersistCache();
            return true;
        }, token);

    private Task<T> RunDatabaseAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken token, bool prepareEmployees = false) =>
        Task.Run(async () =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            await _gate.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                EnsureIdentity();
                if (prepareEmployees && !_employeesPrepared)
                {
                    await _prepareEmployees(timeout.Token).ConfigureAwait(false);
                    _employeesPrepared = true;
                }
                await DatabaseOperationCoordinator.Gate.WaitAsync(timeout.Token).ConfigureAwait(false);
                try
                {
                    await _ensureReady(timeout.Token).ConfigureAwait(false);
                    EnsureIdentity();
                    return await operation(timeout.Token).ConfigureAwait(false);
                }
                finally { DatabaseOperationCoordinator.Gate.Release(); }
            }
            finally { _gate.Release(); }
        }, CancellationToken.None);

    private void EnsureIdentity()
    {
        if (!string.Equals(_databaseIdentity, _currentDatabaseIdentity(), StringComparison.Ordinal))
            throw new InvalidOperationException("La connessione al database è cambiata. Riavvia l'applicazione prima di usare il calendario condiviso.");
    }

    private void PersistCache()
    {
        try
        {
            string json;
            lock (_cacheLock) json = JsonSerializer.Serialize(_cache);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_cachePath))!);
            string temporary = _cachePath + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, _cachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[Schedule] Database state retained in memory; cache write failed: {ex.GetType().Name}");
        }
    }

    private static WorkScheduleSnapshot Clone(WorkScheduleSnapshot snapshot) =>
        JsonSerializer.Deserialize<WorkScheduleSnapshot>(JsonSerializer.Serialize(snapshot))!;

    private static string PageKey(DateTime from, DateTime to) => $"{from:yyyy-MM-dd}/{to:yyyy-MM-dd}";

    private static void ValidateRange(DateTime from, DateTime to)
    {
        if (from.Date >= to.Date) throw new ArgumentException("L'intervallo del calendario non è valido.");
    }

    private sealed class ScheduleCache
    {
        public int Version { get; set; }
        public string DatabaseIdentity { get; set; } = string.Empty;
        public WorkScheduleSettings Settings { get; set; } = new();
        public DateTime? SettingsUpdatedAtUtc { get; set; }
        public Dictionary<string, WorkScheduleSnapshot> Pages { get; set; } = [];
    }
}
