using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class UpdaterLauncherServiceTests
{
    [Fact]
    public void ScheduledTaskRegistrationPassesTheActualDefinitionToWindows()
    {
        var folder = new TaskFolderStub();
        var definition = new object();
        UpdaterAutoUpdateService.RegisterTask(folder, definition, "DOMAIN\\User");
        Assert.Same(definition, folder.Definition);
        Assert.Equal(6, folder.Flags);
        Assert.Equal(3, folder.LogonType);
        Assert.Equal("DOMAIN\\User", folder.UserName);
    }

    public sealed class TaskFolderStub
    {
        public object? Definition;
        public int Flags, LogonType;
        public string? UserName;
        public void RegisterTaskDefinition(string name, object definition, int flags, string userName, object? password, int logonType, object? sddl)
        { Definition = definition; Flags = flags; UserName = userName; LogonType = logonType; }
    }
    [Fact]
    public void AutoUpdaterArgumentsQuoteTheResolvedScriptPath()
    {
        string scriptPath = Path.Combine("C:\\Program Files", "EdilPaint", "Update-EdilPaint.ps1");

        string arguments = UpdaterAutoUpdateService.BuildUpdaterArguments(scriptPath);

        Assert.Equal(
            "-NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File \"C:\\Program Files\\EdilPaint\\Update-EdilPaint.ps1\"",
            arguments);
    }

    [Fact]
    public void ManualUpdaterWaitsForTheApplicationToClose()
    {
        string scriptPath = Path.Combine("C:\\Program Files", "EdilPaint", "Update-EdilPaint.ps1");

        string arguments = UpdaterLauncherService.BuildManualUpdaterArguments(scriptPath);

        Assert.Equal(
            "-NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File \"C:\\Program Files\\EdilPaint\\Update-EdilPaint.ps1\" -ManualUpdate -WaitForApplicationExitSeconds 60",
            arguments);
    }

    [Fact]
    public void ManualArgumentsIdentifyTheActualInstallationAndCallingProcess()
    {
        var arguments = UpdaterLauncherService.BuildManualUpdaterArguments("C:\\Updater\\Update-EdilPaint.ps1", 60,
            "C:\\Program Files\\EdilPaint\\", 123, "C:\\Temp folder\\ready.json");
        Assert.Contains("-ExpectedInstallPath \"C:\\Program Files\\EdilPaint\"", arguments);
        Assert.Contains("-ApplicationProcessId 123", arguments);
        Assert.Contains("-ReadyPath \"C:\\Temp folder\\ready.json\"", arguments);
        Assert.Throws<ArgumentOutOfRangeException>(() => UpdaterLauncherService.BuildManualUpdaterArguments("C:\\update.ps1", 301));
    }

    [Fact]
    public void ManualUpdateRejectsAnUpdaterConfiguredForAnotherInstallation()
    {
        string root = Path.Combine(Path.GetTempPath(), "EdilPaintUpdaterSettingsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string settings = Path.Combine(root, "updater-settings.json");
            File.WriteAllText(settings, System.Text.Json.JsonSerializer.Serialize(new { InstallPath = "other app" }));
            Assert.Throws<InvalidOperationException>(() => UpdaterLauncherService.ValidateManualUpdateSettings(settings, Path.Combine(root, "current app")));
            UpdaterLauncherService.ValidateManualUpdateSettings(settings, Path.Combine(root, "other app"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void StaleBundledCopyCannotDowngradeAnAlreadyRepairedUpdater()
    {
        string root = Path.Combine(Path.GetTempPath(), "EdilPaintUpdaterProtocolTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "updater"));
        try
        {
            string external = Path.Combine(root, "Update-EdilPaint.ps1");
            File.WriteAllText(external, "# EdilPaint updater protocol: 2\n# repaired");
            File.WriteAllText(Path.Combine(root, "updater", "Update-EdilPaint.ps1"), "# old bundled script");
            Assert.False(UpdaterLauncherService.RefreshUpdaterScriptFromBundledCopy(external, root));
            Assert.Contains("# repaired", File.ReadAllText(external));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void StartupIsBlockedOnlyWhileUpdaterOwnsLockOrRecoveryIsIncomplete()
    {
        string root = Path.Combine(Path.GetTempPath(), "EdilPaintUpdaterLockTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, ".edilpaint-install.lock");
            Assert.False(UpdaterLauncherService.IsInstallationUpdateInProgress(root));
            using (var download = File.Open(Path.Combine(root, ".edilpaint-update.lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                Assert.False(UpdaterLauncherService.IsInstallationUpdateInProgress(root));
            using (var handle = File.Open(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                Assert.True(UpdaterLauncherService.IsInstallationUpdateInProgress(root));
            Assert.False(UpdaterLauncherService.IsInstallationUpdateInProgress(root));
            File.WriteAllText(Path.Combine(root, ".edilpaint-update-transaction.json"), "interrupted");
            Assert.True(UpdaterLauncherService.IsInstallationUpdateInProgress(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void BundledUpdaterRefreshesAnExternalUpdaterWithoutTouchingItsSettings()
    {
        string root = Path.Combine(Path.GetTempPath(), "EdilPaintUpdaterBundleTests", Guid.NewGuid().ToString("N"));
        string appDirectory = Path.Combine(root, "app");
        string bundledDirectory = Path.Combine(appDirectory, "updater");
        string externalDirectory = Path.Combine(root, "external-updater");
        string bundledScript = Path.Combine(bundledDirectory, "Update-EdilPaint.ps1");
        string externalScript = Path.Combine(externalDirectory, "Update-EdilPaint.ps1");
        string settingsPath = Path.Combine(externalDirectory, "updater-settings.json");

        try
        {
            Directory.CreateDirectory(bundledDirectory);
            Directory.CreateDirectory(externalDirectory);
            File.WriteAllText(bundledScript, "# updater nuovo");
            File.WriteAllText(externalScript, "# updater vecchio");
            File.WriteAllText(settingsPath, "{ \"InstallPath\": \"C:\\\\EdilPaint\" }");

            bool refreshed = UpdaterLauncherService.RefreshUpdaterScriptFromBundledCopy(externalScript, appDirectory);

            Assert.True(refreshed);
            Assert.Equal("# updater nuovo", File.ReadAllText(externalScript));
            Assert.Equal("{ \"InstallPath\": \"C:\\\\EdilPaint\" }", File.ReadAllText(settingsPath));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolverUsesLocationFileWhenUpdaterIsOutsideInstallTree()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "EdilPaintUpdaterResolverTests",
            Guid.NewGuid().ToString("N"));
        string installDirectory = Path.Combine(root, "install", "programma");
        string updaterDirectory = Path.Combine(root, "external", "updater-service");
        string updaterScript = Path.Combine(updaterDirectory, "Update-EdilPaint.ps1");

        try
        {
            Directory.CreateDirectory(installDirectory);
            Directory.CreateDirectory(updaterDirectory);
            File.WriteAllText(updaterScript, "# test updater");
            File.WriteAllText(
                Path.Combine(installDirectory, "updater-path.txt"),
                updaterDirectory);

            string? resolved = UpdaterLauncherService.ResolveUpdaterScriptPath(installDirectory);

            Assert.Equal(Path.GetFullPath(updaterScript), resolved);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
