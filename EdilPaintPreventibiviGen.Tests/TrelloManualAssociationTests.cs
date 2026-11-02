using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using EdilPaintPreventibiviGen.ViewModels;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

[Collection("Draft saving")]
public sealed class TrelloManualAssociationTests : IDisposable
{
    private const BindingFlags Fields = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly List<SemaphoreSlim> _locks = [];
    private readonly IDataService _data = DispatchProxy.Create<IDataService, DataProxy>();
    private DataProxy Data => (DataProxy)_data;

    [Fact]
    public void ExplicitAssociationAndRemovalPreserveCustomerReferenceBillingSiteAndCommercialContent()
    {
        var vm = Editor();
        string before = CommercialContent(vm);
        var customer = vm.SelectedCustomer;
        var reference = vm.SelectedSecondCustomer;
        var billing = vm.SelectedBillingCustomer;
        vm.AssociateTrelloCard(Card("manual-card"));

        Assert.Equal("manual-card", vm.TrelloCardId);
        Assert.Equal("manual-card", TrelloCard.FromSnapshot(vm.TrelloCardSnapshotJson)!.Id);
        Assert.Equal(before, CommercialContent(vm));
        Assert.Same(customer, vm.SelectedCustomer);
        Assert.Same(reference, vm.SelectedSecondCustomer);
        Assert.Same(billing, vm.SelectedBillingCustomer);

        vm.ClearTrelloCardAssociation();
        Assert.False(vm.HasTrelloCardAssociation);
        Assert.Empty(vm.TrelloCardId);
        Assert.Empty(vm.TrelloCardSnapshotJson);
        Assert.Equal(before, CommercialContent(vm));
        Assert.Empty(Data.Calls);
    }

    [Fact]
    public async Task SelectingACardForAnUnassignedDraftDoesNotChooseACustomerOrWriteToDatabase()
    {
        var vm = Editor();
        Set(vm, "_selectedCustomer", null);
        Set(vm, "_selectedSecondCustomer", null);
        Set(vm, "_selectedBillingCustomer", null);
        await vm.SetManualTrelloAssociationAsync(Card("card-with-customer-like-name"));

        Assert.Equal("card-with-customer-like-name", vm.TrelloCardId);
        Assert.Null(vm.SelectedCustomer);
        Assert.Null(vm.SelectedSecondCustomer);
        Assert.Null(vm.SelectedBillingCustomer);
        Assert.Empty(Data.Calls);
    }

    [Fact]
    public void NewQuoteClearsThePreviousCardsIdAndSnapshot()
    {
        var vm = Editor();
        vm.AssociateTrelloCard(Card("previous-card"));
        vm.ResetQuote();
        Assert.Empty(vm.TrelloCardId);
        Assert.Empty(vm.TrelloCardSnapshotJson);
        Assert.False(vm.HasTrelloCardAssociation);
        Assert.Null(vm.SelectedCustomer);
        Assert.Empty(Data.Calls);
    }

    [Fact]
    public void RecoveringACardOnlyDraftPreservesTheExplicitLinkWithoutSelectingAnyCustomer()
    {
        var vm = Editor();
        var card = Card("recovered-card");
        vm.ApplyDraft(new QuoteHistoryEntry
        {
            QuoteNumber = "42",
            TrelloCardId = card.Id,
            TrelloCardSnapshotJson = card.SerializeSnapshot(),
            CustomerNotes = "Testo commerciale della bozza"
        });

        Assert.Equal(card.Id, vm.TrelloCardId);
        Assert.Equal(card.Name, vm.TrelloCardTitle);
        Assert.Null(vm.SelectedCustomer);
        Assert.Null(vm.SelectedSecondCustomer);
        Assert.Null(vm.SelectedBillingCustomer);
        Assert.Equal("Testo commerciale della bozza", vm.CustomerNotes);
        Assert.Empty(Data.Calls);
    }

