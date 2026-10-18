using System.Diagnostics;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class UpdaterScriptTests
{
    [Fact]
    public async Task ProductionScriptInstallsVerifiesRollsBackAndRecoversOnWindowsPowerShell()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        string? script = null;
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "tools", "updater", "Test-EdilPaintUpdater.ps1");
            if (File.Exists(candidate)) { script = candidate; break; }
            directory = directory.Parent;
        }
        Assert.NotNull(script);
        var root = Path.Combine(Path.GetTempPath(), "EdilPaintUpdaterTests", Guid.NewGuid().ToString("N"));
        try
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script!, "-TestRoot", root }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { process.Kill(entireProcessTree: true); throw; }
            Assert.True(process.ExitCode == 0, await output + "\n" + await errors);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
