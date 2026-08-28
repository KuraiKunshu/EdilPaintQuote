using System.Diagnostics;
using System.IO;

namespace EdilPaintPreventibiviGen.Services;

public static class UpdaterLauncherService
{
    private const string UpdaterScriptName = "Update-EdilPaint.ps1";
    private const string UpdaterLocationFileName = "updater-path.txt";
    private const string UpdaterPathEnvironmentVariable = "EDILPAINT_UPDATER_PATH";

    public static string? ResolveUpdaterScriptPath(string? baseDirectory = null)
    {
        baseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? AppContext.BaseDirectory
            : Path.GetFullPath(baseDirectory);

        foreach (string directory in EnumerateCandidateDirectories(baseDirectory))
        {
            string? configuredPath = ReadConfiguredUpdaterPath(directory);
            string? configuredCandidate = ResolveConfiguredLocation(configuredPath, directory);
            if (configuredCandidate != null && File.Exists(configuredCandidate))
                return configuredCandidate;
        }

        string? environmentCandidate = ResolveConfiguredLocation(
            Environment.GetEnvironmentVariable(UpdaterPathEnvironmentVariable),
            baseDirectory);
        if (environmentCandidate != null && File.Exists(environmentCandidate))
            return environmentCandidate;

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

        foreach (string directory in EnumerateStandardUpdaterDirectories(baseDirectory))
        {
            string candidate = Path.Combine(directory, UpdaterScriptName);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        return null;
    }

    public static void StartUpdater(string scriptPath, int windowCloseDelaySeconds = 0)
    {
        if (string.IsNullOrWhiteSpace(scriptPath) || !File.Exists(scriptPath))
            throw new FileNotFoundException("Script updater non trovato.", scriptPath);

        string? workingDirectory = Path.GetDirectoryName(scriptPath);
        if (string.IsNullOrWhiteSpace(workingDirectory))
            workingDirectory = AppContext.BaseDirectory;

        string arguments = $"-NoProfile -ExecutionPolicy Bypass -File {QuoteArgument(scriptPath)}";
        if (windowCloseDelaySeconds > 0)
            arguments += $" -WindowCloseDelaySeconds {windowCloseDelaySeconds}";

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = arguments,
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

    private static IEnumerable<string> EnumerateStandardUpdaterDirectories(string baseDirectory)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string? directory in new[]
        {
            CombineRootWithUpdaterFolder(Path.GetPathRoot(baseDirectory)),
            CombineRootWithUpdaterFolder(Path.GetPathRoot(Environment.SystemDirectory)),
            CombineRootWithUpdaterFolder(Environment.GetEnvironmentVariable("SystemDrive")),
            CombineFolderWithUpdaterFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)),
            CombineFolderWithUpdaterFolder(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
        })
        {
            if (!string.IsNullOrWhiteSpace(directory) && seen.Add(directory))
                yield return directory;
        }
    }

    private static string? ReadConfiguredUpdaterPath(string directory)
    {
        try
        {
            string locationFile = Path.Combine(directory, UpdaterLocationFileName);
            return File.Exists(locationFile)
                ? File.ReadAllText(locationFile).Trim()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveConfiguredLocation(string? location, string relativeTo)
    {
        if (string.IsNullOrWhiteSpace(location))
            return null;

        try
        {
            string expanded = Environment.ExpandEnvironmentVariables(location)
                .Trim()
                .Trim('"');
            string fullPath = Path.IsPathFullyQualified(expanded)
                ? Path.GetFullPath(expanded)
                : Path.GetFullPath(Path.Combine(relativeTo, expanded));

            return string.Equals(Path.GetExtension(fullPath), ".ps1", StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : Path.Combine(fullPath, UpdaterScriptName);
        }
        catch
        {
            return null;
        }
    }

    private static string? CombineRootWithUpdaterFolder(string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return null;

        try
        {
            return Path.GetFullPath(Path.Combine(root, "EdilPaintUpdater"));
        }
        catch
        {
            return null;
        }
    }

    private static string? CombineFolderWithUpdaterFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return null;

        try
        {
            return Path.GetFullPath(Path.Combine(folder, "EdilPaintUpdater"));
        }
        catch
        {
            return null;
        }
    }

    private static string QuoteArgument(string value)
        => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
