using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EdilPaintPreventibiviGen.Services;

public static class UpdaterLauncherService
{
    private const string UpdaterScriptName = "Update-EdilPaint.ps1";
    private const string UpdaterLocationFileName = "updater-path.txt";
    private const string UpdaterPathEnvironmentVariable = "EDILPAINT_UPDATER_PATH";
    private const string BundledUpdaterFolderName = "updater";

    public static string? ResolveUpdaterScriptPath(string? baseDirectory = null)
    {
        if (CompanyInstallationService.IsGenericInstallation) return null;
        baseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? AppContext.BaseDirectory
            : Path.GetFullPath(baseDirectory);

        string localScript = UpdaterConfigurationService.GetLocalScriptPath(baseDirectory);
        if (File.Exists(localScript) && File.Exists(Path.Combine(Path.GetDirectoryName(localScript)!, UpdaterConfigurationService.SettingsName)))
            return localScript;

        var candidates = new List<string>();
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
                Path.Combine(BundledUpdaterFolderName, UpdaterScriptName),
                Path.Combine("tools", "updater", UpdaterScriptName)
            })
            {
                string candidate = Path.GetFullPath(Path.Combine(directory, relativePath));
                if (File.Exists(candidate))
                    candidates.Add(candidate);
            }
        }

        foreach (string directory in EnumerateStandardUpdaterDirectories(baseDirectory))
        {
            string candidate = Path.Combine(directory, UpdaterScriptName);
            if (File.Exists(candidate))
                candidates.Add(Path.GetFullPath(candidate));
        }

        return SelectUpdaterCandidate(candidates, baseDirectory);
    }

    internal static string? SelectUpdaterCandidate(IEnumerable<string> candidates, string applicationDirectory)
    {
        var existing = candidates.Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists).ToArray();
        foreach (string candidate in existing)
        {
            try { _ = UpdaterConfigurationService.Read(candidate, applicationDirectory); return candidate; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException) { }
        }
        return existing.FirstOrDefault();
    }

    public static async Task<bool> StartUpdaterAsync(string scriptPath, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(scriptPath) || !File.Exists(scriptPath))
            throw new FileNotFoundException("Script updater non trovato.", scriptPath);

        ValidateManualUpdateSettings(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(scriptPath))!, "updater-settings.json"), AppContext.BaseDirectory);
        string session = Path.Combine(Path.GetTempPath(), "EdilPaintUpdater", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(session);
        string ready = Path.Combine(session, "ready.json");
        bool handedOff = false;
        Process? process = null;
        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                Arguments = BuildManualUpdaterArguments(scriptPath, 60, AppContext.BaseDirectory, Environment.ProcessId, ready),
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(scriptPath)),
                UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            }) ?? throw new InvalidOperationException("Impossibile avviare il processo updater.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMinutes(20));
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (File.Exists(ready))
                {
                    using var result = JsonDocument.Parse(await File.ReadAllTextAsync(ready, timeout.Token));
                    string? status = result.RootElement.GetProperty("Status").GetString();
                    if (status == "ready") { handedOff = true; return true; }
                    if (status == "up-to-date") return false;
                    string message = result.RootElement.GetProperty("Message").GetString() ?? "Aggiornamento non riuscito.";
                    string log = result.RootElement.GetProperty("LogPath").GetString() ?? "";
                    throw new InvalidOperationException($"{message}\n\nLog: {log}");
                }
                if (process.HasExited)
                {
                    if (File.Exists(ready)) continue;
                    throw new InvalidOperationException($"L’updater è terminato prima di preparare l’aggiornamento (codice {process.ExitCode}). Il programma rimane aperto.");
                }
                await Task.Delay(250, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new InvalidOperationException("La preparazione dell’aggiornamento ha superato 20 minuti. Nessun file del programma è stato sostituito. Controlla la connessione e il log dell’updater."); }
        finally
        {
            if (!handedOff && process != null)
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            process?.Dispose();
            try { Directory.Delete(session, recursive: true); } catch { }
        }
    }

    public static void ValidateManualUpdateSettings(string settingsPath, string applicationDirectory)
    {
        using var settings = JsonDocument.Parse(File.ReadAllText(settingsPath));
        string? configured = settings.RootElement.TryGetProperty("InstallPath", out var path) ? path.GetString() : null;
        if (string.IsNullOrWhiteSpace(configured)) throw new InvalidOperationException("InstallPath non è configurato nell’updater.");
        string expanded = Environment.ExpandEnvironmentVariables(configured);
        string install = Path.GetFullPath(Path.IsPathRooted(expanded) ? expanded : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath))!, expanded));
        if (!string.Equals(install.TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(applicationDirectory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"L’updater aggiorna {install}, ma il programma aperto si trova in {applicationDirectory}. Correggi InstallPath: il programma rimane aperto.");
        if (settings.RootElement.TryGetProperty("ProcessName", out var name) && !string.IsNullOrWhiteSpace(name.GetString()) &&
            !string.Equals(name.GetString(), typeof(App).Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("ProcessName nell’updater non corrisponde al programma aperto.");
    }

    public static string BuildManualUpdaterArguments(string scriptPath, int waitForApplicationExitSeconds = 60,
        string? expectedInstallPath = null, int applicationProcessId = 0, string? readyPath = null)
    {
        if (string.IsNullOrWhiteSpace(scriptPath))
            throw new ArgumentException("Il percorso dello script updater è obbligatorio.", nameof(scriptPath));

        if (waitForApplicationExitSeconds is < 0 or > 300)
            throw new ArgumentOutOfRangeException(
                nameof(waitForApplicationExitSeconds),
                "L’attesa per la chiusura deve essere fra 0 e 300 secondi.");

        string fullPath = Path.GetFullPath(scriptPath);
        string arguments = $"-NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File {QuoteArgument(fullPath)} -ManualUpdate -WaitForApplicationExitSeconds {waitForApplicationExitSeconds}";
        if (expectedInstallPath != null) arguments += $" -ExpectedInstallPath {QuoteArgument(Path.GetFullPath(expectedInstallPath).TrimEnd(Path.DirectorySeparatorChar))}";
        if (applicationProcessId > 0) arguments += $" -ApplicationProcessId {applicationProcessId}";
        if (readyPath != null) arguments += $" -ReadyPath {QuoteArgument(Path.GetFullPath(readyPath))}";
        return arguments;
    }

    /// <summary>
    /// Aggiorna lo script installato con la copia distribuita assieme all'app.
    /// Le impostazioni restano esterne allo script, quindi non vengono toccate.
    /// </summary>
    public static bool RefreshUpdaterScriptFromBundledCopy(string updaterScriptPath, string? baseDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(updaterScriptPath))
            throw new ArgumentException("Il percorso dello script updater è obbligatorio.", nameof(updaterScriptPath));

        baseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? AppContext.BaseDirectory
            : Path.GetFullPath(baseDirectory);

        string bundledScriptPath = Path.Combine(baseDirectory, BundledUpdaterFolderName, UpdaterScriptName);
        if (!File.Exists(bundledScriptPath))
            return false;

        string targetPath = Path.GetFullPath(updaterScriptPath);
        if (string.Equals(bundledScriptPath, targetPath, StringComparison.OrdinalIgnoreCase))
            return false;

        static int Protocol(string path)
        {
            var match = Regex.Match(File.ReadAllText(path), @"(?m)^# EdilPaint updater protocol: (\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }
        if (File.Exists(targetPath) && Protocol(targetPath) > Protocol(bundledScriptPath)) return false;
        if (File.Exists(targetPath) && File.ReadAllBytes(targetPath).SequenceEqual(File.ReadAllBytes(bundledScriptPath))) return false;
        string temporary = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(bundledScriptPath, temporary);
            File.Move(temporary, targetPath, overwrite: true);
            return true;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static bool IsInstallationUpdateInProgress(string directory)
    {
        if (File.Exists(Path.Combine(directory, ".edilpaint-update-transaction.json"))) return true;
        string path = Path.Combine(directory, ".edilpaint-install.lock");
        if (!File.Exists(path)) return false;
        try { using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return false; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return false; }
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
