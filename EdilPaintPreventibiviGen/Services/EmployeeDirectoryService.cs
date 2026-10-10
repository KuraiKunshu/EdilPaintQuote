using System.Diagnostics;
using System.IO;
using System.Text.Json;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public sealed record EmployeeDirectorySnapshot(IReadOnlyList<EmployeeSettingsModel> Employees, bool IsCurrent);

/// <summary>Shared database directory, with a scoped read-only cache for offline worksheets.</summary>
public sealed class EmployeeDirectoryService
{
    private readonly IEmployeeRepository _repository;
    private readonly string _cachePath;
    private readonly string _databaseIdentity;
    private readonly Func<string> _currentDatabaseIdentity;
    private readonly Func<CancellationToken, Task> _ensureReady;
    private readonly IReadOnlyList<EmployeeSettingsModel> _legacy;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _cacheLock = new();
    private List<EmployeeSettingsModel> _cached = [];
    private bool _imported;

    public EmployeeDirectoryService(IEmployeeRepository repository, string cachePath, string databaseIdentity,
        IEnumerable<EmployeeSettingsModel> legacy, Func<CancellationToken, Task>? ensureReady = null,
        Func<string>? currentDatabaseIdentity = null)
    {
        _repository = repository;
        _cachePath = cachePath;
        _databaseIdentity = databaseIdentity;
        _currentDatabaseIdentity = currentDatabaseIdentity ?? (() => databaseIdentity);
        _ensureReady = ensureReady ?? (_ => Task.CompletedTask);
        _legacy = legacy.Where(x => !string.IsNullOrWhiteSpace(x.FirstName))
            .Select(x => x.CreateValidatedCopy()).ToList();
        _cached = _legacy.Select(x => x.CreateValidatedCopy()).ToList();
        try
        {
            if (File.Exists(cachePath))
            {
                var cache = JsonSerializer.Deserialize<EmployeeCache>(File.ReadAllText(cachePath));
                // An intentionally empty cached list must not revive old local employees.
                _cached = cache?.DatabaseIdentity == databaseIdentity
                    ? cache.Employees.Select(x => x.CreateValidatedCopy()).ToList() : [];
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            Debug.WriteLine($"[Employees] Cache unavailable: {ex.GetType().Name}");
        }
    }

    public EmployeeDirectorySnapshot CachedSnapshot
    {
        get
        {
            EnsureDatabaseIdentity();
            lock (_cacheLock)
                return new(_cached.Select(x => x.CreateValidatedCopy()).ToList(), false);
        }
    }

    public async Task<EmployeeDirectorySnapshot> GetLatestAsync(CancellationToken token = default)
    {
        try { return await RefreshAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Employees] Refresh failed: {ex.GetType().Name}");
            return CachedSnapshot;
        }
    }

    public Task<EmployeeDirectorySnapshot> RefreshAsync(CancellationToken token = default) =>
        Task.Run(() => RefreshCoreAsync(token), CancellationToken.None);

    private async Task<EmployeeDirectorySnapshot> RefreshCoreAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await _gate.WaitAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            EnsureDatabaseIdentity();
            await DatabaseOperationCoordinator.Gate.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                await _ensureReady(timeout.Token).ConfigureAwait(false);
                EnsureDatabaseIdentity();
                await ImportAsync(timeout.Token).ConfigureAwait(false);
                var employees = await _repository.GetEmployeesAsync(timeout.Token).ConfigureAwait(false);
                UpdateCache(employees);
                return new(employees, true);
            }
            finally { DatabaseOperationCoordinator.Gate.Release(); }
        }
        finally { _gate.Release(); }
    }

    public Task<EmployeeDirectorySnapshot> SaveAsync(IReadOnlyList<EmployeeSettingsModel> original,
        IReadOnlyList<EmployeeSettingsModel> edited, CancellationToken token = default) =>
        Task.Run(() => SaveCoreAsync(original, edited, token), CancellationToken.None);

    private async Task<EmployeeDirectorySnapshot> SaveCoreAsync(IReadOnlyList<EmployeeSettingsModel> original,
        IReadOnlyList<EmployeeSettingsModel> edited, CancellationToken token)
    {
        var changes = EmployeeChanges.Between(original, edited);
        if (changes.Count == 0) return CachedSnapshot;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        await _gate.WaitAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            EnsureDatabaseIdentity();
            await DatabaseOperationCoordinator.Gate.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                await _ensureReady(timeout.Token).ConfigureAwait(false);
                EnsureDatabaseIdentity();
                await ImportAsync(timeout.Token).ConfigureAwait(false);
                await _repository.SaveEmployeesAsync(original, edited, timeout.Token).ConfigureAwait(false);

                // The database write succeeded. Retain that fact even if the following
                // read/cache write fails, so the UI does not report a failed shared save.
                var cached = CachedSnapshot.Employees.ToDictionary(x => x.Id);
                foreach (var change in changes)
                {
                    cached.Remove(change.Id);
                    if (change.Updated is { } updated)
                    {
                        var copy = updated.CreateValidatedCopy();
                        copy.Revision = (change.Original?.Revision ?? 0) + 1;
                        cached.Add(copy.Id, copy);
                    }
                }
                UpdateCache(cached.Values.ToList());
                try
                {
                    var employees = await _repository.GetEmployeesAsync(timeout.Token).ConfigureAwait(false);
                    UpdateCache(employees);
                    return new(employees, true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Employees] Saved; subsequent refresh failed: {ex.GetType().Name}");
                    return CachedSnapshot;
                }
            }
            finally { DatabaseOperationCoordinator.Gate.Release(); }
        }
        finally { _gate.Release(); }
    }

    private async Task ImportAsync(CancellationToken token)
    {
        if (_imported) return;
        await _repository.ImportLegacyEmployeesAsync(_legacy, token).ConfigureAwait(false);
        _imported = true;
    }

    private void EnsureDatabaseIdentity()
    {
        if (!string.Equals(_databaseIdentity, _currentDatabaseIdentity(), StringComparison.Ordinal))
            throw new InvalidOperationException("La connessione al database è cambiata. Riavvia l'applicazione prima di gestire i dipendenti.");
    }

    private void UpdateCache(List<EmployeeSettingsModel> employees)
    {
        lock (_cacheLock)
            _cached = employees.Select(x => x.CreateValidatedCopy()).ToList();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_cachePath))!);
            string temporary = _cachePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new EmployeeCache
            {
                DatabaseIdentity = _databaseIdentity, Employees = employees
            }));
            File.Move(temporary, _cachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[Employees] Database data retained in memory; cache write failed: {ex.GetType().Name}");
        }
    }

    private sealed class EmployeeCache
    {
        public string DatabaseIdentity { get; set; } = string.Empty;
        public List<EmployeeSettingsModel> Employees { get; set; } = [];
    }
}
