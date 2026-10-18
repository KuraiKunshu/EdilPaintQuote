using System.Text;
using System.Text.Json;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class UpdaterLauncherHandshakeTests
{
    [Theory]
    [InlineData("ready", true)]
    [InlineData("up-to-date", false)]
    [InlineData("failed", false)]
    [InlineData("exit-without-result", false)]
    public async Task ApplicationOnlyHandsOffAfterUpdaterHasPreparedItsPackage(string status, bool expected)
    {
        string root = Path.Combine(Path.GetTempPath(), "EdilPaintUpdaterHandshakeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string script = Path.Combine(root, "Update-EdilPaint.ps1");
            File.WriteAllText(Path.Combine(root, "updater-settings.json"), JsonSerializer.Serialize(new { InstallPath = AppContext.BaseDirectory }));
            string body = "param([switch]$ManualUpdate,[int]$WaitForApplicationExitSeconds,[string]$ExpectedInstallPath,[int]$ApplicationProcessId,[string]$ReadyPath)\n";
            if (status == "exit-without-result") body += "exit 9\n";
            else body += "$temp=$ReadyPath+'.tmp'\n@{Status='" + status + "';Message='Preparation failed in fixture';LogPath='fixture-log'} | ConvertTo-Json | Set-Content -LiteralPath $temp -Encoding UTF8\n[IO.File]::Move($temp,$ReadyPath)\n";
            File.WriteAllText(script, body, new UTF8Encoding(true));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            if (status is "failed" or "exit-without-result")
                await Assert.ThrowsAsync<InvalidOperationException>(() => UpdaterLauncherService.StartUpdaterAsync(script, timeout.Token));
            else Assert.Equal(expected, await UpdaterLauncherService.StartUpdaterAsync(script, timeout.Token));
        }
        finally
        {
            // A ready updater is deliberately detached; let the fixture process exit.
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (Directory.Exists(root))
            {
                try { Directory.Delete(root, recursive: true); }
                catch (IOException) when (DateTime.UtcNow < deadline) { await Task.Delay(50); }
            }
        }
    }
}
