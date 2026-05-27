using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

/// <summary>
/// DataService con fallback automatico su JSON locale se il database non è disponibile
/// </summary>
public class FallbackDataService : IDataService
{
    private readonly SqlDataService _sqlService;
    private readonly LocalJsonStoreService _localStore;
    private bool _isDatabaseAvailable = true;
    private DateTime _databaseUnavailableSince = DateTime.MinValue;
    private static readonly TimeSpan DbRetryCooldown = TimeSpan.FromMinutes(2);


    // Cache dei numeri preventivo presenti nel DB (query leggera, una sola volta ogni 10 min)
    private HashSet<string>? _dbQuoteNumbersCache;
    private DateTime _dbQuoteNumbersCacheTime = DateTime.MinValue;

    // Cache dei numeri preventivo presenti nel JSON locale (una sola lettura ogni 10 min)
    private HashSet<string>? _localQuoteNumbersCache;
    private DateTime _localQuoteNumbersCacheTime = DateTime.MinValue;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    public FallbackDataService(SqlDataService sqlService, LocalJsonStoreService localStore)
    {
        _sqlService = sqlService;
        _localStore = localStore;
    }

    public async Task InitializeAsync()
    {
        Debug.WriteLine("[FallbackDataService] InitializeAsync starting...");
        try
        {
            await _sqlService.InitializeAsync();
            
            _isDatabaseAvailable = true;
            Debug.WriteLine("[FallbackDataService] ✅ Database initialized successfully");
        }
        catch (Exception ex)
        {
            SetDatabaseUnavailable(ex.Message);
            Debug.WriteLine($"[FallbackDataService] ❌ Database initialization FAILED: {ex.Message}");
            Debug.WriteLine($"[FallbackDataService] StackTrace: {ex.StackTrace}");
            Debug.WriteLine("[FallbackDataService] ⚠️ Will use local JSON fallback");
        }
    }

    #region Cache helpers

