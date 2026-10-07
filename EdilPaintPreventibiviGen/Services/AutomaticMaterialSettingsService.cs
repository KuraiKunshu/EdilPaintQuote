using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace EdilPaintPreventibiviGen.Services;

public sealed class AutomaticMaterialSettingsService
{
    private readonly IAutomaticMaterialSettingsRepository _repository;
    private readonly string _cachePath;
    private readonly string _identity;
    private readonly Func<string> _currentIdentity;
    private readonly Func<CancellationToken, Task> _ensureReady;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AutomaticMaterialSettings? _legacy;
    private AutomaticMaterialSettings _cached;

    public AutomaticMaterialSettingsService(IAutomaticMaterialSettingsRepository repository, string cachePath,
        string identity, RealProfitSettingsModel local, Func<CancellationToken, Task>? ensureReady = null,
        Func<string>? currentIdentity = null)
    {
        _repository = repository; _cachePath = cachePath; _identity = identity;
        _currentIdentity = currentIdentity ?? (() => identity);
        _ensureReady = ensureReady ?? (_ => Task.CompletedTask);
        _cached = AutomaticMaterialSettings.FromLocal(local).CreateValidatedCopy();
        var defaults = new RealProfitSettingsModel(); defaults.Normalize();
        if (!AutomaticMaterialSettings.SameContent(_cached, AutomaticMaterialSettings.FromLocal(defaults)))
            _legacy = _cached.CreateValidatedCopy();
        try
        {
            if (File.Exists(cachePath))
            {
                var cached = JsonSerializer.Deserialize<Cache>(File.ReadAllText(cachePath));
                if (cached?.Identity == identity) _cached = cached.Settings.CreateValidatedCopy();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        { Debug.WriteLine($"[AutomaticMaterials] Cache unavailable: {ex.GetType().Name}"); }
    }

    public AutomaticMaterialSettingsSnapshot CachedSnapshot
    {
        get { EnsureIdentity(); lock (_gate) return new(_cached.CreateValidatedCopy(), false); }
    }

    public async Task<AutomaticMaterialSettingsSnapshot> GetLatestAsync(CancellationToken token = default)
    {
        try
        {
            return await RunAsync(async ct =>
            {
                var latest = await _repository.LoadAutomaticMaterialSettingsAsync(_legacy, ct).ConfigureAwait(false);
                return Update(latest);
            }, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AutomaticMaterials] Read failed: {ex.GetType().Name}");
            return CachedSnapshot;
        }
    }

    public Task<AutomaticMaterialSettingsSnapshot> SaveAsync(AutomaticMaterialSettings settings, CancellationToken token = default)
    {
        var copy = settings.CreateValidatedCopy();
        return RunAsync(async ct => Update(await _repository.SaveAutomaticMaterialSettingsAsync(copy, ct)
            .ConfigureAwait(false)), token);
    }

    private async Task<AutomaticMaterialSettingsSnapshot> RunAsync(
        Func<CancellationToken, Task<AutomaticMaterialSettingsSnapshot>> operation, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        await _gate.WaitAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            EnsureIdentity();
            await DatabaseOperationCoordinator.Gate.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                await _ensureReady(timeout.Token).ConfigureAwait(false); EnsureIdentity();
                return await operation(timeout.Token).ConfigureAwait(false);
            }
            finally { DatabaseOperationCoordinator.Gate.Release(); }
        }
        finally { _gate.Release(); }
    }

    private AutomaticMaterialSettingsSnapshot Update(AutomaticMaterialSettings settings)
    {
        var copy = settings.CreateValidatedCopy(); copy.CatalogIdentity = _identity;
        lock (_gate) _cached = copy;
        try
        {
            var directory = Path.GetDirectoryName(_cachePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temporary = _cachePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Cache { Identity = _identity, Settings = copy }));
            File.Move(temporary, _cachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Debug.WriteLine($"[AutomaticMaterials] Cache write failed: {ex.GetType().Name}"); }
        return new(copy.CreateValidatedCopy(), true);
    }

    private void EnsureIdentity()
    {
        if (_identity != _currentIdentity())
            throw new InvalidOperationException("La connessione è cambiata: riavvia l'applicazione prima di aggiornare i materiali automatici.");
    }
    private sealed class Cache
    {
        public string Identity { get; set; } = "";
        public AutomaticMaterialSettings Settings { get; set; } = new();
    }
}
