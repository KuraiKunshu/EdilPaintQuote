using System.Text.Json.Nodes;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class UpdaterConfigurationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "EdilPaintUpdaterConfigurationTests", Guid.NewGuid().ToString("N"));
    private string AppDirectory => Path.Combine(_root, "installed app");
    private string UserDirectory => Path.Combine(_root, "user data");

    public UpdaterConfigurationServiceTests()
    {
        Directory.CreateDirectory(Path.Combine(AppDirectory, "updater"));
        File.WriteAllText(Path.Combine(AppDirectory, "updater", UpdaterConfigurationService.ScriptName),
            "# EdilPaint updater protocol: 2\n# bundled updater");
    }

    [Fact]
    public void MissingConfigurationIsCreatedOutsideTheInstallationAndPointsToTheActualApp()
    {
        string script = Save();
        var config = UpdaterConfigurationService.Read(script, AppDirectory);
        Assert.Equal("main", config.Branch);
        Assert.True(config.RunTests);
        Assert.Equal(UpdaterConfigurationService.DefaultRepository, config.RepositoryUrl);
        Assert.StartsWith(UserDirectory, script);
        Assert.Contains("# bundled updater", File.ReadAllText(script));
        Assert.False(File.Exists(Path.Combine(AppDirectory, "updater", UpdaterConfigurationService.SettingsName)));
        var settings = ReadSettings(script);
        Assert.Equal(Path.GetFullPath(AppDirectory), settings["InstallPath"]!.GetValue<string>());
        Assert.Equal(Path.Combine(Path.GetDirectoryName(script)!, "source"), settings["SourcePath"]!.GetValue<string>());
        Assert.Equal("EdilPaintPreventibiviGen", settings["ProcessName"]!.GetValue<string>());
        Assert.True(settings["SelfContained"]!.GetValue<bool>());
    }

    [Fact]
    public void ConfigurationIsIsolatedByInstallationAndPathCasingDoesNotCreateAnotherIdentity()
    {
        string path = UpdaterConfigurationService.GetLocalScriptPath(AppDirectory, UserDirectory);
        Assert.Equal(path, UpdaterConfigurationService.GetLocalScriptPath(AppDirectory.ToUpperInvariant() + "\\", UserDirectory));
        Assert.NotEqual(path, UpdaterConfigurationService.GetLocalScriptPath(AppDirectory + " 2", UserDirectory));
    }

    [Fact]
    public void EditingPreservesExistingPathsAndExtraSettingsWithoutResettingTheSelectedBranch()
    {
        string script = Save();
        var settings = ReadSettings(script);
        settings["SourcePath"] = "custom-source";
        settings["LogPath"] = "custom-logs/update.log";
        settings["StartDelaySeconds"] = 125;
        settings["RuntimeIdentifier"] = "win-arm64";
        settings["CompanySpecificSetting"] = "preserved";
        WriteSettings(script, settings);

        string saved = Save(script, "codex/adattamento-multi-azienda", false);
        Assert.Equal(script, saved);
        Assert.Equal("codex/adattamento-multi-azienda", UpdaterConfigurationService.Read(saved, AppDirectory).Branch);
        settings = ReadSettings(saved);
        Assert.Equal("custom-source", settings["SourcePath"]!.GetValue<string>());
        Assert.Equal("custom-logs/update.log", settings["LogPath"]!.GetValue<string>());
        Assert.Equal(125, settings["StartDelaySeconds"]!.GetValue<int>());
        Assert.Equal("win-arm64", settings["RuntimeIdentifier"]!.GetValue<string>());
        Assert.Equal("preserved", settings["CompanySpecificSetting"]!.GetValue<string>());
        Assert.False(settings["RunTests"]!.GetValue<bool>());
    }

    [Fact]
    public void AnotherInstallationsConfigurationCannotBeRepurposed()
    {
        string script = Save();
        string original = File.ReadAllText(SettingsPath(script));
        Directory.CreateDirectory(Path.Combine(AppDirectory + " 2", "updater"));
        File.Copy(Path.Combine(AppDirectory, "updater", UpdaterConfigurationService.ScriptName),
            Path.Combine(AppDirectory + " 2", "updater", UpdaterConfigurationService.ScriptName));
        Assert.Throws<InvalidOperationException>(() => UpdaterConfigurationService.Save(
            AppDirectory + " 2", UpdaterConfigurationService.DefaultRepository, "main", true, script, UserDirectory));
        Assert.Equal(original, File.ReadAllText(SettingsPath(script)));
    }

    [Theory]
    [InlineData("main..other")]
    [InlineData("-main")]
    [InlineData("main other")]
    [InlineData("main\nother")]
    [InlineData("feature/.hidden")]
    [InlineData("feature.lock")]
    [InlineData("feature//other")]
    [InlineData("feature@{1}")]
    public void InvalidBranchDoesNotWriteConfiguration(string branch)
    {
        Assert.Throws<InvalidOperationException>(() => Save(branch: branch));
        Assert.False(Directory.Exists(UserDirectory));
    }

    [Theory]
    [InlineData("-repository")]
    [InlineData("https://user:token@github.com/company/repo.git")]
    [InlineData("https://github.com/company/repo.git\n--other")]
    public void InvalidRepositoryDoesNotWriteConfiguration(string repository)
    {
        Assert.Throws<InvalidOperationException>(() => UpdaterConfigurationService.Save(
            AppDirectory, repository, "main", true, localAppData: UserDirectory));
        Assert.False(Directory.Exists(UserDirectory));
    }

    [Fact]
    public void SourceCacheInsideTheInstallationIsRejectedWithoutReplacingExistingSettings()
    {
        string script = Save();
        var settings = ReadSettings(script);
        settings["SourcePath"] = Path.Combine(AppDirectory, "source");
        WriteSettings(script, settings);
        string original = File.ReadAllText(SettingsPath(script));
        Assert.Throws<InvalidOperationException>(() => Save(script));
        Assert.Equal(original, File.ReadAllText(SettingsPath(script)));
    }

    [Fact]
    public void ResolverPrefersAConfiguredUpdaterOverTheUnconfiguredBundledCopy()
    {
        string configured = Save();
        string bundled = Path.Combine(AppDirectory, "updater", UpdaterConfigurationService.ScriptName);
        Assert.Equal(configured, UpdaterLauncherService.SelectUpdaterCandidate([bundled, configured], AppDirectory));
        Assert.Equal(bundled, UpdaterLauncherService.SelectUpdaterCandidate([bundled, configured], AppDirectory + " 2"));
    }

    [Fact]
    public void ResolverSkipsMalformedConfigurationAndStillFindsTheValidOne()
    {
        string configured = Save();
        string bundled = Path.Combine(AppDirectory, "updater", UpdaterConfigurationService.ScriptName);
        File.WriteAllText(SettingsPath(bundled), "{broken");
        Assert.Equal(configured, UpdaterLauncherService.SelectUpdaterCandidate([bundled, configured], AppDirectory));
    }

    [Fact]
    public void MissingBundledScriptDoesNotCreateHalfAConfiguration()
    {
        File.Delete(Path.Combine(AppDirectory, "updater", UpdaterConfigurationService.ScriptName));
        Assert.Throws<FileNotFoundException>(() => Save());
        Assert.False(Directory.Exists(UserDirectory));
    }

    [Fact]
    public void InstalledVersionShowsCommitAndBranchAndIgnoresMalformedManifest()
    {
        string path = Path.Combine(AppDirectory, "edilpaint-update.json");
        File.WriteAllText(path, "{\"Commit\":\"1234567890abcdef\",\"Branch\":\"main\"}");
        string description = UpdaterConfigurationService.DescribeVersion(AppDirectory);
        Assert.Contains("main", description);
        Assert.Contains("1234567890", description);
        File.WriteAllText(path, "{broken");
        Assert.StartsWith("Versione", UpdaterConfigurationService.DescribeVersion(AppDirectory));
    }

    private string Save(string? existing = null, string branch = "main", bool runTests = true)
        => UpdaterConfigurationService.Save(AppDirectory, UpdaterConfigurationService.DefaultRepository, branch,
            runTests, existing, UserDirectory);
    private static string SettingsPath(string script) => Path.Combine(Path.GetDirectoryName(script)!, UpdaterConfigurationService.SettingsName);
    private static JsonObject ReadSettings(string script) => JsonNode.Parse(File.ReadAllText(SettingsPath(script)))!.AsObject();
    private static void WriteSettings(string script, JsonObject settings) => File.WriteAllText(SettingsPath(script), settings.ToJsonString());
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