    /// <summary>
    /// Restituisce tutti i numeri preventivo presenti nel DB Azure.
    /// Una sola query, risultato cachato per 10 minuti.
    /// </summary>
    private async Task<HashSet<string>> GetDbQuoteNumbersCachedAsync()
    {
        if (_dbQuoteNumbersCache == null || DateTime.UtcNow - _dbQuoteNumbersCacheTime > CacheDuration)
        {
            if (IsDatabaseAvailable())
            {
                try
                {
                    _dbQuoteNumbersCache = await _sqlService.GetAllQuoteNumbersAsync();
                    _dbQuoteNumbersCacheTime = DateTime.UtcNow;
                    Debug.WriteLine($"[FallbackDataService] DB numbers cache refreshed: {_dbQuoteNumbersCache.Count} entries");
                }
                catch( Exception ex)
                {
                    SetDatabaseUnavailable(ex.Message);
                    _dbQuoteNumbersCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
            }
            else
            {
                _dbQuoteNumbersCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }
        return _dbQuoteNumbersCache;
    }

    /// <summary>
    /// Restituisce tutti i numeri preventivo presenti nel JSON locale.
    /// Una sola lettura del file, risultato cachato per 10 minuti.
    /// </summary>
    private async Task<HashSet<string>> GetLocalQuoteNumbersCachedAsync()
    {
        if (_localQuoteNumbersCache == null || DateTime.UtcNow - _localQuoteNumbersCacheTime > CacheDuration)
        {
            var localEntries = await _localStore.LoadHistoryAsync();
            _localQuoteNumbersCache = new HashSet<string>(
                localEntries.Select(q => q.QuoteNumber),
                StringComparer.OrdinalIgnoreCase);
            _localQuoteNumbersCacheTime = DateTime.UtcNow;
            Debug.WriteLine($"[FallbackDataService] Local numbers cache refreshed: {_localQuoteNumbersCache.Count} entries");
        }
        return _localQuoteNumbersCache;
    }

    
    /// <summary>
    /// Invalida entrambe le cache (chiamare dopo SaveQuoteAsync / DeleteQuoteAsync).
    /// </summary>
    private void InvalidateQuoteNumbersCaches()
    {
        _dbQuoteNumbersCache = null;
        _localQuoteNumbersCache = null;
    }

    /// <summary>
    /// Determina lo SyncStatus confrontando le due cache in memoria.
    /// </summary>
    private SyncStatus ResolveSyncStatus(string quoteNumber, HashSet<string> dbNumbers, HashSet<string> localNumbers)
    {
        bool inDb = dbNumbers.Contains(quoteNumber);
        bool inLocal = localNumbers.Contains(quoteNumber);

        var status = (inDb, inLocal) switch
        {
            (true, true) => SyncStatus.Synced,
            (true, false) => SyncStatus.OnlineOnly,
            (false, true) => SyncStatus.LocalOnly,
            _ => SyncStatus.LocalOnly
        };

        // Debug per quote problematiche
        if (status != SyncStatus.Synced)
        {
            Debug.WriteLine($"[ResolveSyncStatus] Quote {quoteNumber}: inDB={inDb}, inLocal={inLocal} → {status}");
        }

        return status;
    }
    private void SetDatabaseUnavailable(string reason)
    {
        if (IsDatabaseAvailable())
        {
            Debug.WriteLine($"[FallbackDataService] ⚠️⚠️⚠️ DATABASE MARKED AS UNAVAILABLE!");
            Debug.WriteLine($"[FallbackDataService] Reason: {reason}");
            _isDatabaseAvailable = false;
            _databaseUnavailableSince = DateTime.UtcNow;
        }
    }
    
    private bool IsDatabaseAvailable()
    {
        if (_isDatabaseAvailable) return true;

        // Dopo il cooldown, riprova
        if ((DateTime.UtcNow - _databaseUnavailableSince) > DbRetryCooldown)
        {
            Debug.WriteLine("[FallbackDataService] 🔄 Retrying database connection...");
            _isDatabaseAvailable = true;
        }

        return _isDatabaseAvailable;
    }

    #endregion

    #region Quotes

    public async Task<List<QuoteHistoryEntry>> GetQuotesAsync()
    {
        Debug.WriteLine($"[FallbackDataService] GetQuotesAsync called. IsDatabaseAvailable() = {IsDatabaseAvailable()}");
    
        if (IsDatabaseAvailable())
        {
            try
            {
                Debug.WriteLine("[FallbackDataService] 🗄️ Attempting to read from SQL database...");
                var result = await _sqlService.GetQuotesAsync();
                Debug.WriteLine($"[FallbackDataService] ✅ SQL returned {result.Count} quotes");
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FallbackDataService] ❌ SQL FAILED: {ex.Message}");
                Debug.WriteLine($"[FallbackDataService] StackTrace: {ex.StackTrace}");
                SetDatabaseUnavailable(ex.Message);
            }
        }

        Debug.WriteLine("[FallbackDataService] ⚠️ Using JSON fallback (DB unavailable)");
        return await _localStore.LoadHistoryAsync();
    }

    public async Task<List<QuoteHistoryEntry>> GetQuotesAsync(int take)
    {
        if (IsDatabaseAvailable())
        {
            try { return await _sqlService.GetQuotesAsync(take); }
            catch(Exception ex) { SetDatabaseUnavailable(ex.Message); }
        }

        var all = await _localStore.LoadHistoryAsync();
        return all.OrderByDescending(q => q.Date).Take(take).ToList();
    }

    public async Task<List<QuoteHistorySummary>> GetQuoteSummariesAsync(int take)
{
    Debug.WriteLine("\n[FallbackDataService] ═══ GetQuoteSummariesAsync START ═══");
    Debug.WriteLine($"[FallbackDataService] Database available: {IsDatabaseAvailable()}");

    if (IsDatabaseAvailable())
    {
        try
        {
            // 1 query DB per i summary visualizzati
            Debug.WriteLine("[FallbackDataService] 🗄️ Fetching summaries from DB...");
            var dbQuotes = await _sqlService.GetQuoteSummariesAsync(take);
            Debug.WriteLine($"[FallbackDataService] 🗄️ DB returned {dbQuotes.Count} summaries");

            // 1 query DB per tutti i numeri (cachata) + 1 lettura JSON (cachata)
            Debug.WriteLine("[FallbackDataService] 🔑 Getting cached quote numbers...");
            var dbAllNumbers = await GetDbQuoteNumbersCachedAsync();
            var localNumbers = await GetLocalQuoteNumbersCachedAsync();

            Debug.WriteLine($"[FallbackDataService] 📊 Cache status:");
            Debug.WriteLine($"[FallbackDataService]    - DB quote numbers in cache: {dbAllNumbers.Count}");
            Debug.WriteLine($"[FallbackDataService]    - Local JSON quote numbers in cache: {localNumbers.Count}");

            // Analisi dettagliata
            var onlyInDb = dbAllNumbers.Except(localNumbers).ToList();
            var onlyInLocal = localNumbers.Except(dbAllNumbers).ToList();
            var inBoth = dbAllNumbers.Intersect(localNumbers).ToList();

            Debug.WriteLine($"[FallbackDataService] 📈 Distribution:");
            Debug.WriteLine($"[FallbackDataService]    - Only in DB (OnlineOnly): {onlyInDb.Count}");
            Debug.WriteLine($"[FallbackDataService]    - Only in Local (LocalOnly - RED): {onlyInLocal.Count}");
            Debug.WriteLine($"[FallbackDataService]    - In both (Synced - GREEN): {inBoth.Count}");

            if (onlyInLocal.Count > 0)
            {
                Debug.WriteLine($"[FallbackDataService] ⚠️ LocalOnly quotes (showing first 20): {string.Join(", ", onlyInLocal.Take(20))}");
            }

            foreach (var q in dbQuotes)
            {
                var oldStatus = q.SyncStatus;
                q.SyncStatus = ResolveSyncStatus(q.QuoteNumber, dbAllNumbers, localNumbers);
                
                if (q.SyncStatus != SyncStatus.Synced)
                {
                    Debug.WriteLine($"[FallbackDataService] ⚠️ Quote {q.QuoteNumber}: Status = {q.SyncStatus} (inDB={dbAllNumbers.Contains(q.QuoteNumber)}, inLocal={localNumbers.Contains(q.QuoteNumber)})");
                }
            }

            Debug.WriteLine("[FallbackDataService] ═══ GetQuoteSummariesAsync END ═══\n");
            return dbQuotes;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FallbackDataService] ❌ Error: {ex.Message}");
            SetDatabaseUnavailable(ex.Message);
        }
    }

    // Fallback: solo JSON locale
    Debug.WriteLine("[FallbackDataService] ⚠️ Using JSON fallback (DB unavailable)");
    var quotes = await _localStore.LoadHistoryAsync();
    return quotes.OrderByDescending(q => q.Date)
        .Take(take)
        .Select(q => new QuoteHistorySummary
        {
            QuoteNumber = q.QuoteNumber,
            Date = q.Date,
            CustomerName = q.CustomerName,
            ReferenceName = q.ReferenceName,
            PdfPath = q.PdfPath,
            Total = (decimal)q.Total,
            Status = q.Status,
            Notes = q.Notes,
            SyncStatus = SyncStatus.LocalOnly
        })
        .ToList();
}

