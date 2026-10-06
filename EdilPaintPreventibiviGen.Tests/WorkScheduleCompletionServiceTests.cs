using System.Reflection;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class WorkScheduleCompletionServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "EdilPaintScheduleCompletion", Guid.NewGuid().ToString("N"));
    private readonly WorkScheduleService _schedule;
    private readonly StoragePathService _storage;
    private readonly ScheduleProxy _repository;
    private readonly IDataService _data;
    private readonly DataProxy _quotes;
    private static readonly DateTime Now = new(2026, 10, 6, 16, 20, 0);

    public WorkScheduleCompletionServiceTests()
    {
        var repository = DispatchProxy.Create<IWorkScheduleRepository, ScheduleProxy>();
        _repository = (ScheduleProxy)repository;
        var employeeId = Guid.NewGuid();
        _repository.Entry = new WorkScheduleEntry
        {
            Id = Guid.NewGuid(), Revision = 4, Date = Now.Date, QuoteNumber = "Q-51",
            Kind = WorkScheduleEntryKind.Job, Status = WorkScheduleEntryStatus.Planned,
            SlotKind = WorkScheduleSlotKind.Custom, StartMinutes = 9 * 60, EndMinutes = 12 * 60,
            SettingsRevision = 3, CustomerName = "Customer", ReferenceName = "Reference", SiteName = "Site",
            Notes = "Accesso laterale", IsOrderActive = false,
            EmployeeIds = [employeeId], Employees = [new() { Id = employeeId, FirstName = "Mario", LastName = "Rossi" }]
        };
        _schedule = new(repository, Path.Combine(_root, "cache", "calendar.json"), "test-database");
        _storage = new(new PdfStorageSettingsModel { RootPath = Path.Combine(_root, "pdf") });
        _data = DispatchProxy.Create<IDataService, DataProxy>();
        _quotes = (DataProxy)_data;
    }

    private WorkScheduleCompletionService Service(Action<RealProfitPdfContext, string>? writer = null) =>
        new(_schedule, _data, _storage, writer ?? ((_, path) => File.WriteAllText(path, "new-pdf")), () => Now);

    private WorkScheduleEntry Selected => _repository.Entry!.CreateValidatedCopy();
    private string FinalPath => _storage.BuildWorkScheduleCostsPdfPath(Selected.QuoteNumber, Selected.Date, Selected.Id);
    private string[] StagingFiles => Directory.Exists(_storage.GetWorkScheduleCostsFolder())
        ? Directory.GetFiles(_storage.GetWorkScheduleCostsFolder(), "*.tmp.pdf") : [];

    [Fact]
    public async Task MissingSavedCalculationDoesNotGenerateOrCompleteTheVisit()
    {
        _quotes.Quote!.RealProfit = null;
        bool written = false;
        var exception = await Assert.ThrowsAsync<WorkScheduleCostsCalculationRequiredException>(() =>
            Service((_, _) => written = true).CompleteAsync(Selected));

        Assert.Equal("Q-51", exception.QuoteNumber);
        Assert.False(written);
        Assert.Equal(0, _repository.CompleteCalls);
        Assert.Equal(WorkScheduleEntryStatus.Planned, _repository.Entry!.Status);
        Assert.False(Directory.Exists(_storage.GetWorkScheduleCostsFolder()));
    }

    [Fact]
    public async Task OfflineCalendarCannotCompleteUsingItsCachedVisit()
    {
        var selected = Selected;
        await _schedule.GetLatestAsync(selected.Date, selected.Date.AddDays(1));
        _repository.Unavailable = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().CompleteAsync(selected));

        Assert.Equal(0, _quotes.QuoteReads);
        Assert.Equal(0, _repository.CompleteCalls);
        Assert.Equal(WorkScheduleEntryStatus.Planned, _repository.Entry!.Status);
    }

    [Fact]
    public async Task QuoteDataThatCannotSynchronizeCannotProduceACompletionReport()
    {
        _quotes.CanSynchronize = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().CompleteAsync(Selected));
        Assert.Equal(0, _repository.CompleteCalls);
        Assert.False(Directory.Exists(_storage.GetWorkScheduleCostsFolder()));
    }

    [Fact]
    public async Task StaleRevisionIsRejectedBeforeReadingTheQuote()
    {
        var selected = Selected;
        _repository.Entry!.Revision++;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().CompleteAsync(selected));
        Assert.Equal(0, _quotes.QuoteReads);
        Assert.Equal(0, _repository.CompleteCalls);
    }

    [Fact]
    public async Task RemovedVisitIsRejectedWithoutGeneratingAReport()
    {
        var selected = Selected;
        _repository.Entry = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().CompleteAsync(selected));
        Assert.Equal(0, _quotes.QuoteReads);
        Assert.Equal(0, _repository.CompleteCalls);
    }

    [Fact]
    public async Task PdfWriterFailureKeepsVisitPlannedAndCleansItsPartialFile()
    {
        await Assert.ThrowsAsync<IOException>(() => Service((_, path) =>
        {
            File.WriteAllText(path, "partial");
            throw new IOException("PDF failed");
        }).CompleteAsync(Selected));

        Assert.Equal(WorkScheduleEntryStatus.Planned, _repository.Entry!.Status);
        Assert.Equal(0, _repository.CompleteCalls);
        Assert.Empty(StagingFiles);
        Assert.False(File.Exists(FinalPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrEmptyGeneratedPdfCannotCompleteTheVisit(bool createEmptyFile)
    {
        await Assert.ThrowsAsync<IOException>(() => Service((_, path) =>
        {
            if (createEmptyFile) File.WriteAllText(path, "");
        }).CompleteAsync(Selected));
        Assert.Equal(0, _repository.CompleteCalls);
        Assert.Equal(WorkScheduleEntryStatus.Planned, _repository.Entry!.Status);
        Assert.Empty(StagingFiles);
    }

    [Fact]
    public async Task CompletionPublishesFlatPdfAndPreservesAllSavedVisitDetailsAndQuoteStatus()
    {
        var selected = Selected;
        RealProfitPdfContext? context = null;
        var result = await Service((value, path) =>
        {
            context = value;
            Assert.Equal(0, _repository.CompleteCalls);
            Assert.False(File.Exists(FinalPath));
            File.WriteAllText(path, "new-pdf");
        }).CompleteAsync(selected);

        Assert.Equal(1, _repository.CompleteCalls);
        Assert.Equal(WorkScheduleEntryStatus.Completed, result.Entry.Status);
        Assert.Equal(selected.Revision + 1, result.Entry.Revision);
        Assert.Equal(selected.Id, result.Entry.Id);
        Assert.Equal(selected.Date, result.Entry.Date);
        Assert.Equal(selected.SettingsRevision, result.Entry.SettingsRevision);
        Assert.Equal(selected.SlotKind, result.Entry.SlotKind);
        Assert.Equal(selected.StartMinutes, result.Entry.StartMinutes);
        Assert.Equal(selected.EndMinutes, result.Entry.EndMinutes);
        Assert.Equal(selected.Notes, result.Entry.Notes);
        Assert.Equal(selected.ReferenceName, result.Entry.ReferenceName);
        Assert.Equal(selected.SiteName, result.Entry.SiteName);
        Assert.Equal(selected.EmployeeIds, result.Entry.EmployeeIds);
        Assert.Equal("Mario", result.Entry.Employees.Single().FirstName);
        Assert.Equal("Rossi", result.Entry.Employees.Single().LastName);
        Assert.False(result.Entry.IsActiveOrder);
        Assert.Equal(QuoteStatus.Confermato, _quotes.Quote!.Status);
        Assert.Equal(FinalPath, result.PdfPath);
        Assert.Equal(_storage.GetWorkScheduleCostsFolder(), Path.GetDirectoryName(result.PdfPath));
        Assert.Equal("new-pdf", File.ReadAllText(result.PdfPath));
        Assert.Empty(StagingFiles);
        Assert.False(_quotes.IncludeAttachments);
        Assert.Equal(Now, context!.GeneratedAt);
        Assert.Equal("Company", context.CompanyName);
        Assert.Equal(1, context.Input.Workers);
        Assert.Equal(120, context.Result.LaborCost);
        Assert.Equal(WorkScheduleEntryStatus.Planned, selected.Status);
    }

    [Fact]
    public async Task DatabaseFailureKeepsPreviousReportAndCleansUnpublishedPdf()
    {
        Directory.CreateDirectory(_storage.GetWorkScheduleCostsFolder());
        File.WriteAllText(FinalPath, "previous-pdf");
        _repository.FailComplete = true;

        await Assert.ThrowsAsync<IOException>(() => Service().CompleteAsync(Selected));

        Assert.Equal(WorkScheduleEntryStatus.Planned, _repository.Entry!.Status);
        Assert.Equal(4, _repository.Entry.Revision);
        Assert.Equal("previous-pdf", File.ReadAllText(FinalPath));
        Assert.Empty(StagingFiles);
    }

    [Fact]
    public async Task VisitChangedDuringPdfGenerationCannotBeCompletedFromAnOldRevision()
    {
        var selected = Selected;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service((_, path) =>
        {
            File.WriteAllText(path, "unpublished");
            _repository.Entry!.Revision++;
            _repository.Entry.Notes = "Modified on another PC";
        }).CompleteAsync(selected));

        Assert.Equal(WorkScheduleEntryStatus.Planned, _repository.Entry!.Status);
        Assert.Equal("Modified on another PC", _repository.Entry.Notes);
        Assert.False(File.Exists(FinalPath));
        Assert.Empty(StagingFiles);
    }

    [Fact]
    public async Task PdfPublicationFailureReportsCommittedVisitAndRetryDoesNotChangeRevision()
    {
        Directory.CreateDirectory(FinalPath);
        var exception = await Assert.ThrowsAsync<WorkScheduleCostsPublicationException>(() =>
            Service().CompleteAsync(Selected));

        Assert.Equal(WorkScheduleEntryStatus.Completed, exception.Entry.Status);
        Assert.Equal(5, exception.Entry.Revision);
        Assert.Equal(FinalPath, exception.PdfPath);
        Assert.Equal(WorkScheduleEntryStatus.Completed, _repository.Entry!.Status);
        Assert.Equal(1, _repository.CompleteCalls);
        Assert.Empty(StagingFiles);

        Directory.Delete(exception.PdfPath);
        var result = await Service().GenerateCostsAsync(exception.Entry);
        Assert.Equal(2, _repository.CompleteCalls);
        Assert.Equal(5, result.Entry.Revision);
        Assert.Equal("new-pdf", File.ReadAllText(result.PdfPath));
    }

    [Fact]
    public async Task CompletingAnAlreadyCompletedVisitOnlyRegeneratesItsReport()
    {
        _repository.Entry!.Status = WorkScheduleEntryStatus.Completed;
        var selected = Selected;
        var result = await Service().CompleteAsync(selected);
        Assert.Equal(1, _repository.CompleteCalls);
        Assert.Equal(selected.Revision, result.Entry.Revision);
        Assert.Equal(WorkScheduleEntryStatus.Completed, result.Entry.Status);
        Assert.True(File.Exists(result.PdfPath));
    }

    [Theory]
    [InlineData("reopened")]
    [InlineData("removed")]
    [InlineData("moved")]
    public async Task RegenerationRejectsFinishedVisitChangedDuringRendering(string change)
    {
        _repository.Entry!.Status = WorkScheduleEntryStatus.Completed;
        var selected = Selected;
        string expectedPath = FinalPath;
        Directory.CreateDirectory(_storage.GetWorkScheduleCostsFolder());
        File.WriteAllText(expectedPath, "previous-pdf");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service((_, path) =>
        {
            File.WriteAllText(path, "unpublished");
            if (change == "removed")
            {
                _repository.Entry = null;
                return;
            }
            _repository.Entry!.Revision++;
            _repository.Entry.Notes = "Modified on another PC";
            if (change == "reopened") _repository.Entry.Status = WorkScheduleEntryStatus.Planned;
            if (change == "moved") _repository.Entry.Date = selected.Date.AddDays(1);
        }).GenerateCostsAsync(selected));

        Assert.Equal(1, _repository.CompleteCalls);
        Assert.Equal("previous-pdf", File.ReadAllText(expectedPath));
        Assert.Empty(StagingFiles);
        if (change == "removed")
        {
            Assert.Null(_repository.Entry);
            return;
        }
        Assert.Equal(selected.Revision + 1, _repository.Entry!.Revision);
        Assert.Equal("Modified on another PC", _repository.Entry.Notes);
        Assert.Equal(change == "reopened" ? WorkScheduleEntryStatus.Planned : WorkScheduleEntryStatus.Completed,
            _repository.Entry.Status);
        Assert.Equal(change == "moved" ? selected.Date.AddDays(1) : selected.Date, _repository.Entry.Date);
    }

    [Fact]
    public async Task RegenerationCannotSilentlyCompleteAPlannedVisit()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().GenerateCostsAsync(Selected));
        Assert.Equal(0, _quotes.QuoteReads);
        Assert.Equal(0, _repository.CompleteCalls);
    }

    [Fact]
    public async Task CancellationDuringGenerationCleansThePdfAndKeepsVisitPlanned()
    {
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service((_, path) =>
        {
            File.WriteAllText(path, "partial");
            cancellation.Cancel();
        }).CompleteAsync(Selected, cancellation.Token));

        Assert.Equal(0, _repository.CompleteCalls);
        Assert.Equal(WorkScheduleEntryStatus.Planned, _repository.Entry!.Status);
        Assert.Empty(StagingFiles);
        Assert.False(File.Exists(FinalPath));
    }

    [Fact]
    public async Task CancellationAfterCommitStillPublishesTheFinishedVisitsReport()
    {
        using var cancellation = new CancellationTokenSource();
        _repository.AfterComplete = cancellation.Cancel;
        var result = await Service().CompleteAsync(Selected, cancellation.Token);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(WorkScheduleEntryStatus.Completed, result.Entry.Status);
        Assert.Equal("new-pdf", File.ReadAllText(result.PdfPath));
        Assert.Empty(StagingFiles);
    }

    [Fact]
    public async Task UnsavedVisitCannotBeCompleted()
    {
        var selected = Selected;
        selected.Revision = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().CompleteAsync(selected));
        Assert.Equal(0, _repository.LoadCalls);
        Assert.Equal(0, _repository.CompleteCalls);
    }

    [Fact]
    public void ReportRecalculatesSavedInputInAnIndependentSnapshotWithoutInventingCrewValues()
    {
        var quote = _quotes.Quote!;
        var saved = quote.RealProfit!.Input;
        saved.ProfitReductionPercentage = 10;
        saved.SupplierDiscount = 5;
        saved.ExcludeMaterials = true;
        saved.Materials = [new() { Name = "Paint", Quantity = 10000.125m, UnitOfMeasure = "m²",
            CustomerUnitPrice = 7.5, CustomerDiscount = 2 }];
        saved.CompanyMaterials = [new() { Name = "Tape", Quantity = 2.5m, UnitOfMeasure = "ml",
            UnitCost = 4, Source = "Automatico" }];
        quote.RealProfit.Result = new() { LaborCost = -999 };
        var context = WorkScheduleCompletionService.CreatePdfContext(quote, new() { Nome = "Company" }, Now);

        Assert.Equal(quote.QuoteNumber, context.QuoteNumber);
        Assert.Equal(quote.Date, context.QuoteDate);
        Assert.Equal(quote.CustomerName, context.CustomerName);
        Assert.Equal("Company", context.CompanyName);
        Assert.True(context.CustomerIsSupplier);
        Assert.Equal(1, context.Input.Workers);
        Assert.Equal(1, context.Input.Days);
        Assert.Equal(3, context.Input.HoursPerDay);
        Assert.Equal(40, context.Input.HourlyCost);
        Assert.Equal(10, context.Input.ProfitReductionPercentage);
        Assert.Equal(5, context.Input.SupplierDiscount);
        Assert.Equal(120, context.Result.LaborCost);
        Assert.Equal(10, context.Result.CompanyMaterialCost);
        Assert.Equal(10000.125m, context.Input.Materials.Single().Quantity);
        Assert.Equal("m²", context.Input.Materials.Single().UnitOfMeasure);
        Assert.Equal(2, context.Input.Materials.Single().CustomerDiscount);
        Assert.Equal("Automatico", context.Input.CompanyMaterials.Single().Source);

        saved.Workers = 9;
        saved.Materials[0].Quantity = 0;
        saved.CompanyMaterials[0].Name = "Changed";
        Assert.Equal(1, context.Input.Workers);
        Assert.Equal(10000.125m, context.Input.Materials.Single().Quantity);
        Assert.Equal("Tape", context.Input.CompanyMaterials.Single().Name);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    public class ScheduleProxy : DispatchProxy
    {
        public WorkScheduleEntry? Entry { get; set; }
        public bool Unavailable { get; set; }
        public bool FailComplete { get; set; }
        public int CompleteCalls { get; private set; }
        public int LoadCalls { get; private set; }
        public Action? AfterComplete { get; set; }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case nameof(IWorkScheduleRepository.LoadWorkScheduleAsync):
                    LoadCalls++;
                    if (Unavailable) throw new IOException("Offline");
                    return Task.FromResult(new WorkScheduleSnapshot
                    {
                        From = (DateTime)args![0]!, To = (DateTime)args[1]!,
                        Entries = Entry == null ? [] : [Entry.CreateValidatedCopy()]
                    });
                case nameof(IWorkScheduleRepository.CompleteWorkScheduleEntryAsync):
                    CompleteCalls++;
                    if (FailComplete) throw new IOException("Database failure");
                    Entry = WorkScheduleRules.CreateCompletedCopy(Entry, (Guid)args![0]!, (long)args[1]!);
                    AfterComplete?.Invoke();
                    return Task.FromResult(Entry.CreateValidatedCopy());
                default:
                    throw new InvalidOperationException($"Unexpected repository mutation: {method.Name}");
            }
        }
    }

    public class DataProxy : DispatchProxy
    {
        public bool CanSynchronize { get; set; } = true;
        public int QuoteReads { get; private set; }
        public bool IncludeAttachments { get; private set; } = true;
        public QuoteHistoryEntry? Quote { get; set; } = new()
        {
            QuoteNumber = "Q-51", Date = new(2026, 10, 1), CustomerName = "Customer", Status = QuoteStatus.Confermato,
            RealProfit = new() { Input = new() { QuoteRevenue = 1000, Workers = 1, Days = 1, HoursPerDay = 3, HourlyCost = 40 } }
        };

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "get_CanSynchronize": return CanSynchronize;
                case nameof(IDataService.GetQuoteByNumberAsync):
                    QuoteReads++;
                    IncludeAttachments = (bool)args![2]!;
                    Assert.Equal("Q-51", args[0]);
                    return Task.FromResult(Quote);
                case nameof(IDataService.GetCompanyAsync): return Task.FromResult<Company?>(new() { Nome = "Company" });
                default: throw new InvalidOperationException($"Unexpected data mutation: {method.Name}");
            }
        }
    }
}
