using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using EdilPaintPreventibiviGen;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using EdilPaintPreventibiviGen.ViewModels;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

[CollectionDefinition("Draft saving", DisableParallelization = true)]
public sealed class DraftSavingCollection { }

[Collection("Draft saving")]
public sealed class DraftAutosavePolicyTests : IDisposable
{
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "EdilPaintDraftPolicy", Guid.NewGuid().ToString("N"));
    private readonly AppSettingsService? _previousSettings = App.AppSettings;
    private readonly AppSettingsServiceModel _settings = new() { DeviceName = "Test PC" };
    private readonly List<SemaphoreSlim> _locks = [];
    private readonly IDataService _data = DispatchProxy.Create<IDataService, DataProxy>();
    private DataProxy Data => (DataProxy)_data;
    private LocalDraftService Drafts { get; }

    public DraftAutosavePolicyTests()
    {
        Directory.CreateDirectory(_root);
        Drafts = new LocalDraftService(_root);
        var settings = (AppSettingsService)RuntimeHelpers.GetUninitializedObject(typeof(AppSettingsService));
        typeof(AppSettingsService).GetField("<App>k__BackingField", InstanceFields)!.SetValue(settings, _settings);
        typeof(App).GetProperty(nameof(App.AppSettings))!.SetValue(null, settings);
    }

    private MainViewModel Editor(QuoteStatus? status = null, bool existing = false)
    {
        // Avoid the production constructor: it loads real per-user paths and sessions.
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        foreach (var field in typeof(MainViewModel).GetFields(InstanceFields))
        {
            if (field.FieldType == typeof(string)) field.SetValue(vm, "");
            if (field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(ObservableCollection<>))
                field.SetValue(vm, Activator.CreateInstance(field.FieldType));
        }
        var saveLock = new SemaphoreSlim(1, 1); _locks.Add(saveLock);
        Set(vm, "_draftSaveLock", saveLock);
        Set(vm, "_draftService", Drafts);
        Set(vm, "_dataService", _data);
        var paths = new StoragePathService(new PdfStorageSettingsModel { RootPath = _root });
        Set(vm, "_storagePathService", paths);
        Set(vm, "_quoteHistoryService", new QuoteHistoryService(_data, paths));
        Set(vm, "_attachmentHashCacheLock", new object());
        var cacheField = typeof(MainViewModel).GetField("_attachmentHashCache", InstanceFields)!;
        cacheField.SetValue(vm, Activator.CreateInstance(cacheField.FieldType));
        Set(vm, "_selectedCustomer", new Customer { BusinessName = "Cliente", SyncId = Guid.NewGuid() });
        Set(vm, "_quoteNumber", "42");
        Set(vm, "_ivaType", "esclusa");
        Set(vm, "_customerNotes", "Nuove note da confermare");
        Set(vm, "_paymentTerms", "Pagamento modificato");
        Set(vm, "_totaleGenerale", 900d);
        Set(vm, "_loadedQuoteDate", new DateTime(2026, 10, 1));
        Set(vm, "_isEditingExistingQuote", existing);
        Set(vm, "_loadedQuoteStatus", status);
        Set(vm, "_loadedQuoteBaseRevision", existing ? 4L : 0L);
        Set(vm, "_isDraftQuoteNumberAllocated", existing);
        vm.Materials.Add(new() { Name = "Materiale modificato", Quantity = 10000.25m, UnitPrice = 20 });
        return vm;
    }

    [Fact]
    public async Task PdfOrCloseModeSkipsEveryPeriodicWriteAndSavesANewDraftOnClose()
    {
        _settings.SaveDraftOnlyOnPdfOrClose = true;
        var vm = Editor();
        Assert.False(vm.IsPeriodicDraftSaveEnabled);
        await vm.SaveDraftAsync();
        Assert.Null(await Drafts.LoadAsync());
        Assert.Empty(Data.Calls);

        await vm.SaveDraftAsync(forceDatabaseSave: true);
        Assert.False(vm.HasDraftSyncError, vm.DraftSyncStatus);
        Assert.Equal(1, Data.Saves);
        Assert.Equal(QuoteStatus.Bozza, Data.Stored!.Status);
        Assert.Equal("Nuove note da confermare", Data.Stored.CustomerNotes);
        Assert.Equal(1, Data.NumberAllocations);
        Assert.Equal(QuoteStatus.Bozza, (await Drafts.LoadAsync())!.DraftSourceStatus);
    }

    public static IEnumerable<object[]> ProtectedStatuses => Enum.GetValues<QuoteStatus>()
        .Where(status => status != QuoteStatus.Bozza)
        .SelectMany(status => new[] { new object[] { status, false }, new object[] { status, true } });

    [Theory]
    [MemberData(nameof(ProtectedStatuses))]
    public async Task KnownFinalizedQuoteCannotBeUpdatedByPeriodicOrClosingSave(QuoteStatus status, bool closing)
    {
        Data.Stored = Stored(status);
        string before = JsonSerializer.Serialize(Data.Stored);
        var vm = Editor(status, existing: true);
        await vm.SaveDraftAsync(forceDatabaseSave: closing);
        Assert.False(vm.HasDraftSyncError, vm.DraftSyncStatus);
        Assert.Equal(0, Data.Saves);
        Assert.Empty(Data.Calls);
        Assert.Equal(before, JsonSerializer.Serialize(Data.Stored));
        var recovery = Assert.IsType<QuoteHistoryEntry>(await Drafts.LoadAsync());
        Assert.Equal(QuoteStatus.Bozza, recovery.Status);
        Assert.Equal(status, recovery.DraftSourceStatus);
        Assert.Equal("Nuove note da confermare", recovery.CustomerNotes);
        Assert.Equal(10000.25m, recovery.Materials.Single().Quantity);
        Assert.Contains("Genera PDF", vm.DraftSyncStatus);
    }

    [Theory]
    [InlineData(QuoteStatus.Bozza)]
    [InlineData(null)]
    public async Task AnotherPcFinalizingADraftAlsoProtectsLegacyRecovery(QuoteStatus? editorStatus)
    {
        Data.Stored = Stored(QuoteStatus.Finalizzato);
        string before = JsonSerializer.Serialize(Data.Stored);
        var vm = Editor(editorStatus, existing: true);
        await vm.SaveDraftAsync(forceDatabaseSave: true);
        Assert.False(vm.HasDraftSyncError, vm.DraftSyncStatus);
        Assert.Equal(0, Data.Saves);
        Assert.Equal(before, JsonSerializer.Serialize(Data.Stored));
        Assert.Equal(QuoteStatus.Finalizzato, (await Drafts.LoadAsync())!.DraftSourceStatus);
        Assert.Contains("Genera PDF", vm.DraftSyncStatus);
        Data.Calls.Clear();
        await vm.SaveDraftAsync(forceDatabaseSave: true);
        Assert.Empty(Data.Calls);
    }

    [Fact]
    public async Task UnchangedFinalizedQuoteDoesNotCreateAPhantomRecoveryDraft()
    {
        var vm = Editor(QuoteStatus.Finalizzato, existing: true);
        var snapshot = (QuoteHistoryEntry)Call(vm, "CreateDraftEntry")!;
        Set(vm, "_lastSharedDraftContentHash", Call(vm, "ComputeSharedDraftContentHash", snapshot));
        await vm.SaveDraftAsync(forceDatabaseSave: true);
        Assert.Null(await Drafts.LoadAsync());
        Assert.Empty(Data.Calls);
    }

    [Fact]
    public async Task ExistingDraftRetainsAutomaticSharingWhenTheOptionIsOff()
    {
        Data.Stored = Stored(QuoteStatus.Bozza);
        var vm = Editor(QuoteStatus.Bozza, existing: true);
        Assert.True(vm.IsPeriodicDraftSaveEnabled);
        await vm.SaveDraftAsync();
        Assert.False(vm.HasDraftSyncError, vm.DraftSyncStatus);
        Assert.Equal(1, Data.Saves);
        Assert.Equal(QuoteStatus.Bozza, Data.Stored.Status);
        Assert.Equal("Nuove note da confermare", Data.Stored.CustomerNotes);
        Assert.Equal(5, Data.Stored.BaseRevision);
    }

    [Fact]
    public async Task ExplicitPdfHistorySaveCanConfirmEditsWhileSubsequentClosingCannot()
    {
        _settings.SaveDraftOnlyOnPdfOrClose = true;
        Data.Stored = Stored(QuoteStatus.Finalizzato);
        var vm = Editor(QuoteStatus.Finalizzato, existing: true);
        await vm.SaveDraftAsync(forceDatabaseSave: true);
        Assert.Equal(0, Data.Saves);

        bool saved = await (Task<bool>)Call(vm, "SaveToHistoryAsync", Path.Combine(_root, "quote.pdf"), false, new DateTime(2026, 10, 1))!;
        Assert.True(saved);
        Assert.Equal(1, Data.Saves);
        Assert.Equal(QuoteStatus.Finalizzato, Data.Stored!.Status);
        Assert.Equal("Nuove note da confermare", Data.Stored.CustomerNotes);
        await vm.SaveDraftAsync(forceDatabaseSave: true);
        Assert.Null(await Drafts.LoadAsync());

        Set(vm, "_customerNotes", "Modifica provvisoria successiva");
        await vm.SaveDraftAsync(forceDatabaseSave: true);
        Assert.Equal(1, Data.Saves);
        Assert.Equal("Nuove note da confermare", Data.Stored.CustomerNotes);
        Assert.Equal("Modifica provvisoria successiva", (await Drafts.LoadAsync())!.CustomerNotes);
    }

    [Fact]
    public async Task RecoveryPersistsTheSourceStatusEvenWhenOnlyThatMetadataChanges()
    {
        var draft = Stored(QuoteStatus.Bozza);
        Assert.True(await Drafts.SaveIfChangedAsync(draft));
        draft.DraftSourceStatus = QuoteStatus.Finalizzato;
        Assert.True(await Drafts.SaveIfChangedAsync(draft));
        Assert.Equal(QuoteStatus.Finalizzato, (await Drafts.LoadAsync())!.DraftSourceStatus);
    }

    [Fact]
    public async Task RevisionConflictAfterTheReadKeepsTheOtherPcsFinalizedVersionAndLocalRecovery()
    {
        Data.Stored = Stored(QuoteStatus.Bozza);
        Data.FinalizeBeforeSave = true;
        var vm = Editor(QuoteStatus.Bozza, existing: true);
        await vm.SaveDraftAsync(forceDatabaseSave: true);
        Assert.True(vm.HasDraftSyncError);
        Assert.Equal(0, Data.Saves);
        Assert.Equal(QuoteStatus.Finalizzato, Data.Stored.Status);
        Assert.Equal("Note confermate", Data.Stored.CustomerNotes);
        Assert.Equal("Nuove note da confermare", (await Drafts.LoadAsync())!.CustomerNotes);
        Assert.Contains("altra postazione", vm.DraftSyncStatus);
    }

    [Fact]
    public void ExistingSettingsKeepAutosaveAndTheNewPreferenceSurvivesSerialization()
    {
        Assert.False(JsonSerializer.Deserialize<AppSettingsServiceModel>("{}")!.SaveDraftOnlyOnPdfOrClose);
        _settings.SaveDraftOnlyOnPdfOrClose = true;
        Assert.True(JsonSerializer.Deserialize<AppSettingsServiceModel>(JsonSerializer.Serialize(_settings))!.SaveDraftOnlyOnPdfOrClose);
    }

    private static QuoteHistoryEntry Stored(QuoteStatus status) => new()
    {
        QuoteNumber = "42", CustomerName = "Cliente", Date = new(2026, 10, 1), Status = status,
        Revision = 4, BaseRevision = 4, LastModifiedUtc = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc),
        CustomerNotes = "Note confermate", PaymentTerms = "Pagamento confermato", Total = 100,
        Notes = "Nota operativa", MaterialStatus = "In magazzino", SupplierName = "Fornitore",
        Materials = [new() { Name = "Materiale confermato", Quantity = 1, UnitPrice = 100 }]
    };
    private static void Set(object owner, string field, object? value) => owner.GetType().GetField(field, InstanceFields)!.SetValue(owner, value);
    private static object? Call(object owner, string name, params object?[] args) => owner.GetType().GetMethod(name, InstanceFields)!.Invoke(owner, args);

    public void Dispose()
    {
        typeof(App).GetProperty(nameof(App.AppSettings))!.SetValue(null, _previousSettings);
        foreach (var saveLock in _locks) saveLock.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    public class DataProxy : DispatchProxy
    {
        public List<string> Calls { get; } = [];
        public QuoteHistoryEntry? Stored { get; set; }
        public int Saves, NumberAllocations;
        public bool FinalizeBeforeSave;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            string method = targetMethod!.Name;
            if (method == "get_CanSynchronize") return true;
            Calls.Add(method);
            if (method == nameof(IDataService.GetQuoteByNumberAsync))
                return Task.FromResult(Stored == null ? null : JsonSerializer.Deserialize<QuoteHistoryEntry>(JsonSerializer.Serialize(Stored)));
            if (method == nameof(IDataService.GetNextQuoteNumberAsync)) { NumberAllocations++; return Task.FromResult(42); }
            if (method == nameof(IDataService.SaveQuoteAsync))
            {
                if (FinalizeBeforeSave)
                {
                    Stored!.Status = QuoteStatus.Finalizzato;
                    Stored.Revision++;
                    throw new QuoteConflictException(Stored.QuoteNumber);
                }
                Saves++;
                var entry = (QuoteHistoryEntry)args![0]!;
                entry.BaseRevision = entry.Revision = (Stored?.Revision ?? 0) + 1;
                entry.LastModifiedUtc = entry.BaseVersionUtc = DateTime.UtcNow;
                Stored = JsonSerializer.Deserialize<QuoteHistoryEntry>(JsonSerializer.Serialize(entry));
                return Task.CompletedTask;
            }
            throw new InvalidOperationException("Unexpected data operation: " + method);
        }
    }
}
