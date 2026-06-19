using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using EdilPaintPreventibiviGen.Models;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace EdilPaintPreventibiviGen.Services;

/// <summary>
/// DataService con fallback automatico su JSON locale se il database non è disponibile
/// </summary>
public class FallbackDataService : IDataService
{
    private readonly SqlDataService _sqlService;
    private readonly LocalJsonStoreService _localStore;
    private readonly LocalQuotePatchOutboxService _quotePatchOutbox;
    private readonly LocalDeletionOutboxService _deletionOutbox;
    private bool _isDatabaseAvailable = true;
    private DateTime _databaseUnavailableSince = DateTime.MinValue;
    private static readonly TimeSpan DbRetryCooldown = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DbConnectionAttemptTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan DbStartupWakeupTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan DbInteractiveWakeupTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan DbWakeupRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DbSchemaInitializationTimeout = TimeSpan.FromSeconds(60);
    private const int DbSaveRetryCount = 2;


    // Cache dei numeri preventivo presenti nel DB (query leggera, una sola volta ogni 10 min)
    private HashSet<string>? _dbQuoteNumbersCache;
    private DateTime _dbQuoteNumbersCacheTime = DateTime.MinValue;

    // Cache dei metadati preventivo usati per capire se il pallino sync e' davvero verde/rosso.
    private Dictionary<string, QuoteMetadata>? _dbQuoteMetadataCache;
    private DateTime _dbQuoteMetadataCacheTime = DateTime.MinValue;

    // Cache dei numeri preventivo presenti nel JSON locale (una sola lettura ogni 10 min)
    private HashSet<string>? _localQuoteNumbersCache;
    private DateTime _localQuoteNumbersCacheTime = DateTime.MinValue;

    private Dictionary<string, QuoteMetadata>? _localQuoteMetadataCache;
    private DateTime _localQuoteMetadataCacheTime = DateTime.MinValue;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    public FallbackDataService(
        SqlDataService sqlService,
        LocalJsonStoreService localStore,
        LocalQuotePatchOutboxService quotePatchOutbox,
        LocalDeletionOutboxService deletionOutbox)
    {
        _sqlService = sqlService;
        _localStore = localStore;
        _quotePatchOutbox = quotePatchOutbox;
        _deletionOutbox = deletionOutbox;
    }

