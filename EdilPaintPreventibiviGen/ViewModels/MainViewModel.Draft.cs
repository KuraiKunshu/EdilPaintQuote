using System.Diagnostics;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;

namespace EdilPaintPreventibiviGen.ViewModels;

public partial class MainViewModel
{
    private readonly LocalDraftService _draftService = new(LocalApplicationDataService.GetDataDirectoryPath());

    public async Task<QuoteHistoryEntry?> LoadDraftAsync(CancellationToken cancellationToken = default)
    {
        return await _draftService.LoadAsync(cancellationToken);
    }

    public async Task SaveDraftAsync(CancellationToken cancellationToken = default)
    {
        bool lockTaken = false;
        try
        {
            await _draftSaveLock.WaitAsync(cancellationToken);
            lockTaken = true;

            if (_isGeneratingPdf || _isGeneratingCostsPdf)
                return;

            if (!HasDraftContent())
            {
                await _draftService.DeleteAsync(cancellationToken);
                DraftSyncStatus = string.Empty;
                HasDraftSyncError = false;
                return;
            }

            var draft = CreateDraftEntry();

            // Il file locale viene sempre aggiornato per primo: resta disponibile
            // anche quando il database cloud e' temporaneamente irraggiungibile.
            await _draftService.SaveAsync(draft, cancellationToken);

            if (SelectedCustomer == null)
            {
                DraftSyncStatus = "Bozza locale: seleziona un cliente per condividerla";
                HasDraftSyncError = false;
                return;
            }

            if (!_dataService.CanSynchronize)
            {
                DraftSyncStatus = "Bozza salvata solo su questo PC: database non disponibile";
                HasDraftSyncError = true;
                return;
            }

            if (!_isEditingExistingQuote)
            {
                int nextNumber = await _dataService.GetNextQuoteNumberAsync();
                QuoteNumber = nextNumber.ToString();
                draft.QuoteNumber = QuoteNumber;
                await _draftService.SaveAsync(draft, cancellationToken);
            }

            string contentHash = QuoteSyncHashService.Compute(draft);
            if (string.Equals(contentHash, _lastSharedDraftContentHash, StringComparison.Ordinal))
            {
                return;
            }

            await SaveSharedDraftAsync(draft, cancellationToken);
            if (draft.BaseRevision > 0)
            {
                _lastSharedDraftContentHash = contentHash;
                await _draftService.SaveAsync(draft, cancellationToken);
            }
            DraftSyncStatus = $"Bozza condivisa alle {DateTime.Now:HH:mm:ss}";
            HasDraftSyncError = false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Draft] Salvataggio bozza non riuscito: {ex.Message}");
            DraftSyncStatus = "Bozza non condivisa: " + ex.Message;
            HasDraftSyncError = true;
        }
        finally
        {
            if (lockTaken)
                _draftSaveLock.Release();
        }
    }

    public Task DiscardDraftAsync(CancellationToken cancellationToken = default) =>
        _draftService.DeleteAsync(cancellationToken);

    public async Task DiscardCurrentWorkAsync(CancellationToken cancellationToken = default)
    {
        var draft = await _draftService.LoadAsync(cancellationToken);
        await _draftService.DeleteAsync(cancellationToken);

        if (draft is not { IsEditingExistingQuoteDraft: true } ||
            string.IsNullOrWhiteSpace(draft.QuoteNumber) ||
            !_dataService.CanSynchronize)
        {
            return;
        }

        var stored = await _dataService.GetQuoteByNumberAsync(draft.QuoteNumber);
        if (stored?.Status == QuoteStatus.Bozza)
            await _dataService.DeleteQuoteAsync(draft.QuoteNumber);
    }

    public void ApplyDraft(QuoteHistoryEntry draft)
    {
        ResetQuote();

        QuoteNumber = string.IsNullOrWhiteSpace(draft.QuoteNumber)
            ? _companyData.Counter.ToString()
            : draft.QuoteNumber;

        SelectedCustomer = AllCustomers.FirstOrDefault(c =>
            c.BusinessName.Equals(draft.CustomerName, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(draft.ReferenceName))
        {
            IsSecondCustomerEnabled = true;
            SelectedSecondCustomer = AllCustomers.FirstOrDefault(c =>
                c.BusinessName.Equals(draft.ReferenceName, StringComparison.OrdinalIgnoreCase));
        }

        PaymentTerms = draft.PaymentTerms;
        IvaType = !string.IsNullOrWhiteSpace(draft.IvaType) ? draft.IvaType : IvaType;
        MaterialDiscount = draft.MaterialDiscount;
        LaborDiscount = draft.LaborDiscount;
        IsJointVenture = draft.IsJointVenture;
        PartnerCompanyName = draft.PartnerCompanyName;

        Materials.Clear();
        foreach (var item in draft.Materials)
            Materials.Add(CloneItem(item));

        Labors.Clear();
        foreach (var item in draft.Labors)
            Labors.Add(CloneItem(item));

        AttachedImages.Clear();
        foreach (var attachment in draft.Attachments)
        {
            AttachedImages.Add(new SelectedAttachment
            {
                FileName = attachment.FileName,
                FilePath = string.Empty,
                ContentType = attachment.ContentType,
                Content = attachment.Content
            });
        }

        OurCosts.Clear();
        foreach (var cost in draft.OurCosts)
            OurCosts.Add(CloneCost(cost));

        PartnerCosts.Clear();
        foreach (var cost in draft.PartnerCosts)
            PartnerCosts.Add(CloneCost(cost));

        AdditionalCosts.Clear();
        foreach (var cost in draft.AdditionalCosts)
            AdditionalCosts.Add(CloneCost(cost));

        bool resumesExistingQuote = draft.IsEditingExistingQuoteDraft &&
                                    (draft.BaseRevision > 0 || draft.BaseVersionUtc != default);
        _isEditingExistingQuote = resumesExistingQuote;
        _hasPersistedCurrentQuote = resumesExistingQuote;
        _loadedQuoteDate = resumesExistingQuote ? draft.Date : null;
        _loadedQuoteBaseVersionUtc = resumesExistingQuote
            ? draft.BaseVersionUtc
            : default;
        _loadedQuoteBaseRevision = resumesExistingQuote ? draft.BaseRevision : 0;
        _lastSharedDraftContentHash = string.Empty;

        UpdateItemSortOrders();
        CalculateTotals();
    }

    private QuoteHistoryEntry CreateDraftEntry()
    {
        string deviceName = DeviceNameService.GetCurrentDeviceName();
        return new QuoteHistoryEntry
        {
            QuoteNumber = QuoteNumber,
            Date = _loadedQuoteDate ?? DateTime.Now,
            CustomerName = SelectedCustomer?.BusinessName ?? string.Empty,
            ReferenceName = IsSecondCustomerEnabled ? SelectedSecondCustomer?.BusinessName ?? string.Empty : string.Empty,
            PaymentTerms = PaymentTerms,
            IvaType = IvaType,
            Materials = Materials.Select(CloneItem).ToList(),
            Labors = Labors.Select(CloneItem).ToList(),
            Imponibile = Imponibile,
            MaterialDiscount = MaterialDiscount,
            LaborDiscount = LaborDiscount,
            Total = TotaleGenerale,
            Status = QuoteStatus.Bozza,
            BaseVersionUtc = _isEditingExistingQuote
                ? _loadedQuoteBaseVersionUtc
                : default,
            BaseRevision = _isEditingExistingQuote ? _loadedQuoteBaseRevision : 0,
            IsEditingExistingQuoteDraft = _isEditingExistingQuote,
            CreatedByDevice = deviceName,
            LastModifiedByDevice = deviceName,
            IsJointVenture = IsJointVenture,
            PartnerCompanyName = PartnerCompanyName,
            OurCosts = OurCosts.Select(CloneCost).ToList(),
            PartnerCosts = PartnerCosts.Select(CloneCost).ToList(),
            AdditionalCosts = AdditionalCosts.Select(CloneCost).ToList(),
            Attachments = AttachedImages.Select(a => new StoredFile
            {
                FileName = a.FileName,
                ContentType = a.ContentType,
                Content = a.Content,
                ImportedAt = DateTime.UtcNow
            }).ToList(),
            HasCompleteAttachmentSnapshot = true,
            Events = []
        };
    }

    private async Task SaveSharedDraftAsync(
        QuoteHistoryEntry draft,
        CancellationToken cancellationToken)
    {
        QuoteHistoryEntry? existing = await _dataService.GetQuoteByNumberAsync(draft.QuoteNumber);
        if (existing != null)
        {
            draft.Date = existing.Date;
            draft.PdfPath = existing.PdfPath;
            draft.Notes = existing.Notes;
            draft.Status = existing.Status;
            draft.CreatedByDevice = existing.CreatedByDevice;
            draft.SentAtUtc = existing.SentAtUtc;
            draft.SentMethod = existing.SentMethod;
            draft.SentRecipient = existing.SentRecipient;
            draft.SentByDevice = existing.SentByDevice;
            draft.LastReminderAtUtc = existing.LastReminderAtUtc;
            draft.ReminderCount = existing.ReminderCount;
            draft.LastReminderByDevice = existing.LastReminderByDevice;
            draft.Events = existing.Events.ToList();

            // Mantiene la versione dalla quale l'utente ha iniziato a lavorare.
            // Non usiamo il timestamp appena letto, altrimenti perderemmo il
            // controllo sulle modifiche concorrenti degli altri PC.
            if (draft.BaseVersionUtc == default)
                draft.BaseVersionUtc = _loadedQuoteBaseVersionUtc;
            if (draft.BaseRevision == 0)
                draft.BaseRevision = _loadedQuoteBaseRevision;
        }
        else
        {
            draft.Status = QuoteStatus.Bozza;
        }

        draft.Events.Add(new QuoteEventEntry
        {
            CreatedAtUtc = DateTime.UtcNow,
            DeviceName = DeviceNameService.GetCurrentDeviceName(),
            EventType = "bozza",
            Description = "Bozza condivisa aggiornata"
        });

        await _dataService.SaveQuoteAsync(draft, cancellationToken);

        _isEditingExistingQuote = true;
        _hasPersistedCurrentQuote = true;
        _loadedQuoteDate = draft.Date;
        _loadedQuoteBaseVersionUtc = draft.BaseVersionUtc;
        _loadedQuoteBaseRevision = draft.BaseRevision;
        draft.IsEditingExistingQuoteDraft = true;
    }

    private bool HasDraftContent()
    {
        return SelectedCustomer != null ||
               SelectedSecondCustomer != null ||
               Materials.Count > 0 ||
               Labors.Count > 0 ||
               AttachedImages.Count > 0 ||
               OurCosts.Count > 0 ||
               PartnerCosts.Count > 0 ||
               AdditionalCosts.Count > 0 ||
               !string.IsNullOrWhiteSpace(InputName) ||
               !string.IsNullOrWhiteSpace(InputDescription) ||
               InputValue != 0 ||
               InputQuantity != 1 ||
               MaterialDiscount != 0 ||
               LaborDiscount != 0 ||
               IsJointVenture;
    }

    private static Item CloneItem(Item item)
    {
        return new Item
        {
            Name = item.Name,
            Description = item.Description,
            UnitPrice = item.UnitPrice,
            Quantity = item.Quantity,
            Discount = item.Discount,
            IsSignificant = item.IsSignificant,
            SortOrder = item.SortOrder
        };
    }

    private static CostAllocationItem CloneCost(CostAllocationItem item)
    {
        return new CostAllocationItem
        {
            Description = item.Description,
            Amount = item.Amount,
            Notes = item.Notes
        };
    }
}
