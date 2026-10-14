using EdilPaintPreventibiviGen.Data;
using EdilPaintPreventibiviGen.Data.Entities;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class PosDraftTests
{
    [Fact]
    public void WorkAndEquipmentCatalogsAreSeparatedAndSavedEquipmentSurvivesArchiveAndEdits()
    {
        var equipment = new PosWorkItem { Kind = PosSheetKind.Equipment, Name = "Trapano", Procedures = "Prima, durante e dopo l’uso", Dpi = "Occhiali", Revision = 1 };
        var work = new PosWorkItem { Name = "Installazione", EquipmentIds = [equipment.Id] };
        var draft = new PosDraft { QuoteNumber = "42", Works = [PosDraftRules.Copy(work)], EquipmentSheets = [PosDraftRules.Copy(equipment)] };
        equipment.Procedures = "Nuove istruzioni"; equipment.IsArchived = true;
        Assert.Single(PosDraftRules.WorkChoices([work, equipment], draft));
        var saved = Assert.Single(PosDraftRules.EquipmentChoices([work, equipment], draft));
        Assert.True(saved.IsSelected);
        Assert.NotEmpty(saved.Notice);
        Assert.Equal("Prima, durante e dopo l’uso", saved.Sheet.Procedures);
        saved.Sheet.Dpi = "Personalizzati";
        Assert.Equal("Occhiali", draft.EquipmentSheets[0].Dpi);
        Assert.Equal(equipment.Id, Assert.Single(PosDraftRules.ValidateDraft(draft).Works[0].EquipmentIds));
    }

    [Fact]
    public void OldPosJsonRetainsCombinedTextsAndDefaultsToWorkWithoutLosingData()
    {
        var id = Guid.NewGuid();
        var json = "{\"QuoteNumber\":\"42\",\"Works\":[{\"Id\":\"" + id + "\",\"Name\":\"Finestre\",\"Equipment\":\"Trapano e guanti\",\"Risks\":\"Rischi e misure originali\",\"Verified\":true}]}";
        var draft = PosDraftRules.ValidateDraft(System.Text.Json.JsonSerializer.Deserialize<PosDraft>(json)!);
        var work = Assert.Single(draft.Works);
        Assert.Equal(PosSheetKind.Work, work.Kind);
        Assert.Equal("Trapano e guanti", work.Equipment);
        Assert.Equal("Rischi e misure originali", work.Risks);
        Assert.True(work.Verified);
        Assert.Empty(draft.EquipmentSheets);
        Assert.Empty(work.EquipmentIds);
    }

    [Fact]
    public void EquipmentReferencesRequireOneIncludedSnapshotAndValidSheetKinds()
    {
        var equipment = new PosWorkItem { Kind = PosSheetKind.Equipment, Name = "Trapano" };
        var work = new PosWorkItem { Name = "Finestre", EquipmentIds = [equipment.Id] };
        var draft = new PosDraft { QuoteNumber = "42", Works = [work] };
        Assert.Contains("Schede attrezzature mancanti: Finestre", PosDraftRules.Pending(draft));
        Assert.Throws<InvalidOperationException>(() => PosDraftRules.ValidateDraft(draft));
        draft.EquipmentSheets = [equipment];
        var another = new PosWorkItem { Name = "Linea vita", EquipmentIds = [equipment.Id] };
        draft.Works.Add(another);
        Assert.Single(PosDraftRules.ValidateDraft(draft).EquipmentSheets);
        draft.EquipmentSheets.Add(equipment);
        Assert.Throws<InvalidOperationException>(() => PosDraftRules.ValidateDraft(draft));
        Assert.Throws<InvalidOperationException>(() => PosDraftRules.ValidateTemplate(new() { Kind = PosSheetKind.Equipment, Name = "Trapano", EquipmentIds = [work.Id] }));
        Assert.Throws<InvalidOperationException>(() => PosDraftRules.ValidateTemplate(new() { Kind = (PosSheetKind)99, Name = "Errata" }));
    }

    [Fact]
    public void EditingDpiProceduresOrEquipmentLinksResetsReviewAndNeverMutatesCopiedLinks()
    {
        var sheet = new PosWorkItem { Name = "Finestre", EquipmentIds = [Guid.NewGuid()], Verified = true };
        var copy = PosDraftRules.Copy(sheet);
        copy.EquipmentIds = [];
        Assert.False(copy.Verified);
        Assert.Single(sheet.EquipmentIds);
        copy.Verified = true; copy.Dpi = "Guanti";
        Assert.False(copy.Verified);
        copy.Verified = true; copy.Procedures = "Procedura";
        Assert.False(copy.Verified);
    }

    [Fact]
    public void QuoteLaborsDoNotSelectPosWorksAndSiteAddressDoesNotUseBillingAddress()
    {
        var quote = new QuoteHistoryEntry { QuoteNumber = "42", CustomerName = "Cliente", SiteName = "Cantiere", Labors = [new Item { Name = "Installazione Finestre da tetto" }] };
        var draft = PosDraftRules.Create(quote, new Company { Nome = "Impresa" }, [new Customer { BusinessName = "Cliente", Address = "Indirizzo fatturazione" }]);
        Assert.Empty(draft.Works);
        Assert.Empty(draft.Employees);
        Assert.Equal("", draft.SiteAddress);
        Assert.Equal("Cantiere", draft.SiteName);
        Assert.Equal("Cliente", draft.Client);
        Assert.Equal("Impresa", draft.CompanyName);
    }

    [Fact]
    public void StableReferenceIdentitySuppliesSiteAddressAfterContactRename()
    {
        var id = Guid.NewGuid();
        var draft = PosDraftRules.Create(new() { QuoteNumber = "42", ReferenceName = "Vecchio nome", ReferenceCustomerSyncId = id },
            new(), [new Customer { SyncId = id, BusinessName = "Nuovo nome", Address = "Cantiere reale" }]);
        Assert.Equal("Cantiere reale", draft.SiteAddress);
    }

    [Fact]
    public void SavedPosRetainsOriginalTextAndRemovedEmployeeAndArchivedTemplate()
    {
        var template = new PosWorkItem { Name = "Finestre", Description = "Testo originale", Revision = 1 };
        var employee = new PosEmployee { EmployeeId = Guid.NewGuid(), Name = "Mario Rossi", Qualification = "Installatore" };
        var draft = new PosDraft { QuoteNumber = "42", Works = [PosDraftRules.Copy(template)], Employees = [employee] };
        template.Description = "Testo modificato da un altro PC"; template.Revision = 2;
        var active = PosDraftRules.WorkChoices([template], draft);
        Assert.Equal("Testo originale", Assert.Single(active).Sheet.Description);
        active[0].Sheet.Description = "Testo personalizzato";
        Assert.Equal("Testo originale", draft.Works[0].Description);
        Assert.Equal("Testo modificato da un altro PC", template.Description);
        var archived = PosDraftRules.WorkChoices([], draft);
        Assert.True(Assert.Single(archived).IsSelected);
        Assert.NotEmpty(archived[0].Notice);
        var removed = PosDraftRules.EmployeeChoices([], draft);
        Assert.True(Assert.Single(removed).IsSelected);
        Assert.Equal("Mario Rossi", removed[0].Employee.Name);
        var renamed = PosDraftRules.EmployeeChoices([new EmployeeSettingsModel { Id = employee.EmployeeId, FirstName = "Nome cambiato" }], draft);
        Assert.Equal("Mario Rossi", Assert.Single(renamed).Employee.Name);
    }

    [Fact]
    public void EditingAReviewedSectionRequiresAnotherReviewAndCopiesRetainReview()
    {
        var item = new PosWorkItem { Name = "Linea vita", Description = "Testo", Verified = true };
        Assert.True(PosDraftRules.Copy(item).Verified);
        item.Description = "Testo";
        Assert.True(item.Verified);
        item.Risks = "Misure da verificare";
        Assert.False(item.Verified);
        item.Verified = true;
        Assert.False(PosDraftRules.ValidateTemplate(item).Verified);
    }

    [Fact]
    public void IncompleteAndReversedDatesCanBeSavedAsDraftAndRemainInReview()
    {
        var empty = PosDraftRules.ValidateDraft(new PosDraft { QuoteNumber = " 42 " });
        Assert.Equal("42", empty.QuoteNumber);
        Assert.Contains("Lavorazioni POS", PosDraftRules.Pending(empty));
        var reversed = new PosDraft { QuoteNumber = "42", StartDate = new(2026, 10, 20), EndDate = new(2026, 10, 16) };
        Assert.NotNull(PosDraftRules.ValidateDraft(reversed));
        Assert.Contains("Fine lavori precedente all’inizio", PosDraftRules.Pending(reversed));
    }

    [Fact]
    public void DuplicateSnapshotsAndInvalidIdsAreRejected()
    {
        var item = new PosWorkItem { Name = "Finestre" };
        Assert.Throws<InvalidOperationException>(() => PosDraftRules.ValidateDraft(new() { QuoteNumber = "42", Works = [item, item] }));
        Assert.Throws<InvalidOperationException>(() => PosDraftRules.ValidateDraft(new() { QuoteNumber = "42", Employees = [new PosEmployee()] }));
        Assert.Throws<InvalidOperationException>(() => PosDraftRules.ValidateTemplate(new() { Name = " " }));
        Assert.Throws<InvalidOperationException>(() => PosDraftRules.ValidateTemplate(new() { Name = new string('x', 251) }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SqlModelProtectsConcurrentEditsAndAllowsLargeTexts(bool postgres)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        if (postgres) options.UseNpgsql("Host=localhost;Database=test;Username=test;Password=test");
        else options.UseSqlServer("Server=localhost;Database=test;Integrated Security=true;TrustServerCertificate=true");
        using var db = new AppDbContext(options.Options);
        var draft = db.Model.FindEntityType(typeof(PosDraftEntity))!;
        Assert.True(draft.FindProperty(nameof(PosDraftEntity.Revision))!.IsConcurrencyToken);
        Assert.True(Assert.Single(draft.GetIndexes()).IsUnique);
        Assert.Null(draft.FindProperty(nameof(PosDraftEntity.PayloadJson))!.GetMaxLength());
        Assert.Empty(draft.GetForeignKeys()); // Historical drafts survive changes to the source quote.
        Assert.True(db.Model.FindEntityType(typeof(PosTemplateEntity))!.FindProperty(nameof(PosTemplateEntity.Revision))!.IsConcurrencyToken);
    }

    [Fact]
    public async Task TwoPcsShareDraftsAndStandardTextsWhileRejectingStaleChanges()
    {
        var repository = new SharedRepository();
        var first = Service(repository); var second = Service(repository);
        var template = await first.SaveTemplateAsync(new() { Name = "Finestre", Description = "Originale" });
        Assert.Equal(template.Id, Assert.Single(await second.LoadTemplatesAsync()).Id);
        var draft = await first.SaveDraftAsync(new() { QuoteNumber = "42", Works = [template], Notes = "Prima versione" });
        var fromOtherPc = (await second.LoadDraftAsync("42"))!;
        fromOtherPc.Notes = "Secondo PC";
        await second.SaveDraftAsync(fromOtherPc);
        draft.Notes = "Versione vecchia";
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => first.SaveDraftAsync(draft));
        Assert.Contains("altro PC", error.Message);
        Assert.Equal("Secondo PC", (await first.LoadDraftAsync("42"))!.Notes);
        var newText = PosDraftRules.Copy(template); newText.Description = "Nuovo standard";
        await second.SaveTemplateAsync(newText);
        Assert.Equal("Originale", (await first.LoadDraftAsync("42"))!.Works[0].Description);
        await Assert.ThrowsAsync<InvalidOperationException>(() => first.SaveTemplateAsync(template));
        await Assert.ThrowsAsync<InvalidOperationException>(() => first.SaveDraftAsync(new() { QuoteNumber = "42" }));
    }

    [Fact]
    public async Task DatabaseUnavailableOrChangedNeverWritesAnUnsynchronizedDraft()
    {
        var repository = new SharedRepository();
        var offline = new PosService(repository, "a", () => "a", _ => throw new InvalidOperationException("offline"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => offline.SaveDraftAsync(new() { QuoteNumber = "42" }));
        var database = "a";
        var changed = new PosService(repository, "a", () => database, _ => { database = "b"; return Task.CompletedTask; });
        await Assert.ThrowsAsync<InvalidOperationException>(() => changed.SaveDraftAsync(new() { QuoteNumber = "42" }));
        Assert.Empty(repository.Drafts);
    }

    private static PosService Service(SharedRepository repository) => new(repository, "company", () => "company", _ => Task.CompletedTask);
    private sealed class SharedRepository : IPosRepository
    {
        public Dictionary<string, PosDraft> Drafts { get; } = [];
        private readonly Dictionary<Guid, PosWorkItem> _templates = [];
        public Task<List<PosWorkItem>> LoadPosTemplatesAsync(CancellationToken token = default) => Task.FromResult(_templates.Values.Where(x => !x.IsArchived).Select(PosDraftRules.Copy).ToList());
        public Task<PosWorkItem> SavePosTemplateAsync(PosWorkItem template, CancellationToken token = default)
        {
            _templates.TryGetValue(template.Id, out var stored); PosDraftRules.RequireRevision(template.Revision, stored?.Revision);
            var copy = PosDraftRules.Copy(template); copy.Revision++; _templates[copy.Id] = copy;
            return Task.FromResult(PosDraftRules.Copy(copy));
        }
        public Task<PosDraft?> LoadPosDraftAsync(string number, CancellationToken token = default) => Task.FromResult(Drafts.TryGetValue(number, out var draft) ? PosDraftRules.Copy(draft) : null);
        public Task<PosDraft> SavePosDraftAsync(PosDraft draft, CancellationToken token = default)
        {
            Drafts.TryGetValue(draft.QuoteNumber, out var stored); PosDraftRules.RequireRevision(draft.Revision, stored?.Revision);
            var copy = PosDraftRules.Copy(draft); copy.Revision++; copy.SavedAtUtc = DateTime.UtcNow; Drafts[copy.QuoteNumber] = copy;
            return Task.FromResult(PosDraftRules.Copy(copy));
        }
    }
}