    public bool CanSynchronize => IsDatabaseAvailable();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Debug.WriteLine("[FallbackDataService] InitializeAsync starting...");
        try
        {
            await EnsureDatabaseReachableAsync(cancellationToken);
            await InitializeDatabaseSchemaAsync(cancellationToken);
            
            _isDatabaseAvailable = true;
            Debug.WriteLine("[FallbackDataService] ✅ Database initialized successfully");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            SetDatabaseUnavailable(
                $"Timeout inizializzazione SQL. Risveglio: {DbStartupWakeupTimeout.TotalSeconds:F0}s; schema: {DbSchemaInitializationTimeout.TotalSeconds:F0}s.");
            Debug.WriteLine("[FallbackDataService] Database initialization timed out. Using local fallback.");
        }
        catch (TimeoutException ex)
        {
            HandleDatabaseException("InitializeAsync", ex);
            Debug.WriteLine($"[FallbackDataService] Database wake-up timeout: {ex.Message}");
        }
        catch (Exception ex)
        {
            HandleDatabaseException("InitializeAsync", ex);
            Debug.WriteLine($"[FallbackDataService] ❌ Database initialization FAILED: {ex.Message}");
            SetDatabaseUnavailable($"InitializeAsync: {ex.GetBaseException().Message}");
            Debug.WriteLine($"[FallbackDataService] StackTrace: {ex.StackTrace}");
            Debug.WriteLine("[FallbackDataService] ⚠️ Will use local JSON fallback");
        }
    }

    private async Task EnsureDatabaseReachableAsync(CancellationToken cancellationToken)
    {
        await EnsureDatabaseReachableAsync(DbStartupWakeupTimeout, cancellationToken);
    }

    private async Task EnsureDatabaseReachableAsync(TimeSpan wakeupTimeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + wakeupTimeout;
        Exception? lastError = null;
        int attempt = 0;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;

            using var attemptCts = new CancellationTokenSource(DbConnectionAttemptTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, attemptCts.Token);

            try
            {
                Debug.WriteLine($"[FallbackDataService] Tentativo connessione SQL #{attempt}...");
                if (await _sqlService.CanConnectAsync(linkedCts.Token))
                {
                    Debug.WriteLine($"[FallbackDataService] SQL raggiungibile al tentativo #{attempt}.");
                    return;
                }

                lastError = new InvalidOperationException("Database SQL non raggiungibile.");
                Debug.WriteLine($"[FallbackDataService] Tentativo SQL #{attempt} fallito: CanConnect=false.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = new TimeoutException($"Tentativo SQL #{attempt} scaduto dopo {DbConnectionAttemptTimeout.TotalSeconds:F0}s.");
                Debug.WriteLine($"[FallbackDataService] {lastError.Message}");
            }
            catch (Exception ex)
            {
                lastError = ex;
                Debug.WriteLine($"[FallbackDataService] Tentativo SQL #{attempt} fallito: {ex.Message}");
            }

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;

            var delay = remaining < DbWakeupRetryDelay ? remaining : DbWakeupRetryDelay;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
        }

        throw new TimeoutException(
            $"Database SQL non disponibile dopo {wakeupTimeout.TotalSeconds:F0}s di attesa. Ultimo errore: {lastError?.Message ?? "nessun dettaglio"}");
    }

    private async Task InitializeDatabaseSchemaAsync(CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(DbSchemaInitializationTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        await _sqlService.InitializeAsync(linkedCts.Token);
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
                    HandleDatabaseException("GetDbQuoteNumbersCachedAsync", ex);
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

    private async Task<Dictionary<string, QuoteMetadata>> GetDbQuoteMetadataCachedAsync(CancellationToken cancellationToken)
    {
        if (_dbQuoteMetadataCache == null || DateTime.UtcNow - _dbQuoteMetadataCacheTime > CacheDuration)
        {
            if (!IsDatabaseAvailable())
                return new Dictionary<string, QuoteMetadata>(StringComparer.OrdinalIgnoreCase);

            try
            {
                _dbQuoteMetadataCache = await _sqlService.GetQuoteMetadataAsync(cancellationToken);
                _dbQuoteMetadataCacheTime = DateTime.UtcNow;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FallbackDataService] DB metadata cache unavailable: {ex.Message}");
                _dbQuoteMetadataCache = new Dictionary<string, QuoteMetadata>(StringComparer.OrdinalIgnoreCase);
                _dbQuoteMetadataCacheTime = DateTime.UtcNow;
            }
        }

        return _dbQuoteMetadataCache;
    }

    private async Task<Dictionary<string, QuoteMetadata>> GetLocalQuoteMetadataCachedAsync(CancellationToken cancellationToken)
    {
        if (_localQuoteMetadataCache == null || DateTime.UtcNow - _localQuoteMetadataCacheTime > CacheDuration)
        {
            var localEntries = await _localStore.LoadHistoryAsync(cancellationToken);
            _localQuoteMetadataCache = localEntries
                .GroupBy(q => q.QuoteNumber, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(q => q.LastModifiedUtc).First())
                .ToDictionary(
                    q => q.QuoteNumber,
                    q => new QuoteMetadata
                    {
                        QuoteNumber = q.QuoteNumber,
                        LastModifiedUtc = q.LastModifiedUtc,
                        SyncHash = q.SyncHash
                    },
                    StringComparer.OrdinalIgnoreCase);
            _localQuoteMetadataCacheTime = DateTime.UtcNow;
        }

        return _localQuoteMetadataCache;
    }

    
    /// <summary>
    /// Invalida entrambe le cache (chiamare dopo SaveQuoteAsync / DeleteQuoteAsync).
    /// </summary>
    internal void InvalidateQuoteNumbersCaches()
    {
        _dbQuoteNumbersCache = null;
        _localQuoteNumbersCache = null;
        _dbQuoteMetadataCache = null;
        _localQuoteMetadataCache = null;
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

    private static SyncStatus ResolveSyncStatus(
        string quoteNumber,
        IReadOnlyDictionary<string, QuoteMetadata> dbMetadata,
        IReadOnlyDictionary<string, QuoteMetadata> localMetadata)
    {
        bool inDb = dbMetadata.TryGetValue(quoteNumber, out var dbQuote);
        bool inLocal = localMetadata.TryGetValue(quoteNumber, out var localQuote);

        return (inDb, inLocal) switch
        {
            (true, false) => SyncStatus.OnlineOnly,
            (false, true) => SyncStatus.LocalOnly,
            (true, true) when string.Equals(dbQuote!.SyncHash, localQuote!.SyncHash, StringComparison.Ordinal) =>
                SyncStatus.Synced,
            (true, true) when Math.Abs((localQuote!.LastModifiedUtc - dbQuote!.LastModifiedUtc).TotalSeconds) <= 2 =>
                SyncStatus.Synced,
            (true, true) when localQuote!.LastModifiedUtc > dbQuote!.LastModifiedUtc =>
                SyncStatus.LocalOnly,
            (true, true) => SyncStatus.OnlineOnly,
            _ => SyncStatus.LocalOnly
        };
    }

    private static void EnsureDbMetadataForDisplayedSummaries(
        IEnumerable<QuoteHistorySummary> summaries,
        IDictionary<string, QuoteMetadata> dbMetadata)
    {
        foreach (var summary in summaries)
        {
            if (dbMetadata.ContainsKey(summary.QuoteNumber))
                continue;

            dbMetadata[summary.QuoteNumber] = new QuoteMetadata
            {
                QuoteNumber = summary.QuoteNumber,
                LastModifiedUtc = DateTime.MaxValue,
                SyncHash = string.Empty
            };
        }
    }

    private async Task ApplySyncStatusAsync(
        IEnumerable<QuoteHistorySummary> summaries,
        CancellationToken cancellationToken)
    {
        try
        {
            var summaryList = summaries as IList<QuoteHistorySummary> ?? summaries.ToList();
            var dbMetadata = await GetDbQuoteMetadataCachedAsync(cancellationToken);
            var localMetadata = await GetLocalQuoteMetadataCachedAsync(cancellationToken);
            EnsureDbMetadataForDisplayedSummaries(summaryList, dbMetadata);

            foreach (var q in summaryList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                q.SyncStatus = ResolveSyncStatus(q.QuoteNumber, dbMetadata, localMetadata);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FallbackDataService] Sync status metadata unavailable: {ex.Message}");

            foreach (var q in summaries)
                q.SyncStatus = SyncStatus.OnlineOnly;
        }
    }
    private void SetDatabaseUnavailable(string reason)
    {
        if (IsDatabaseAvailable())
        {
            Debug.WriteLine($"[FallbackDataService] ⚠️⚠️⚠️ DATABASE MARKED AS UNAVAILABLE!");
            Debug.WriteLine($"[FallbackDataService] Reason: {reason}");
            WriteDatabaseLog($"DATABASE NON DISPONIBILE: {reason}");
            _isDatabaseAvailable = false;
            _databaseUnavailableSince = DateTime.UtcNow;
        }
    }

    private void MarkDatabaseAvailable(string reason)
    {
        bool wasUnavailable = !_isDatabaseAvailable;
        _isDatabaseAvailable = true;
        _databaseUnavailableSince = DateTime.MinValue;

        if (wasUnavailable)
            WriteDatabaseLog($"DATABASE DISPONIBILE: {reason}");
    }

    private async Task<bool> TryEnsureDatabaseAvailableAsync(
        string operation,
        TimeSpan wakeupTimeout,
        CancellationToken cancellationToken)
    {
        try
        {
            await EnsureDatabaseReachableAsync(wakeupTimeout, cancellationToken);
            MarkDatabaseAvailable(operation);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            HandleDatabaseException(operation, ex);
            return false;
        }
    }

    private void HandleDatabaseException(string operation, Exception ex)
    {
        WriteDatabaseLog($"{operation}: {BuildExceptionDetails(ex)}");

        if (IsDatabaseConnectivityException(ex))
        {
            SetDatabaseUnavailable($"{operation}: {ex.Message}");
            return;
        }

        Debug.WriteLine($"[FallbackDataService] Errore SQL non di connessione ({operation}): {ex.Message}");
    }

    private static string BuildExceptionDetails(Exception ex)
    {
        var parts = new List<string>();
        for (Exception? current = ex; current != null; current = current.InnerException)
            parts.Add($"{current.GetType().Name}: {current.Message}");

        return string.Join(" -> ", parts);
    }

    private static Exception CreateDatabaseRejectedException(string operation, string itemName, Exception ex)
    {
        string detail = ex.GetBaseException().Message;
        return new InvalidOperationException(
            $"{operation} '{itemName}' non salvato nel database. Il database ha risposto, ma ha rifiutato il salvataggio.\n\nDettaglio SQL: {detail}",
            ex);
    }

    private static bool IsDatabaseConnectivityException(Exception ex)
    {
        if (ex is TimeoutException)
            return true;

        if (ex is SqlException sqlException)
        {
            foreach (SqlError error in sqlException.Errors)
            {
                if (IsTransientSqlError(error.Number))
                    return true;
            }
        }

        if (ex is NpgsqlException npgsqlException)
            return npgsqlException.IsTransient;

        return ex.InnerException != null && IsDatabaseConnectivityException(ex.InnerException);
    }

    private static bool IsTransientSqlError(int number)
    {
        return number is
            -2 or 20 or 64 or 233 or 258 or
            10053 or 10054 or 10060 or 11001 or
            40143 or 40197 or 40501 or 40613 or
            49918 or 49919 or 49920 or
            10928 or 10929;
    }

    private static void WriteDatabaseLog(string message)
    {
        try
        {
            string logDirectory = ResolveDatabaseLogDirectory();
            Directory.CreateDirectory(logDirectory);
            string logPath = Path.Combine(logDirectory, $"database-{DateTime.Now:yyyyMMdd}.log");
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            File.AppendAllText(logPath, line + Environment.NewLine);
            Debug.WriteLine("[DB] " + message);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DB] Impossibile scrivere il log database: {ex.Message}");
        }
    }

    private static string ResolveDatabaseLogDirectory()
    {
        string appLogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DatabaseLogs");
        if (CanWriteToDirectory(appLogDirectory))
            return appLogDirectory;

        return Path.Combine(LocalApplicationDataService.GetDataDirectoryPath(), "DatabaseLogs");
    }

    private static bool CanWriteToDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            string testPath = Path.Combine(directory, ".writetest");
            File.WriteAllText(testPath, "test");
            File.Delete(testPath);
            return true;
        }
        catch
        {
            return false;
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

    private static void EnsureDeviceMetadata(QuoteHistoryEntry quote)
    {
        string deviceName = DeviceNameService.GetCurrentDeviceName();

        if (string.IsNullOrWhiteSpace(quote.CreatedByDevice))
        {
            quote.CreatedByDevice = deviceName;
            quote.Events.Add(new QuoteEventEntry
            {
                CreatedAtUtc = quote.LastModifiedUtc == default ? DateTime.UtcNow : quote.LastModifiedUtc,
                DeviceName = deviceName,
                EventType = "creazione",
                Description = "Preventivo creato"
            });
        }

        quote.LastModifiedByDevice = deviceName;
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
                HandleDatabaseException("GetQuotesAsync", ex);
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
            catch(Exception ex) { HandleDatabaseException("GetQuotesAsync(take)", ex); }
        }

        var all = await _localStore.LoadHistoryAsync();
        return all.OrderByDescending(q => q.Date).Take(take).ToList();
    }

    public async Task<List<QuoteHistorySummary>> GetQuoteSummariesAsync(
        int take,
        CancellationToken cancellationToken = default)
{
    Debug.WriteLine("\n[FallbackDataService] ═══ GetQuoteSummariesAsync START ═══");
    bool databaseAvailable = IsDatabaseAvailable();
    Debug.WriteLine($"[FallbackDataService] Database available: {databaseAvailable}");

    cancellationToken.ThrowIfCancellationRequested();

    if (!databaseAvailable)
        databaseAvailable = await TryEnsureDatabaseAvailableAsync(
            "Caricamento storico",
            DbInteractiveWakeupTimeout,
            cancellationToken);

    if (databaseAvailable)
    {
        try
        {
            // 1 query DB per i summary visualizzati
            Debug.WriteLine("[FallbackDataService] 🗄️ Fetching summaries from DB...");
            var dbQuotes = await _sqlService.GetQuoteSummariesAsync(take, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Debug.WriteLine($"[FallbackDataService] 🗄️ DB returned {dbQuotes.Count} summaries");

            // 1 query DB per tutti i numeri (cachata) + 1 lettura JSON (cachata)
            Debug.WriteLine("[FallbackDataService] 🔑 Getting cached quote numbers...");
            var dbMetadata = await GetDbQuoteMetadataCachedAsync(cancellationToken);
            var localMetadata = await GetLocalQuoteMetadataCachedAsync(cancellationToken);
            EnsureDbMetadataForDisplayedSummaries(dbQuotes, dbMetadata);
            var dbAllNumbers = new HashSet<string>(dbMetadata.Keys, StringComparer.OrdinalIgnoreCase);
            var localNumbers = new HashSet<string>(localMetadata.Keys, StringComparer.OrdinalIgnoreCase);

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
                cancellationToken.ThrowIfCancellationRequested();
                var oldStatus = q.SyncStatus;
                q.SyncStatus = ResolveSyncStatus(q.QuoteNumber, dbMetadata, localMetadata);
                
                if (q.SyncStatus != SyncStatus.Synced)
                {
                    Debug.WriteLine($"[FallbackDataService] ⚠️ Quote {q.QuoteNumber}: Status = {q.SyncStatus} (inDB={dbAllNumbers.Contains(q.QuoteNumber)}, inLocal={localNumbers.Contains(q.QuoteNumber)})");
                }
            }

            Debug.WriteLine("[FallbackDataService] ═══ GetQuoteSummariesAsync END ═══\n");
            return dbQuotes;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FallbackDataService] ❌ Error: {ex.Message}");
            HandleDatabaseException("GetQuoteSummariesAsync", ex);
        }
    }

    // Fallback: solo JSON locale
    Debug.WriteLine("[FallbackDataService] ⚠️ Using JSON fallback (DB unavailable)");
    var quotes = await _localStore.LoadHistoryAsync();
    cancellationToken.ThrowIfCancellationRequested();
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
            IvaType = q.IvaType,
            MaterialDiscount = q.MaterialDiscount,
            LaborDiscount = q.LaborDiscount,
            Status = q.Status,
            Notes = q.Notes,
            IsJointVenture = q.IsJointVenture,
            PartnerCompanyName = q.PartnerCompanyName,
            CreatedByDevice = q.CreatedByDevice,
            LastModifiedByDevice = q.LastModifiedByDevice,
            SentAtUtc = q.SentAtUtc,
            SentMethod = q.SentMethod,
            SentRecipient = q.SentRecipient,
            SentByDevice = q.SentByDevice,
            LastReminderAtUtc = q.LastReminderAtUtc,
            ReminderCount = q.ReminderCount,
            LastReminderByDevice = q.LastReminderByDevice,
            SyncStatus = SyncStatus.LocalOnly
        })
        .ToList();
}

    public async Task<List<QuoteHistorySummary>> GetSentOpenQuoteSummariesAsync(
        DateTime sinceUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        bool databaseAvailable = IsDatabaseAvailable();
        if (!databaseAvailable)
            databaseAvailable = await TryEnsureDatabaseAvailableAsync(
                "Preventivi inviati aperti",
                DbInteractiveWakeupTimeout,
                cancellationToken);

        if (databaseAvailable)
        {
            try
            {
                var dbQuotes = await _sqlService.GetSentOpenQuoteSummariesAsync(sinceUtc, cancellationToken);
                var dbMetadata = await GetDbQuoteMetadataCachedAsync(cancellationToken);
                var localMetadata = await GetLocalQuoteMetadataCachedAsync(cancellationToken);
                EnsureDbMetadataForDisplayedSummaries(dbQuotes, dbMetadata);

                foreach (var q in dbQuotes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    q.SyncStatus = ResolveSyncStatus(q.QuoteNumber, dbMetadata, localMetadata);
                }

                return dbQuotes;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                HandleDatabaseException("GetSentOpenQuoteSummariesAsync", ex);
            }
        }

        var localQuotes = await _localStore.LoadHistoryAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return localQuotes
            .Where(q => QuoteHistoryService.IsSentOpenWithin(q.SentAtUtc, q.Status, sinceUtc))
            .OrderByDescending(q => q.SentAtUtc)
            .ThenByDescending(q => q.Date)
            .Select(q => new QuoteHistorySummary
            {
                QuoteNumber = q.QuoteNumber,
                Date = q.Date,
                CustomerName = q.CustomerName,
                ReferenceName = q.ReferenceName,
                PdfPath = q.PdfPath,
                Total = (decimal)q.Total,
                IvaType = q.IvaType,
                MaterialDiscount = q.MaterialDiscount,
                LaborDiscount = q.LaborDiscount,
                Status = q.Status,
                Notes = q.Notes,
                IsJointVenture = q.IsJointVenture,
                PartnerCompanyName = q.PartnerCompanyName,
                CreatedByDevice = q.CreatedByDevice,
                LastModifiedByDevice = q.LastModifiedByDevice,
                SentAtUtc = q.SentAtUtc,
                SentMethod = q.SentMethod,
                SentRecipient = q.SentRecipient,
                SentByDevice = q.SentByDevice,
                LastReminderAtUtc = q.LastReminderAtUtc,
                ReminderCount = q.ReminderCount,
                LastReminderByDevice = q.LastReminderByDevice,
                SyncStatus = SyncStatus.LocalOnly
            })
            .ToList();
    }

    public async Task<List<QuoteHistorySummary>> SearchQuoteSummariesAsync(
        string searchText,
        int take,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        bool databaseAvailable = IsDatabaseAvailable();
        if (!databaseAvailable)
            databaseAvailable = await TryEnsureDatabaseAvailableAsync(
                "Ricerca storico",
                DbInteractiveWakeupTimeout,
                cancellationToken);

        if (databaseAvailable)
        {
            try
            {
                // 1 query DB per i risultati di ricerca
                var dbQuotes = await _sqlService.SearchQuoteSummariesAsync(searchText, take, cancellationToken);

                // Riusa le cache — nessuna ulteriore lettura su disco o DB
                var dbMetadata = await GetDbQuoteMetadataCachedAsync(cancellationToken);
                var localMetadata = await GetLocalQuoteMetadataCachedAsync(cancellationToken);
                EnsureDbMetadataForDisplayedSummaries(dbQuotes, dbMetadata);

                foreach (var q in dbQuotes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    q.SyncStatus = ResolveSyncStatus(q.QuoteNumber, dbMetadata, localMetadata);
                }

                return dbQuotes;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch( Exception ex)
            {
                HandleDatabaseException("SearchQuoteSummariesAsync", ex);
            }
        }

        // Fallback: solo JSON locale
        var allQuotes = await _localStore.LoadHistoryAsync();
        cancellationToken.ThrowIfCancellationRequested();

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
                IvaType = q.IvaType,
                MaterialDiscount = q.MaterialDiscount,
                LaborDiscount = q.LaborDiscount,
                Status = q.Status,
                Notes = q.Notes,
                IsJointVenture = q.IsJointVenture,
                PartnerCompanyName = q.PartnerCompanyName,
                CreatedByDevice = q.CreatedByDevice,
                LastModifiedByDevice = q.LastModifiedByDevice,
                SentAtUtc = q.SentAtUtc,
                SentMethod = q.SentMethod,
                SentRecipient = q.SentRecipient,
                SentByDevice = q.SentByDevice,
                LastReminderAtUtc = q.LastReminderAtUtc,
                ReminderCount = q.ReminderCount,
                LastReminderByDevice = q.LastReminderByDevice,
                SyncStatus = SyncStatus.LocalOnly
            })
            .ToList();
    }

    public async Task<QuoteHistoryEntry?> GetQuoteByNumberAsync(string quoteNumber)
    {
        if (IsDatabaseAvailable())
        {
            try { return await _sqlService.GetQuoteByNumberAsync(quoteNumber); }
            catch(Exception ex) { HandleDatabaseException("GetQuoteByNumberAsync", ex); }
        }

        var localQuote = await _localStore.GetQuoteByNumberAsync(quoteNumber);
        if (localQuote == null)
            return null;

        return localQuote;
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
            CreatedByDevice = entry.CreatedByDevice,
            LastModifiedByDevice = entry.LastModifiedByDevice,
            SentAtUtc = entry.SentAtUtc,
            SentMethod = entry.SentMethod,
            SentRecipient = entry.SentRecipient,
            SentByDevice = entry.SentByDevice,
            LastReminderAtUtc = entry.LastReminderAtUtc,
            ReminderCount = entry.ReminderCount,
            LastReminderByDevice = entry.LastReminderByDevice,
            Events = entry.Events.ToList(),
            LastModifiedUtc = entry.LastModifiedUtc,
            BaseVersionUtc = entry.BaseVersionUtc,
            IsEditingExistingQuoteDraft = entry.IsEditingExistingQuoteDraft,
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
    public async Task SaveQuoteAsync(QuoteHistoryEntry quote, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        quote.LastModifiedUtc = DateTime.UtcNow;
        EnsureDeviceMetadata(quote);

        var lightEntry = CreateLightEntry(quote);
        lightEntry.SyncHash = QuoteSyncHashService.Compute(lightEntry);
        quote.SyncHash = lightEntry.SyncHash;

        await _localStore.SaveOrUpdateQuoteAsync(lightEntry);

        try
        {
            bool savedOnline = await SaveQuoteOnlineWithRetryAsync(lightEntry, cancellationToken);
            if (savedOnline)
            {
                await _localStore.SaveOrUpdateQuoteAsync(lightEntry);
                quote.LastModifiedUtc = lightEntry.LastModifiedUtc;
                quote.BaseVersionUtc = lightEntry.BaseVersionUtc;
                quote.SyncHash = lightEntry.SyncHash;
            }
        }
        catch (QuoteConflictException)
        {
            await _localStore.ArchiveQuoteConflictAsync(
                quote,
                "Salvataggio completo rifiutato: il database contiene una versione piu' recente.",
                cancellationToken);
            var databaseVersion = await _sqlService.GetQuoteByNumberAsync(quote.QuoteNumber);
            if (databaseVersion != null)
                await _localStore.BulkUpdateQuotesAsync([databaseVersion], cancellationToken);

            throw;
        }

        // Invalida le cache dopo ogni salvataggio
        InvalidateQuoteNumbersCaches();
    }

    private async Task<bool> SaveQuoteOnlineWithRetryAsync(
        QuoteHistoryEntry lightEntry,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;

        for (int attempt = 1; attempt <= DbSaveRetryCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await EnsureDatabaseReachableAsync(DbInteractiveWakeupTimeout, cancellationToken);
                await _sqlService.SaveQuoteAsync(lightEntry, cancellationToken);
                MarkDatabaseAvailable($"Preventivo {lightEntry.QuoteNumber} salvato online.");
                return true;
            }
            catch (QuoteConflictException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                WriteDatabaseLog($"SaveQuoteAsync tentativo {attempt}/{DbSaveRetryCount} per {lightEntry.QuoteNumber}: {ex.GetType().Name}: {ex.Message}");

                if (!IsDatabaseConnectivityException(ex))
                {
                    Debug.WriteLine($"[FallbackDataService] Salvataggio SQL non riuscito ma DB non marcato offline: {ex.Message}");
                    return false;
                }

                if (attempt < DbSaveRetryCount)
                    await Task.Delay(DbWakeupRetryDelay, cancellationToken);
            }
        }

        if (lastError != null)
            SetDatabaseUnavailable($"SaveQuoteAsync {lightEntry.QuoteNumber}: {lastError.Message}");

        return false;
    }

    public async Task DeleteQuoteAsync(string quoteNumber)
    {
        await _deletionOutbox.AddQuoteAsync(quoteNumber);
        await _localStore.DeleteQuoteAsync(quoteNumber);

        if (IsDatabaseAvailable())
        {
            try
            {
                await _sqlService.DeleteQuoteAsync(quoteNumber);
                await _deletionOutbox.RemoveQuoteAsync(quoteNumber);
            }
            catch(Exception ex) { HandleDatabaseException("DeleteQuoteAsync", ex); }
        }

        // Invalida le cache dopo ogni eliminazione
        InvalidateQuoteNumbersCaches();
    }

    public async Task UpdateQuoteNotesAsync(
        string quoteNumber,
        string notes,
        CancellationToken cancellationToken = default)
    {
        await _quotePatchOutbox.StoreNotesAsync(quoteNumber, notes, cancellationToken);
        await _localStore.UpdateQuoteNotesAsync(quoteNumber, notes);

        if (!IsDatabaseAvailable())
            return;

        try
        {
            await _sqlService.UpdateQuoteNotesAsync(quoteNumber, notes, cancellationToken);
            var databaseVersion = await _sqlService.GetQuoteByNumberAsync(quoteNumber);
            if (databaseVersion != null)
                await _localStore.BulkUpdateQuotesAsync([databaseVersion], cancellationToken);
            await _quotePatchOutbox.RemoveAppliedAsync(quoteNumber, patch => patch.Notes = null, cancellationToken);
        }
        catch (Exception ex)
        {
            HandleDatabaseException("UpdateQuoteNotesAsync", ex);
        }
    }

    public async Task UpdateQuoteStatusAsync(
        string quoteNumber,
        QuoteStatus status,
        CancellationToken cancellationToken = default)
    {
        await _quotePatchOutbox.StoreStatusAsync(quoteNumber, status, cancellationToken);
        await _localStore.UpdateQuoteStatusAsync(quoteNumber, status);

        if (!IsDatabaseAvailable())
            return;

        try
        {
            await _sqlService.UpdateQuoteStatusAsync(quoteNumber, status, cancellationToken);
            var databaseVersion = await _sqlService.GetQuoteByNumberAsync(quoteNumber);
            if (databaseVersion != null)
                await _localStore.BulkUpdateQuotesAsync([databaseVersion], cancellationToken);
            await _quotePatchOutbox.RemoveAppliedAsync(quoteNumber, patch => patch.Status = null, cancellationToken);
        }
        catch (Exception ex)
        {
            HandleDatabaseException("UpdateQuoteStatusAsync", ex);
        }
    }

    public async Task UpdateQuoteSendInfoAsync(
        string quoteNumber,
        QuoteSendInfo sendInfo,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sendInfo.DeviceName))
            sendInfo.DeviceName = DeviceNameService.GetCurrentDeviceName();
        if (sendInfo.SentAtUtc == default)
            sendInfo.SentAtUtc = DateTime.UtcNow;

        await _quotePatchOutbox.StoreSendInfoAsync(quoteNumber, sendInfo, cancellationToken);
        await _localStore.UpdateQuoteSendInfoAsync(quoteNumber, sendInfo);

        if (!IsDatabaseAvailable())
            return;

        try
        {
            await _sqlService.UpdateQuoteSendInfoAsync(quoteNumber, sendInfo, cancellationToken);
            var databaseVersion = await _sqlService.GetQuoteByNumberAsync(quoteNumber);
            if (databaseVersion != null)
                await _localStore.BulkUpdateQuotesAsync([databaseVersion], cancellationToken);
            await _quotePatchOutbox.RemoveAppliedAsync(quoteNumber, patch => patch.SendInfo = null, cancellationToken);
        }
        catch (Exception ex)
        {
            HandleDatabaseException("UpdateQuoteSendInfoAsync", ex);
        }
    }

    public async Task RegisterQuoteReminderAsync(
        string quoteNumber,
        QuoteReminderInfo reminderInfo,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reminderInfo.DeviceName))
            reminderInfo.DeviceName = DeviceNameService.GetCurrentDeviceName();
        if (reminderInfo.ReminderAtUtc == default)
            reminderInfo.ReminderAtUtc = DateTime.UtcNow;

        await _quotePatchOutbox.StoreReminderAsync(quoteNumber, reminderInfo, cancellationToken);
        await _localStore.RegisterQuoteReminderAsync(quoteNumber, reminderInfo);

        if (!IsDatabaseAvailable())
            return;

        try
        {
            await _sqlService.RegisterQuoteReminderAsync(quoteNumber, reminderInfo, cancellationToken);
            var databaseVersion = await _sqlService.GetQuoteByNumberAsync(quoteNumber);
            if (databaseVersion != null)
                await _localStore.BulkUpdateQuotesAsync([databaseVersion], cancellationToken);
            await _quotePatchOutbox.RemoveAppliedAsync(quoteNumber, patch => patch.ReminderInfo = null, cancellationToken);
        }
        catch (Exception ex)
        {
            HandleDatabaseException("RegisterQuoteReminderAsync", ex);
        }
    }

    public async Task<HashSet<string>> GetAllQuoteNumbersAsync()
    {
        if (IsDatabaseAvailable())
        {
            try { return await _sqlService.GetAllQuoteNumbersAsync(); }
            catch (Exception ex) { HandleDatabaseException("GetAllQuoteNumbersAsync", ex); }
        }

        return await GetLocalQuoteNumbersCachedAsync();
    }

    #endregion

    #region Customers

    public async Task<List<Customer>> GetCustomersAsync(CancellationToken cancellationToken = default)
    {
        if (IsDatabaseAvailable())
        {
            try { return await _sqlService.GetCustomersAsync(cancellationToken); }
            catch(Exception ex) { HandleDatabaseException("GetCustomersAsync", ex); }
        }
        return await _localStore.LoadCustomersAsync(cancellationToken);
    }

    public async Task<Customer> AddCustomerAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        NormalizeCustomerForSave(customer);

        if (customer.SyncId == Guid.Empty)
            customer.SyncId = Guid.NewGuid();

        customer.LastModifiedUtc = DateTime.UtcNow;
        bool databaseAvailable = IsDatabaseAvailable();
        if (!databaseAvailable)
            databaseAvailable = await TryEnsureDatabaseAvailableAsync(
                "Salvataggio cliente",
                DbInteractiveWakeupTimeout,
                cancellationToken);

        if (databaseAvailable)
        {
            try
            {
                var saved = await _sqlService.AddCustomerAsync(customer, cancellationToken);
                MarkDatabaseAvailable($"Cliente {saved.BusinessName} salvato online.");
                await _localStore.BulkUpdateCustomersAsync([saved], cancellationToken);
                return saved;
            }
            catch (Exception ex) when (IsDatabaseConnectivityException(ex))
            {
                Debug.WriteLine($"[FallbackDataService] AddCustomerAsync DB non raggiungibile: {ex.Message}");
                HandleDatabaseException("AddCustomerAsync", ex);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FallbackDataService] ❌ AddCustomerAsync DB FAILED: {ex.Message}");
                Debug.WriteLine($"[FallbackDataService] StackTrace: {ex.StackTrace}");
                HandleDatabaseException("AddCustomerAsync", ex);
                throw CreateDatabaseRejectedException("Cliente", customer.BusinessName, ex);
                // Non blocca — il cliente è già salvato nel JSON locale
            }
        }

        await _localStore.SaveOrUpdateCustomerAsync(customer);
        WriteDatabaseLog($"Cliente salvato solo localmente: {customer.BusinessName}");
        return customer;
    }

    public async Task<Customer> UpdateCustomerAsync(string originalBusinessName, Customer customer)
    {
        NormalizeCustomerForSave(customer);

        customer.LastModifiedUtc = DateTime.UtcNow;
        bool databaseAvailable = IsDatabaseAvailable();
        if (!databaseAvailable)
            databaseAvailable = await TryEnsureDatabaseAvailableAsync(
                "Aggiornamento cliente",
                DbInteractiveWakeupTimeout,
                CancellationToken.None);

        if (databaseAvailable)
        {
            try
            {
                var saved = await _sqlService.UpdateCustomerAsync(originalBusinessName, customer);
                MarkDatabaseAvailable($"Cliente {saved.BusinessName} aggiornato online.");
                await _localStore.BulkUpdateCustomersAsync([saved]);
                return saved;
            }
            catch (Exception ex) when (IsDatabaseConnectivityException(ex))
            {
                Debug.WriteLine($"[FallbackDataService] UpdateCustomerAsync DB non raggiungibile: {ex.Message}");
                HandleDatabaseException("UpdateCustomerAsync", ex);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FallbackDataService] UpdateCustomerAsync DB FAILED: {ex.Message}");
                HandleDatabaseException("UpdateCustomerAsync", ex);
                throw CreateDatabaseRejectedException("Cliente", customer.BusinessName, ex);
            }
        }

        await _localStore.UpdateCustomerAsync(originalBusinessName, customer);
        WriteDatabaseLog($"Cliente aggiornato solo localmente: {customer.BusinessName}");
        return customer;
    }

    private static void NormalizeCustomerForSave(Customer customer)
    {
        customer.BusinessName = (customer.BusinessName ?? string.Empty).Trim();
        customer.Address = customer.Address?.Trim() ?? string.Empty;
        customer.Email = customer.Email?.Trim() ?? string.Empty;
        customer.Phone = customer.Phone?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(customer.BusinessName))
            throw new InvalidOperationException("Impossibile salvare un cliente senza ragione sociale.");
    }
    
    public async Task DeleteCustomerAsync(Customer customer)
    {
        await _deletionOutbox.AddCustomerAsync(customer.SyncId, customer.BusinessName);
        await _localStore.DeleteCustomerAsync(customer);

        if (IsDatabaseAvailable())
        {
            try
            {
                await _sqlService.DeleteCustomerAsync(customer.SyncId, customer.BusinessName);
                await _deletionOutbox.RemoveCustomerAsync(customer.SyncId, customer.BusinessName);
            }
            catch (Exception ex) { HandleDatabaseException("DeleteCustomerAsync", ex); }
        }
    }

    #endregion

    #region Other methods - SQL with local fallback

    public async Task<Company?> GetCompanyAsync()
    {
        if (IsDatabaseAvailable())
        {
            try
            {
                var company = await _sqlService.GetCompanyAsync();
                if (company != null)
                {
                    string selectedLogo = company.Logo_index >= 0 && company.Logo_index < company.Logo.Count
                        ? System.IO.Path.GetFileName(company.Logo[company.Logo_index])
                        : string.Empty;

                    await _localStore.SaveCompanyAsync(company, selectedLogo);
                }

                return company;
            }
            catch (Exception ex) { HandleDatabaseException("GetCompanyAsync", ex); }
        }

        return await _localStore.LoadCompanyAsync();
    }

    public async Task SaveCompanyAsync(Company company, string selectedLogo)
    {
        await _localStore.SaveCompanyAsync(company, selectedLogo);

        if (IsDatabaseAvailable())
        {
            try { await _sqlService.SaveCompanyAsync(company, selectedLogo); }
            catch (Exception ex) { HandleDatabaseException("SaveCompanyAsync", ex); }
        }
    }

    public async Task<List<Item>> GetLaborCatalogAsync()
    {
        if (IsDatabaseAvailable())
        {
            try
            {
                var labors = await _sqlService.GetLaborCatalogAsync();
                await _localStore.SaveLaborCatalogAsync(labors);
                return labors;
            }
            catch (Exception ex) { HandleDatabaseException("GetLaborCatalogAsync", ex); }
        }

        return await _localStore.LoadLaborCatalogAsync();
    }

    public async Task SaveLaborCatalogAsync(IEnumerable<Item> labors)
    {
        var laborList = labors.ToList();
        await _localStore.SaveLaborCatalogAsync(laborList);

        if (IsDatabaseAvailable())
        {
            try { await _sqlService.SaveLaborCatalogAsync(laborList); }
            catch (Exception ex) { HandleDatabaseException("SaveLaborCatalogAsync", ex); }
        }
    }

    public async Task<List<Item>> GetPersonalMaterialsAsync()
    {
        if (IsDatabaseAvailable())
        {
            try
            {
                var materials = await _sqlService.GetPersonalMaterialsAsync();
                await _localStore.SavePersonalMaterialsAsync(materials);
                return materials;
            }
            catch (Exception ex) { HandleDatabaseException("GetPersonalMaterialsAsync", ex); }
        }

        return await _localStore.LoadPersonalMaterialsAsync();
    }

    public async Task SavePersonalMaterialsAsync(IEnumerable<Item> materials)
    {
        var materialList = materials.ToList();
        await _localStore.SavePersonalMaterialsAsync(materialList);

        if (IsDatabaseAvailable())
        {
            try { await _sqlService.SavePersonalMaterialsAsync(materialList); }
            catch (Exception ex) { HandleDatabaseException("SavePersonalMaterialsAsync", ex); }
        }
    }

    public async Task<int> GetNextQuoteNumberAsync()
    {
        if (IsDatabaseAvailable())
        {
            try { return await _sqlService.GetNextQuoteNumberAsync(); }
            catch (Exception ex) { HandleDatabaseException("GetNextQuoteNumberAsync", ex); }
        }

        throw new InvalidOperationException("Database non disponibile: impossibile assegnare un numero preventivo ufficiale. Riprova quando la connessione e' disponibile.");
    }

    public async Task<bool> IsDatabaseEmptyAsync()
    {
        if (IsDatabaseAvailable())
        {
            try { return await _sqlService.IsDatabaseEmptyAsync(); }
            catch (Exception ex) { HandleDatabaseException("IsDatabaseEmptyAsync", ex); }
        }

        var localHistory = await _localStore.LoadHistoryAsync();
        var localCustomers = await _localStore.LoadCustomersAsync();
        return localHistory.Count == 0 && localCustomers.Count == 0;
    }
    public async Task<Dictionary<string, QuoteMetadata>> GetQuoteMetadataAsync(CancellationToken cancellationToken = default)
    {
        if (IsDatabaseAvailable())
        {
            try
            {
                return await _sqlService.GetQuoteMetadataAsync(cancellationToken);
            }
            catch( Exception ex)
            {
                HandleDatabaseException("GetQuoteMetadataAsync", ex);
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
    
    public async Task<List<QuoteHistoryEntry>> GetQuotesByNumbersAsync(
        IEnumerable<string> quoteNumbers,
        CancellationToken cancellationToken = default)
    {
        if (IsDatabaseAvailable())
        {
            try
            {
                return await _sqlService.GetQuotesByNumbersAsync(quoteNumbers, cancellationToken);
            }
            catch( Exception ex)
            {
                HandleDatabaseException("GetQuotesByNumbersAsync", ex);
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
