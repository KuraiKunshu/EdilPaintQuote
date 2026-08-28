using System;
using System.Collections.Concurrent;
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
    private static readonly TimeSpan DbStartupTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan DbInteractiveWakeupTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan DbWakeupRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DbSchemaInitializationTimeout = TimeSpan.FromSeconds(60);
    private const int DbSaveRetryCount = 1;


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
    private readonly ConcurrentDictionary<string, QuoteMetadata> _sessionQuoteMetadata =
        new(StringComparer.OrdinalIgnoreCase);

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
    public bool IsOfflineMode => !_isDatabaseAvailable;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Debug.WriteLine("[FallbackDataService] InitializeAsync starting...");
        using var startupTimeoutCts = new CancellationTokenSource(DbStartupTimeout);
        using var startupCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            startupTimeoutCts.Token);

        try
        {
            await EnsureDatabaseReachableAsync(startupCts.Token);
            await InitializeDatabaseSchemaAsync(startupCts.Token);
            
            _isDatabaseAvailable = true;
            Debug.WriteLine("[FallbackDataService] ✅ Database initialized successfully");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            SetDatabaseUnavailable(
                $"Timeout inizializzazione SQL dopo {DbStartupTimeout.TotalSeconds:F0}s.");
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
        await EnsureDatabaseReachableAsync(DbStartupTimeout, cancellationToken);
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

            TimeSpan remainingBeforeAttempt = deadline - DateTime.UtcNow;
            TimeSpan attemptTimeout = remainingBeforeAttempt < DbConnectionAttemptTimeout
                ? remainingBeforeAttempt
                : DbConnectionAttemptTimeout;
            using var attemptCts = new CancellationTokenSource(attemptTimeout);
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
            _localQuoteNumbersCache.UnionWith(_sessionQuoteMetadata.Keys);
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
                        SyncHash = q.SyncHash,
                        Revision = q.Revision,
                        HasPendingDatabaseWrite = q.HasPendingDatabaseWrite
                    },
                    StringComparer.OrdinalIgnoreCase);
            _localQuoteMetadataCacheTime = DateTime.UtcNow;
        }

        foreach (var pair in _sessionQuoteMetadata)
            _localQuoteMetadataCache[pair.Key] = pair.Value;

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

    internal void MarkLocalQuoteCacheUpdated(IEnumerable<string> quoteNumbers)
    {
        foreach (string quoteNumber in quoteNumbers)
            _sessionQuoteMetadata.TryRemove(quoteNumber, out _);
        InvalidateQuoteNumbersCaches();
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
            (true, true) when localQuote!.HasPendingDatabaseWrite => SyncStatus.LocalOnly,
            (true, true) when string.Equals(dbQuote!.SyncHash, localQuote!.SyncHash, StringComparison.Ordinal) =>
                SyncStatus.Synced,
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
                SyncHash = string.Empty,
                Revision = 0
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

    private async Task EnsureDatabaseRequiredAsync(
        string operation,
        CancellationToken cancellationToken = default)
    {
        bool databaseAvailable = IsDatabaseAvailable();
        if (!databaseAvailable)
            databaseAvailable = await TryEnsureDatabaseAvailableAsync(
                operation,
                DbInteractiveWakeupTimeout,
                cancellationToken);

        if (!databaseAvailable)
            throw CreateDatabaseUnavailableException(operation);
    }

    internal async Task EnsureInteractiveDatabaseReadyAsync(
        string operation,
        CancellationToken cancellationToken = default)
    {
        bool databaseAvailable = await TryEnsureDatabaseAvailableAsync(
            operation,
            DbInteractiveWakeupTimeout,
            cancellationToken);
        if (!databaseAvailable)
            throw CreateDatabaseUnavailableException(operation);
    }

    private static InvalidOperationException CreateDatabaseUnavailableException(string operation) =>
        new(
            $"Database non disponibile durante: {operation}.\n\n" +
            "Operazione annullata: i dati condivisi devono essere letti e salvati dal database. " +
            "Riprova quando la connessione al database e' disponibile.");

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
        await EnsureDatabaseRequiredAsync("Caricamento preventivi");
    
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
        throw CreateDatabaseUnavailableException("Caricamento preventivi");
    }

    public async Task<List<QuoteHistoryEntry>> GetQuotesAsync(int take)
    {
        await EnsureDatabaseRequiredAsync("Caricamento ultimi preventivi");

        if (IsDatabaseAvailable())
        {
            try { return await _sqlService.GetQuotesAsync(take); }
            catch(Exception ex) { HandleDatabaseException("GetQuotesAsync(take)", ex); }
        }

        throw CreateDatabaseUnavailableException("Caricamento ultimi preventivi");
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
    throw CreateDatabaseUnavailableException("Caricamento storico");
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

        throw CreateDatabaseUnavailableException("Preventivi inviati aperti");
    }

    public async Task<List<QuoteHistorySummary>> GetSupplierOrderSummariesAsync(
        string searchText,
        int take,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        bool databaseAvailable = IsDatabaseAvailable();
        if (!databaseAvailable)
            databaseAvailable = await TryEnsureDatabaseAvailableAsync(
                "Caricamento ordini fornitori",
                DbInteractiveWakeupTimeout,
                cancellationToken);

        if (databaseAvailable)
        {
            try
            {
                var dbQuotes = await _sqlService.GetSupplierOrderSummariesAsync(
                    searchText,
                    take,
                    cancellationToken);
                var dbMetadata = await GetDbQuoteMetadataCachedAsync(cancellationToken);
                var localMetadata = await GetLocalQuoteMetadataCachedAsync(cancellationToken);
                EnsureDbMetadataForDisplayedSummaries(dbQuotes, dbMetadata);

                foreach (var quote in dbQuotes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    quote.SyncStatus = ResolveSyncStatus(quote.QuoteNumber, dbMetadata, localMetadata);
                }

                return dbQuotes;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                HandleDatabaseException("GetSupplierOrderSummariesAsync", ex);
            }
        }

        throw CreateDatabaseUnavailableException("Caricamento ordini fornitori");
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

        throw CreateDatabaseUnavailableException("Ricerca storico");
    }

    public async Task<QuoteHistoryEntry?> GetQuoteByNumberAsync(
        string quoteNumber,
        CancellationToken cancellationToken = default,
        bool includeAttachments = true)
    {
        await EnsureDatabaseRequiredAsync($"Apertura preventivo {quoteNumber}", cancellationToken);

        if (IsDatabaseAvailable())
        {
            try
            {
                return await _sqlService.GetQuoteByNumberAsync(
                    quoteNumber,
                    cancellationToken,
                    includeAttachments);
            }
            catch (OperationCanceledException) { throw; }
            catch(Exception ex) { HandleDatabaseException("GetQuoteByNumberAsync", ex); }
        }

        throw CreateDatabaseUnavailableException($"Apertura preventivo {quoteNumber}");
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
            CustomerSyncId = entry.CustomerSyncId,
            ReferenceName = entry.ReferenceName,
            ReferenceCustomerSyncId = entry.ReferenceCustomerSyncId,
            SiteName = entry.SiteName,
            BillingCustomerName = entry.BillingCustomerName,
            BillingCustomerSyncId = entry.BillingCustomerSyncId,
            PdfPath = entry.PdfPath,
            PaymentTerms = entry.PaymentTerms,
            CustomerNotes = entry.CustomerNotes,
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
            SupplierName = entry.SupplierName,
            MaterialOrderDate = entry.MaterialOrderDate,
            ExpectedDeliveryDate = entry.ExpectedDeliveryDate,
            MaterialStatus = entry.MaterialStatus,
            MaterialsOrderedByCustomer = entry.MaterialsOrderedByCustomer,
            RealProfit = entry.RealProfit,
            LastModifiedUtc = entry.LastModifiedUtc,
            BaseVersionUtc = entry.BaseVersionUtc,
            Revision = entry.Revision,
            BaseRevision = entry.BaseRevision,
            HasPendingDatabaseWrite = entry.HasPendingDatabaseWrite,
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
            }).ToList(),
            // La cache JSON contiene solo metadati allegato, mai i byte.
            HasCompleteAttachmentSnapshot = false
        };
    }
    public async Task SaveQuoteAsync(QuoteHistoryEntry quote, CancellationToken cancellationToken = default)
    {
        try
        {
            await SaveQuoteDatabaseOnlyAsync(quote, cancellationToken);
            await UpdateLocalQuoteCacheAfterDatabaseSaveAsync(quote, cancellationToken);
        }
        catch (QuoteConflictException)
        {
            await _localStore.ArchiveQuoteConflictAsync(
                quote,
                "Salvataggio completo rifiutato: il database contiene una versione piu' recente.",
                cancellationToken);
            var databaseVersion = await _sqlService.GetQuoteByNumberAsync(
                quote.QuoteNumber,
                cancellationToken,
                includeAttachments: false);
            if (databaseVersion != null)
                await _localStore.BulkUpdateQuotesAsync([databaseVersion], cancellationToken);

            throw;
        }
    }

    internal async Task SaveQuoteDatabaseOnlyAsync(
        QuoteHistoryEntry quote,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        quote.LastModifiedUtc = DateTime.UtcNow;
        quote.HasPendingDatabaseWrite = true;
        EnsureDeviceMetadata(quote);

        var lightEntry = CreateLightEntry(quote);
        quote.SyncHash = QuoteSyncHashService.Compute(lightEntry);

        bool savedOnline = await SaveQuoteOnlineWithRetryAsync(quote, cancellationToken);
        if (!savedOnline)
        {
            throw new DatabaseWritePendingException(
                $"Il preventivo {quote.QuoteNumber} non e' stato salvato: il database non ha confermato il salvataggio.");
        }

        quote.HasPendingDatabaseWrite = false;
        _sessionQuoteMetadata[quote.QuoteNumber] = new QuoteMetadata
        {
            QuoteNumber = quote.QuoteNumber,
            LastModifiedUtc = quote.LastModifiedUtc,
            SyncHash = quote.SyncHash,
            Revision = quote.Revision,
            HasPendingDatabaseWrite = false
        };
        InvalidateQuoteNumbersCaches();
    }

    internal async Task UpdateLocalQuoteCacheAfterDatabaseSaveAsync(
        QuoteHistoryEntry quote,
        CancellationToken cancellationToken = default)
    {
        var lightEntry = CreateLightEntry(quote);
        lightEntry.SyncHash = quote.SyncHash;
        lightEntry.HasPendingDatabaseWrite = false;
        try
        {
            await _localStore.SaveOrUpdateQuoteAsync(lightEntry, cancellationToken);
            MarkLocalQuoteCacheUpdated([quote.QuoteNumber]);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            WriteDatabaseLog(
                $"Preventivo {quote.QuoteNumber} salvato nel DB, ma cache locale non aggiornata: {ex.Message}");
        }
    }

    private async Task<bool> SaveQuoteOnlineWithRetryAsync(
        QuoteHistoryEntry onlineEntry,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;

        for (int attempt = 1; attempt <= DbSaveRetryCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (!IsDatabaseAvailable())
                    await EnsureDatabaseReachableAsync(DbInteractiveWakeupTimeout, cancellationToken);
                await _sqlService.SaveQuoteAsync(onlineEntry, cancellationToken);
                MarkDatabaseAvailable($"Preventivo {onlineEntry.QuoteNumber} salvato online.");
                return true;
            }
            catch (QuoteConflictException)
            {
                // Il salvataggio completo nasce dall'editor: il contenuto del
                // file aperto e modificato e' autorevole. Una concorrenza nella
                // stretta finestra SELECT->UPDATE viene quindi ritentata una
                // sola volta su un nuovo DbContext, che rilegge la revisione.
                WriteDatabaseLog(
                    $"SaveQuoteAsync concorrenza transitoria per {onlineEntry.QuoteNumber}: ritento come editor autorevole.");
                await _sqlService.SaveQuoteAsync(onlineEntry, cancellationToken);
                MarkDatabaseAvailable($"Preventivo {onlineEntry.QuoteNumber} salvato online al secondo tentativo.");
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                WriteDatabaseLog($"SaveQuoteAsync tentativo {attempt}/{DbSaveRetryCount} per {onlineEntry.QuoteNumber}: {ex.GetType().Name}: {ex.Message}");

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
            SetDatabaseUnavailable($"SaveQuoteAsync {onlineEntry.QuoteNumber}: {lastError.Message}");

        return false;
    }

    public async Task DeleteQuoteAsync(
        string quoteNumber,
        CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseRequiredAsync($"Eliminazione preventivo {quoteNumber}", cancellationToken);

        try
        {
            await _sqlService.DeleteQuoteAsync(quoteNumber, cancellationToken);
            await _deletionOutbox.RemoveQuoteAsync(quoteNumber, cancellationToken);
            // history.json e' una cache: la sync elimina tutte le quote tombstonate
            // in un solo batch, evitando una riscrittura da decine di MB qui.
            _sessionQuoteMetadata.TryRemove(quoteNumber, out _);
        }
        catch(Exception ex)
        {
            HandleDatabaseException("DeleteQuoteAsync", ex);
            throw;
        }

        // Invalida le cache dopo ogni eliminazione
        InvalidateQuoteNumbersCaches();
    }

    public async Task UpdateQuoteNotesAsync(
        string quoteNumber,
        string notes,
        CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseRequiredAsync($"Aggiornamento note preventivo {quoteNumber}", cancellationToken);

        try
        {
            await _sqlService.UpdateQuoteNotesAsync(quoteNumber, notes, cancellationToken);
            // La cache JSON viene riallineata in batch dalla sync periodica.
            // Non riscriviamo l'intero storico per ogni singola azione UI.
            await _quotePatchOutbox.RemoveNotesIfMatchesAsync(quoteNumber, notes, cancellationToken);
            InvalidateQuoteNumbersCaches();
        }
        catch (Exception ex)
        {
            HandleDatabaseException("UpdateQuoteNotesAsync", ex);
            throw;
        }
    }

    public async Task UpdateQuoteStatusAsync(
        string quoteNumber,
        QuoteStatus status,
        CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseRequiredAsync($"Aggiornamento stato preventivo {quoteNumber}", cancellationToken);

        try
        {
            await _sqlService.UpdateQuoteStatusAsync(quoteNumber, status, cancellationToken);
            await _quotePatchOutbox.RemoveStatusIfMatchesAsync(quoteNumber, status, cancellationToken);
            InvalidateQuoteNumbersCaches();
        }
        catch (Exception ex)
        {
            HandleDatabaseException("UpdateQuoteStatusAsync", ex);
            throw;
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

        await EnsureDatabaseRequiredAsync($"Aggiornamento invio preventivo {quoteNumber}", cancellationToken);

        try
        {
            await _sqlService.UpdateQuoteSendInfoAsync(quoteNumber, sendInfo, cancellationToken);
            await _quotePatchOutbox.RemoveSendInfoIfMatchesAsync(quoteNumber, sendInfo, cancellationToken);
            InvalidateQuoteNumbersCaches();
        }
        catch (Exception ex)
        {
            HandleDatabaseException("UpdateQuoteSendInfoAsync", ex);
            throw;
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

        await EnsureDatabaseRequiredAsync($"Aggiornamento promemoria preventivo {quoteNumber}", cancellationToken);

        try
        {
            await _sqlService.RegisterQuoteReminderAsync(quoteNumber, reminderInfo, cancellationToken);
            await _quotePatchOutbox.RemoveReminderInfoIfMatchesAsync(quoteNumber, reminderInfo, cancellationToken);
            InvalidateQuoteNumbersCaches();
        }
        catch (Exception ex)
        {
            HandleDatabaseException("RegisterQuoteReminderAsync", ex);
            throw;
        }
    }

    public async Task UpdateQuoteSupplierInfoAsync(
        string quoteNumber,
        QuoteSupplierInfo supplierInfo,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(supplierInfo.DeviceName))
            supplierInfo.DeviceName = DeviceNameService.GetCurrentDeviceName();

        await EnsureDatabaseRequiredAsync($"Aggiornamento fornitori preventivo {quoteNumber}", cancellationToken);

        try
        {
            await _sqlService.UpdateQuoteSupplierInfoAsync(quoteNumber, supplierInfo, cancellationToken);
            await _quotePatchOutbox.RemoveSupplierInfoIfMatchesAsync(quoteNumber, supplierInfo, cancellationToken);
            InvalidateQuoteNumbersCaches();
        }
        catch (Exception ex)
        {
            HandleDatabaseException("UpdateQuoteSupplierInfoAsync", ex);
            throw;
        }
    }

    public async Task UpdateQuoteRealProfitAsync(
        string quoteNumber,
        RealProfitSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(snapshot.CalculatedByDevice))
            snapshot.CalculatedByDevice = DeviceNameService.GetCurrentDeviceName();
        if (snapshot.CalculatedAtUtc == default)
            snapshot.CalculatedAtUtc = DateTime.UtcNow;

        await EnsureDatabaseRequiredAsync(
            $"Salvataggio guadagno reale preventivo {quoteNumber}",
            cancellationToken);

        try
        {
            await _sqlService.UpdateQuoteRealProfitAsync(quoteNumber, snapshot, cancellationToken);
            QuoteHistoryEntry? databaseVersion = await _sqlService.GetQuoteByNumberAsync(
                quoteNumber,
                cancellationToken,
                includeAttachments: false);
            if (databaseVersion != null)
                await _localStore.BulkUpdateQuotesAsync([databaseVersion], cancellationToken);
            InvalidateQuoteNumbersCaches();
        }
        catch (Exception ex)
        {
            HandleDatabaseException("UpdateQuoteRealProfitAsync", ex);
            throw;
        }
    }

    public async Task<HashSet<string>> GetAllQuoteNumbersAsync()
    {
        await EnsureDatabaseRequiredAsync("Caricamento numeri preventivo");

        if (IsDatabaseAvailable())
        {
            try { return await _sqlService.GetAllQuoteNumbersAsync(); }
            catch (Exception ex) { HandleDatabaseException("GetAllQuoteNumbersAsync", ex); }
        }

        throw CreateDatabaseUnavailableException("Caricamento numeri preventivo");
    }

    #endregion

    #region Customers

    public async Task<List<Customer>> GetCustomersAsync(CancellationToken cancellationToken = default)
    {
        if (IsDatabaseAvailable())
        {
            try
            {
                var customers = await _sqlService.GetCustomersAsync(cancellationToken);
                var referencedCustomerIds = await _sqlService
                    .GetReferencedCustomerSyncIdsAsync(cancellationToken);
                return CustomerDuplicateFilter
                    .Compact(customers, referencedCustomerIds)
                    .Kept
                    .ToList();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (IsDatabaseConnectivityException(ex))
            {
                HandleDatabaseException("GetCustomersAsync", ex);
            }
        }

        Debug.WriteLine("[FallbackDataService] Caricamento clienti dalla cache locale.");
        return await _localStore.LoadCustomersAsync(cancellationToken);
    }

    public async Task<Customer> AddCustomerAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        NormalizeCustomerForSave(customer);

        if (customer.SyncId == Guid.Empty)
            customer.SyncId = Guid.NewGuid();

        customer.LastModifiedUtc = DateTime.UtcNow;
        customer.HasPendingDatabaseWrite = true;
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
                saved.HasPendingDatabaseWrite = false;
                MarkDatabaseAvailable($"Cliente {saved.BusinessName} salvato online.");
                // clienti.json e' una cache: la sync completa raggruppa gli
                // aggiornamenti, senza riscrivere 4+ MB per ogni click UI.
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

        throw CreateDatabaseUnavailableException($"Salvataggio cliente {customer.BusinessName}");
    }

    public async Task<Customer> UpdateCustomerAsync(string originalBusinessName, Customer customer)
    {
        NormalizeCustomerForSave(customer);

        customer.LastModifiedUtc = DateTime.UtcNow;
        customer.HasPendingDatabaseWrite = true;
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
                saved.HasPendingDatabaseWrite = false;
                MarkDatabaseAvailable($"Cliente {saved.BusinessName} aggiornato online.");
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

        throw CreateDatabaseUnavailableException($"Aggiornamento cliente {customer.BusinessName}");
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
        await EnsureDatabaseRequiredAsync($"Eliminazione cliente {customer.BusinessName}");

        try
        {
            await _sqlService.DeleteCustomerAsync(customer.SyncId, customer.BusinessName);
            await _deletionOutbox.RemoveCustomerAsync(customer.SyncId, customer.BusinessName);
        }
        catch (Exception ex)
        {
            HandleDatabaseException("DeleteCustomerAsync", ex);
            throw;
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
            catch (Exception ex) when (IsDatabaseConnectivityException(ex))
            {
                HandleDatabaseException("GetCompanyAsync", ex);
            }
        }

        Debug.WriteLine("[FallbackDataService] Caricamento impostazioni azienda dalla cache locale.");
        return await _localStore.LoadCompanyAsync();
    }

    public async Task SaveCompanyAsync(Company company, string selectedLogo)
    {
        await EnsureDatabaseRequiredAsync("Salvataggio impostazioni azienda");

        try
        {
            await _sqlService.SaveCompanyAsync(company, selectedLogo);
            await _localStore.SaveCompanyAsync(company, selectedLogo);
        }
        catch (Exception ex)
        {
            HandleDatabaseException("SaveCompanyAsync", ex);
            throw;
        }
    }

    public async Task<List<Item>> GetLaborCatalogAsync()
    {
        if (IsDatabaseAvailable())
        {
            try
            {
                var labors = await _sqlService.GetLaborCatalogAsync();
                var localLabors = await _localStore.LoadLaborCatalogAsync();
                if (!CatalogCollectionsHaveSameContent(localLabors, labors))
                    await _localStore.SaveLaborCatalogAsync(labors);
                return labors;
            }
            catch (Exception ex) when (IsDatabaseConnectivityException(ex))
            {
                HandleDatabaseException("GetLaborCatalogAsync", ex);
            }
        }

        Debug.WriteLine("[FallbackDataService] Caricamento lavorazioni dalla cache locale.");
        return await _localStore.LoadLaborCatalogAsync();
    }

    public async Task SaveLaborCatalogAsync(IEnumerable<Item> labors)
    {
        var laborList = labors.ToList();
        bool available = IsDatabaseAvailable() || await TryEnsureDatabaseAvailableAsync(
            "Salvataggio catalogo lavorazioni", DbInteractiveWakeupTimeout, CancellationToken.None);
        if (!available)
            throw new DatabaseWritePendingException("Catalogo lavorazioni non modificato: database non disponibile.");

        await _sqlService.SaveLaborCatalogAsync(laborList);
        await _localStore.SaveLaborCatalogAsync(laborList);
    }

    public async Task DeleteLaborCatalogItemAsync(Item labor, CancellationToken cancellationToken = default)
    {
        bool available = IsDatabaseAvailable() || await TryEnsureDatabaseAvailableAsync(
            "Eliminazione lavorazione", DbInteractiveWakeupTimeout, cancellationToken);
        if (!available)
            throw new DatabaseWritePendingException("Lavorazione non eliminata: database non disponibile.");

        await _sqlService.DeleteLaborCatalogItemAsync(labor, cancellationToken);

        var local = await _localStore.LoadLaborCatalogAsync();
        local.RemoveAll(x => CatalogItemsMatch(x, labor));
        await _localStore.SaveLaborCatalogAsync(local);
    }

    public async Task<List<Item>> GetPersonalMaterialsAsync()
    {
        if (IsDatabaseAvailable())
        {
            try
            {
                var materials = await _sqlService.GetPersonalMaterialsAsync();
                var localMaterials = await _localStore.LoadPersonalMaterialsAsync();
                if (!CatalogCollectionsHaveSameContent(localMaterials, materials))
                    await _localStore.SavePersonalMaterialsAsync(materials);
                return materials;
            }
            catch (Exception ex) when (IsDatabaseConnectivityException(ex))
            {
                HandleDatabaseException("GetPersonalMaterialsAsync", ex);
            }
        }

        Debug.WriteLine("[FallbackDataService] Caricamento materiali dalla cache locale.");
        return await _localStore.LoadPersonalMaterialsAsync();
    }

    public async Task SavePersonalMaterialsAsync(IEnumerable<Item> materials)
    {
        var materialList = materials.ToList();
        bool available = IsDatabaseAvailable() || await TryEnsureDatabaseAvailableAsync(
            "Salvataggio materiali personali", DbInteractiveWakeupTimeout, CancellationToken.None);
        if (!available)
            throw new DatabaseWritePendingException("Materiali non modificati: database non disponibile.");

        await _sqlService.SavePersonalMaterialsAsync(materialList);
        await _localStore.SavePersonalMaterialsAsync(materialList);
    }

    public async Task DeletePersonalMaterialAsync(Item material, CancellationToken cancellationToken = default)
    {
        bool available = IsDatabaseAvailable() || await TryEnsureDatabaseAvailableAsync(
            "Eliminazione materiale", DbInteractiveWakeupTimeout, cancellationToken);
        if (!available)
            throw new DatabaseWritePendingException("Materiale non eliminato: database non disponibile.");

        await _sqlService.DeletePersonalMaterialAsync(material, cancellationToken);

        var local = await _localStore.LoadPersonalMaterialsAsync();
        local.RemoveAll(x => CatalogItemsMatch(x, material));
        await _localStore.SavePersonalMaterialsAsync(local);
    }

    private static bool CatalogItemsMatch(Item left, Item right) =>
        (left.PersistentId > 0 && right.PersistentId > 0 && left.PersistentId == right.PersistentId) ||
        left.Name.Equals(right.Name, StringComparison.OrdinalIgnoreCase);

    private static bool CatalogCollectionsHaveSameContent(
        IReadOnlyCollection<Item> left,
        IReadOnlyCollection<Item> right)
    {
        if (left.Count != right.Count)
            return false;

        static string Identity(Item item) => item.PersistentId > 0
            ? $"id:{item.PersistentId:D10}"
            : $"name:{item.Name}";

        var orderedLeft = left
            .OrderBy(Identity, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var orderedRight = right
            .OrderBy(Identity, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return orderedLeft.Zip(orderedRight).All(pair =>
            pair.First.PersistentId == pair.Second.PersistentId &&
            string.Equals(pair.First.Name, pair.Second.Name, StringComparison.Ordinal) &&
            string.Equals(pair.First.Description, pair.Second.Description, StringComparison.Ordinal) &&
            pair.First.UnitPrice.Equals(pair.Second.UnitPrice) &&
            pair.First.Quantity == pair.Second.Quantity &&
            pair.First.Discount.Equals(pair.Second.Discount) &&
            pair.First.IsSignificant == pair.Second.IsSignificant &&
            pair.First.IsCompanyMaterial == pair.Second.IsCompanyMaterial &&
            pair.First.SortOrder == pair.Second.SortOrder);
    }

    public async Task<int> GetNextQuoteNumberAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsDatabaseAvailable())
        {
            try { return await _sqlService.GetNextQuoteNumberAsync(cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { HandleDatabaseException("GetNextQuoteNumberAsync", ex); }
        }

        throw new InvalidOperationException("Database non disponibile: impossibile assegnare un numero preventivo ufficiale. Riprova quando la connessione e' disponibile.");
    }

    public async Task<bool> IsDatabaseEmptyAsync()
    {
        await EnsureDatabaseRequiredAsync("Controllo database vuoto");

        if (IsDatabaseAvailable())
        {
            try { return await _sqlService.IsDatabaseEmptyAsync(); }
            catch (Exception ex) { HandleDatabaseException("IsDatabaseEmptyAsync", ex); }
        }

        throw CreateDatabaseUnavailableException("Controllo database vuoto");
    }
    public async Task<Dictionary<string, QuoteMetadata>> GetQuoteMetadataAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseRequiredAsync("Caricamento metadati preventivi", cancellationToken);

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

        throw CreateDatabaseUnavailableException("Caricamento metadati preventivi");
    }
    
    public async Task<List<QuoteHistoryEntry>> GetQuotesByNumbersAsync(
        IEnumerable<string> quoteNumbers,
        CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseRequiredAsync("Caricamento preventivi per numero", cancellationToken);

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

        throw CreateDatabaseUnavailableException("Caricamento preventivi per numero");
    }

    #endregion
    
}
