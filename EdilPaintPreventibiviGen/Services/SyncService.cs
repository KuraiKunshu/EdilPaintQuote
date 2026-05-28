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
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private DateTime _lastSyncTime = DateTime.MinValue;

    public SyncService(IDataService dataService, SqlDataService sqlService, LocalJsonStoreService localStore)
    {
        _dataService = dataService;
        _sqlService = sqlService;
        _localStore = localStore;
    }
    
    public async Task<SyncResult> SyncAllAsync(bool force = false, int take = 0)
    {
        if (!await _syncLock.WaitAsync(0))
        {
            Debug.WriteLine("[Sync] Already syncing, skipping...");
            return new SyncResult { AlreadyRunning = true };
        }

        try
        {
            if (!force && (DateTime.UtcNow - _lastSyncTime).TotalSeconds < 30)
            {
                Debug.WriteLine("[Sync] Too soon since last sync, skipping...");
                return new SyncResult { Skipped = true };
            }

            var result = new SyncResult { StartTime = DateTime.UtcNow };
            Debug.WriteLine($"╔══════════════════════════════════════════════════╗");
            Debug.WriteLine($"║  SYNC SERVICE - STARTING SYNC (take={take})");
            Debug.WriteLine($"╚══════════════════════════════════════════════════╝");

            var quotesResult = await SyncQuotesAsync(take);
            result.QuotesSynced = quotesResult.synced;
            result.QuotesConflicts = quotesResult.conflicts;

            var customersResult = await SyncCustomersAsync();
            result.CustomersSynced = customersResult.synced;
            result.CustomersConflicts = customersResult.conflicts;

            _lastSyncTime = DateTime.UtcNow;
            result.EndTime = DateTime.UtcNow;

            Debug.WriteLine($"║ SYNC COMPLETED in {result.Duration.TotalSeconds:F2}s");
            Debug.WriteLine($"║ Quotes={result.QuotesSynced}, Customers={result.CustomersSynced}");

            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Sync] ❌ ERROR: {ex.Message}");
            return new SyncResult { Error = ex.Message };
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task<(int synced, int conflicts)> SyncQuotesAsync(int take = 0)
    {
        int synced = 0;
        int conflicts = 0;

        try
        {
            Debug.WriteLine("\n[Sync] ═══ QUOTES SYNC START ═══");

            // Carica solo i METADATA dal JSON locale
            var jsonQuotes = await _localStore.LoadHistoryAsync();
            Debug.WriteLine($"[Sync] 📂 JSON quotes loaded: {jsonQuotes.Count}");

            var jsonDict = jsonQuotes
                .GroupBy(q => q.QuoteNumber, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(q => q.LastModifiedUtc).First(),
                    StringComparer.OrdinalIgnoreCase);

            // Carica i metadata dal DB — se take > 0, limita agli ultimi N
            Debug.WriteLine("[Sync] 🗄️ Loading DB quote metadata...");
            var dbMetadata = await _sqlService.GetQuoteMetadataAsync();
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
                var toLoad = await _sqlService.GetQuotesByNumbersAsync(onlyInDb);
                quotesPendingJsonUpdate.AddRange(toLoad);
                synced += toLoad.Count;
            }

            foreach (var key in onlyInJson)
            {
                if (jsonDict.TryGetValue(key, out var jsonQuote))
                {
                    quotesPendingDbUpdate.Add(jsonQuote);
                    synced++;
                }
            }

            var keysNeedingDbLoad = new List<string>();

            foreach (var key in inBoth)
            {
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
                    Debug.WriteLine($"[Sync] Quote {key}: hash diverso con timestamp ravvicinati ({timeDiff:F1}s). Uso la versione piu' recente.");
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

            if (keysNeedingDbLoad.Count > 0)
            {
                var dbToJson = await _sqlService.GetQuotesByNumbersAsync(keysNeedingDbLoad);
                quotesPendingJsonUpdate.AddRange(dbToJson);
                synced += dbToJson.Count;
            }

            Debug.WriteLine($"[Sync]    - Pending JSON updates: {quotesPendingJsonUpdate.Count}");
            Debug.WriteLine($"[Sync]    - Pending DB updates: {quotesPendingDbUpdate.Count}");

            if (quotesPendingJsonUpdate.Count > 0)
                await _localStore.BulkUpdateQuotesAsync(quotesPendingJsonUpdate);

            if (quotesPendingDbUpdate.Count > 0)
            {
                foreach (var q in quotesPendingDbUpdate)
                {
                    try
                    {
                        if (q.PdfFile == null || q.PdfFile.Content.Length == 0)
                        {
                            var existingPdf = await _sqlService.GetQuotePdfContentAsync(q.QuoteNumber);
                            if (existingPdf != null && existingPdf.Length > 0)
                            {
                                q.PdfFile ??= new StoredFile { FileName = $"{q.QuoteNumber}.pdf", ContentType = "application/pdf", ImportedAt = DateTime.UtcNow };
                                q.PdfFile.Content = existingPdf;
                            }
                        }
                        await _dataService.SaveQuoteAsync(q);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Sync] ❌ DB save error for {q.QuoteNumber}: {ex.Message}");
                    }
                }
            }

            Debug.WriteLine("[Sync] ═══ QUOTES SYNC END ═══\n");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Sync] ❌ Error syncing quotes: {ex.Message}");
        }

        return (synced, conflicts);
    }

    private async Task<(int synced, int conflicts)> SyncCustomersAsync()
    {
        int synced = 0;
        int conflicts = 0;

        try
        {
            Debug.WriteLine("\n[Sync] ═══ CUSTOMERS SYNC START ═══");

            var dbCustomers = await _sqlService.GetCustomersAsync();
            var jsonCustomers = await _localStore.LoadCustomersAsync();

            Debug.WriteLine($"[Sync] 🗄️ DB customers: {dbCustomers.Count}");
            Debug.WriteLine($"[Sync] 📂 JSON customers: {jsonCustomers.Count}");

            var dbDict = dbCustomers
                .GroupBy(c => c.BusinessName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var jsonDict = jsonCustomers
                .GroupBy(c => c.BusinessName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var allKeys = dbDict.Keys.Union(jsonDict.Keys).ToList();

            // Raccogli tutte le modifiche in memoria prima di scrivere
            var toUpdateInJson = new List<Customer>();
            var toUpdateInDb = new List<Customer>();

            foreach (var key in allKeys)
            {
                try
                {
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
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Sync] ❌ Error processing customer {key}: {ex.Message}");
                }
            }

            // Scrivi il JSON UNA SOLA VOLTA con tutti i clienti aggiornati
            if (toUpdateInJson.Count > 0)
            {
                Debug.WriteLine($"[Sync] 📂 Writing {toUpdateInJson.Count} customers to JSON (batch)...");
                await _localStore.BulkUpdateCustomersAsync(toUpdateInJson);
            }

            // Scrivi nel DB in sequenza (già ottimizzato lato SQL)
            foreach (var c in toUpdateInDb)
            {
                try { await _sqlService.AddCustomerAsync(c); }
                catch (Exception ex) { Debug.WriteLine($"[Sync] ❌ DB customer error {c.BusinessName}: {ex.Message}"); }
            }

            Debug.WriteLine($"[Sync] ═══ CUSTOMERS SYNC END: synced={synced} ═══\n");
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

