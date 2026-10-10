using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

/// <summary>Local UI preferences; failed disk writes remain usable for this session.</summary>
public sealed class WindowSizeStore
{
    private const int CurrentVersion = 1;
    private const int MaximumKeyLength = 512;
    private static readonly ConcurrentDictionary<string, object> PathLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;
    private readonly object _sync;
    private Dictionary<string, WindowSizePreference> _preferences = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WindowSizePreference> _pending = new(StringComparer.Ordinal);

    public WindowSizeStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _sync = PathLocks.GetOrAdd(_path, _ => new object());
    }

    public WindowSizePreference? Get(string key)
    {
        if (!IsValidKey(key)) return null;
        lock (_sync)
        {
            RefreshFromDisk();
            return _preferences.TryGetValue(key, out var preference) ? preference.CreateCopy() : null;
        }
    }

    /// <returns>True when the snapshot was also written to disk.</returns>
    public bool Save(string key, WindowSizePreference preference)
    {
        if (!IsValidKey(key) || preference == null) return false;
        var snapshot = preference.CreateCopy();
        if (!snapshot.IsValid) return false;

        lock (_sync)
        {
            RefreshFromDisk();
            _preferences[key] = snapshot;
            _pending[key] = snapshot.CreateCopy();
            string temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var envelope = new { Version = CurrentVersion, Windows = _preferences };
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(envelope, JsonOptions));
                File.Move(temporaryPath, _path, overwrite: true);
                _pending.Clear();
                return true;
            }
            catch (Exception ex) when (IsStorageError(ex))
            {
                Log("save", ex);
                return false;
            }
            finally
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                catch (Exception ex) when (IsStorageError(ex)) { Log("cleanup", ex); }
            }
        }
    }

    private void RefreshFromDisk()
    {
        try
        {
            if (!File.Exists(_path)) return;
            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("Version", out var version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out int schemaVersion) || schemaVersion != CurrentVersion
                || !root.TryGetProperty("Windows", out var windows) || windows.ValueKind != JsonValueKind.Object)
                return;

            var validPreferences = new Dictionary<string, WindowSizePreference>(StringComparer.Ordinal);
            foreach (var item in windows.EnumerateObject())
            {
                if (!IsValidKey(item.Name)) continue;
                try
                {
                    var preference = item.Value.Deserialize<WindowSizePreference>(JsonOptions);
                    if (preference is { IsValid: true }) validPreferences[item.Name] = preference;
                }
                catch (JsonException) { /* Ignore just the invalid entry. */ }
            }
            _preferences = validPreferences;
        }
        catch (Exception ex) when (IsStorageError(ex)) { Log("load", ex); }
        finally
        {
            foreach (var item in _pending) _preferences[item.Key] = item.Value.CreateCopy();
        }
    }

    private static bool IsValidKey(string? key) => !string.IsNullOrWhiteSpace(key) && key.Length <= MaximumKeyLength;
    private static bool IsStorageError(Exception exception) => exception is IOException
        or UnauthorizedAccessException or JsonException or System.Security.SecurityException
        or NotSupportedException or ArgumentException;
    private static void Log(string operation, Exception exception)
        => Debug.WriteLine($"[WindowSize] {operation} failed: {exception.GetType().Name}");
}
