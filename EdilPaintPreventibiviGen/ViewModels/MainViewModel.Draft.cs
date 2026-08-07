using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;

namespace EdilPaintPreventibiviGen.ViewModels;

public partial class MainViewModel
{
    private static readonly TimeSpan SharedDraftSaveInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SharedDraftCloudTimeout = TimeSpan.FromSeconds(10);
    private readonly LocalDraftService _draftService = new(LocalApplicationDataService.GetDataDirectoryPath());
    private DateTime _lastSharedDraftSaveAttemptUtc = DateTime.MinValue;
    private bool _isDraftQuoteNumberAllocated;
    private bool _sharedDraftCreatedByAutosave;

    public async Task<QuoteHistoryEntry?> LoadDraftAsync(CancellationToken cancellationToken = default)
    {
        return await _draftService.LoadAsync(cancellationToken);
    }

    public async Task SaveDraftAsync(
        CancellationToken cancellationToken = default,
        bool forceDatabaseSave = false)
    {
        bool lockTaken = false;
        bool databaseGateTaken = false;
        CancellationTokenSource? cloudSaveCts = null;

        void ReleaseDatabaseGate()
        {
            if (!databaseGateTaken)
                return;

            DatabaseOperationCoordinator.Gate.Release();
            databaseGateTaken = false;
        }

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
            await _draftService.SaveIfChangedAsync(draft, cancellationToken);

            if (IsBillingCustomerEnabled && SelectedBillingCustomer == null)
            {
                DraftSyncStatus = "Bozza locale: riseleziona il cliente di fatturazione";
                HasDraftSyncError = false;
                return;
            }
            if (IsSecondCustomerEnabled && SelectedSecondCustomer == null)
            {
                DraftSyncStatus = "Bozza locale: riseleziona il riferimento";
                HasDraftSyncError = false;
                return;
            }

            // Il sync lavora sugli stessi record e sulle stesse cache. Durante una
            // sincronizzazione manteniamo al sicuro la bozza locale e rimandiamo
            // il salvataggio cloud al tick successivo, evitando contesa e blocchi UI.
            if (App.SyncService is { IsSyncRunning: true })
            {
                DraftSyncStatus = "Bozza salvata su questo PC: sincronizzazione in corso";
                HasDraftSyncError = false;
                return;
            }

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

            DateTime nowUtc = DateTime.UtcNow;
            CancellationToken databaseCancellationToken = cancellationToken;
            if (!forceDatabaseSave)
            {
                cloudSaveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cloudSaveCts.CancelAfter(SharedDraftCloudTimeout);
                databaseCancellationToken = cloudSaveCts.Token;
            }

            string contentHash;
            string attachmentHash;
            if (_isEditingExistingQuote)
            {
                (contentHash, attachmentHash) = await Task.Run(
                    () =>
                    {
                        string currentAttachmentHash = ComputeAttachmentContentHash(draft.Attachments);
                        return (
                            ComputeSharedDraftContentHash(draft, currentAttachmentHash),
                            currentAttachmentHash);
                    },
                    cancellationToken);
                if (string.Equals(contentHash, _lastSharedDraftContentHash, StringComparison.Ordinal))
                    return;

                if (!forceDatabaseSave &&
                    nowUtc - _lastSharedDraftSaveAttemptUtc < SharedDraftSaveInterval)
                {
                    DraftSyncStatus = "Bozza salvata su questo PC: condivisione in attesa";
                    HasDraftSyncError = false;
                    return;
                }

                await DatabaseOperationCoordinator.EnsureInteractiveDatabaseReadyAsync(
                    _dataService,
                    "Condivisione bozza preventivo",
                    databaseCancellationToken);
            }
            else
            {
                // Per una nuova bozza il throttle precede l'allocazione atomica:
                // un tick rinviato non deve consumare un nuovo numero preventivo.
                if (!forceDatabaseSave &&
                    nowUtc - _lastSharedDraftSaveAttemptUtc < SharedDraftSaveInterval)
                {
                    DraftSyncStatus = "Bozza salvata su questo PC: condivisione in attesa";
                    HasDraftSyncError = false;
                    return;
                }


                await DatabaseOperationCoordinator.EnsureInteractiveDatabaseReadyAsync(
                    _dataService,
                    "Condivisione nuova bozza",
                    databaseCancellationToken);

                if (!_isDraftQuoteNumberAllocated)
                {
                    await DatabaseOperationCoordinator.Gate.WaitAsync(databaseCancellationToken);
                    databaseGateTaken = true;
                    int nextNumber = await RunDraftDatabaseOperationAsync(
                        () => _dataService.GetNextQuoteNumberAsync(databaseCancellationToken),
                        databaseCancellationToken);
                    ReleaseDatabaseGate();

                    QuoteNumber = nextNumber.ToString();
                    draft.QuoteNumber = QuoteNumber;
                    _isDraftQuoteNumberAllocated = true;
                    draft.IsDraftQuoteNumberAllocated = true;
                    await _draftService.SaveIfChangedAsync(draft, CancellationToken.None);
                }

                (contentHash, attachmentHash) = await Task.Run(
                    () =>
                    {
                        string currentAttachmentHash = ComputeAttachmentContentHash(draft.Attachments);
                        return (
                            ComputeSharedDraftContentHash(draft, currentAttachmentHash),
                            currentAttachmentHash);
                    },
                    cancellationToken);
            }
            _lastSharedDraftSaveAttemptUtc = nowUtc;

            // Foto e PDF sono spesso la parte piu' pesante della bozza. Il DB li
            // riceve solo alla prima creazione o quando il loro contenuto cambia;
            // negli altri autosave la relazione esistente viene preservata.
            draft.HasCompleteAttachmentSnapshot =
                !_isEditingExistingQuote ||
                !string.Equals(
                    attachmentHash,
                    _lastSharedDraftAttachmentHash,
                    StringComparison.Ordinal);

            DateTime loadedBaseVersionUtc = _loadedQuoteBaseVersionUtc;
            long loadedBaseRevision = _loadedQuoteBaseRevision;
            await DatabaseOperationCoordinator.Gate.WaitAsync(databaseCancellationToken);
            databaseGateTaken = true;
            var savedState = await RunDraftDatabaseOperationAsync(
                () => SaveSharedDraftCoreAsync(
                    draft,
                    loadedBaseVersionUtc,
                    loadedBaseRevision,
                    databaseCancellationToken),
                databaseCancellationToken);
            ReleaseDatabaseGate();

            // Questi campi alimentano il binding WPF: vengono applicati solo
            // quando il lavoro DB e' terminato e l'await e' tornato al chiamante.
            _isEditingExistingQuote = true;
            _hasPersistedCurrentQuote = true;
            _loadedQuoteDate = savedState.Date;
            _loadedQuoteBaseVersionUtc = savedState.BaseVersionUtc;
            _loadedQuoteBaseRevision = savedState.BaseRevision;
            _isDraftQuoteNumberAllocated = true;
            _sharedDraftCreatedByAutosave = savedState.WasCreatedByAutosave;
            draft.IsEditingExistingQuoteDraft = true;
            draft.IsDraftQuoteNumberAllocated = true;
            draft.WasCreatedByDraftAutosave = _sharedDraftCreatedByAutosave;

            if (draft.BaseRevision > 0)
            {
                draft.HasCompleteAttachmentSnapshot = true;
                draft.SharedDraftContentHash = contentHash;
                await _draftService.SaveIfChangedAsync(
                    draft,
                    CancellationToken.None,
                    forceWrite: true);
                _lastSharedDraftContentHash = contentHash;
                _lastSharedDraftAttachmentHash = attachmentHash;
            }
            DraftSyncStatus = $"Bozza condivisa alle {DateTime.Now:HH:mm:ss}";
            HasDraftSyncError = false;
        }
        catch (OperationCanceledException) when (
            cloudSaveCts?.IsCancellationRequested == true &&
            !cancellationToken.IsCancellationRequested)
        {
            DraftSyncStatus = "Bozza salvata su questo PC: database lento, condivisione rinviata";
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
            cloudSaveCts?.Dispose();
            ReleaseDatabaseGate();
            if (lockTaken)
                _draftSaveLock.Release();
        }
    }

    public Task DiscardDraftAsync(CancellationToken cancellationToken = default) =>
        _draftService.DeleteAsync(cancellationToken);

    public async Task DiscardCurrentWorkAsync(CancellationToken cancellationToken = default)
    {
        bool lockTaken = false;
        bool databaseGateTaken = false;
        CancellationTokenSource? cleanupCts = null;
        try
        {
            await _draftSaveLock.WaitAsync(cancellationToken);
            lockTaken = true;

            var draft = await _draftService.LoadAsync(cancellationToken);
            await _draftService.DeleteAsync(cancellationToken);

            if (draft is not { IsEditingExistingQuoteDraft: true, WasCreatedByDraftAutosave: true } ||
                string.IsNullOrWhiteSpace(draft.QuoteNumber) ||
                !_dataService.CanSynchronize)
            {
                return;
            }

            cleanupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cleanupCts.CancelAfter(TimeSpan.FromSeconds(8));
            CancellationToken cleanupToken = cleanupCts.Token;
            await DatabaseOperationCoordinator.EnsureInteractiveDatabaseReadyAsync(
                _dataService,
                $"Pulizia bozza {draft.QuoteNumber}",
                cleanupToken);
            await DatabaseOperationCoordinator.Gate.WaitAsync(cleanupToken);
            databaseGateTaken = true;

            var stored = await RunDraftDatabaseOperationAsync(
                () => _dataService.GetQuoteByNumberAsync(
                    draft.QuoteNumber,
                    cleanupToken,
                    includeAttachments: false),
                cleanupToken);
            if (stored?.Status == QuoteStatus.Bozza)
            {
                await RunDraftDatabaseOperationAsync(
                    () => _dataService.DeleteQuoteAsync(draft.QuoteNumber, cleanupToken),
                    cleanupToken);
            }
        }
        catch (OperationCanceledException) when (
            cleanupCts?.IsCancellationRequested == true &&
            !cancellationToken.IsCancellationRequested)
        {
            Debug.WriteLine("[Draft] Pulizia bozza cloud rinviata: database lento.");
        }
        finally
        {
            cleanupCts?.Dispose();
            if (databaseGateTaken)
                DatabaseOperationCoordinator.Gate.Release();
            if (lockTaken)
            {
                try
                {
                    ResetQuote();
                }
                finally
                {
                    _draftSaveLock.Release();
                }
            }
        }
    }

    public void ApplyDraft(QuoteHistoryEntry draft)
    {
        ResetQuote();

        QuoteNumber = string.IsNullOrWhiteSpace(draft.QuoteNumber)
            ? _companyData.Counter.ToString()
            : draft.QuoteNumber;

        SelectedCustomer = FindCustomerByIdentity(draft.CustomerSyncId, draft.CustomerName);
        if (SelectedCustomer == null)
            _unresolvedCustomerName = draft.CustomerName;

        if (!string.IsNullOrWhiteSpace(draft.ReferenceName))
        {
            IsSecondCustomerEnabled = true;
            SelectedSecondCustomer = FindCustomerByIdentity(
                draft.ReferenceCustomerSyncId,
                draft.ReferenceName);
            if (SelectedSecondCustomer == null)
                _unresolvedReferenceCustomerName = draft.ReferenceName;
        }

        if (!string.IsNullOrWhiteSpace(draft.SiteName))
        {
            IsSiteCustomerEnabled = true;
            SiteAddress = draft.SiteName;
        }

        if (!string.IsNullOrWhiteSpace(draft.BillingCustomerName))
        {
            IsBillingCustomerEnabled = true;
            SelectedBillingCustomer = FindCustomerByIdentity(
                draft.BillingCustomerSyncId,
                draft.BillingCustomerName);
            if (SelectedBillingCustomer == null)
                _unresolvedBillingCustomerName = draft.BillingCustomerName;
        }

        PaymentTerms = draft.PaymentTerms;
        CustomerNotes = draft.CustomerNotes;
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
        _isDraftQuoteNumberAllocated = draft.IsDraftQuoteNumberAllocated || resumesExistingQuote;
        _sharedDraftCreatedByAutosave = draft.WasCreatedByDraftAutosave;
        _lastSharedDraftSaveAttemptUtc = DateTime.MinValue;

        UpdateItemSortOrders();
        CalculateTotals();
        string recoveredContentHash = ComputeSharedDraftContentHash(CreateDraftEntry());
        bool matchesLastSharedVersion = string.Equals(
            recoveredContentHash,
            draft.SharedDraftContentHash,
            StringComparison.Ordinal);
        _lastSharedDraftContentHash = matchesLastSharedVersion
            ? recoveredContentHash
            : string.Empty;
        _lastSharedDraftAttachmentHash = matchesLastSharedVersion
            ? ComputeAttachmentContentHash(draft.Attachments)
            : string.Empty;
    }

    private QuoteHistoryEntry CreateDraftEntry()
    {
        string deviceName = DeviceNameService.GetCurrentDeviceName();
        return new QuoteHistoryEntry
        {
            QuoteNumber = QuoteNumber,
            Date = _loadedQuoteDate ?? DateTime.Now,
            CustomerName = SelectedCustomer?.BusinessName ?? _unresolvedCustomerName,
            CustomerSyncId = SelectedCustomer?.SyncId ?? Guid.Empty,
            ReferenceName = IsSecondCustomerEnabled
                ? SelectedSecondCustomer?.BusinessName ?? _unresolvedReferenceCustomerName
                : string.Empty,
            ReferenceCustomerSyncId = IsSecondCustomerEnabled
                ? SelectedSecondCustomer?.SyncId ?? Guid.Empty
                : Guid.Empty,
            SiteName = IsSiteCustomerEnabled ? SiteAddress.Trim() : string.Empty,
            BillingCustomerName = IsBillingCustomerEnabled
                ? SelectedBillingCustomer?.BusinessName ?? _unresolvedBillingCustomerName
                : string.Empty,
            BillingCustomerSyncId = IsBillingCustomerEnabled
                ? SelectedBillingCustomer?.SyncId ?? Guid.Empty
                : Guid.Empty,
            PaymentTerms = PaymentTerms,
            CustomerNotes = CustomerNotes,
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
            IsDraftQuoteNumberAllocated = _isDraftQuoteNumberAllocated,
            WasCreatedByDraftAutosave = _sharedDraftCreatedByAutosave,
            SharedDraftContentHash = _lastSharedDraftContentHash,
            Events = []
        };
    }

    private async Task<SharedDraftSaveState> SaveSharedDraftCoreAsync(
        QuoteHistoryEntry draft,
        DateTime loadedBaseVersionUtc,
        long loadedBaseRevision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        QuoteHistoryEntry? existing = await _dataService.GetQuoteByNumberAsync(
                draft.QuoteNumber,
                cancellationToken,
                includeAttachments: false)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        bool createdByAutosave = draft.WasCreatedByDraftAutosave || existing == null;
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
            draft.SupplierName = existing.SupplierName;
            draft.MaterialOrderDate = existing.MaterialOrderDate;
            draft.ExpectedDeliveryDate = existing.ExpectedDeliveryDate;
            draft.MaterialStatus = existing.MaterialStatus;

            // Mantiene la versione dalla quale l'utente ha iniziato a lavorare.
            // Non usiamo il timestamp appena letto, altrimenti perderemmo il
            // controllo sulle modifiche concorrenti degli altri PC.
            if (draft.BaseVersionUtc == default)
                draft.BaseVersionUtc = loadedBaseVersionUtc;
            if (draft.BaseRevision == 0)
                draft.BaseRevision = loadedBaseRevision;
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

        // Il gate copre soltanto il commit autorevole SQL. history.json e' una
        // cache e verra' riallineato in un unico batch dalla sync periodica.
        await SaveQuoteToAuthoritativeDatabaseAsync(draft, cancellationToken)
            .ConfigureAwait(false);
        return new SharedDraftSaveState(
            draft.Date,
            draft.BaseVersionUtc,
            draft.BaseRevision,
            createdByAutosave);
    }

    private static async Task<T> RunDraftDatabaseOperationAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await operation().ConfigureAwait(false);
        }, CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task RunDraftDatabaseOperationAsync(
        Func<Task> operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await operation().ConfigureAwait(false);
        }, CancellationToken.None).ConfigureAwait(false);
    }

    private string ComputeSharedDraftContentHash(QuoteHistoryEntry draft)
    {
        string attachmentHash = ComputeAttachmentContentHash(draft.Attachments);
        return ComputeSharedDraftContentHash(draft, attachmentHash);
    }

    private static string ComputeSharedDraftContentHash(
        QuoteHistoryEntry draft,
        string attachmentHash)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        static void AppendText(IncrementalHash target, string? value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            target.AppendData(BitConverter.GetBytes(bytes.Length));
            target.AppendData(bytes);
        }

        AppendText(hash, QuoteSyncHashService.Compute(draft));
        AppendText(hash, draft.CustomerSyncId.ToString("N"));
        AppendText(hash, draft.ReferenceCustomerSyncId.ToString("N"));
        AppendText(hash, draft.BillingCustomerSyncId.ToString("N"));
        AppendText(hash, draft.HasCompleteAttachmentSnapshot.ToString());
        AppendText(hash, attachmentHash);

        return Convert.ToBase64String(hash.GetHashAndReset());
    }

    private string ComputeAttachmentContentHash(IReadOnlyList<StoredFile> attachments)
    {
        lock (_attachmentHashCacheLock)
        {
            bool cacheMatches = attachments.Count == _attachmentHashCache.Count;
            for (int index = 0; cacheMatches && index < attachments.Count; index++)
            {
                var attachment = attachments[index];
                var cached = _attachmentHashCache[index];
                cacheMatches =
                    string.Equals(attachment.FileName, cached.FileName, StringComparison.Ordinal) &&
                    string.Equals(attachment.ContentType, cached.ContentType, StringComparison.Ordinal) &&
                    ReferenceEquals(attachment.Content, cached.Content);
            }

            if (cacheMatches && !string.IsNullOrEmpty(_cachedAttachmentHash))
                return _cachedAttachmentHash;

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            static void AppendText(IncrementalHash target, string? value)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
                target.AppendData(BitConverter.GetBytes(bytes.Length));
                target.AppendData(bytes);
            }

            hash.AppendData(BitConverter.GetBytes(attachments.Count));
            foreach (var attachment in attachments)
            {
                AppendText(hash, attachment.FileName);
                AppendText(hash, attachment.ContentType);
                byte[] content = attachment.Content ?? [];
                hash.AppendData(BitConverter.GetBytes(content.Length));
                hash.AppendData(content);
            }

            _attachmentHashCache = attachments
                .Select(attachment => new AttachmentHashCacheEntry(
                    attachment.FileName,
                    attachment.ContentType,
                    attachment.Content))
                .ToList();
            _cachedAttachmentHash = Convert.ToBase64String(hash.GetHashAndReset());
            return _cachedAttachmentHash;
        }
    }

    private sealed record AttachmentHashCacheEntry(
        string FileName,
        string ContentType,
        byte[]? Content);

    private sealed record SharedDraftSaveState(
        DateTime Date,
        DateTime BaseVersionUtc,
        long BaseRevision,
        bool WasCreatedByAutosave);

    private bool HasDraftContent()
    {
        return SelectedCustomer != null ||
               !string.IsNullOrWhiteSpace(_unresolvedCustomerName) ||
               SelectedSecondCustomer != null ||
               !string.IsNullOrWhiteSpace(_unresolvedReferenceCustomerName) ||
               !string.IsNullOrWhiteSpace(SiteAddress) ||
               SelectedBillingCustomer != null ||
               !string.IsNullOrWhiteSpace(_unresolvedBillingCustomerName) ||
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