    public async Task<List<QuoteHistorySummary>> SearchQuoteSummariesAsync(string searchText, int take)
    {
        if (IsDatabaseAvailable())
        {
            try
            {
                // 1 query DB per i risultati di ricerca
                var dbQuotes = await _sqlService.SearchQuoteSummariesAsync(searchText, take);

                // Riusa le cache — nessuna ulteriore lettura su disco o DB
                var dbAllNumbers = await GetDbQuoteNumbersCachedAsync();
                var localNumbers = await GetLocalQuoteNumbersCachedAsync();

                foreach (var q in dbQuotes)
                    q.SyncStatus = ResolveSyncStatus(q.QuoteNumber, dbAllNumbers, localNumbers);

                return dbQuotes;
            }
            catch( Exception ex)
            {
                SetDatabaseUnavailable(ex.Message);
            }
        }

        // Fallback: solo JSON locale
        var allQuotes = await _localStore.LoadHistoryAsync();
        var filtered = string.IsNullOrWhiteSpace(searchText)
            ? allQuotes
            : allQuotes.Where(q =>
                q.QuoteNumber.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                q.CustomerName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                q.ReferenceName.Contains(searchText, StringComparison.OrdinalIgnoreCase));

        return filtered.OrderByDescending(q => q.Date)
            .Take(take)
            .Select(q => new QuoteHistorySummary
            {
                QuoteNumber = q.QuoteNumber,
                Date = q.Date,
                CustomerName = q.CustomerName,
                ReferenceName = q.ReferenceName,
                PdfPath = q.PdfPath,
                Total = (decimal)q.Total,
                Status = q.Status,
                Notes = q.Notes,
                SyncStatus = SyncStatus.LocalOnly
            })
            .ToList();
    }

