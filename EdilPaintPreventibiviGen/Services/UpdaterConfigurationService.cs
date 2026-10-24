using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EdilPaintPreventibiviGen.Services;

public sealed record UpdaterConfiguration(string ScriptPath, string RepositoryUrl, string Branch, bool RunTests);

/// <summary>Configuration belongs to one installation, independently of its write permissions.</summary>
public static class UpdaterConfigurationService
{
    public const string DefaultRepository = "https://github.com/KuraiKunshu/EdilPaintQuote.git";
    public const string ScriptName = "Update-EdilPaint.ps1";
    public const string SettingsName = "updater-settings.json";

    public static string GetLocalScriptPath(string applicationDirectory, string? localAppData = null)
    {
        string identity = Path.GetFullPath(applicationDirectory).TrimEnd('\\', '/').ToUpperInvariant();
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        return Path.Combine(localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EdilPaintUpdater", "installations", key, ScriptName);
    }

    public static UpdaterConfiguration Read(string scriptPath, string applicationDirectory)
    {
        string settingsPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(scriptPath))!, SettingsName);
        UpdaterLauncherService.ValidateManualUpdateSettings(settingsPath, applicationDirectory);
        var settings = JsonNode.Parse(File.ReadAllText(settingsPath))?.AsObject()
            ?? throw new InvalidOperationException("Configurazione aggiornamenti vuota.");
        return new(scriptPath, settings["RepositoryUrl"]?.GetValue<string>() ?? DefaultRepository,
            settings["Branch"]?.GetValue<string>() ?? "main", settings["RunTests"]?.GetValue<bool>() ?? true);
    }

    public static string Save(string applicationDirectory, string repositoryUrl, string branch, bool runTests,
        string? existingScriptPath = null, string? localAppData = null)
    {
        if (CompanyInstallationService.IsGenericInstallation)
            throw new InvalidOperationException("Questa installazione si aggiorna tramite il pacchetto della propria azienda.");
        repositoryUrl = repositoryUrl.Trim();
        branch = branch.Trim();
        ValidateSource(repositoryUrl, branch);
        applicationDirectory = Path.GetFullPath(applicationDirectory);
        string bundled = Path.Combine(applicationDirectory, "updater", ScriptName);
        if (!File.Exists(bundled))
            throw new FileNotFoundException("Lo script di aggiornamento manca dal programma. Ripristina la cartella updater dal pacchetto di installazione.", bundled);

        string scriptPath = GetLocalScriptPath(applicationDirectory, localAppData);
        JsonObject settings = new();
        if (existingScriptPath != null)
        {
            // Only edit a configuration that already belongs to this installation.
            _ = Read(existingScriptPath, applicationDirectory);
            scriptPath = Path.GetFullPath(existingScriptPath);
            settings = JsonNode.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(scriptPath)!, SettingsName)))!.AsObject();
        }

        string directory = Path.GetDirectoryName(scriptPath)!;
        settings["RepositoryUrl"] = repositoryUrl;
        settings["Branch"] = branch;
        settings["InstallPath"] = applicationDirectory;
        settings["ProcessName"] = typeof(App).Assembly.GetName().Name;
        settings["RunTests"] = runTests;
        settings["SourcePath"] ??= Path.Combine(directory, "source");
        settings["ProjectPath"] ??= "EdilPaintPreventibiviGen\\EdilPaintPreventibiviGen.csproj";
        settings["TestProjectPath"] ??= "EdilPaintPreventibiviGen.Tests\\EdilPaintPreventibiviGen.Tests.csproj";
        settings["Configuration"] ??= "Release";
        settings["RuntimeIdentifier"] ??= "win-x64";
        settings["SelfContained"] ??= true;
        settings["StartDelaySeconds"] ??= 60;
        settings["LogPath"] ??= Path.Combine(directory, "logs", "update.log");

        string source = Environment.ExpandEnvironmentVariables(settings["SourcePath"]!.GetValue<string>());
        source = Path.GetFullPath(Path.IsPathRooted(source) ? source : Path.Combine(directory, source));
        string install = applicationDirectory.TrimEnd('\\', '/');
        source = source.TrimEnd('\\', '/');
        if (string.Equals(source, install, StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith(install + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            install.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La cartella dei sorgenti dell’updater deve essere separata dalla cartella del programma.");

        Directory.CreateDirectory(directory);
        UpdaterLauncherService.RefreshUpdaterScriptFromBundledCopy(scriptPath, applicationDirectory);
        string settingsPath = Path.Combine(directory, SettingsName);
        string temporary = settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            UpdaterLauncherService.ValidateManualUpdateSettings(temporary, applicationDirectory);
            File.Move(temporary, settingsPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return scriptPath;
    }

    internal static void ValidateSource(string repositoryUrl, string branch)
    {
        if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "ssh") || string.IsNullOrWhiteSpace(uri.Host) ||
            repositoryUrl.Any(char.IsWhiteSpace) || (uri.Scheme == "https" && uri.UserInfo.Length > 0))
            throw new InvalidOperationException("Inserisci un indirizzo repository HTTPS o SSH valido, senza password nell’indirizzo.");
        if (string.IsNullOrWhiteSpace(branch) || branch is "@" || branch.StartsWith('-') ||
            branch.Contains("..") || branch.Contains("@{") ||
            branch.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || "~^:?*[\\".Contains(c)) ||
            branch.Split('/').Any(part => part.Length == 0 || part.StartsWith('.') || part.EndsWith('.') || part.EndsWith(".lock")))
            throw new InvalidOperationException("Inserisci un nome di branch valido, per esempio main.");
    }

    public static bool IsDevelopmentDirectory(string applicationDirectory)
    {
        for (var directory = new DirectoryInfo(applicationDirectory); directory != null; directory = directory.Parent)
            if ((directory.Name is "bin" or "obj") && directory.Parent != null &&
                directory.Parent.EnumerateFiles("*.csproj").Any()) return true;
        return false;
    }

    public static string DescribeVersion(string applicationDirectory)
    {
        string version = $"Versione {typeof(App).Assembly.GetName().Version}";
        string path = Path.Combine(applicationDirectory, "edilpaint-update.json");
        if (!File.Exists(path)) return version;
        try
        {
            var manifest = JsonNode.Parse(File.ReadAllText(path));
            string commit = manifest?["Commit"]?.GetValue<string>() ?? "";
            string branch = manifest?["Branch"]?.GetValue<string>() ?? "";
            return $"{version} · {branch} · {commit[..Math.Min(commit.Length, 10)]}";
        }
        catch { return version; }
    }
}
