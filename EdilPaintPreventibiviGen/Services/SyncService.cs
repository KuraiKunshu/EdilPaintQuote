using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EdilPaintPreventibiviGen.Data;
using EdilPaintPreventibiviGen.Models;
using Microsoft.EntityFrameworkCore;

namespace EdilPaintPreventibiviGen.Services;

public class SyncService
{
    public event EventHandler<SyncCompletedEventArgs>? SyncCompleted;
    private readonly IDataService _dataService;
    private readonly LocalJsonStoreService _localStore;
    private readonly SqlDataService _sqlService;
    private readonly LocalQuotePatchOutboxService _quotePatchOutbox;
    private readonly LocalDeletionOutboxService _deletionOutbox;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly object _statusLock = new();
    private int _activeSyncRequests;
    private DateTime _lastSyncTime = DateTime.MinValue;
    private DateTime? _lastSyncCompletedUtc;
    private string _lastSyncSummary = "Sincronizzazione non ancora eseguita.";

    public bool IsSyncRunning => Volatile.Read(ref _activeSyncRequests) > 0;

    public DateTime? LastSyncCompletedUtc
    {
        get
        {
            lock (_statusLock)
                return _lastSyncCompletedUtc;
        }
    }

    public string LastSyncSummary
    {
        get
        {
            lock (_statusLock)
                return _lastSyncSummary;
        }
    }

    public SyncService(
        IDataService dataService,
        SqlDataService sqlService,
        LocalJsonStoreService localStore,
        LocalQuotePatchOutboxService quotePatchOutbox,
        LocalDeletionOutboxService deletionOutbox)
    {
        _dataService = dataService;
        _sqlService = sqlService;
        _localStore = localStore;
        _quotePatchOutbox = quotePatchOutbox;
        _deletionOutbox = deletionOutbox;
    }
    
    public Task<SyncResult> SyncAllAsync(
        bool force = false,
        int take = 0,
        CancellationToken cancellationToken = default,
        bool waitForCurrentRun = false,
        bool includeCustomers = true)
    {
        // La sincronizzazione comprende anche confronto hash, serializzazione JSON
        // e aggiornamento delle cache locali. Deve sempre partire dal thread pool:
        // se invocata da una finestra WPF, le continuazioni non devono occupare
        // il dispatcher e bloccare l'utente durante il lavoro.
        Interlocked.Increment(ref _activeSyncRequests);
        return RunSyncOnBackgroundAsync(
            force,
            take,
            cancellationToken,
            waitForCurrentRun,
            includeCustomers);
    }

    private async Task<SyncResult> RunSyncOnBackgroundAsync(
        bool force,
        int take,
        CancellationToken cancellationToken,
        bool waitForCurrentRun,
        bool includeCustomers)
    {
        try
        {
            // Non passiamo il token allo scheduler: il delegate deve sempre
            // partire per poter azzerare in modo affidabile lo stato di sync.
            return await Task.Run(
                    () => SyncAllCoreAsync(
                        force,
                        take,
                        cancellationToken,
                        waitForCurrentRun,
                        includeCustomers),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _activeSyncRequests);
        }
    }

