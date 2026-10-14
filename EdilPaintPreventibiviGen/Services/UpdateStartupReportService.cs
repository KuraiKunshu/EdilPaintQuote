using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace EdilPaintPreventibiviGen.Services;

internal sealed class UpdateStartupReportService
{
    internal const string ArgumentName = "--update-startup-result";
    private readonly string _resultPath;
    private bool _reported;

    private UpdateStartupReportService(string resultPath) => _resultPath = resultPath;

    internal static UpdateStartupReportService? FromArguments(string[] arguments, string? startupRoot = null)
    {
        int index = Array.IndexOf(arguments, ArgumentName);
        if (index < 0 || index + 1 >= arguments.Length) return null;
        startupRoot ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EdilPaintUpdater", "startup");
        try
        {
            string root = Path.GetFullPath(startupRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(arguments[index + 1]);
            string? directory = Path.GetDirectoryName(path);
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(path) != "startup.json" || directory == null ||
                !Guid.TryParseExact(Path.GetFileName(directory), "N", out _) ||
                !string.Equals(Path.GetDirectoryName(directory) + Path.DirectorySeparatorChar, root, StringComparison.OrdinalIgnoreCase))
                return null;
            return new UpdateStartupReportService(path);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { return null; }
    }

    internal void ReportReady() => Report("ready", "Finestra principale aperta.", "", false);

    internal void ReportFailure(string message, string logPath = "", bool dialogShown = false) =>
        Report("failed", message, logPath, dialogShown);

    private void Report(string status, string message, string logPath, bool dialogShown)
    {
        if (_reported) return;
        string temporary = _resultPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_resultPath)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(new
            {
                Status = status, Message = message, LogPath = logPath,
                ProcessId = Environment.ProcessId, DialogShown = dialogShown, UpdatedAtUtc = DateTime.UtcNow
            }));
            File.Move(temporary, _resultPath, overwrite: true);
            _reported = true;
        }
        catch (Exception ex) { Debug.WriteLine($"[STARTUP] Impossibile comunicare l’esito all’updater: {ex}"); }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) { Debug.WriteLine(ex); }
        }
    }

    internal static string WriteFailureLog(Exception exception, string? logDirectory = null)
    {
        try
        {
            logDirectory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EdilPaintUpdater", "startup-errors");
            Directory.CreateDirectory(logDirectory);
            string path = Path.Combine(logDirectory, $"startup-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}-{Guid.NewGuid():N}.log");
            File.WriteAllText(path, $"{DateTime.UtcNow:O}\nProgramma: {AppContext.BaseDirectory}\n{exception}");
            return path;
        }
        catch (Exception ex) { Debug.WriteLine($"[STARTUP] Errore di scrittura log: {ex}"); return ""; }
    }
}
