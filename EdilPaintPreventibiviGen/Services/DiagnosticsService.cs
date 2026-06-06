using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using EdilPaintPreventibiviGen;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public static class DiagnosticsService
{
    public static DiagnosticsSnapshot CreateSnapshot()
    {
        string localDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EdilPaintPreventivi",
            "Data");

        var updater = ReadUpdaterState();
        var assembly = Assembly.GetExecutingAssembly();
        string executablePath = AppDomain.CurrentDomain.BaseDirectory;
        string version = assembly.GetName().Version?.ToString() ?? "non disponibile";

        try
        {
            string? location = assembly.Location;
            if (!string.IsNullOrWhiteSpace(location))
            {
                var info = FileVersionInfo.GetVersionInfo(location);
                if (!string.IsNullOrWhiteSpace(info.ProductVersion))
                    version = info.ProductVersion;
            }
        }
        catch
        {
        }

        var database = App.AppSettings.Database;
        string databaseStatus = !database.IsConfigured
            ? "Non configurato"
            : App.DataService.CanSynchronize
                ? "Online"
                : "Offline, uso dati locali";

        var deletes = CountDeletes(localDataPath);

        return new DiagnosticsSnapshot
        {
            AppVersion = version,
            ExecutablePath = executablePath,
            SettingsPath = App.AppSettings.SettingsPath,
            SettingsDirectory = Path.GetDirectoryName(App.AppSettings.SettingsPath) ?? string.Empty,
            PdfRootPath = App.AppSettings.PdfStorage.RootPath,
            LocalDataPath = localDataPath,
            DatabaseStatus = databaseStatus,
            SyncStatus = App.SyncService.IsSyncRunning ? "In corso" : App.SyncService.LastSyncSummary,
            LastSync = App.SyncService.LastSyncCompletedUtc?.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") ?? "Mai",
            UpdaterStatus = updater.status,
            UpdaterStatePath = updater.path,
            PdfTemplateName = App.AppSettings.PdfTemplate.ActiveTemplate,
            PendingPdfs = CountFiles(Path.Combine(localDataPath, "PendingPdfs"), "*.pdf"),
            PendingAttachments = CountFiles(Path.Combine(localDataPath, "PendingAttachments"), "*.json"),
            PendingCostsPdfs = CountFiles(Path.Combine(localDataPath, "PendingCostsPdfs"), "*.json"),
            PendingQuotePatches = CountFiles(Path.Combine(localDataPath, "PendingQuotePatches"), "*.json"),
            PendingQuoteDeletes = deletes.quotes,
            PendingCustomerDeletes = deletes.customers
        };
    }

    private static int CountFiles(string path, string pattern)
    {
        try
        {
            return Directory.Exists(path)
                ? Directory.EnumerateFiles(path, pattern).Count()
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static (int quotes, int customers) CountDeletes(string localDataPath)
    {
        string path = Path.Combine(localDataPath, "PendingDeletes.json");
        if (!File.Exists(path))
            return (0, 0);

        try
        {
            string json = File.ReadAllText(path);
            var pending = JsonSerializer.Deserialize<PendingDeletes>(json);
            return (pending?.Quotes.Count ?? 0, pending?.Customers.Count ?? 0);
        }
        catch
        {
            return (0, 0);
        }
    }

    private static (string status, string path) ReadUpdaterState()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] candidates =
        [
            Path.GetFullPath(Path.Combine(baseDir, "updater", "state", "deployed-commit.txt")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "updater", "state", "deployed-commit.txt")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "state", "deployed-commit.txt"))
        ];

        foreach (string path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(path))
                    continue;

                string commit = File.ReadAllText(path).Trim();
                if (commit.Length > 12)
                    commit = commit[..12];

                return (string.IsNullOrWhiteSpace(commit) ? "Commit non leggibile" : $"Commit installato {commit}", path);
            }
            catch
            {
                return ("Commit updater non leggibile", path);
            }
        }

        return ("Updater non rilevato vicino al programma", string.Empty);
    }
}
