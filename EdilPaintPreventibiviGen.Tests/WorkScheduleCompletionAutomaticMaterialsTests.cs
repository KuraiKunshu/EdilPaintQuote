using System.Reflection;
using System.Text.Json;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class WorkScheduleCompletionAutomaticMaterialsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "EdilPaintCompletionAutomaticMaterials", Guid.NewGuid().ToString("N"));
    private readonly WorkScheduleService _schedule;
    private readonly StoragePathService _storage;
    private readonly WorkScheduleCompletionServiceTests.ScheduleProxy _visits;
    private readonly IDataService _data;
    private readonly DataProxy _catalogs;
    private readonly RulesRepository _rules = new();
    private readonly AutomaticMaterialSettingsService _automatic;
    private RealProfitPdfContext? _writtenContext;

    public WorkScheduleCompletionAutomaticMaterialsTests()
    {
        var repository = DispatchProxy.Create<IWorkScheduleRepository, WorkScheduleCompletionServiceTests.ScheduleProxy>();
        _visits = (WorkScheduleCompletionServiceTests.ScheduleProxy)repository;
        var worker = Guid.NewGuid();
        _visits.Entry = new WorkScheduleEntry
        {
            Id = Guid.NewGuid(), Revision = 4, Date = new(2026, 10, 7), QuoteNumber = "Q-51",
            Kind = WorkScheduleEntryKind.Job, Status = WorkScheduleEntryStatus.Planned,
            SlotKind = WorkScheduleSlotKind.Custom, StartMinutes = 540, EndMinutes = 720,
            EmployeeIds = [worker], Employees = [new() { Id = worker, FirstName = "Mario" }]
        };
        _schedule = new(repository, Path.Combine(_root, "calendar-cache.json"), "test-database");
        _storage = new(new PdfStorageSettingsModel { RootPath = Path.Combine(_root, "pdf") });
        _data = DispatchProxy.Create<IDataService, DataProxy>();
        _catalogs = (DataProxy)_data;
        var local = new RealProfitSettingsModel();
        local.Normalize();
        _automatic = new(_rules, Path.Combine(_root, "automatic-cache.json"), "test-database", local);
    }

    [Fact]
    public async Task FreshSharedRulesAndCatalogPricesReplaceAutomaticCostsAndPreserveManualCosts()
    {
        _rules.Stored.WindowMaterialRules[0].QuantityParameter = 1;
        await _automatic.GetLatestAsync();
        // A different computer changes the rule and the current catalogue price.
        _rules.Stored.WindowMaterialRules[0].QuantityParameter = 2;
        _catalogs.Materials[0].UnitPrice = 17.5;
        string savedBefore = JsonSerializer.Serialize(_catalogs.Quote.RealProfit);

        var result = await Service().CompleteAsync(Selected);

        Assert.Equal(2, _rules.LoadCalls);
        Assert.Equal(1, _catalogs.LaborReads);
        Assert.Equal(1, _catalogs.MaterialReads);
        var input = _writtenContext!.Input;
        var automatic = Assert.Single(input.CompanyMaterials, cost => cost.Source == "Automatico");
        Assert.Equal("Stucco", automatic.Name);
        Assert.Equal(6, automatic.Quantity);
        Assert.Equal(17.5, automatic.UnitCost);
        Assert.Equal("kg", automatic.UnitOfMeasure);
        var manual = Assert.Single(input.CompanyMaterials, cost => cost.Source == "Manuale");
        Assert.Equal("Noleggio", manual.Name);
        Assert.Equal(10, manual.Total);
        Assert.Equal(115, _writtenContext.Result.CompanyMaterialCost);
        Assert.Equal(120, _writtenContext.Result.LaborCost);
        Assert.Equal(235, _writtenContext.Result.TotalCosts);
        Assert.Equal(savedBefore, JsonSerializer.Serialize(_catalogs.Quote.RealProfit));
        Assert.Equal(12, _catalogs.Quote.Labors[0].PersistentId);
        Assert.Equal(WorkScheduleEntryStatus.Completed, result.Entry.Status);
        Assert.Equal(1, _visits.CompleteCalls);
        Assert.True(File.Exists(result.PdfPath));
    }

    [Fact]
    public async Task CachedAutomaticRulesCannotFinishVisitWhenSharedRulesAreOffline()
    {
        await _automatic.GetLatestAsync();
        _rules.FailRead = true;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Service().CompleteAsync(Selected));

        Assert.Contains("regole dei costi automatici", exception.Message);
        Assert.Contains("connessione", exception.Message);
        Assert.Equal(0, _catalogs.LaborReads);
        Assert.Equal(0, _catalogs.MaterialReads);
        AssertNotWrittenOrCompleted();
    }

    [Fact]
    public async Task UnresolvedRuleStopsReportWithTheSpecificMissingCatalogItem()
    {
        _rules.Stored.WindowMaterialRules[0].MaterialCatalogId = 999;
        _rules.Stored.WindowMaterialRules[0].MaterialName = "Materiale scomparso";
        _catalogs.Materials.Clear();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Service().CompleteAsync(Selected));

        Assert.Contains("Verifica i costi automatici nelle impostazioni", exception.Message);
        Assert.Contains("Regola 1", exception.Message);
        Assert.Contains("Materiale scomparso", exception.Message);
        Assert.Contains("non riconosciuto in modo univoco", exception.Message);
        AssertNotWrittenOrCompleted();
    }

    [Fact]
    public async Task ExcludingMaterialsSkipsAutomaticRuleAndCatalogReads()
    {
        _catalogs.Quote.RealProfit!.Input.ExcludeMaterials = true;
        _rules.FailRead = true;

        var result = await Service().CompleteAsync(Selected);

        Assert.Equal(0, _rules.LoadCalls);
        Assert.Equal(0, _catalogs.LaborReads);
        Assert.Equal(0, _catalogs.MaterialReads);
        Assert.True(_writtenContext!.Input.ExcludeMaterials);
        Assert.Equal(WorkScheduleEntryStatus.Completed, result.Entry.Status);
        Assert.True(File.Exists(result.PdfPath));
    }

    [Fact]
    public async Task DisabledWindowAutomationsRemoveSpecificAutomaticCostsAndKeepManualCosts()
    {
        _rules.Stored.WindowMaterialRules[0].IsWindowAutomation = true;
        _catalogs.Quote.Materials = [new() { Name = "GGL 0061", Quantity = 3, UnitOfMeasure = "pz" }];

        var result = await Service(enableWindowAutomations: false).CompleteAsync(Selected);

        var manual = Assert.Single(_writtenContext!.Input.CompanyMaterials);
        Assert.Equal("Manuale", manual.Source);
        Assert.Equal("Noleggio", manual.Name);
        Assert.Equal(10, _writtenContext.Result.CompanyMaterialCost);
        Assert.DoesNotContain(_writtenContext.Input.CompanyMaterials, cost => cost.Source == "Automatico");
        Assert.Equal(WorkScheduleEntryStatus.Completed, result.Entry.Status);
        Assert.True(File.Exists(result.PdfPath));
        Assert.Equal(2, _catalogs.Quote.RealProfit!.Input.CompanyMaterials.Count);
    }

    [Fact]
    public async Task ConnectionLostWhileReadingCatalogsStopsReportBeforeRenderingOrCompleting()
    {
        _catalogs.GoOfflineOnCatalogRead = true;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Service().CompleteAsync(Selected));

        Assert.Contains("database non è disponibile", exception.Message);
        Assert.Equal(1, _catalogs.LaborReads);
        Assert.Equal(1, _catalogs.MaterialReads);
        AssertNotWrittenOrCompleted();
    }

    private WorkScheduleEntry Selected => _visits.Entry!.CreateValidatedCopy();

    private WorkScheduleCompletionService Service(bool enableWindowAutomations = true) =>
        new(_schedule, _data, _storage, (context, path) =>
        {
            _writtenContext = context;
            File.WriteAllText(path, "mock-pdf");
        }, automaticMaterials: _automatic, enableWindowAutomations: enableWindowAutomations);

    private void AssertNotWrittenOrCompleted()
    {
        Assert.Null(_writtenContext);
        Assert.Equal(0, _visits.CompleteCalls);
        Assert.Equal(WorkScheduleEntryStatus.Planned, _visits.Entry!.Status);
        Assert.False(Directory.Exists(_storage.GetWorkScheduleCostsFolder()));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class RulesRepository : IAutomaticMaterialSettingsRepository
    {
        public bool FailRead { get; set; }
        public int LoadCalls { get; private set; }
        public AutomaticMaterialSettings Stored { get; } = new()
        {
            Revision = 5, WindowProductPrefixes = ["GGL"], WindowMaterialRules =
            [
                new()
                {
                    Enabled = true, IsWindowAutomation = false,
                    LaborCatalogId = 12, LaborName = "Finitura",
                    MaterialCatalogId = 5, MaterialName = "Stucco",
                    CalculationMode = "FixedPerWindow", QuantityParameter = 2
                }
            ]
        };

        public Task<AutomaticMaterialSettings> LoadAutomaticMaterialSettingsAsync(AutomaticMaterialSettings? legacy,
            CancellationToken token = default)
        {
            LoadCalls++;
            if (FailRead) throw new IOException("Offline automatic rules");
            return Task.FromResult(Stored.CreateValidatedCopy());
        }

        public Task<AutomaticMaterialSettings> SaveAutomaticMaterialSettingsAsync(AutomaticMaterialSettings settings,
            CancellationToken token = default) => throw new InvalidOperationException("Unexpected settings mutation");
    }

    public class DataProxy : DispatchProxy
    {
        public bool CanSynchronize { get; set; } = true;
        public bool GoOfflineOnCatalogRead { get; set; }
        public int LaborReads { get; private set; }
        public int MaterialReads { get; private set; }
        public List<Item> Labors { get; } = [new() { PersistentId = 120, Name = "Finitura" }];
        public List<Item> Materials { get; } =
            [new() { PersistentId = 50, Name = "Stucco", IsCompanyMaterial = true, UnitPrice = 10, UnitOfMeasure = "kg" }];
        public QuoteHistoryEntry Quote { get; } = new()
        {
            QuoteNumber = "Q-51", Date = new(2026, 10, 1), CustomerName = "Cliente", Status = QuoteStatus.Confermato,
            Labors = [new() { PersistentId = 12, Name = "Finitura", Quantity = 3 }],
            RealProfit = new()
            {
                Input = new()
                {
                    QuoteRevenue = 1000, Workers = 10, Days = 10, HoursPerDay = 8, HourlyCost = 40,
                    CompanyMaterials =
                    [
                        new() { Name = "Costo automatico vecchio", Quantity = 1, UnitCost = 999, Source = "Automatico" },
                        new() { Name = "Noleggio", Quantity = 2, UnitCost = 5, Source = "Manuale" }
                    ]
                }
            }
        };

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "get_CanSynchronize": return CanSynchronize;
                case nameof(IDataService.GetQuoteByNumberAsync):
                    Assert.Equal("Q-51", args![0]);
                    return Task.FromResult<QuoteHistoryEntry?>(Quote);
                case nameof(IDataService.GetCompanyAsync):
                    return Task.FromResult<Company?>(new() { Nome = "EdilPaint" });
                case nameof(IDataService.GetLaborCatalogAsync):
                    LaborReads++;
                    return Task.FromResult(Labors);
                case nameof(IDataService.GetPersonalMaterialsAsync):
                    MaterialReads++;
                    if (GoOfflineOnCatalogRead) CanSynchronize = false;
                    return Task.FromResult(Materials);
                default: throw new InvalidOperationException($"Unexpected data operation: {method.Name}");
            }
        }
    }
}
