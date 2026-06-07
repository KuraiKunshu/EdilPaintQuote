using System;
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
    private readonly IDataService _dataService;
    private readonly LocalJsonStoreService _localStore;
    private readonly SqlDataService _sqlService;
    private readonly LocalQuotePatchOutboxService _quotePatchOutbox;
    private readonly LocalDeletionOutboxService _deletionOutbox;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly object _statusLock = new();
    private DateTime _lastSyncTime = DateTime.MinValue;
    private DateTime? _lastSyncCompletedUtc;
    private string _lastSyncSummary = "Sincronizzazione non ancora eseguita.";

    public bool IsSyncRunning => _syncLock.CurrentCount == 0;

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
    
    public async Task<SyncResult> SyncAllAsync(
        bool force = false,
        int take = 0,
        CancellationToken cancellationToken = default,
        bool waitForCurrentRun = false)
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

            var quotesResult = await SyncQuotesAsync(take, cancellationToken);
            result.QuotesSynced = quotesResult.synced;
            result.QuotesConflicts = quotesResult.conflicts;

            var customersResult = await SyncCustomersAsync(cancellationToken);
            result.CustomersSynced = customersResult.synced;
            result.CustomersConflicts = customersResult.conflicts;

            _lastSyncTime = DateTime.UtcNow;
            result.EndTime = DateTime.UtcNow;
            UpdateSyncStatus(result.EndTime, $"Completata: preventivi {result.QuotesSynced}, clienti {result.CustomersSynced}, conflitti {result.QuotesConflicts + result.CustomersConflicts}.");

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
        CancellationToken cancellationToken)
    {
        int synced = 0;
        int conflicts = 0;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Debug.WriteLine("\n[Sync] ═══ QUOTES SYNC START ═══");

            // Carica solo i METADATA dal JSON locale
            var jsonQuotes = await _localStore.LoadHistoryAsync(cancellationToken);
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
            int loggedConflicts = 0;

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
                    continue;

                var timeDiff = (jsonQuote.LastModifiedUtc - dbMeta.LastModifiedUtc).TotalSeconds;
                if (Math.Abs(timeDiff) <= 60)
                {
                    conflicts++;
                    if (loggedConflicts < 10)
                        Debug.WriteLine($"[Sync] Quote {key}: contenuto diverso con timestamp ravvicinati ({timeDiff:F1}s). Uso la versione piu' recente.");

                    loggedConflicts++;

                    if (jsonQuote.LastModifiedUtc > dbMeta.LastModifiedUtc)
                    {
                        var dbVersion = (await _sqlService.GetQuotesByNumbersAsync([key], cancellationToken))
                            .FirstOrDefault();
                        if (dbVersion != null)
                            await _localStore.ArchiveQuoteConflictAsync(
                                dbVersion,
                                "Versione SQL archiviata prima di applicare una modifica locale ravvicinata.",
                                cancellationToken);
                    }
                    else
                    {
                        await _localStore.ArchiveQuoteConflictAsync(
                            jsonQuote,
                            "Versione locale archiviata prima di applicare la versione SQL ravvicinata.",
                            cancellationToken);
                    }
                }

                if (dbMeta.LastModifiedUtc == DateTime.MinValue || jsonQuote.LastModifiedUtc > dbMeta.LastModifiedUtc)
                {
                    quotesPendingDbUpdate.Add(jsonQuote);
                    synced++;
                }
                else
                {
                    keysNeedingDbLoad.Add(key);
                }
            }

            if (loggedConflicts > 10)
                Debug.WriteLine($"[Sync] Altri {loggedConflicts - 10} conflitti ravvicinati omessi dal log.");

            if (keysNeedingDbLoad.Count > 0)
            {
                var dbToJson = await _sqlService.GetQuotesByNumbersAsync(keysNeedingDbLoad, cancellationToken);
                quotesPendingJsonUpdate.AddRange(dbToJson);
                synced += dbToJson.Count;
            }

            Debug.WriteLine($"[Sync]    - Pending JSON updates: {quotesPendingJsonUpdate.Count}");
            Debug.WriteLine($"[Sync]    - Pending DB updates: {quotesPendingDbUpdate.Count}");

            if (quotesPendingJsonUpdate.Count > 0)
                await _localStore.BulkUpdateQuotesAsync(quotesPendingJsonUpdate, cancellationToken);

            if (quotesPendingDbUpdate.Count > 0)
            {
                foreach (var q in quotesPendingDbUpdate)
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        await _sqlService.SaveQuoteAsync(q, cancellationToken);
                        await _localStore.BulkUpdateQuotesAsync([q], cancellationToken);
                    }
                    catch (QuoteConflictException ex)
                    {
                        await _localStore.ArchiveQuoteConflictAsync(q, ex.Message, cancellationToken);
                        var databaseVersion = await _sqlService.GetQuoteByNumberAsync(q.QuoteNumber);
                        if (databaseVersion != null)
                            await _localStore.BulkUpdateQuotesAsync([databaseVersion], cancellationToken);
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
        var mismatchKeys = inBoth
            .Where(key =>
                jsonQuotes.TryGetValue(key, out var jsonQuote) &&
                dbMetadata.TryGetValue(key, out var dbMeta) &&
                !string.Equals(jsonQuote.SyncHash, dbMeta.SyncHash, StringComparison.Ordinal))
            .ToList();

        if (mismatchKeys.Count == 0)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var dbSnapshots = await _sqlService.GetQuoteSyncSnapshotsAsync(mismatchKeys, cancellationToken);
        var dbSnapshotDict = dbSnapshots.ToDictionary(
            quote => quote.QuoteNumber,
            quote => quote,
            StringComparer.OrdinalIgnoreCase);

        var normalizedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dbHashUpdates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in mismatchKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!dbSnapshotDict.TryGetValue(key, out var dbSnapshot))
                continue;

            var jsonQuote = jsonQuotes[key];
            string jsonCanonicalHash = QuoteSyncHashService.Compute(jsonQuote);
            string dbCanonicalHash = QuoteSyncHashService.Compute(dbSnapshot);

            if (!string.Equals(jsonCanonicalHash, dbCanonicalHash, StringComparison.Ordinal))
                continue;

            normalizedKeys.Add(key);

            if (!string.Equals(jsonQuote.SyncHash, jsonCanonicalHash, StringComparison.Ordinal))
            {
                jsonQuote.SyncHash = jsonCanonicalHash;
                quotesPendingJsonUpdate.Add(jsonQuote);
            }

            if (!string.Equals(dbMetadata[key].SyncHash, dbCanonicalHash, StringComparison.Ordinal))
                dbHashUpdates[key] = dbCanonicalHash;
        }

        if (dbHashUpdates.Count > 0)
            await _sqlService.UpdateQuoteSyncHashesAsync(dbHashUpdates, cancellationToken);

        if (normalizedKeys.Count > 0)
            Debug.WriteLine($"[Sync] Riallineati {normalizedKeys.Count} hash obsoleti senza riscrivere i preventivi.");

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
                await _sqlService.DeleteQuoteAsync(quote.QuoteNumber);
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
                await _sqlService.DeleteCustomerAsync(customer.SyncId, customer.BusinessName);
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
        foreach (var patch in await _quotePatchOutbox.LoadAllAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (patch.Notes != null)
                    await _sqlService.UpdateQuoteNotesAsync(patch.QuoteNumber, patch.Notes, cancellationToken);
                if (patch.Status.HasValue)
                    await _sqlService.UpdateQuoteStatusAsync(patch.QuoteNumber, patch.Status.Value, cancellationToken);
                if (patch.SendInfo != null)
                    await _sqlService.UpdateQuoteSendInfoAsync(patch.QuoteNumber, patch.SendInfo, cancellationToken);
                if (patch.ReminderInfo != null)
                    await _sqlService.RegisterQuoteReminderAsync(patch.QuoteNumber, patch.ReminderInfo, cancellationToken);

                var databaseVersion = await _sqlService.GetQuoteByNumberAsync(patch.QuoteNumber);
                if (databaseVersion != null)
                    await _localStore.BulkUpdateQuotesAsync([databaseVersion], cancellationToken);

                await _quotePatchOutbox.RemoveAsync(patch.QuoteNumber);
                Debug.WriteLine($"[Sync] Metadati pendenti sincronizzati per {patch.QuoteNumber}.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Sync] Metadati pendenti non sincronizzati per {patch.QuoteNumber}: {ex.Message}");
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
    }

    private async Task<(int synced, int conflicts)> SyncCustomersAsync(CancellationToken cancellationToken)
    {
        int synced = 0;
        int conflicts = 0;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Debug.WriteLine("\n[Sync] ═══ CUSTOMERS SYNC START ═══");

            var dbCustomers = await _sqlService.GetCustomersAsync(cancellationToken);
            var deletedDbCustomers = await _sqlService.GetDeletedCustomersAsync(cancellationToken);
            var jsonCustomers = await _localStore.LoadCustomersAsync(cancellationToken);

            Debug.WriteLine($"[Sync] 🗄️ DB customers: {dbCustomers.Count}");
            Debug.WriteLine($"[Sync] 📂 JSON customers: {jsonCustomers.Count}");

            var locallyStaleDeletedCustomers = jsonCustomers
                .Where(local => deletedDbCustomers.Any(deleted =>
                    (local.SyncId != Guid.Empty && deleted.SyncId == local.SyncId) ||
                    deleted.BusinessName.Equals(local.BusinessName, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (locallyStaleDeletedCustomers.Count > 0)
            {
                await _localStore.DeleteCustomersAsync(locallyStaleDeletedCustomers, cancellationToken);
                jsonCustomers.RemoveAll(local => locallyStaleDeletedCustomers.Any(deleted =>
                    (local.SyncId != Guid.Empty && local.SyncId == deleted.SyncId) ||
                    local.BusinessName.Equals(deleted.BusinessName, StringComparison.OrdinalIgnoreCase)));
            }

            var normalizedCustomers = new List<Customer>();
            foreach (var local in jsonCustomers.Where(x => x.SyncId == Guid.Empty))
            {
                local.SyncId = dbCustomers
                    .FirstOrDefault(db => db.BusinessName.Equals(local.BusinessName, StringComparison.OrdinalIgnoreCase))
                    ?.SyncId ?? Guid.NewGuid();
                normalizedCustomers.Add(local);
            }
            if (normalizedCustomers.Count > 0)
                await _localStore.BulkUpdateCustomersAsync(normalizedCustomers, cancellationToken);

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
                        toUpdateInJson.Add(jsonCustomer!);
                        toUpdateInDb.Add(jsonCustomer!);
                        synced++;
                        Debug.WriteLine($"[Sync] ✅ Customer {key}: JSON → DB");
                    }
                    else if (inDb && inJson)
                    {
                        if (dbCustomer!.LastModifiedUtc > jsonCustomer!.LastModifiedUtc)
                        {
                            toUpdateInJson.Add(dbCustomer);
                            synced++;
                            Debug.WriteLine($"[Sync] ✅ Customer {key}: DB più recente → aggiornato JSON");
                        }
                        else if (jsonCustomer.LastModifiedUtc > dbCustomer.LastModifiedUtc)
                        {
                            toUpdateInDb.Add(jsonCustomer);
                            synced++;
                            Debug.WriteLine($"[Sync] ✅ Customer {key}: JSON più recente → aggiornato DB");
                        }
                        else
                        {
                            // Stesso timestamp — solo se manca nel JSON, aggiorna
                            // nessuna azione necessaria
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

            // Scrivi il JSON UNA SOLA VOLTA con tutti i clienti aggiornati
            if (toUpdateInJson.Count > 0)
            {
                Debug.WriteLine($"[Sync] 📂 Writing {toUpdateInJson.Count} customers to JSON (batch)...");
                await _localStore.BulkUpdateCustomersAsync(toUpdateInJson, cancellationToken);
            }

            // Scrivi nel DB in sequenza (già ottimizzato lato SQL)
            foreach (var c in toUpdateInDb)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { await _sqlService.AddCustomerAsync(c, cancellationToken); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Debug.WriteLine($"[Sync] DB customer error {c.BusinessName}: {ex.Message}"); }
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

