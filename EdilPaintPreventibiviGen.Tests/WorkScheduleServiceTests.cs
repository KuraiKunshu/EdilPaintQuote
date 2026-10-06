using System.IO;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class WorkScheduleServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "EdilPaintScheduleTests", Guid.NewGuid().ToString("N"));
    private readonly Repository _database = new();
    private readonly DateTime _monday = new(2026, 10, 12);

    private WorkScheduleService Pc(string name, string identity = "company-a", Func<string>? currentIdentity = null) =>
        new(_database, Path.Combine(_root, name, "schedule.json"), identity, currentDatabaseIdentity: currentIdentity);

    [Fact]
    public async Task TwoPcsReadSharedScheduleAndSettingsAfterEveryRefresh()
    {
        var first = Pc("first"); var second = Pc("second");
        await first.SaveEntriesAsync([Entry(_monday)]);
        var latest = await second.GetLatestAsync(_monday, _monday.AddDays(7));
        Assert.True(latest.IsCurrent);
        Assert.Single(latest.Entries);
        var settings = (await first.GetSettingsAsync()).Settings;
        settings.UseEmployeeAbbreviations = false;
        await first.SaveSettingsAsync(settings);
        Assert.False((await second.GetLatestAsync(_monday, _monday.AddDays(7))).Settings.UseEmployeeAbbreviations);
        var entry = latest.Entries.Single();
        await second.DeleteEntryAsync(entry.Id, entry.Revision);
        Assert.Empty((await first.GetLatestAsync(_monday, _monday.AddDays(7))).Entries);
    }

    [Fact]
    public async Task OfflineRestartConsultsDownloadedWeekAndSettingsWithoutSavingLocally()
    {
        var first = Pc("first");
        await first.SaveEntriesAsync([Entry(_monday)]);
        await first.GetLatestAsync(_monday, _monday.AddDays(7));
        _database.Unavailable = true;
        var restarted = Pc("first");
        var cached = await restarted.GetLatestAsync(_monday, _monday.AddDays(7));
        Assert.False(cached.IsCurrent);
        Assert.True(cached.HasCachedData);
        Assert.NotNull(cached.UpdatedAtUtc);
        Assert.Single(cached.Entries);
        Assert.False((await restarted.GetSettingsAsync()).IsCurrent);
        await Assert.ThrowsAsync<IOException>(() => restarted.SaveEntriesAsync([Entry(_monday.AddDays(1))]));
        await Assert.ThrowsAsync<IOException>(() => restarted.DeleteEntryAsync(cached.Entries[0].Id, cached.Entries[0].Revision));
        Assert.Single(restarted.CachedSnapshot(_monday, _monday.AddDays(7)).Entries);
        Assert.False(restarted.CachedSnapshot(_monday.AddDays(7), _monday.AddDays(14)).HasCachedData);
    }

    [Fact]
    public async Task CachedWeeksAreScopedToDatabaseAndRestartRequiredAfterConnectionChange()
    {
        string identity = "company-a";
        var first = Pc("first", currentIdentity: () => identity);
        await first.SaveEntriesAsync([Entry(_monday)]);
        await first.GetLatestAsync(_monday, _monday.AddDays(7));
        _database.Unavailable = true;
        Assert.Empty((await Pc("first", "company-b").GetLatestAsync(_monday, _monday.AddDays(7))).Entries);
        identity = "company-b";
        await Assert.ThrowsAsync<InvalidOperationException>(() => first.GetLatestAsync(_monday, _monday.AddDays(7)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => first.SaveSettingsAsync(new WorkScheduleSettings()));
    }

    [Fact]
    public async Task FailedWriteDoesNotModifyKnownSchedule()
    {
        var first = Pc("first");
        await first.SaveEntriesAsync([Entry(_monday)]);
        var latest = await first.GetLatestAsync(_monday, _monday.AddDays(7));
        _database.Unavailable = true;
        var edit = latest.Entries.Single().CreateValidatedCopy();
        edit.Notes = "Non salvata";
        await Assert.ThrowsAsync<IOException>(() => first.SaveEntriesAsync([edit]));
        Assert.Equal("", first.CachedSnapshot(_monday, _monday.AddDays(7)).Entries.Single().Notes);
    }

    [Fact]
    public async Task CancelledReadDoesNotPretendToBeAnOfflineResult()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Pc("first").GetLatestAsync(_monday, _monday.AddDays(7), cts.Token));
    }

    [Fact]
    public void WorksheetUsesFullNamesDateNotesAndActualTime()
    {
        var entry = Entry(_monday);
        entry.StartMinutes = 9 * 60 + 15; entry.EndMinutes = 12 * 60 + 45;
        entry.Notes = "Accesso dal cancello laterale";
        entry.Employees = [new() { FirstName = "Mario", LastName = "Rossi" }];
        var options = WorkScheduleActions.CreateWorkSheetOptions(entry);
        Assert.Equal(_monday, options.InterventionDate);
        Assert.Equal("09:15–12:45", options.InterventionTime);
        Assert.Equal("Mario Rossi", Assert.Single(options.EmployeeNames));
        Assert.Equal(entry.Notes, options.AdditionalNotes);
        var context = WorkSheetService.CreateContext(new() { QuoteNumber = "2026/001" }, [], [], options);
        Assert.Equal(options.InterventionTime, context.InterventionTime);
    }

    [Fact]
    public async Task SuccessfulPresetChangeRebasesOfflineCacheForUpcomingPlannedVisits()
    {
        var day = DateTime.Today.AddDays(2);
        var first = Pc("first");
        var entry = Entry(day);
        await first.SaveEntriesAsync([entry]);
        await first.GetLatestAsync(day, day.AddDays(7));
        var settings = (await first.GetSettingsAsync()).Settings;
        settings.FullDayEndMinutes = 18 * 60;
        await first.SaveSettingsAsync(settings);
        _database.Unavailable = true;
        var cached = Pc("first").CachedSnapshot(day, day.AddDays(7));
        Assert.Equal(18 * 60, cached.Settings.FullDayEndMinutes);
        Assert.Equal(18 * 60, Assert.Single(cached.Entries).EndMinutes);
        Assert.Equal(2, cached.Entries[0].Revision);
        Assert.Equal(cached.Settings.Revision, cached.Entries[0].SettingsRevision);
    }

    [Fact]
    public async Task CommittedDatabaseWriteIsSuccessfulWhenLocalCacheCannotBeWritten()
    {
        string cachePath = Path.Combine(_root, "directory-not-file");
        Directory.CreateDirectory(cachePath);
        var service = new WorkScheduleService(_database, cachePath, "company-a");
        Assert.Single(await service.SaveEntriesAsync([Entry(_monday)]));
        Assert.Single((await service.GetLatestAsync(_monday, _monday.AddDays(7))).Entries);
    }

    [Fact]
    public async Task InvalidJsonCacheDoesNotPreventOnlineRecovery()
    {
        string cachePath = Path.Combine(_root, "first", "schedule.json");
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        File.WriteAllText(cachePath, "{invalid");
        var snapshot = await Pc("first").GetLatestAsync(_monday, _monday.AddDays(7));
        Assert.True(snapshot.IsCurrent);
        Assert.Empty(snapshot.Entries);
    }

    [Theory]
    [InlineData("{\"DatabaseIdentity\":\"company-a\",\"Settings\":null}")]
    [InlineData("{\"DatabaseIdentity\":\"company-a\",\"Pages\":null}")]
    [InlineData("{\"DatabaseIdentity\":\"company-a\",\"Pages\":{\"week\":null}}")]
    [InlineData("{\"DatabaseIdentity\":\"company-a\",\"Pages\":{\"week\":{\"Entries\":null}}}")]
    [InlineData("{\"DatabaseIdentity\":\"company-a\",\"Pages\":{\"week\":{\"Employees\":[null]}}}")]
    public async Task StructurallyInvalidCacheDoesNotPreventOnlineRecovery(string json)
    {
        string cachePath = Path.Combine(_root, "first", "schedule.json");
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        File.WriteAllText(cachePath, json.Replace("\"DatabaseIdentity\"", "\"Version\":2,\"DatabaseIdentity\""));
        var snapshot = await Pc("first").GetLatestAsync(_monday, _monday.AddDays(7));
        Assert.True(snapshot.IsCurrent);
        Assert.Empty(snapshot.Entries);
    }

    [Fact]
    public async Task CacheBuiltWithFormerConfirmedQuoteCriteriaIsNotPresentedAsTheNewOrderList()
    {
        var first = Pc("first");
        await first.SaveEntriesAsync([Entry(_monday)]);
        await first.GetLatestAsync(_monday, _monday.AddDays(7));
        string cachePath = Path.Combine(_root, "first", "schedule.json");
        var oldCache = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(cachePath))!;
        oldCache.AsObject().Remove("Version");
        File.WriteAllText(cachePath, oldCache.ToJsonString());
        _database.Unavailable = true;
        var restarted = await Pc("first").GetLatestAsync(_monday, _monday.AddDays(7));
        Assert.False(restarted.IsCurrent);
        Assert.False(restarted.HasCachedData);
        Assert.Empty(restarted.Orders);
        Assert.Empty(restarted.Entries);
    }

    [Fact]
    public async Task SharedHoursDoNotRebaseInactiveFutureJobsInTheOfflineCache()
    {
        var day = DateTime.Today.AddDays(2);
        var entry = Entry(day);
        entry.IsOrderActive = false;
        var first = Pc("first");
        await first.SaveEntriesAsync([entry]);
        await first.GetLatestAsync(day, day.AddDays(7));
        var settings = (await first.GetSettingsAsync()).Settings;
        settings.FullDayEndMinutes = 18 * 60;
        await first.SaveSettingsAsync(settings);
        var cached = first.CachedSnapshot(day, day.AddDays(7)).Entries.Single();
        Assert.Equal(17 * 60, cached.EndMinutes);
        Assert.Equal(1, cached.Revision);
    }

    private static WorkScheduleEntry Entry(DateTime date) => new()
    {
        Date = date, QuoteNumber = "2026/001", CustomerName = "Cliente Rossi", SiteName = "Via Roma"
    };

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private sealed class Repository : IWorkScheduleRepository
    {
        private WorkScheduleSettings _settings = new();
        private readonly List<WorkScheduleEntry> _entries = [];
        public bool Unavailable;

        public Task<WorkScheduleSnapshot> LoadWorkScheduleAsync(DateTime from, DateTime to, CancellationToken token = default)
        {
            Check(token);
            return Task.FromResult(new WorkScheduleSnapshot
            {
                Settings = _settings.CreateValidatedCopy(),
                Entries = _entries.Where(x => x.Date >= from && x.Date < to).Select(x => x.CreateValidatedCopy()).ToList()
            });
        }
        public Task<WorkScheduleSettings> GetWorkScheduleSettingsAsync(CancellationToken token = default)
        { Check(token); return Task.FromResult(_settings.CreateValidatedCopy()); }
        public Task<WorkScheduleSettings> SaveWorkScheduleSettingsAsync(WorkScheduleSettings settings, CancellationToken token = default)
        { Check(token); _settings = settings.CreateValidatedCopy(); _settings.Revision++; return Task.FromResult(_settings.CreateValidatedCopy()); }
        public Task<List<WorkScheduleEntry>> SaveWorkScheduleEntriesAsync(IReadOnlyList<WorkScheduleEntry> entries, CancellationToken token = default)
        {
            Check(token);
            var saved = entries.Select(x => x.CreateValidatedCopy()).ToList();
            foreach (var entry in saved) { entry.Revision++; _entries.RemoveAll(x => x.Id == entry.Id); _entries.Add(entry); }
            return Task.FromResult(saved.Select(x => x.CreateValidatedCopy()).ToList());
        }
        public Task DeleteWorkScheduleEntryAsync(Guid id, long revision, CancellationToken token = default)
        { Check(token); _entries.RemoveAll(x => x.Id == id); return Task.CompletedTask; }
        private void Check(CancellationToken token) { token.ThrowIfCancellationRequested(); if (Unavailable) throw new IOException("Offline"); }
    }
}
