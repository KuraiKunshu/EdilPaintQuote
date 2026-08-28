using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class UpdaterLauncherServiceTests
{
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
