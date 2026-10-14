using System.Text.Json;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class UpdateStartupReportServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "EdilPaintStartupTests", Guid.NewGuid().ToString("N"));
    private string ResultPath => Path.Combine(_root, new string('a', 32), "startup.json");

    [Fact]
    public void NormalLaunchHasNoUpdateReport()
    {
        Assert.Null(UpdateStartupReportService.FromArguments([], _root));
        Assert.Null(UpdateStartupReportService.FromArguments([UpdateStartupReportService.ArgumentName], _root));
    }

    [Theory]
    [InlineData("../outside/startup.json")]
    [InlineData("not-a-session/startup.json")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/other.json")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/nested/startup.json")]
    public void CommandLineCannotWriteOutsideAnUpdaterStartupSession(string relativePath)
    {
        Assert.Null(UpdateStartupReportService.FromArguments([UpdateStartupReportService.ArgumentName, Path.Combine(_root, relativePath)], _root));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void ReadyConfirmsTheActualProcessAndCannotBecomeFailureOnExit()
    {
        var service = UpdateStartupReportService.FromArguments([UpdateStartupReportService.ArgumentName, ResultPath], _root);
        Assert.NotNull(service);
        service.ReportReady();
        service.ReportFailure("on exit");
        using var json = JsonDocument.Parse(File.ReadAllText(ResultPath));
        Assert.Equal("ready", json.RootElement.GetProperty("Status").GetString());
        Assert.Equal(Environment.ProcessId, json.RootElement.GetProperty("ProcessId").GetInt32());
        Assert.False(json.RootElement.GetProperty("DialogShown").GetBoolean());
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(ResultPath)!));
    }

    [Fact]
    public void FailureIncludesPersistentExceptionDetailsAndVisibleDialogStatus()
    {
        var exception = new InvalidOperationException("startup failed", new IOException("inner error"));
        string log = UpdateStartupReportService.WriteFailureLog(exception, Path.Combine(_root, "logs"));
        Assert.Contains("startup failed", File.ReadAllText(log));
        Assert.Contains("inner error", File.ReadAllText(log));
        var service = UpdateStartupReportService.FromArguments([UpdateStartupReportService.ArgumentName, ResultPath], _root)!;
        service.ReportFailure(exception.Message, log, dialogShown: true);
        using var json = JsonDocument.Parse(File.ReadAllText(ResultPath));
        Assert.Equal("failed", json.RootElement.GetProperty("Status").GetString());
        Assert.Equal(log, json.RootElement.GetProperty("LogPath").GetString());
        Assert.True(json.RootElement.GetProperty("DialogShown").GetBoolean());
    }

    [Fact]
    public void AnUnavailableResultDirectoryDoesNotCrashTheApplication()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.GetDirectoryName(ResultPath)!, "blocked");
        var service = UpdateStartupReportService.FromArguments([UpdateStartupReportService.ArgumentName, ResultPath], _root)!;
        service.ReportFailure("startup failed");
        Assert.False(File.Exists(ResultPath));
        Assert.Empty(UpdateStartupReportService.WriteFailureLog(new Exception("failure"), Path.GetDirectoryName(ResultPath)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