    [Fact]
    public async Task RejectedStalePatchLeavesThePreviousAssociationAndAllEditorDataUntouched()
    {
        var vm = Editor(existing: true);
        vm.AssociateTrelloCard(Card("original-card"));
        Data.RejectPatch = true;
        string before = CommercialContent(vm);

        await Assert.ThrowsAsync<QuoteConflictException>(() => vm.SetManualTrelloAssociationAsync(Card("new-card")));

        Assert.Equal("original-card", vm.TrelloCardId);
        Assert.Equal("original-card", TrelloCard.FromSnapshot(vm.TrelloCardSnapshotJson)!.Id);
        Assert.Equal(4L, Get<long>(vm, "_loadedQuoteBaseRevision"));
        Assert.Equal(before, CommercialContent(vm));
        Assert.Equal(4L, Data.ExpectedRevision);
        Assert.Equal(nameof(IDataService.UpdateQuoteTrelloAssociationAsync), Assert.Single(Data.Calls));
    }

    [Fact]
    public async Task SuccessfulPatchUsesTheExactReturnedRevisionWithoutReloadingOrReassigningCustomers()
    {
        var vm = Editor(existing: true);
        string before = CommercialContent(vm);
        await vm.SetManualTrelloAssociationAsync(Card("selected-card"));

        Assert.Equal("selected-card", vm.TrelloCardId);
        Assert.Equal(before, CommercialContent(vm));
        Assert.Equal(4L, Data.ExpectedRevision);
        Assert.Equal(Data.Metadata.Revision, Get<long>(vm, "_loadedQuoteBaseRevision"));
        Assert.Equal(Data.Metadata.LastModifiedUtc, Get<DateTime>(vm, "_loadedQuoteBaseVersionUtc"));
        Assert.Equal(nameof(IDataService.UpdateQuoteTrelloAssociationAsync), Assert.Single(Data.Calls));
    }

    [Fact]
    public void AnAssociationChangedFromHistoryDoesNotAdoptANewerCommercialRevisionIntoAStaleEditor()
    {
        var vm = Editor(existing: true);
        string before = CommercialContent(vm);
        var card = Card("explicit-history-choice");
        vm.ApplyPersistedTrelloAssociation("42", card.Id, card.SerializeSnapshot(), expectedRevision: 8,
            new QuoteMetadata { QuoteNumber = "42", Revision = 9, LastModifiedUtc = DateTime.UtcNow });

        Assert.Equal(card.Id, vm.TrelloCardId);
        Assert.Equal(4L, Get<long>(vm, "_loadedQuoteBaseRevision"));
        Assert.Equal(before, CommercialContent(vm));
        Assert.Empty(Data.Calls);
    }

    [Fact]
    public async Task DeletedCardCannotReplaceAnExistingExplicitChoice()
    {
        var vm = Editor();
        vm.AssociateTrelloCard(Card("original"));
        var deleted = Card("deleted"); deleted.IsDeleted = true;
        await Assert.ThrowsAsync<ArgumentException>(() => vm.SetManualTrelloAssociationAsync(deleted));
        Assert.Equal("original", vm.TrelloCardId);
        Assert.Empty(Data.Calls);
    }

