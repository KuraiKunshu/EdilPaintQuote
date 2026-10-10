using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class AutomaticMaterialSettingsServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "EdilPaintAutomaticSettingsTests", Guid.NewGuid().ToString("N"));
    private string Cache => Path.Combine(_directory, "settings.json");

    [Fact]
    public async Task NewComputerReadsSharedRulesInsteadOfItsDefaults()
    {
        var repository = new Repository { Stored = Shared("Lavoro aziendale") };
        var snapshot = await Service(repository).GetLatestAsync();
        Assert.True(snapshot.IsCurrent);
        Assert.Equal("Lavoro aziendale", Assert.Single(snapshot.Settings.WindowMaterialRules).LaborName);
        Assert.Null(repository.LastLegacy);
        Assert.Equal("company", snapshot.Settings.CatalogIdentity);
    }

    [Fact]
    public async Task LocalCustomizedRulesAreOfferedForOneTimeImport()
    {
        var repository = new Repository();
        var local = Local(); local.WindowMaterialRules[0].LaborName = "Personalizzato";
        var snapshot = await Service(repository, local).GetLatestAsync();
        Assert.Equal("Personalizzato", Assert.Single(repository.LastLegacy!.WindowMaterialRules).LaborName);
        Assert.Equal("Personalizzato", snapshot.Settings.WindowMaterialRules[0].LaborName);
    }

    [Fact]
    public async Task OldLocalRulesCannotOverwriteConfiguredSharedRules()
    {
        var repository = new Repository { Stored = Shared("Corrente"), Configured = true };
        var local = Local(); local.WindowMaterialRules[0].LaborName = "Vecchio";
        var snapshot = await Service(repository, local).GetLatestAsync();
        Assert.Equal("Corrente", snapshot.Settings.WindowMaterialRules[0].LaborName);
    }

    [Fact]
    public async Task LegacyRulesWithAnotherEndpointAreOfferedForVerifiedNameResolution()
    {
        var repository = new Repository();
        var local = Local(); local.WindowMaterialRules[0].LaborName = "Altra azienda";
        local.WindowMaterialCatalogIdentity = "other-company";
        await Service(repository, local).GetLatestAsync();
        Assert.NotNull(repository.LastLegacy);
        Assert.Equal("other-company", repository.LastLegacy.CatalogIdentity);
    }

    [Fact]
    public async Task ServerAliasDoesNotRejectRulesReadFromDatabase()
    {
        var repository = new Repository { Stored = Shared("Shared"), Configured = true };
        repository.Stored.CatalogIdentity = "first-pc-endpoint";
        var snapshot = await Service(repository, identity: "second-pc-endpoint").GetLatestAsync();
        Assert.True(snapshot.IsCurrent);
        Assert.Equal("second-pc-endpoint", snapshot.Settings.CatalogIdentity);
        Assert.Equal("Shared", snapshot.Settings.WindowMaterialRules[0].LaborName);
    }

    [Fact]
    public async Task EmptySharedRulesStayEmptyAfterRestartAndOfflineRead()
    {
        var repository = new Repository { Stored = Shared(), Configured = true };
        repository.Stored.WindowMaterialRules.Clear();
        await Service(repository).GetLatestAsync();
        repository.Fail = true;
        var restarted = Service(repository);
        var snapshot = await restarted.GetLatestAsync();
        Assert.False(snapshot.IsCurrent); Assert.Empty(snapshot.Settings.WindowMaterialRules);
    }

    [Fact]
    public async Task CacheIsScopedToDatabase()
    {
        var repository = new Repository { Stored = Shared("Company A"), Configured = true };
        await Service(repository).GetLatestAsync(); repository.Fail = true;
        var other = Service(repository, identity: "company-b");
        Assert.DoesNotContain((await other.GetLatestAsync()).Settings.WindowMaterialRules, x => x.LaborName == "Company A");
    }

    [Fact]
    public async Task TwoComputersSeeSavedChange()
    {
        var repository = new Repository { Stored = Shared("Old"), Configured = true };
        var first = Service(repository);
        var second = new AutomaticMaterialSettingsService(repository, Cache + ".second", "company", Local());
        var draft = (await first.GetLatestAsync()).Settings;
        await second.GetLatestAsync(); draft.WindowMaterialRules[0].LaborName = "New";
        var saved = await first.SaveAsync(draft);
        var remote = await second.GetLatestAsync();
        Assert.True(remote.IsCurrent); Assert.Equal("New", remote.Settings.WindowMaterialRules[0].LaborName);
        Assert.Equal(saved.Settings.Revision, remote.Settings.Revision);
    }

    [Fact]
    public async Task StaleRevisionCannotOverwriteOtherComputer()
    {
        var repository = new Repository { Stored = Shared("Old"), Configured = true };
        var service = Service(repository);
        var original = (await service.GetLatestAsync()).Settings;
        var changed = original.CreateValidatedCopy(); changed.WindowMaterialRules.Clear();
        await service.SaveAsync(changed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(original));
        Assert.Empty(service.CachedSnapshot.Settings.WindowMaterialRules);
    }

    [Fact]
    public async Task FailedSaveDoesNotPersistDraftToCache()
    {
        var repository = new Repository { Stored = Shared("Old"), Configured = true };
        var service = Service(repository);
        var edited = (await service.GetLatestAsync()).Settings;
        edited.WindowMaterialRules[0].LaborName = "Unsaved"; repository.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => service.SaveAsync(edited));
        Assert.Equal("Old", service.CachedSnapshot.Settings.WindowMaterialRules[0].LaborName);
    }

    [Fact]
    public async Task CancellationIsNotConvertedToCachedSuccess()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(new Repository()).GetLatestAsync(cancellation.Token));
    }

    [Fact]
    public async Task ConnectionChangeRequiresRestart()
    {
        string identity = "company";
        var service = new AutomaticMaterialSettingsService(new Repository(), Cache, identity, Local(),
            currentIdentity: () => identity);
        identity = "other";
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetLatestAsync());
    }

    [Fact]
    public async Task MutatingReturnedSnapshotDoesNotChangeCachedRules()
    {
        var service = Service(new Repository { Stored = Shared("Saved"), Configured = true });
        var snapshot = await service.GetLatestAsync(); snapshot.Settings.WindowMaterialRules.Clear();
        Assert.Single(service.CachedSnapshot.Settings.WindowMaterialRules);
    }

    private AutomaticMaterialSettingsService Service(Repository repository, RealProfitSettingsModel? local = null,
        string identity = "company") => new(repository, Cache, identity, local ?? Local());
    private static RealProfitSettingsModel Local() { var local = new RealProfitSettingsModel(); local.Normalize(); return local; }
    private static AutomaticMaterialSettings Shared(string labor = "Finitura") => new()
    {
        Revision = 3, WindowProductPrefixes = ["PRODOTTO"],
        WindowMaterialRules = [new() { LaborName = labor, MaterialName = "Materiale", QuantityParameter = 2 }]
    };

    private sealed class Repository : IAutomaticMaterialSettingsRepository
    {
        public AutomaticMaterialSettings? Stored;
        public AutomaticMaterialSettings? LastLegacy;
        public bool Configured;
        public bool Fail;
        public Task<AutomaticMaterialSettings> LoadAutomaticMaterialSettingsAsync(AutomaticMaterialSettings? legacy,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); if (Fail) throw new IOException("Offline");
            LastLegacy = legacy?.CreateValidatedCopy();
            if (!Configured && legacy != null) { Stored = legacy.CreateValidatedCopy(); Configured = true; }
            Stored ??= Shared(); Stored.Revision = Math.Max(1, Stored.Revision);
            return Task.FromResult(Stored.CreateValidatedCopy());
        }
        public Task<AutomaticMaterialSettings> SaveAutomaticMaterialSettingsAsync(AutomaticMaterialSettings settings,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); if (Fail) throw new IOException("Offline");
            if (Stored?.Revision != settings.Revision) throw new InvalidOperationException("Conflict");
            Stored = settings.CreateValidatedCopy(); Stored.Revision++; Configured = true;
            return Task.FromResult(Stored.CreateValidatedCopy());
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