    private async Task<SyncResult> SyncAllCoreAsync(
        bool force,
        int take,
        CancellationToken cancellationToken,
        bool waitForCurrentRun,
        bool includeCustomers)
    {
        bool lockTaken = false;

        try
        {
            if (waitForCurrentRun)
            {
                await _syncLock.WaitAsync(cancellationToken);
                lockTaken = true;
            }
            else if (!await _syncLock.WaitAsync(0, cancellationToken))
            {
                Debug.WriteLine("[Sync] Already syncing, skipping...");
                UpdateSyncStatus(null, "Sincronizzazione gia' in corso.");
                return new SyncResult { AlreadyRunning = true };
            }
            else
            {
                lockTaken = true;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (!_dataService.CanSynchronize)
            {
                Debug.WriteLine("[Sync] Database unavailable, skipping automatic synchronization.");
                UpdateSyncStatus(DateTime.UtcNow, "Database non disponibile: sincronizzazione saltata.");
                return new SyncResult { Skipped = true };
            }

            using (var readinessCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                readinessCts.CancelAfter(TimeSpan.FromSeconds(8));
                try
                {
                    if (!await _sqlService.CanConnectAsync(readinessCts.Token))
                    {
                        UpdateSyncStatus(DateTime.UtcNow, "Database non raggiungibile: sincronizzazione rinviata.");
                        return new SyncResult { Skipped = true };
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    UpdateSyncStatus(DateTime.UtcNow, "Database lento: sincronizzazione rinviata.");
                    return new SyncResult { Skipped = true };
                }
            }

            if (!force && (DateTime.UtcNow - _lastSyncTime).TotalSeconds < 30)
            {
                Debug.WriteLine("[Sync] Too soon since last sync, skipping...");
                UpdateSyncStatus(_lastSyncTime, "Sincronizzazione saltata: eseguita da meno di 30 secondi.");
                return new SyncResult { Skipped = true };
            }

            var result = new SyncResult { StartTime = DateTime.UtcNow };
            Debug.WriteLine($"╔══════════════════════════════════════════════════╗");
            Debug.WriteLine($"║  SYNC SERVICE - STARTING SYNC (take={take})");
            Debug.WriteLine($"╚══════════════════════════════════════════════════╝");

            await FlushPendingDeletesAsync(cancellationToken);
            await PropagateDeletedQuotesAsync(cancellationToken);
            await FlushPendingQuotePatchesAsync(cancellationToken);

            // I preventivi referenziano i clienti tramite SyncId: prima
            // sincronizziamo le anagrafiche complete, poi le quote.
            // Lo storico locale e' il file piu' grande: lo deserializziamo una
            // sola volta e riutilizziamo lo snapshot sia per proteggere gli ID
            // cliente delle quote pending, sia per la sincronizzazione quote.
            var localQuotesSnapshot = await _localStore.LoadHistoryAsync(cancellationToken);

            if (includeCustomers)
            {
                var customersResult = await SyncCustomersAsync(
                    localQuotesSnapshot,
                    cancellationToken);
                result.CustomersSynced = customersResult.synced;
                result.CustomersConflicts = customersResult.conflicts;
            }
            else
            {
                Debug.WriteLine("[Sync] Sync clienti rinviata: esecuzione periodica/chiusura preventivi-only.");
            }

            var quotesResult = await SyncQuotesAsync(
                take,
                localQuotesSnapshot,
                cancellationToken);
            result.QuotesSynced = quotesResult.synced;
            result.QuotesConflicts = quotesResult.conflicts;

            _lastSyncTime = DateTime.UtcNow;
            result.EndTime = DateTime.UtcNow;
            string customersSummary = includeCustomers
                ? $"clienti {result.CustomersSynced}"
                : "clienti non necessari in questo ciclo";
            UpdateSyncStatus(result.EndTime, $"Completata: preventivi {result.QuotesSynced}, {customersSummary}, conflitti {result.QuotesConflicts + result.CustomersConflicts}.");
            SyncCompleted?.Invoke(this, new SyncCompletedEventArgs(result));

            Debug.WriteLine($"║ SYNC COMPLETED in {result.Duration.TotalSeconds:F2}s");
            Debug.WriteLine($"║ Quotes={result.QuotesSynced}, Customers={result.CustomersSynced}");

            return result;
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[Sync] Cancelled.");
            UpdateSyncStatus(DateTime.UtcNow, "Sincronizzazione annullata.");
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Sync] ❌ ERROR: {ex.Message}");
            UpdateSyncStatus(DateTime.UtcNow, $"Errore sincronizzazione: {ex.Message}");
            return new SyncResult { Error = ex.Message };
        }
        finally
        {
            if (lockTaken)
                _syncLock.Release();
        }
    }

    private void UpdateSyncStatus(DateTime? completedUtc, string summary)
    {
        lock (_statusLock)
        {
            if (completedUtc.HasValue)
                _lastSyncCompletedUtc = completedUtc;

            _lastSyncSummary = summary;
        }
    }

    private async Task<(int synced, int conflicts)> SyncQuotesAsync(
        int take,
        IReadOnlyList<QuoteHistoryEntry> localQuotesSnapshot,
        CancellationToken cancellationToken)
    {
        int synced = 0;
        int conflicts = 0;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Debug.WriteLine("\n[Sync] ═══ QUOTES SYNC START ═══");

            // Carica solo i METADATA dal JSON locale
            var jsonQuotes = localQuotesSnapshot;
            Debug.WriteLine($"[Sync] 📂 JSON quotes loaded: {jsonQuotes.Count}");

            var jsonDict = jsonQuotes
                .GroupBy(q => q.QuoteNumber, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(q => q.LastModifiedUtc).First(),
                    StringComparer.OrdinalIgnoreCase);

            // Carica i metadata dal DB — se take > 0, limita agli ultimi N
            Debug.WriteLine("[Sync] 🗄️ Loading DB quote metadata...");
            var dbMetadata = await _sqlService.GetQuoteMetadataAsync(cancellationToken);
            Debug.WriteLine($"[Sync] 🗄️ DB metadata loaded: {dbMetadata.Count}");

            // Se take > 0, considera solo le quote più recenti dal DB
            IEnumerable<string> dbKeys = dbMetadata.Keys;
            if (take > 0)
            {
                // Ordina per LastModifiedUtc decrescente e prendi le prime N
                dbKeys = dbMetadata
                    .OrderByDescending(kv => kv.Value.LastModifiedUtc)
                    .Take(take)
                    .Select(kv => kv.Key);
                
                Debug.WriteLine($"[Sync] 🔢 Limiting sync to last {take} quotes");
            }

            var dbKeySet = new HashSet<string>(dbKeys, StringComparer.OrdinalIgnoreCase);

            var onlyInDb = dbKeySet.Except(jsonDict.Keys).ToList();
            var onlyInJson = jsonDict.Keys.Except(dbMetadata.Keys).ToList(); // Tutti quelli nel JSON che non sono nel DB
            var inBoth = dbKeySet.Intersect(jsonDict.Keys).ToList();

            Debug.WriteLine($"[Sync]    - Only in DB (subset): {onlyInDb.Count}");
            Debug.WriteLine($"[Sync]    - Only in JSON: {onlyInJson.Count}");
            Debug.WriteLine($"[Sync]    - In both: {inBoth.Count}");

            var quotesPendingJsonUpdate = new List<QuoteHistoryEntry>();
            var quotesPendingDbUpdate = new List<QuoteHistoryEntry>();

            if (onlyInDb.Count > 0)
            {
                var toLoad = await _sqlService.GetQuotesByNumbersAsync(onlyInDb, cancellationToken);
                quotesPendingJsonUpdate.AddRange(toLoad);
                synced += toLoad.Count;
            }

            foreach (var key in onlyInJson)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (jsonDict.TryGetValue(key, out var jsonQuote))
                {
                    HydratePendingAttachments(jsonQuote);
                    quotesPendingDbUpdate.Add(jsonQuote);
                    synced++;
                }
            }

            var normalizedKeys = await NormalizeMatchingQuoteHashesAsync(
                inBoth,
                jsonDict,
                dbMetadata,
                quotesPendingJsonUpdate,
                cancellationToken);

            var keysNeedingDbLoad = new List<string>();
            foreach (var key in inBoth)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (normalizedKeys.Contains(key))
                    continue;

                var dbMeta = dbMetadata[key];
                var jsonQuote = jsonDict[key];

                if (!string.IsNullOrEmpty(dbMeta.SyncHash) &&
                    !string.IsNullOrEmpty(jsonQuote.SyncHash) &&
                    dbMeta.SyncHash == jsonQuote.SyncHash)
                {
                    if (jsonQuote.HasPendingDatabaseWrite ||
                        jsonQuote.BaseRevision != dbMeta.Revision)
                        keysNeedingDbLoad.Add(key);
                    continue;
                }

                if (jsonQuote.HasPendingDatabaseWrite &&
                    jsonQuote.BaseRevision > 0 &&
                    jsonQuote.BaseRevision == dbMeta.Revision)
                {
                    HydratePendingAttachments(jsonQuote);
                    quotesPendingDbUpdate.Add(jsonQuote);
                    synced++;
                    continue;
                }

                if (!jsonQuote.HasPendingDatabaseWrite)
                {
                    // history.json e' una cache: se non contiene una modifica
                    // locale esplicita, una differenza non e' un conflitto da
                    // archiviare. Il DB e' autorevole e riallinea il batch.
                    keysNeedingDbLoad.Add(key);
                    continue;
                }

                conflicts++;
                await _localStore.ArchiveQuoteConflictAsync(
                    jsonQuote,
                    "Versione locale archiviata: per un preventivo gia' presente il database e' autorevole.",
                    cancellationToken);
                keysNeedingDbLoad.Add(key);
            }