    public async Task<QuoteHistoryEntry?> GetQuoteByNumberAsync(string quoteNumber)
    {
        if (IsDatabaseAvailable())
        {
            try { return await _sqlService.GetQuoteByNumberAsync(quoteNumber); }
            catch(Exception ex) { SetDatabaseUnavailable(ex.Message); }
        }
        return await _localStore.GetQuoteByNumberAsync(quoteNumber);
    }
    
    /// <summary>
    /// Crea una copia dell'entry senza i contenuti binari (PDF e allegati),
    /// adatta per la serializzazione nel JSON locale.
    /// </summary>
    /// 
    private static QuoteHistoryEntry CreateLightEntry(QuoteHistoryEntry entry)
    {
        return new QuoteHistoryEntry
        {
            QuoteNumber = entry.QuoteNumber,
            Date = entry.Date,
            CustomerName = entry.CustomerName,
            ReferenceName = entry.ReferenceName,
            PdfPath = entry.PdfPath,
            PaymentTerms = entry.PaymentTerms,
            IvaType = entry.IvaType,
            Notes = entry.Notes,
            Imponibile = entry.Imponibile,
            MaterialDiscount = entry.MaterialDiscount,
            LaborDiscount = entry.LaborDiscount,
            Total = entry.Total,
            Status = entry.Status,
            LastModifiedUtc = entry.LastModifiedUtc,
            SyncHash = entry.SyncHash,
            IsJointVenture = entry.IsJointVenture,
            PartnerCompanyName = entry.PartnerCompanyName,
            OurCosts = entry.OurCosts,
            PartnerCosts = entry.PartnerCosts,
            AdditionalCosts = entry.AdditionalCosts,
            Materials = entry.Materials,
            Labors = entry.Labors,
            PdfFile = entry.PdfFile == null ? null : new StoredFile
            {
                FileName = entry.PdfFile.FileName,
                ContentType = entry.PdfFile.ContentType,
                Content = [],   // nessun byte nel JSON locale
                ImportedAt = entry.PdfFile.ImportedAt
            },
            Attachments = entry.Attachments.Select(a => new StoredFile
            {
                FileName = a.FileName,
                ContentType = a.ContentType,
                Content = [],   // nessun byte nel JSON locale
                ImportedAt = a.ImportedAt
            }).ToList()
        };
    }
    public async Task SaveQuoteAsync(QuoteHistoryEntry quote)
    {
        quote.LastModifiedUtc = DateTime.UtcNow;
        
        var lightEntry = CreateLightEntry(quote);
        lightEntry.SyncHash = QuoteSyncHashService.Compute(lightEntry);
        quote.SyncHash = lightEntry.SyncHash;

        await _localStore.SaveOrUpdateQuoteAsync(lightEntry);

        if (IsDatabaseAvailable())
        {
            try { await _sqlService.SaveQuoteAsync(quote); }
            catch (Exception ex)
            {
                SetDatabaseUnavailable(ex.Message);
                Debug.WriteLine($"[FallbackDataService] Could not save to DB: {ex.Message}");
            }
        }

        // Invalida le cache dopo ogni salvataggio
        InvalidateQuoteNumbersCaches();
    }

    public async Task DeleteQuoteAsync(string quoteNumber)
    {
        await _localStore.DeleteQuoteAsync(quoteNumber);

        if (IsDatabaseAvailable())
        {
            try { await _sqlService.DeleteQuoteAsync(quoteNumber); }
            catch(Exception ex) { SetDatabaseUnavailable(ex.Message); }
        }

        // Invalida le cache dopo ogni eliminazione
        InvalidateQuoteNumbersCaches();
    }

    public Task<HashSet<string>> GetAllQuoteNumbersAsync() => _sqlService.GetAllQuoteNumbersAsync();

    #endregion

    #region Customers

    public async Task<List<Customer>> GetCustomersAsync()
    {
        if (IsDatabaseAvailable())
        {
            try { return await _sqlService.GetCustomersAsync(); }
            catch(Exception ex) { SetDatabaseUnavailable(ex.Message); }
        }
        return await _localStore.LoadCustomersAsync();
    }

