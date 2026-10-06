using System.IO;
using EdilPaintPreventibiviGen.Data;
using EdilPaintPreventibiviGen.Data.Entities;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class EmployeeDirectoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "EdilPaintEmployeesTests", Guid.NewGuid().ToString("N"));
    private readonly SharedRepository _database = new();

    private EmployeeDirectoryService Pc(string name, IEnumerable<EmployeeSettingsModel>? legacy = null,
        string database = "company-a", Func<string>? identity = null) => new(_database,
            Path.Combine(_root, name, "dipendenti.json"), database, legacy ?? [], currentDatabaseIdentity: identity);

    [Fact]
    public async Task TwoPcsSeeAddsRenamesAndRemovalsWithoutLosingConcurrentAdditions()
    {
        var firstPc = Pc("first");
        var secondPc = Pc("second");
        var firstOriginal = await firstPc.RefreshAsync();
        var secondOriginal = await secondPc.RefreshAsync();
        var mario = new EmployeeSettingsModel { FirstName = " Mario ", LastName = "Rossi" };
        var anna = new EmployeeSettingsModel { FirstName = "Anna" };
        await firstPc.SaveAsync(firstOriginal.Employees, [mario]);
        await secondPc.SaveAsync(secondOriginal.Employees, [anna]);

        var latest = await firstPc.GetLatestAsync();
        Assert.True(latest.IsCurrent);
        Assert.Equal(2, latest.Employees.Count);
        var edited = latest.Employees.Select(x => x.CreateValidatedCopy()).ToList();
        edited.Single(x => x.Id == mario.Id).LastName = "Bianchi";
        await firstPc.SaveAsync(latest.Employees, edited);

        var remote = await secondPc.GetLatestAsync();
        Assert.Equal("Bianchi", remote.Employees.Single(x => x.Id == mario.Id).LastName);
        await secondPc.SaveAsync(remote.Employees, remote.Employees.Where(x => x.Id != mario.Id).ToList());
        Assert.Equal("Anna", Assert.Single((await firstPc.GetLatestAsync()).Employees).FirstName);
        Assert.True(_database.Rows.Single(x => x.Id == mario.Id).IsDeleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleRenameOrRemovalDoesNotOverwriteAnotherPc(bool remove)
    {
        var first = Pc("first");
        var second = Pc("second");
        var employee = new EmployeeSettingsModel { FirstName = "Mario" };
        await first.SaveAsync([], [employee]);
        var baseline = (await first.RefreshAsync()).Employees;
        var changed = baseline.Single().CreateValidatedCopy();
        changed.LastName = "Rossi";
        await second.SaveAsync(baseline, [changed]);

        var stale = baseline.Single().CreateValidatedCopy();
        stale.LastName = "Bianchi";
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => first.SaveAsync(baseline, remove ? [] : [stale]));
        Assert.Contains("altro PC", error.Message);
        Assert.Equal("Rossi", Assert.Single((await second.RefreshAsync()).Employees).LastName);
    }

    [Fact]
    public void ConflictRejectsWholeBatchIncludingOtherChanges()
    {
        var first = new EmployeeSettingsModel { FirstName = "Mario", Revision = 1 };
        var second = new EmployeeSettingsModel { FirstName = "Anna", Revision = 1 };
        var editedFirst = first.CreateValidatedCopy();
        editedFirst.LastName = "Bianchi";
        var editedSecond = second.CreateValidatedCopy();
        editedSecond.LastName = "Rossi";
        var rows = new Dictionary<Guid, EmployeeEntity>
        {
            [first.Id] = new() { Id = first.Id, FirstName = first.FirstName, Revision = 1 },
            [second.Id] = new() { Id = second.Id, FirstName = second.FirstName, Revision = 2 }
        };
        Assert.Throws<InvalidOperationException>(() => EmployeeChanges.Apply(
            EmployeeChanges.Between([first, second], [editedFirst, editedSecond]), rows));
        Assert.Equal("", rows[first.Id].LastName);
        Assert.Equal(1, rows[first.Id].Revision);
    }

    [Fact]
    public async Task SavingAnUnrelatedEditDoesNotRevertAnotherPcsRename()
    {
        var pc = Pc("first");
        await pc.SaveAsync([], [new() { FirstName = "Mario" }, new() { FirstName = "Anna" }]);
        var baseline = (await pc.RefreshAsync()).Employees;
        var firstEdit = baseline.Select(x => x.CreateValidatedCopy()).ToList();
        firstEdit.Single(x => x.FirstName == "Mario").LastName = "Rossi";
        await pc.SaveAsync(baseline, firstEdit);
        var secondEdit = baseline.Select(x => x.CreateValidatedCopy()).ToList();
        secondEdit.Single(x => x.FirstName == "Anna").LastName = "Bianchi";
        var latest = await Pc("second").SaveAsync(baseline, secondEdit);
        Assert.Contains(latest.Employees, x => x.FirstName == "Mario" && x.LastName == "Rossi");
        Assert.Contains(latest.Employees, x => x.FirstName == "Anna" && x.LastName == "Bianchi");
    }

    [Fact]
    public async Task NewEmployeesMayHaveIdenticalNames()
    {
        var pc = Pc("first");
        await pc.SaveAsync([], [new() { FirstName = "Mario", LastName = "Rossi" },
            new() { FirstName = "Mario", LastName = "Rossi" }]);
        var employees = (await pc.RefreshAsync()).Employees;
        Assert.Equal(2, employees.Count);
        Assert.NotEqual(employees[0].Id, employees[1].Id);
    }

    [Fact]
    public async Task DeletedEmployeeCannotBeRestoredByStaleSettings()
    {
        var pc = Pc("first");
        await pc.SaveAsync([], [new() { FirstName = "Mario" }]);
        var original = (await pc.RefreshAsync()).Employees;
        await pc.SaveAsync(original, []);
        var edited = original.Single().CreateValidatedCopy();
        edited.LastName = "Rossi";
        await Assert.ThrowsAsync<InvalidOperationException>(() => Pc("second").SaveAsync(original, [edited]));
        Assert.Empty((await pc.RefreshAsync()).Employees);
    }

    [Fact]
    public async Task LegacyListsAreMergedOnceAndDoNotReviveRenamedOrDeletedEmployees()
    {
        var legacy = new EmployeeSettingsModel { FirstName = "Mario", LastName = "De Rossi" };
        var first = Pc("first", [legacy]);
        var original = (await first.RefreshAsync()).Employees;
        var duplicate = new EmployeeSettingsModel { FirstName = " mario ", LastName = "DE   ROSSI" };
        Assert.Single((await Pc("second", [duplicate]).RefreshAsync()).Employees);
        var renamed = original.Single().CreateValidatedCopy();
        renamed.FirstName = "Maria";
        await first.SaveAsync(original, [renamed]);
        var third = await Pc("third", [legacy]).RefreshAsync();
        Assert.Equal("Maria", Assert.Single(third.Employees).FirstName);
        await first.SaveAsync(third.Employees, []);
        Assert.Empty((await Pc("fourth", [duplicate]).RefreshAsync()).Employees);
    }

    [Fact]
    public async Task LegacyImportMatchedByNameStaysRecordedAfterOriginalIsRenamed()
    {
        var pc = Pc("first");
        await pc.SaveAsync([], [new() { FirstName = "Anna" }]);
        var legacy = new EmployeeSettingsModel { FirstName = "Anna" };
        var original = (await Pc("second", [legacy]).RefreshAsync()).Employees;
        var renamed = original.Single().CreateValidatedCopy();
        renamed.FirstName = "Anna Maria";
        await pc.SaveAsync(original, [renamed]);
        Assert.Equal("Anna Maria", Assert.Single((await Pc("third", [legacy]).RefreshAsync()).Employees).FirstName);
    }

    [Fact]
    public async Task OfflinePcUsesPersistedCacheButCannotSaveLocalOnlyChanges()
    {
        var pc = Pc("first");
        await pc.SaveAsync([], [new() { FirstName = "Niccolò", LastName = "D'Amico" }]);
        _database.Unavailable = true;
        var restarted = Pc("first");
        var cached = await restarted.GetLatestAsync();
        Assert.False(cached.IsCurrent);
        Assert.Equal("Niccolò", Assert.Single(cached.Employees).FirstName);
        await Assert.ThrowsAsync<IOException>(() => restarted.SaveAsync(cached.Employees, []));
        Assert.Single(restarted.CachedSnapshot.Employees);
        Assert.Single(_database.Rows, x => !x.IsDeleted);
    }

    [Fact]
    public async Task EmptyCacheDoesNotFallBackToOldLocalSettings()
    {
        var legacy = new EmployeeSettingsModel { FirstName = "Mario" };
        var pc = Pc("first", [legacy]);
        var snapshot = await pc.RefreshAsync();
        await pc.SaveAsync(snapshot.Employees, []);
        _database.Unavailable = true;
        Assert.Empty((await Pc("first", [legacy]).GetLatestAsync()).Employees);
    }

    [Fact]
    public async Task CacheCannotExposeStaffFromAnotherDatabase()
    {
        await Pc("first").SaveAsync([], [new() { FirstName = "Mario" }]);
        _database.Unavailable = true;
        Assert.Empty((await Pc("first", database: "company-b").GetLatestAsync()).Employees);
    }

    [Fact]
    public async Task ChangingDatabaseRequiresRestartBeforeAnyEmployeeReadOrWrite()
    {
        string identity = "company-a";
        var pc = Pc("first", identity: () => identity);
        await pc.RefreshAsync();
        identity = "company-b";
        await Assert.ThrowsAsync<InvalidOperationException>(() => pc.GetLatestAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => pc.SaveAsync([], [new() { FirstName = "Mario" }]));
        Assert.Empty(_database.Rows);
    }

    [Fact]
    public async Task CancellationIsPropagatedInsteadOfReturningCache()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Pc("first").GetLatestAsync(cts.Token));
    }

    [Fact]
    public async Task CommittedWriteSurvivesFailureOfFollowingRefresh()
    {
        var pc = Pc("first");
        _database.FailReadAfterSave = true;
        var saved = await pc.SaveAsync([], [new() { FirstName = "Anna" }]);
        Assert.False(saved.IsCurrent);
        Assert.Equal("Anna", Assert.Single(saved.Employees).FirstName);
        Assert.Single(_database.Rows);
        Assert.Equal(1, Assert.Single(Pc("first").CachedSnapshot.Employees).Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothDatabaseProvidersMapSharedStaffAndCheckRevisions(bool postgres)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        if (postgres) builder.UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused");
        else builder.UseSqlServer("Server=localhost;Database=unused;Trusted_Connection=True;TrustServerCertificate=True");
        using var db = new AppDbContext(builder.Options);
        var entity = db.Model.FindEntityType(typeof(EmployeeEntity))!;
        Assert.Equal("Employees", entity.GetTableName());
        Assert.True(entity.FindProperty(nameof(EmployeeEntity.Revision))!.IsConcurrencyToken);
        Assert.Equal(250, entity.FindProperty(nameof(EmployeeEntity.FirstName))!.GetMaxLength());
        string schema = SqlDataService.BuildEmployeeSchemaSql(postgres);
        Assert.Contains(postgres ? "CREATE TABLE IF NOT EXISTS" : "IF OBJECT_ID", schema);
        Assert.Contains("Revision", schema);
        Assert.Contains("IsDeleted", db.Database.GenerateCreateScript());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class SharedRepository : IEmployeeRepository
    {
        public List<EmployeeEntity> Rows { get; } = [];
        public bool Unavailable { get; set; }
        public bool FailReadAfterSave { get; set; }

        public Task<List<EmployeeSettingsModel>> GetEmployeesAsync(CancellationToken token = default)
        {
            Check(token);
            return Task.FromResult(Rows.Where(x => !x.IsDeleted).Select(EmployeeChanges.ToModel).ToList());
        }

        public Task ImportLegacyEmployeesAsync(IReadOnlyList<EmployeeSettingsModel> employees, CancellationToken token = default)
        {
            Check(token);
            Rows.AddRange(EmployeeChanges.LegacyImports(employees, Rows));
            return Task.CompletedTask;
        }

        public Task SaveEmployeesAsync(IReadOnlyList<EmployeeSettingsModel> original,
            IReadOnlyList<EmployeeSettingsModel> edited, CancellationToken token = default)
        {
            Check(token);
            Rows.AddRange(EmployeeChanges.Apply(EmployeeChanges.Between(original, edited), Rows.ToDictionary(x => x.Id)));
            if (FailReadAfterSave) Unavailable = true;
            return Task.CompletedTask;
        }

        private void Check(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Unavailable) throw new IOException("Database non disponibile.");
        }
    }
}
