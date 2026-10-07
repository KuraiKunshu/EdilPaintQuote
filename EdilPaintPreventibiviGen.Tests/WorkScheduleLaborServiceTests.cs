using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class WorkScheduleLaborServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "EdilPaintQuoteLabor", Guid.NewGuid().ToString("N"));
    private readonly Repository _database = new();
    private static readonly DateTime Monday = new(2026, 10, 12);
    private WorkScheduleService Pc(string name, string identity = "company-a", Func<string>? currentIdentity = null) =>
        new(_database, Path.Combine(_root, name, "schedule.json"), identity, currentDatabaseIdentity: currentIdentity);

    [Fact]
    public async Task EveryPcReadsAllQuoteDatesWithoutLoadingCalendarWeeks()
    {
        var first = Pc("first"); var second = Pc("second");
        var employee = new EmployeeSettingsModel { FirstName = "Anna" };
        var planned = Entry(Monday, employee);
        var completed = Entry(Monday.AddMonths(-2), employee); completed.Status = WorkScheduleEntryStatus.Completed;
        var otherQuote = Entry(Monday, employee); otherQuote.QuoteNumber = "other-quote";
        var absence = Entry(Monday, employee); absence.Kind = WorkScheduleEntryKind.Absence; absence.QuoteNumber = "";
        var saved = await first.SaveEntriesAsync([planned, completed, otherQuote, absence]);
        var labor = await second.GetQuoteLaborAsync("2026/001");
        Assert.True(labor.IsCurrent); Assert.True(labor.HasCachedData); Assert.NotNull(labor.UpdatedAtUtc);
        Assert.Equal(2, labor.Labor!.Rows.Count); Assert.Equal(16, labor.Labor.TotalPersonHours);
        Assert.Equal(1, labor.Labor.DistinctWorkers); Assert.Equal(2, labor.Labor.Days);
        Assert.Equal(0, _database.WeekLoadCalls); Assert.Equal(1, _database.QuoteLoadCalls);
        var changed = saved.Single(entry => entry.Id == planned.Id);
        var extra = new EmployeeSettingsModel { FirstName = "Mario" };
        changed.EmployeeIds.Add(extra.Id); changed.Employees.Add(extra);
        await first.SaveEntriesAsync([changed]);
        var updated = await second.GetQuoteLaborAsync("2026/001");
        Assert.Equal(24, updated.Labor!.TotalPersonHours); Assert.Equal(2, updated.Labor.DistinctWorkers);
    }

    [Fact]
    public async Task OfflineRestartRetainsKnownCrewButNeverClaimsItIsCurrent()
    {
        var first = Pc("first"); var employee = new EmployeeSettingsModel { FirstName = "Anna" };
        var entry = Entry(Monday, employee); await first.SaveEntriesAsync([entry]);
        var current = await first.GetQuoteLaborAsync(entry.QuoteNumber);
        current.Labor!.Rows[0].Employees[0].Name = "Modified returned copy";
        _database.Unavailable = true;
        var cached = await Pc("first").GetQuoteLaborAsync(entry.QuoteNumber);
        Assert.False(cached.IsCurrent); Assert.True(cached.HasCachedData); Assert.NotNull(cached.UpdatedAtUtc);
        Assert.Equal("Anna", cached.Labor!.Rows[0].Employees[0].Name); Assert.Equal(8, cached.Labor.TotalPersonHours);
        var unknown = await Pc("first").GetQuoteLaborAsync("unknown");
        Assert.False(unknown.IsCurrent); Assert.False(unknown.HasCachedData); Assert.Null(unknown.Labor);
    }

    [Fact]
    public async Task AnEmptyReadIsFreshOnlyWhenItComesFromTheDatabase()
    {
        var service = Pc("first"); var empty = await service.GetQuoteLaborAsync("empty");
        Assert.True(empty.IsCurrent); Assert.Null(empty.Labor); Assert.True(empty.HasCachedData);
        _database.Unavailable = true;
        var knownEmpty = await service.GetQuoteLaborAsync("empty");
        Assert.False(knownEmpty.IsCurrent); Assert.Null(knownEmpty.Labor); Assert.True(knownEmpty.HasCachedData);
    }

    [Fact]
    public async Task CacheTracksLocalSaveCompletionDeletionAndQuoteMove()
    {
        var service = Pc("first"); var saved = Assert.Single(await service.SaveEntriesAsync([Entry(Monday)]));
        await service.GetQuoteLaborAsync(saved.QuoteNumber);
        var completed = await service.CompleteEntryAsync(saved.Id, saved.Revision);
        Assert.True(service.CachedQuoteLabor(saved.QuoteNumber).Labor!.Rows.Single().IsCompleted);
        var changed = completed.CreateValidatedCopy(); changed.QuoteNumber = "other";
        await service.GetQuoteLaborAsync(changed.QuoteNumber);
        var moved = Assert.Single(await service.SaveEntriesAsync([changed]));
        Assert.Null(service.CachedQuoteLabor(saved.QuoteNumber).Labor);
        Assert.Single(service.CachedQuoteLabor(changed.QuoteNumber).Labor!.Rows);
        await service.DeleteEntryAsync(moved.Id, moved.Revision);
        Assert.Null(service.CachedQuoteLabor(changed.QuoteNumber).Labor);
    }

    [Fact]
    public async Task CacheIsScopedToDatabaseAndCancellationIsNotOffline()
    {
        string identity = "company-a";
        var service = Pc("first", currentIdentity: () => identity); await service.GetQuoteLaborAsync("empty");
        _database.Unavailable = true;
        Assert.False((await Pc("first", "company-b").GetQuoteLaborAsync("empty")).HasCachedData);
        identity = "company-b";
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetQuoteLaborAsync("empty"));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Pc("second").GetQuoteLaborAsync("empty", cts.Token));
    }

    [Fact]
    public async Task PresetChangeUpdatesKnownQuoteHoursAndKeepsCachedDataNonCurrent()
    {
        var service = Pc("first"); var employee = new EmployeeSettingsModel { FirstName = "Anna" };
        var entry = Entry(DateTime.Today.AddDays(2), employee); await service.SaveEntriesAsync([entry]);
        await service.GetQuoteLaborAsync(entry.QuoteNumber);
        var settings = (await service.GetSettingsAsync()).Settings;
        settings.FullDayEndMinutes = 18 * 60; await service.SaveSettingsAsync(settings);
        _database.Unavailable = true;
        var cached = await Pc("first").GetQuoteLaborAsync(entry.QuoteNumber);
        Assert.False(cached.IsCurrent); Assert.Equal(9, cached.Labor!.TotalPersonHours);
        Assert.Equal(18 * 60, cached.Labor.Rows.Single().EndMinutes);
    }

    [Theory]
    [InlineData("{\"QuotePages\":null}")]
    [InlineData("{\"QuotePages\":{\"2026/001\":null}}")]
    [InlineData("{\"QuotePages\":{\"2026/001\":{\"Settings\":null}}}")]
    [InlineData("{\"QuotePages\":{\"2026/001\":{\"Entries\":null}}}")]
    public async Task InvalidQuoteCacheCanRecoverOnlineAndNeverPretendsToBeCurrent(string data)
    {
        string cachePath = Path.Combine(_root, "first", "schedule.json");
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        var node = System.Text.Json.Nodes.JsonNode.Parse(data)!;
        node["Version"] = 2; node["DatabaseIdentity"] = "company-a";
        File.WriteAllText(cachePath, node.ToJsonString());
        _database.Unavailable = true;
        var service = Pc("first");
        Assert.False((await service.GetQuoteLaborAsync("2026/001")).HasCachedData);
        _database.Unavailable = false;
        Assert.True((await service.GetQuoteLaborAsync("2026/001")).IsCurrent);
    }

    private static WorkScheduleEntry Entry(DateTime date, EmployeeSettingsModel? employee = null) => new()
    {
        Date = date, QuoteNumber = "2026/001", EmployeeIds = employee == null ? [] : [employee.Id],
        Employees = employee == null ? [] : [employee]
    };

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private sealed class Repository : IWorkScheduleRepository
    {
        private WorkScheduleSettings _settings = new();
        private readonly List<WorkScheduleEntry> _entries = [];
        public bool Unavailable;
        public int WeekLoadCalls;
        public int QuoteLoadCalls;
        public Task<WorkScheduleSnapshot> LoadWorkScheduleAsync(DateTime from, DateTime to, CancellationToken token = default)
        { Check(token); WeekLoadCalls++; throw new InvalidOperationException("Crew costs must use the dedicated quote query."); }
        public Task<WorkScheduleSnapshot> LoadWorkScheduleForQuoteAsync(string quoteNumber, CancellationToken token = default)
        {
            Check(token); QuoteLoadCalls++;
            return Task.FromResult(new WorkScheduleSnapshot
            {
                Settings = _settings.CreateValidatedCopy(),
                Entries = _entries.Where(x => x.Kind == WorkScheduleEntryKind.Job && x.QuoteNumber == quoteNumber)
                    .Select(x => x.CreateValidatedCopy()).ToList()
            });
        }
        public Task<WorkScheduleSettings> GetWorkScheduleSettingsAsync(CancellationToken token = default)
        { Check(token); return Task.FromResult(_settings.CreateValidatedCopy()); }
        public Task<WorkScheduleSettings> SaveWorkScheduleSettingsAsync(WorkScheduleSettings settings, CancellationToken token = default)
        { Check(token); _settings = settings.CreateValidatedCopy(); _settings.Revision++; return Task.FromResult(_settings.CreateValidatedCopy()); }
        public Task<List<WorkScheduleEntry>> SaveWorkScheduleEntriesAsync(IReadOnlyList<WorkScheduleEntry> entries, CancellationToken token = default)
        {
            Check(token); var saved = entries.Select(x => x.CreateValidatedCopy()).ToList();
            foreach (var entry in saved) { entry.Revision++; _entries.RemoveAll(x => x.Id == entry.Id); _entries.Add(entry); }
            return Task.FromResult(saved.Select(x => x.CreateValidatedCopy()).ToList());
        }
        public Task<WorkScheduleEntry> CompleteWorkScheduleEntryAsync(Guid id, long revision, CancellationToken token = default)
        {
            Check(token); var completed = WorkScheduleRules.CreateCompletedCopy(_entries.SingleOrDefault(entry => entry.Id == id), id, revision);
            _entries.RemoveAll(entry => entry.Id == id); _entries.Add(completed); return Task.FromResult(completed.CreateValidatedCopy());
        }
        public Task DeleteWorkScheduleEntryAsync(Guid id, long revision, CancellationToken token = default)
        { Check(token); _entries.RemoveAll(x => x.Id == id); return Task.CompletedTask; }
        private void Check(CancellationToken token) { token.ThrowIfCancellationRequested(); if (Unavailable) throw new IOException("Offline"); }
    }
}