    private MainViewModel Editor(bool existing = false)
    {
        // Match the existing draft-policy fixture: never load real per-user
        // sessions, configuration, paths, database connections or WPF resources.
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        foreach (var field in typeof(MainViewModel).GetFields(Fields))
        {
            if (field.FieldType == typeof(string)) field.SetValue(vm, string.Empty);
            if (field.FieldType.IsGenericType &&
                (field.FieldType.GetGenericTypeDefinition() == typeof(ObservableCollection<>) ||
                 field.FieldType.GetGenericTypeDefinition() == typeof(List<>)))
                field.SetValue(vm, Activator.CreateInstance(field.FieldType));
        }
        var saveLock = new SemaphoreSlim(1, 1); _locks.Add(saveLock);
        Set(vm, "_draftSaveLock", saveLock);
        Set(vm, "_dataService", _data);
        Set(vm, "_quoteCalculator", new QuoteCalculator());
        Set(vm, "_companyData", new Company { Counter = 43 });
        Set(vm, "_attachmentHashCacheLock", new object());
        Set(vm, "_selectedCustomer", new Customer { BusinessName = "Cliente scelto dall'operatore", SyncId = Guid.NewGuid() });
        Set(vm, "_selectedSecondCustomer", new Customer { BusinessName = "Referente scelto", SyncId = Guid.NewGuid() });
        Set(vm, "_selectedBillingCustomer", new Customer { BusinessName = "Fatturazione scelta", SyncId = Guid.NewGuid() });
        Set(vm, "_isSecondCustomerEnabled", true);
        Set(vm, "_isBillingCustomerEnabled", true);
        Set(vm, "_isSiteCustomerEnabled", true);
        Set(vm, "_siteAddress", "Cantiere confermato dall'operatore");
        Set(vm, "_customerNotes", "Note commerciali riservate al preventivo");
        Set(vm, "_paymentTerms", "Pagamento concordato");
        Set(vm, "_quoteNumber", "42");
        Set(vm, "_ivaType", "esclusa");
        Set(vm, "_loadedQuoteDate", new DateTime(2026, 11, 2));
        Set(vm, "_isEditingExistingQuote", existing);
        Set(vm, "_hasPersistedCurrentQuote", existing);
        Set(vm, "_loadedQuoteBaseRevision", existing ? 4L : 0L);
        vm.Materials.Add(new Item { Name = "Materiale confermato", Quantity = 2, UnitPrice = 50 });
        return vm;
    }

    private static TrelloCard Card(string id) => new()
    {
        Id = id,
        Name = "Altro cliente — sopralluogo da associare manualmente",
        Description = "Altro indirizzo, altri recapiti, note operative che non devono sostituire il preventivo",
        Url = "https://trello.com/c/abcdefgh/sopralluogo",
        SyncedAtUtc = new DateTime(2026, 11, 2, 8, 30, 0, DateTimeKind.Utc)
    };

    private static string CommercialContent(MainViewModel vm) => JsonSerializer.Serialize(new
    {
        vm.QuoteNumber, Customer = vm.SelectedCustomer?.SyncId, Reference = vm.SelectedSecondCustomer?.SyncId,
        Billing = vm.SelectedBillingCustomer?.SyncId, vm.SiteAddress, vm.CustomerNotes, vm.PaymentTerms,
        vm.IvaType, vm.MaterialDiscount, vm.LaborDiscount, vm.Materials, vm.Labors
    });

    private static void Set(object owner, string name, object? value) => owner.GetType().GetField(name, Fields)!.SetValue(owner, value);
    private static T Get<T>(object owner, string name) => (T)owner.GetType().GetField(name, Fields)!.GetValue(owner)!;
    public void Dispose() { foreach (var item in _locks) item.Dispose(); }

    public class DataProxy : DispatchProxy
    {
        public List<string> Calls { get; } = [];
        public bool RejectPatch { get; set; }
        public long ExpectedRevision { get; private set; }
        public QuoteMetadata Metadata { get; } = new()
        {
            QuoteNumber = "42", Revision = 5,
            LastModifiedUtc = new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc), SyncHash = "returned-commit-hash"
        };
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls.Add(targetMethod!.Name);
            if (targetMethod.Name == nameof(IDataService.UpdateQuoteTrelloAssociationAsync) && args is { Length: 5 })
            {
                ExpectedRevision = (long)args[3]!;
                return RejectPatch ? Task.FromException<QuoteMetadata>(new QuoteConflictException("42")) : Task.FromResult(Metadata);
            }
            throw new InvalidOperationException("Unexpected data operation: " + targetMethod.Name);
        }
    }
}