            if (keysNeedingDbLoad.Count > 0)
            {
                var dbToJson = await _sqlService.GetQuotesByNumbersAsync(keysNeedingDbLoad, cancellationToken);
                quotesPendingJsonUpdate.AddRange(dbToJson);
                synced += dbToJson.Count;
            }

            Debug.WriteLine($"[Sync]    - Pending JSON updates: {quotesPendingJsonUpdate.Count}");
            Debug.WriteLine($"[Sync]    - Pending DB updates: {quotesPendingDbUpdate.Count}");

            if (quotesPendingDbUpdate.Count > 0)
            {
                foreach (var q in quotesPendingDbUpdate)
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        await ExecuteDatabaseStepAsync(async () =>
                        {
                            var currentMetadata = await _sqlService.GetQuoteMetadataByNumberAsync(
                                q.QuoteNumber,
                                cancellationToken);
                            bool revisionChanged = currentMetadata != null &&
                                (q.BaseRevision == 0 || q.BaseRevision != currentMetadata.Revision);
                            bool quoteAppearedAfterSnapshot = currentMetadata != null &&
                                !dbMetadata.ContainsKey(q.QuoteNumber);
                            if (revisionChanged || quoteAppearedAfterSnapshot)
                                throw new QuoteConflictException(q.QuoteNumber);

                            await _sqlService.SaveQuoteWithExpectedRevisionAsync(
                                q,
                                cancellationToken,
                                expectedRevision: currentMetadata?.Revision ?? 0);
                        }, cancellationToken);
                        q.HasPendingDatabaseWrite = false;
                        quotesPendingJsonUpdate.Add(q);
                    }
                    catch (QuoteConflictException ex)
                    {
                        await _localStore.ArchiveQuoteConflictAsync(q, ex.Message, cancellationToken);
                        var databaseVersion = await _sqlService.GetQuoteByNumberAsync(
                            q.QuoteNumber,
                            cancellationToken,
                            includeAttachments: false);
                        if (databaseVersion != null)
                            quotesPendingJsonUpdate.Add(databaseVersion);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Sync] ❌ DB save error for {q.QuoteNumber}: {ex.Message}");
                    }
                }

                if (_dataService is FallbackDataService fallbackDataService)
                    fallbackDataService.InvalidateQuoteNumbersCaches();
            }

            // history.json contiene l'intero storico, allegati compresi. Una
            // scrittura per ogni preventivo pendente moltiplicava decine di MB
            // di I/O; tutte le versioni definitive vengono ora salvate insieme.
            if (quotesPendingJsonUpdate.Count > 0)
            {
                var jsonUpdateBatch = quotesPendingJsonUpdate
                    .GroupBy(q => q.QuoteNumber, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.Last())
                    .ToList();
                await _localStore.BulkUpdateQuotesAsync(jsonUpdateBatch, cancellationToken);
                if (_dataService is FallbackDataService fallbackDataService)
                {
                    fallbackDataService.MarkLocalQuoteCacheUpdated(
                        jsonUpdateBatch.Select(quote => quote.QuoteNumber));
                }
            }

            Debug.WriteLine("[Sync] ═══ QUOTES SYNC END ═══\n");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Sync] ❌ Error syncing quotes: {ex.Message}");
        }

        return (synced, conflicts);
    }

    private async Task<HashSet<string>> NormalizeMatchingQuoteHashesAsync(
        IEnumerable<string> inBoth,
        IReadOnlyDictionary<string, QuoteHistoryEntry> jsonQuotes,
        IReadOnlyDictionary<string, QuoteMetadata> dbMetadata,
        ICollection<QuoteHistoryEntry> quotesPendingJsonUpdate,
        CancellationToken cancellationToken)
    {
        var candidateKeys = inBoth
            .Where(key =>
                jsonQuotes.TryGetValue(key, out var jsonQuote) &&
                dbMetadata.TryGetValue(key, out var dbMeta) &&
                (!string.Equals(jsonQuote.SyncHash, dbMeta.SyncHash, StringComparison.Ordinal) ||
                 (!string.IsNullOrEmpty(dbMeta.SyncHash) &&
                  string.Equals(
                      dbMeta.SyncHash,
                      QuoteSyncHashService.ComputeLegacy(jsonQuote),
                      StringComparison.Ordinal))))
            .ToList();

        if (candidateKeys.Count == 0)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var dbSnapshots = await _sqlService.GetQuoteSyncSnapshotsAsync(candidateKeys, cancellationToken);
        var dbSnapshotDict = dbSnapshots.ToDictionary(
            quote => quote.QuoteNumber,
            quote => quote,
            StringComparer.OrdinalIgnoreCase);

        var normalizedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dbHashUpdates = new Dictionary<string, (string SyncHash, long ExpectedRevision)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var key in candidateKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!dbSnapshotDict.TryGetValue(key, out var dbSnapshot))
                continue;

            var jsonQuote = jsonQuotes[key];
            string jsonCanonicalHash = QuoteSyncHashService.Compute(jsonQuote);
            string dbCanonicalHash = QuoteSyncHashService.Compute(dbSnapshot);

            bool canonicalHashesMatch = string.Equals(
                jsonCanonicalHash,
                dbCanonicalHash,
                StringComparison.Ordinal);
            if (!canonicalHashesMatch)
            {
                // Gli hash storici non includevano gli ID cliente. Se tutto il
                // resto coincide, migriamo la cache alla relazione autorevole
                // del DB senza creare migliaia di falsi conflitti. Una modifica
                // locale pending con un ID esplicito diverso resta invece locale.
                bool legacyHashesMatch = string.Equals(
                    QuoteSyncHashService.ComputeLegacy(jsonQuote),
                    QuoteSyncHashService.ComputeLegacy(dbSnapshot),
                    StringComparison.Ordinal);
                bool hasExplicitPendingIdentityChange =
                    jsonQuote.HasPendingDatabaseWrite &&
                    ((jsonQuote.CustomerSyncId != Guid.Empty &&
                      jsonQuote.CustomerSyncId != dbSnapshot.CustomerSyncId) ||
                     (jsonQuote.ReferenceCustomerSyncId != Guid.Empty &&
                      jsonQuote.ReferenceCustomerSyncId != dbSnapshot.ReferenceCustomerSyncId) ||
                     (jsonQuote.BillingCustomerSyncId != Guid.Empty &&
                      jsonQuote.BillingCustomerSyncId != dbSnapshot.BillingCustomerSyncId));

                if (!legacyHashesMatch || hasExplicitPendingIdentityChange)
                    continue;
            }

            normalizedKeys.Add(key);

            if (!canonicalHashesMatch)
            {
                dbSnapshot.HasPendingDatabaseWrite = false;
                quotesPendingJsonUpdate.Add(dbSnapshot);
                if (!string.Equals(dbMetadata[key].SyncHash, dbCanonicalHash, StringComparison.Ordinal))
                    dbHashUpdates[key] = (dbCanonicalHash, dbMetadata[key].Revision);
                continue;
            }

            if (!jsonQuote.HasPendingDatabaseWrite &&
                jsonQuote.BaseRevision != dbMetadata[key].Revision)
            {
                dbSnapshot.HasPendingDatabaseWrite = false;
                quotesPendingJsonUpdate.Add(dbSnapshot);
                if (!string.Equals(dbMetadata[key].SyncHash, dbCanonicalHash, StringComparison.Ordinal))
                    dbHashUpdates[key] = (dbCanonicalHash, dbMetadata[key].Revision);
                continue;
            }

            if (jsonQuote.HasPendingDatabaseWrite)
            {
                dbSnapshot.HasPendingDatabaseWrite = false;
                quotesPendingJsonUpdate.Add(dbSnapshot);
                continue;
            }

            if (!string.Equals(jsonQuote.SyncHash, jsonCanonicalHash, StringComparison.Ordinal))
            {
                jsonQuote.SyncHash = jsonCanonicalHash;
                quotesPendingJsonUpdate.Add(jsonQuote);
            }

            if (!string.Equals(dbMetadata[key].SyncHash, dbCanonicalHash, StringComparison.Ordinal))
                dbHashUpdates[key] = (dbCanonicalHash, dbMetadata[key].Revision);
        }

        if (dbHashUpdates.Count > 0)
        {
            // Il riallineamento iniziale puo' coinvolgere migliaia di quote:
            // piccoli batch lasciano passare subito i salvataggi interattivi.
            foreach (var batch in dbHashUpdates.Chunk(100))
            {
                var updates = batch.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.OrdinalIgnoreCase);
                await ExecuteDatabaseStepAsync(
                    () => _sqlService.UpdateQuoteSyncHashesAsync(updates, cancellationToken),
                    cancellationToken);
            }
        }

        if (normalizedKeys.Count > 0)
            Debug.WriteLine($"[Sync] Riallineati {normalizedKeys.Count} hash o identita' cliente obsoleti.");

        return normalizedKeys;
    }

    private async Task FlushPendingDeletesAsync(CancellationToken cancellationToken)
    {
        var pending = await _deletionOutbox.LoadAsync(cancellationToken);

        foreach (var quote in pending.Quotes.ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await ExecuteDatabaseStepAsync(
                    () => _sqlService.DeleteQuoteAsync(quote.QuoteNumber, cancellationToken),
                    cancellationToken);
                await _deletionOutbox.RemoveQuoteAsync(quote.QuoteNumber, cancellationToken);
                Debug.WriteLine($"[Sync] Eliminazione preventivo sincronizzata: {quote.QuoteNumber}.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Sync] Eliminazione preventivo pendente {quote.QuoteNumber}: {ex.Message}");
            }
        }

        foreach (var customer in pending.Customers.ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await ExecuteDatabaseStepAsync(
                    () => _sqlService.DeleteCustomerAsync(customer.SyncId, customer.BusinessName),
                    cancellationToken);
                await _deletionOutbox.RemoveCustomerAsync(customer.SyncId, customer.BusinessName, cancellationToken);
                Debug.WriteLine($"[Sync] Eliminazione cliente sincronizzata: {customer.BusinessName}.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Sync] Eliminazione cliente pendente {customer.BusinessName}: {ex.Message}");
            }
        }
    }

    private async Task FlushPendingQuotePatchesAsync(CancellationToken cancellationToken)
    {
        var databaseVersions = new List<QuoteHistoryEntry>();

        foreach (var patch in await _quotePatchOutbox.LoadAllAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                bool appliedAny = false;
                if (patch.Notes != null)
                {
                    string appliedNotes = patch.Notes;
                    await ExecuteDatabaseStepAsync(
                        () => _sqlService.UpdateQuoteNotesAsync(
                            patch.QuoteNumber,
                            appliedNotes,
                            cancellationToken),
                        cancellationToken);
                    await _quotePatchOutbox.RemoveNotesIfMatchesAsync(
                        patch.QuoteNumber, appliedNotes, cancellationToken);
                    appliedAny = true;
                }
                if (patch.Status.HasValue)
                {
                    QuoteStatus appliedStatus = patch.Status.Value;
                    await ExecuteDatabaseStepAsync(
                        () => _sqlService.UpdateQuoteStatusAsync(
                            patch.QuoteNumber,
                            appliedStatus,
                            cancellationToken),
                        cancellationToken);
                    await _quotePatchOutbox.RemoveStatusIfMatchesAsync(
                        patch.QuoteNumber, appliedStatus, cancellationToken);
                    appliedAny = true;
                }
                if (patch.SendInfo != null)
                {
                    QuoteSendInfo appliedSendInfo = patch.SendInfo;
                    await ExecuteDatabaseStepAsync(
                        () => _sqlService.UpdateQuoteSendInfoAsync(
                            patch.QuoteNumber,
                            appliedSendInfo,
                            cancellationToken),
                        cancellationToken);
                    await _quotePatchOutbox.RemoveSendInfoIfMatchesAsync(
                        patch.QuoteNumber, appliedSendInfo, cancellationToken);
                    appliedAny = true;
                }
                if (patch.ReminderInfo != null)
                {
                    QuoteReminderInfo appliedReminderInfo = patch.ReminderInfo;
                    await ExecuteDatabaseStepAsync(
                        () => _sqlService.RegisterQuoteReminderAsync(
                            patch.QuoteNumber,
                            appliedReminderInfo,
                            cancellationToken),
                        cancellationToken);
                    await _quotePatchOutbox.RemoveReminderInfoIfMatchesAsync(
                        patch.QuoteNumber, appliedReminderInfo, cancellationToken);
                    appliedAny = true;
                }
                if (patch.SupplierInfo != null)
                {
                    QuoteSupplierInfo appliedSupplierInfo = patch.SupplierInfo;
                    await ExecuteDatabaseStepAsync(
                        () => _sqlService.UpdateQuoteSupplierInfoAsync(
                            patch.QuoteNumber,
                            appliedSupplierInfo,
                            cancellationToken),
                        cancellationToken);
                    await _quotePatchOutbox.RemoveSupplierInfoIfMatchesAsync(
                        patch.QuoteNumber, appliedSupplierInfo, cancellationToken);
                    appliedAny = true;
                }

                if (appliedAny)
                {
                    var databaseVersion = await _sqlService.GetQuoteByNumberAsync(
                        patch.QuoteNumber,
                        cancellationToken,
                        includeAttachments: false);
                    if (databaseVersion != null)
                        databaseVersions.Add(databaseVersion);
                    Debug.WriteLine($"[Sync] Metadati pendenti sincronizzati per {patch.QuoteNumber}.");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Sync] Metadati pendenti non sincronizzati per {patch.QuoteNumber}: {ex.Message}");
            }
        }

        if (databaseVersions.Count > 0)
        {
            // history.json contiene tutto lo storico: scriverlo una volta per
            // ogni patch offline moltiplicava decine di MB di I/O.
            var updateBatch = databaseVersions
                .GroupBy(quote => quote.QuoteNumber, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToList();
            try
            {
                await _localStore.BulkUpdateQuotesAsync(updateBatch, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Il database resta autorevole; SyncQuotes riallineera' la
                // cache senza riapplicare le patch gia' consumate.
                Debug.WriteLine($"[Sync] Cache locale patch non aggiornata: {ex.Message}");
            }
        }
    }

    private async Task PropagateDeletedQuotesAsync(CancellationToken cancellationToken)
    {
        var deletedQuoteNumbers = await _sqlService.GetDeletedQuoteNumbersAsync(cancellationToken);
        if (deletedQuoteNumbers.Count == 0)
            return;

        await _localStore.DeleteQuotesAsync(deletedQuoteNumbers, cancellationToken);
        foreach (string quoteNumber in deletedQuoteNumbers)
        {
            await _quotePatchOutbox.RemoveAsync(quoteNumber);
        }

        if (_dataService is FallbackDataService fallback)
            fallback.MarkLocalQuoteCacheUpdated(deletedQuoteNumbers);
    }

    private async Task<(int synced, int conflicts)> SyncCustomersAsync(
        IReadOnlyList<QuoteHistoryEntry> localQuotesSnapshot,
        CancellationToken cancellationToken)
    {
        int synced = 0;
        int conflicts = 0;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Debug.WriteLine("\n[Sync] ═══ CUSTOMERS SYNC START ═══");

            // Il JSON viene letto per primo: le modifiche locali pending devono
            // proteggere il relativo SyncId prima di compattare la vista DB.
            var jsonCustomers = await _localStore.LoadCustomersAsync(cancellationToken);
            var dbCustomers = await _sqlService.GetCustomersAsync(cancellationToken);
            var deletedDbCustomers = await _sqlService.GetDeletedCustomersAsync(cancellationToken);
            var protectedCustomerIds = await _sqlService
                .GetReferencedCustomerSyncIdsAsync(cancellationToken);

            protectedCustomerIds.UnionWith(jsonCustomers
                .Where(customer => customer.HasPendingDatabaseWrite)
                .Select(customer => customer.SyncId)
                .Where(syncId => syncId != Guid.Empty));
            protectedCustomerIds.UnionWith(localQuotesSnapshot
                .Where(quote => quote.HasPendingDatabaseWrite)
                .SelectMany(quote => new[]
                {
                    quote.CustomerSyncId,
                    quote.ReferenceCustomerSyncId,
                    quote.BillingCustomerSyncId
                })
                .Where(syncId => syncId != Guid.Empty));

            dbCustomers = dbCustomers
                .Where(c => !string.IsNullOrWhiteSpace(c.BusinessName))
                .ToList();
            deletedDbCustomers = deletedDbCustomers
                .Where(c => !string.IsNullOrWhiteSpace(c.BusinessName))
                .ToList();

            var blankLocalCustomers = jsonCustomers
                .Where(c => string.IsNullOrWhiteSpace(c.BusinessName))
                .ToList();
            if (blankLocalCustomers.Count > 0)
            {
                await _localStore.DeleteCustomersAsync(blankLocalCustomers, cancellationToken);
                jsonCustomers.RemoveAll(c => string.IsNullOrWhiteSpace(c.BusinessName));
                Debug.WriteLine($"[Sync] Rimossi {blankLocalCustomers.Count} clienti locali senza ragione sociale.");
            }

            Debug.WriteLine($"[Sync] 🗄️ DB customers: {dbCustomers.Count}");
            Debug.WriteLine($"[Sync] 📂 JSON customers: {jsonCustomers.Count}");

            var deletedCustomerIds = deletedDbCustomers
                .Select(customer => customer.SyncId)
                .Where(syncId => syncId != Guid.Empty)
                .ToHashSet();
            var compaction = CustomerDuplicateFilter.Compact(
                dbCustomers,
                protectedCustomerIds);
            dbCustomers = compaction.Kept.ToList();

            var ignoredLocalDuplicates = jsonCustomers
                .Where(customer =>
                    customer.SyncId != Guid.Empty &&
                    compaction.IgnoredIds.Contains(customer.SyncId))
                .ToList();
            if (ignoredLocalDuplicates.Count > 0)
            {
                // Questa pulizia riguarda soltanto la cache JSON. I record
                // duplicati nel DB restano intatti e quindi recuperabili.
                await _localStore.DeleteCustomersAsync(
                    ignoredLocalDuplicates,
                    cancellationToken);
                jsonCustomers.RemoveAll(customer =>
                    customer.SyncId != Guid.Empty &&
                    compaction.IgnoredIds.Contains(customer.SyncId));
            }
            if (compaction.IgnoredIds.Count > 0)
            {
                Debug.WriteLine(
                    $"[Sync] Vista clienti compattata: {compaction.IgnoredIds.Count} ID DB ignorati; " +
                    $"{ignoredLocalDuplicates.Count} copie rimosse dalla cache locale.");
            }

            var allDatabaseCustomerGroupsByName = dbCustomers
                .Concat(deletedDbCustomers)
                .GroupBy(customer => customer.BusinessName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

            var locallyStaleDeletedCustomers = jsonCustomers
                // Una riga legacy senza ID non puo' essere attribuita a un
                // tombstone solo per nome: potrebbero esistere omonimi attivi.
                .Where(local => local.SyncId != Guid.Empty && deletedCustomerIds.Contains(local.SyncId))
                .ToList();
            if (locallyStaleDeletedCustomers.Count > 0)
            {
                await _localStore.DeleteCustomersAsync(locallyStaleDeletedCustomers, cancellationToken);
                jsonCustomers.RemoveAll(local => locallyStaleDeletedCustomers.Any(deleted =>
                    CustomersRepresentSameIdentity(local, deleted)));
                synced += locallyStaleDeletedCustomers.Count;
            }

            var normalizedCustomers = new List<Customer>();
            var ambiguousLegacyCustomers = new List<Customer>();
            foreach (var local in jsonCustomers.Where(x => x.SyncId == Guid.Empty))
            {
                Customer? matchingDatabaseCustomer = null;
                if (allDatabaseCustomerGroupsByName.TryGetValue(local.BusinessName, out var sameNameCustomers))
                {
                    var sameContentIds = sameNameCustomers
                        .Where(database => CustomersHaveSameContent(database, local))
                        .Select(database => database.SyncId)
                        .Distinct()
                        .Take(2)
                        .ToList();
                    Guid? resolvedId = sameContentIds.Count == 1
                        ? sameContentIds[0]
                        : sameNameCustomers.Select(customer => customer.SyncId).Distinct().Take(2).Count() == 1
                            ? sameNameCustomers[0].SyncId
                            : null;

                    if (resolvedId.HasValue && !deletedCustomerIds.Contains(resolvedId.Value))
                        matchingDatabaseCustomer = dbCustomers.First(customer => customer.SyncId == resolvedId.Value);
                    else
                    {
                        ambiguousLegacyCustomers.Add(local);
                        conflicts++;
                        continue;
                    }
                }

                local.SyncId = matchingDatabaseCustomer?.SyncId ?? Guid.NewGuid();
                normalizedCustomers.Add(local);
            }
            if (normalizedCustomers.Count > 0)
            {
                // Se piu' alias legacy convergono sullo stesso ID, il bulk usa
                // l'ultimo: ordiniamo quindi in modo che pending e record piu'
                // recenti siano quelli conservati.
                var orderedNormalizedCustomers = normalizedCustomers
                    .OrderBy(customer => customer.HasPendingDatabaseWrite)
                    .ThenBy(customer => customer.LastModifiedUtc)
                    .ToList();
                await _localStore.BulkUpdateCustomersAsync(orderedNormalizedCustomers, cancellationToken);
                // Il bulk update elimina anche gli alias legacy basati sul nome.
                // Ricarichiamo il set compatto per non processare nello stesso giro
                // le vecchie righe senza SyncId appena sostituite.
                jsonCustomers = await _localStore.LoadCustomersAsync(cancellationToken);
            }
            if (ambiguousLegacyCustomers.Count > 0)
            {
                // Con piu' clienti DB omonimi non possiamo attribuire in sicurezza
                // una riga legacy priva di ID. La preserviamo nel file locale ma
                // la escludiamo dal push per non sovrascrivere un omonimo a caso.
                jsonCustomers.RemoveAll(customer => customer.SyncId == Guid.Empty);
                Debug.WriteLine($"[Sync] Clienti legacy ambigui non sincronizzati: {ambiguousLegacyCustomers.Count}");
            }

            var dbDict = dbCustomers
                .GroupBy(c => c.SyncId)
                .ToDictionary(g => g.Key, g => g.First());

            var jsonDict = jsonCustomers
                .GroupBy(c => c.SyncId)
                .ToDictionary(g => g.Key, g => g.First());

            var allKeys = dbDict.Keys.Union(jsonDict.Keys).ToList();

            // Raccogli tutte le modifiche in memoria prima di scrivere
            var toUpdateInJson = new List<Customer>();
            var toUpdateInDb = new List<Customer>();

            foreach (var key in allKeys)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    bool inDb = dbDict.TryGetValue(key, out var dbCustomer);
                    bool inJson = jsonDict.TryGetValue(key, out var jsonCustomer);

                    if (inDb && !inJson)
                    {
                        toUpdateInJson.Add(dbCustomer!);
                        synced++;
                        Debug.WriteLine($"[Sync] ✅ Customer {key}: DB → JSON");
                    }
                    else if (!inDb && inJson)
                    {
                        toUpdateInDb.Add(jsonCustomer!);
                        Debug.WriteLine($"[Sync] ✅ Customer {key}: JSON → DB");
                    }
                    else if (inDb && inJson)
                    {
                        bool pendingCanBeApplied = jsonCustomer!.HasPendingDatabaseWrite &&
                            (jsonCustomer.BaseVersionUtc == default ||
                             jsonCustomer.BaseVersionUtc == dbCustomer!.LastModifiedUtc);

                        if (pendingCanBeApplied)
                        {
                            toUpdateInDb.Add(jsonCustomer);
                        }
                        else if (!CustomersHaveSameSyncState(dbCustomer!, jsonCustomer))
                        {
                            toUpdateInJson.Add(dbCustomer!);
                            if (!CustomersHaveSameContent(dbCustomer!, jsonCustomer))
                                conflicts++;
                            synced++;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Sync] ❌ Error processing customer {key}: {ex.Message}");
                }
            }

            // Scrivi nel DB in sequenza (già ottimizzato lato SQL)
            foreach (var c in toUpdateInDb)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    Customer? newerDatabaseCustomer = null;
                    bool databaseCustomerWasDeleted = false;
                    bool existedAtSnapshot = dbDict.ContainsKey(c.SyncId) ||
                                             deletedCustomerIds.Contains(c.SyncId);
                    var saved = await ExecuteDatabaseStepAsync(async () =>
                    {
                        var currentState = await _sqlService.GetCustomerSyncStateAsync(
                            c.SyncId,
                            cancellationToken);
                        if (currentState.Customer != null)
                        {
                            bool matchesExpectedVersion =
                                c.BaseVersionUtc != default &&
                                c.BaseVersionUtc == currentState.Customer.LastModifiedUtc;
                            if (!matchesExpectedVersion || currentState.IsDeleted)
                            {
                                newerDatabaseCustomer = currentState.Customer;
                                databaseCustomerWasDeleted = currentState.IsDeleted;
                                return null;
                            }
                        }
                        else if (existedAtSnapshot)
                        {
                            // Il record e' sparito dopo la lettura iniziale: non
                            // deve essere ricreato da uno snapshot locale vecchio.
                            databaseCustomerWasDeleted = true;
                            return null;
                        }

                        return await _sqlService.AddCustomerWithExpectedVersionAsync(
                            c,
                            cancellationToken,
                            expectedLastModifiedUtc:
                                currentState.Customer?.LastModifiedUtc ?? default);
                    },
                        cancellationToken);

                    if (saved == null)
                    {
                        conflicts++;
                        if (!databaseCustomerWasDeleted && newerDatabaseCustomer != null)
                        {
                            newerDatabaseCustomer.HasPendingDatabaseWrite = false;
                            toUpdateInJson.Add(newerDatabaseCustomer);
                            synced++;
                        }
                        continue;
                    }

                    saved.HasPendingDatabaseWrite = false;
                    toUpdateInJson.Add(saved);
                    synced++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Sync] DB customer error {c.BusinessName}: {ex.Message} | Inner: {ex.GetBaseException().Message}");
                }
            }

            if (toUpdateInJson.Count > 0)
            {
                Debug.WriteLine($"[Sync] 📂 Writing {toUpdateInJson.Count} customers to JSON (batch)...");
                await _localStore.BulkUpdateCustomersAsync(toUpdateInJson, cancellationToken);
            }

            Debug.WriteLine($"[Sync] ═══ CUSTOMERS SYNC END: synced={synced} ═══\n");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Sync] ❌ Error syncing customers: {ex.Message}");
        }

        return (synced, conflicts);
    }

    private static async Task ExecuteDatabaseStepAsync(
        Func<Task> operation,
        CancellationToken cancellationToken)
    {
        await DatabaseOperationCoordinator.Gate.WaitAsync(cancellationToken);
        try
        {
            await operation();
        }
        finally
        {
            DatabaseOperationCoordinator.Gate.Release();
        }
    }

    private static async Task<T> ExecuteDatabaseStepAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        await DatabaseOperationCoordinator.Gate.WaitAsync(cancellationToken);
        try
        {
            return await operation();
        }
        finally
        {
            DatabaseOperationCoordinator.Gate.Release();
        }
    }

    private static bool CustomersHaveSameContent(Customer left, Customer right) =>
        string.Equals(left.BusinessName, right.BusinessName, StringComparison.Ordinal) &&
        string.Equals(left.Address, right.Address, StringComparison.Ordinal) &&
        string.Equals(left.Email, right.Email, StringComparison.Ordinal) &&
        string.Equals(left.Phone, right.Phone, StringComparison.Ordinal) &&
        left.MaterialDiscount.Equals(right.MaterialDiscount) &&
        left.LaborDiscount.Equals(right.LaborDiscount) &&
        left.SupplierDiscount.Equals(right.SupplierDiscount) &&
        left.IsSupplier == right.IsSupplier;

    private static bool CustomersHaveSameSyncState(Customer database, Customer local) =>
        CustomersHaveSameContent(database, local) &&
        database.SyncId == local.SyncId &&
        database.LastModifiedUtc == local.LastModifiedUtc &&
        !local.HasPendingDatabaseWrite;

    private static bool CustomersRepresentSameIdentity(Customer left, Customer right)
    {
        if (left.SyncId != Guid.Empty || right.SyncId != Guid.Empty)
            return left.SyncId != Guid.Empty &&
                   right.SyncId != Guid.Empty &&
                   left.SyncId == right.SyncId;

        return left.BusinessName.Equals(right.BusinessName, StringComparison.OrdinalIgnoreCase);
    }

    private static void HydratePendingAttachments(QuoteHistoryEntry quote)
    {
        if (!quote.HasCompleteAttachmentSnapshot || quote.Attachments.Count == 0)
            return;

        try
        {
            string? parent = Path.GetDirectoryName(quote.PdfPath);
            if (string.IsNullOrWhiteSpace(parent))
            {
                quote.HasCompleteAttachmentSnapshot = false;
                return;
            }

            string directory = Path.Combine(parent, "Allegati_" + quote.QuoteNumber);
            if (!Directory.Exists(directory))
            {
                quote.HasCompleteAttachmentSnapshot = false;
                return;
            }

            quote.Attachments = Directory.EnumerateFiles(directory)
                .Select(path => new StoredFile
                {
                    FileName = Path.GetFileName(path),
                    ContentType = GetAttachmentContentType(path),
                    Content = File.ReadAllBytes(path),
                    ImportedAt = File.GetLastWriteTimeUtc(path)
                })
                .ToList();
        }
        catch (Exception ex)
        {
            quote.HasCompleteAttachmentSnapshot = false;
            Debug.WriteLine($"[Sync] Allegati locali non caricabili per {quote.QuoteNumber}: {ex.Message}");
        }
    }

    private static string GetAttachmentContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".bmp" => "image/bmp",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };
}

public class SyncResult
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;

    public int QuotesSynced { get; set; }
    public int QuotesConflicts { get; set; }

    public int CustomersSynced { get; set; }
    public int CustomersConflicts { get; set; }

    public bool AlreadyRunning { get; set; }
    public bool Skipped { get; set; }
    public string? Error { get; set; }

    public bool IsSuccess => string.IsNullOrEmpty(Error) && !AlreadyRunning;
}

public sealed class SyncCompletedEventArgs(SyncResult result) : EventArgs
{
    public SyncResult Result { get; } = result;
}