    public async Task<Customer> AddCustomerAsync(Customer customer)
    {
        customer.LastModifiedUtc = DateTime.UtcNow;
        await _localStore.SaveOrUpdateCustomerAsync(customer);

        if (IsDatabaseAvailable())
        {
            try
            {
                return await _sqlService.AddCustomerAsync(customer);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FallbackDataService] ❌ AddCustomerAsync DB FAILED: {ex.Message}");
                Debug.WriteLine($"[FallbackDataService] StackTrace: {ex.StackTrace}");
                SetDatabaseUnavailable(ex.Message);
                // Non blocca — il cliente è già salvato nel JSON locale
            }
        }

        return customer;
    }
    
    public async Task DeleteCustomerAsync(string businessName)
    {
        await _localStore.DeleteCustomerAsync(businessName);

        if (IsDatabaseAvailable())
        {
            try { await _sqlService.DeleteCustomerAsync(businessName); }
            catch (Exception ex) { SetDatabaseUnavailable(ex.Message); }
        }
    }

    #endregion

    #region Other methods - delegate to SQL

    public Task<Company?> GetCompanyAsync() => _sqlService.GetCompanyAsync();
    public Task SaveCompanyAsync(Company company, string selectedLogo) =>
        _sqlService.SaveCompanyAsync(company, selectedLogo);
    public Task<List<Item>> GetLaborCatalogAsync() => _sqlService.GetLaborCatalogAsync();
    public Task SaveLaborCatalogAsync(IEnumerable<Item> labors) =>
        _sqlService.SaveLaborCatalogAsync(labors);
    public Task<List<Item>> GetPersonalMaterialsAsync() => _sqlService.GetPersonalMaterialsAsync();
    public Task SavePersonalMaterialsAsync(IEnumerable<Item> materials) =>
        _sqlService.SavePersonalMaterialsAsync(materials);
    public Task<int> GetNextQuoteNumberAsync() => _sqlService.GetNextQuoteNumberAsync();
    public Task<bool> IsDatabaseEmptyAsync() => _sqlService.IsDatabaseEmptyAsync();
    public async Task<byte[]?> GetQuotePdfContentAsync(string quoteNumber)
    {
        if (IsDatabaseAvailable())
        {
            try { return await _sqlService.GetQuotePdfContentAsync(quoteNumber); }
            catch (Exception ex) { SetDatabaseUnavailable(ex.Message); }
        }

        return null;
    }

    public async Task<List<StoredFile>> GetQuoteAttachmentsAsync(string quoteNumber)
    {
        if (IsDatabaseAvailable())
        {
            try { return await _sqlService.GetQuoteAttachmentsAsync(quoteNumber); }
            catch (Exception ex) { SetDatabaseUnavailable(ex.Message); }
        }

        return [];
    }

    public async Task<Dictionary<string, QuoteMetadata>> GetQuoteMetadataAsync()
    {
        if (IsDatabaseAvailable())
        {
            try
            {
                return await _sqlService.GetQuoteMetadataAsync();
            }
            catch( Exception ex)
            {
                SetDatabaseUnavailable(ex.Message);
            }
        }

        // Fallback: carica dal JSON locale e costruisci i metadata
        var allQuotes = await _localStore.LoadHistoryAsync();
    
        return allQuotes
            .GroupBy(q => q.QuoteNumber, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => new QuoteMetadata
                {
                    QuoteNumber = g.Key,
                    LastModifiedUtc = g.First().LastModifiedUtc,
                    SyncHash = g.First().SyncHash
                },
                StringComparer.OrdinalIgnoreCase);
    }
    
    public async Task<List<QuoteHistoryEntry>> GetQuotesByNumbersAsync(IEnumerable<string> quoteNumbers)
    {
        if (IsDatabaseAvailable())
        {
            try
            {
                return await _sqlService.GetQuotesByNumbersAsync(quoteNumbers);
            }
            catch( Exception ex)
            {
                SetDatabaseUnavailable(ex.Message);
            }
        }

        // Fallback: carica dal JSON locale e filtra
        var allQuotes = await _localStore.LoadHistoryAsync();
        var numberSet = new HashSet<string>(quoteNumbers, StringComparer.OrdinalIgnoreCase);
    
        return allQuotes
            .Where(q => numberSet.Contains(q.QuoteNumber))
            .ToList();
    }

    #endregion
    
}
