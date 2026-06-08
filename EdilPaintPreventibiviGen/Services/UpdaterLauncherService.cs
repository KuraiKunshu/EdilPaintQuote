using System.Diagnostics;
using System.IO;

namespace EdilPaintPreventibiviGen.Services;

public static class UpdaterLauncherService
{
    private const string UpdaterScriptName = "Update-EdilPaint.ps1";

    public static string? ResolveUpdaterScriptPath()
    {
        string baseDirectory = AppContext.BaseDirectory;

        foreach (string directory in EnumerateCandidateDirectories(baseDirectory))
        {
            foreach (string relativePath in new[]
            {
                UpdaterScriptName,
                Path.Combine("updater", UpdaterScriptName),
                Path.Combine("tools", "updater", UpdaterScriptName)
            })
            {
                string candidate = Path.GetFullPath(Path.Combine(directory, relativePath));
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    public static void StartUpdater(string scriptPath)
    {
        if (string.IsNullOrWhiteSpace(scriptPath) || !File.Exists(scriptPath))
            throw new FileNotFoundException("Script updater non trovato.", scriptPath);

        string? workingDirectory = Path.GetDirectoryName(scriptPath);
        if (string.IsNullOrWhiteSpace(workingDirectory))
            workingDirectory = AppContext.BaseDirectory;

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File {QuoteArgument(scriptPath)}",
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal
        });

        if (process == null)
            throw new InvalidOperationException("Impossibile avviare il processo updater.");
    }

    private static IEnumerable<string> EnumerateCandidateDirectories(string startDirectory)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DirectoryInfo? current = new DirectoryInfo(startDirectory);

        while (current != null)
        {
            if (seen.Add(current.FullName))
                yield return current.FullName;

            current = current.Parent;
        }
    }

    private static string QuoteArgument(string value)
        => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
