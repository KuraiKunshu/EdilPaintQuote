using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class UpdaterLauncherServiceTests
{
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
            "-NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File \"C:\\Program Files\\EdilPaint\\Update-EdilPaint.ps1\" -ManualUpdate -WaitForApplicationExitSeconds 15",
            arguments);
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
