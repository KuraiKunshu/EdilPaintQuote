using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using EdilPaintPreventibiviGen.Data;
using EdilPaintPreventibiviGen.Services;
using EdilPaintPreventibiviGen.ViewModels;

namespace EdilPaintPreventibiviGen;

public partial class App : Application
{
    public static IDataService DataService { get; private set; } = null!;
    public static AppSettingsService AppSettings { get; private set; } = null!;
    public static SyncService SyncService { get; private set; } = null!;
    public static MainViewModel? MainVm { get; private set; }
    public static bool IsSilentStartup { get; private set; }
    public static bool IsOfflineMode { get; private set; }

    private const int ShutdownSyncTimeoutSeconds = 2;
    private const int ShutdownBackgroundTimeoutSeconds = 2;
    private const int HardShutdownTimeoutSeconds = 8;
    private const int StartupSyncQuoteLimit = 150;
    private static readonly TimeSpan StartupSyncForegroundTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan PeriodicSyncInterval = TimeSpan.FromMinutes(15);
    private static System.Timers.Timer? _syncTimer;
    private static CancellationTokenSource? _shutdownCts;
    private static bool _isShuttingDown;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _isShuttingDown = false;
            _shutdownCts = AppShutdownManager.CreateLinkedTokenSource();
            var shutdownToken = _shutdownCts.Token;

            var startupWatch = Stopwatch.StartNew();

            var configuration = AppSettingsFileService.BuildConfiguration();

            AppSettings = new AppSettingsService(configuration);
            IsSilentStartup = AppSettings.App.IsSilentStartup;

            StoragePathService.Initialize(AppSettings);

            var sqlService = new SqlDataService(AppSettings);

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            [
                Path.Combine(baseDir, "Assets"),
                Path.Combine(baseDir, "assets"),
                Path.Combine(baseDir, "..", "..", "..", "Assets"),
                Path.Combine(baseDir, "..", "..", "..", "assets")
            ];
            string assetsPath = candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
            string localDataPath = LocalApplicationDataService.EnsureDataDirectory(assetsPath);
            var localStore = new LocalJsonStoreService(localDataPath);
            var quotePatchOutbox = new LocalQuotePatchOutboxService(localDataPath);
            var deletionOutbox = new LocalDeletionOutboxService(localDataPath);

            var fallbackDataService = new FallbackDataService(
                sqlService, localStore, quotePatchOutbox, deletionOutbox);
            DataService = fallbackDataService;
            SyncService = new SyncService(
                DataService, sqlService, localStore, quotePatchOutbox, deletionOutbox);
            SyncService.SyncCompleted += OnSyncCompleted;

            var loadingWindow = new LoadingWindow
            {
                Title = "Avvio applicazione",
                StatusText = "Preparazione iniziale...",
                IsLoading = true
            };
            loadingWindow.Show();
            await ForceUiRefreshAsync(loadingWindow);

            try
            {
                await SetLoadingStatusAsync(loadingWindow, "1/5 - Connessione al database...");
                await DataService.InitializeAsync(shutdownToken);
                IsOfflineMode = fallbackDataService.IsOfflineMode;

                if (IsOfflineMode)
                {
                    await SetLoadingStatusAsync(
                        loadingWindow,
                        "2/5 - Database non raggiungibile: avvio offline...");
                }
                else if (AppSettings.App.FirstStartup)
                {
                    await SetLoadingStatusAsync(loadingWindow, "2/5 - Import dati legacy...");
                    var importer = new JsonImportService(sqlService);
                    await Task.Run(() => importer.ImportAllAsync(assetsPath), shutdownToken);
                }
                else
                {
                    await SetLoadingStatusAsync(loadingWindow, "2/5 - Database pronto");
                }

                if (IsOfflineMode)
                {
                    await SetLoadingStatusAsync(
                        loadingWindow,
                        "3/5 - Modalita' offline: sincronizzazione rinviata");
                }
                else
                {
                    await SetLoadingStatusAsync(loadingWindow, "3/5 - Sincronizzazione dati...");
                    await RunStartupSyncAsync(shutdownToken);
                }

                await SetLoadingStatusAsync(loadingWindow, "4/5 - Caricamento dati applicazione...");
                MainVm = new MainViewModel();
                await MainVm.InitializeAsync();

                await SetLoadingStatusAsync(loadingWindow, "5/5 - Apertura finestra principale...");
                await ForceUiRefreshAsync(loadingWindow);

                var mainWindow = new MainWindow(MainVm);
                if (IsOfflineMode)
                    mainWindow.Title = "Gestione Preventivi - EdilPaint (OFFLINE)";
                MainWindow = mainWindow;
                mainWindow.Show();

                StartPeriodicSyncIfEnabled();
                if (!IsOfflineMode)
                    _ = RunStartupPdfGenerationAsync(shutdownToken);

                Debug.WriteLine($"[STARTUP] Startup completato in {startupWatch.Elapsed}");
            }
            finally
            {
                loadingWindow.Close();
            }
        }
        catch (Exception ex)
        {
            if (IsSilentStartup)
            {
                Debug.WriteLine($"[STARTUP][ERROR] {ex}");
                Shutdown();
                return;
            }

            MessageBox.Show(
                $"Errore durante l'avvio dell'applicazione.\n\n{ex.Message}",
                "Errore di avvio",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppShutdownManager.ArmProcessKillSwitch(TimeSpan.FromSeconds(HardShutdownTimeoutSeconds));

        try
        {
            _isShuttingDown = true;
            AppShutdownManager.RequestShutdown();
            _shutdownCts?.Cancel();

            if (_syncTimer != null)
            {
                _syncTimer.Stop();
                _syncTimer.Elapsed -= OnPeriodicSyncElapsed;
                _syncTimer.Dispose();
                _syncTimer = null;
            }

            CloseRemainingWindows();
            AppShutdownManager.WaitForCompletionAsync(TimeSpan.FromSeconds(ShutdownBackgroundTimeoutSeconds))
                .GetAwaiter()
                .GetResult();
            RunFinalSyncWithTimeoutAsync().GetAwaiter().GetResult();
            AppShutdownManager.WaitForCompletionAsync(TimeSpan.FromSeconds(1))
                .GetAwaiter()
                .GetResult();

            MainVm?.Dispose();
            MainVm = null;

            _shutdownCts?.Dispose();
            _shutdownCts = null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SHUTDOWN] Cleanup error: {ex}");
        }
        finally
        {
            base.OnExit(e);
        }
    }

    private static async Task RunStartupSyncAsync(CancellationToken token)
    {
        var syncWatch = Stopwatch.StartNew();
        Task syncTask = AppShutdownManager.Track("Startup sync", async operationToken =>
        {
            try
            {
                if (operationToken.IsCancellationRequested || _isShuttingDown)
                    return;

                var result = await SyncService.SyncAllAsync(
                    force: true,
                    take: StartupSyncQuoteLimit,
                    cancellationToken: operationToken);
                Debug.WriteLine($"[STARTUP] Sync completed in {syncWatch.Elapsed}: Quotes={result.QuotesSynced}, Customers={result.CustomersSynced}");
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[STARTUP] Sync cancelled.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[STARTUP] Sync error: {ex.Message}");
            }
        }, token);

        Task completed = await Task.WhenAny(syncTask, Task.Delay(StartupSyncForegroundTimeout, token));
        if (completed == syncTask)
        {
            await syncTask;
            return;
        }

        Debug.WriteLine(
            $"[STARTUP] Sync ancora in corso dopo {StartupSyncForegroundTimeout.TotalSeconds:F0}s: apertura app, completamento in background.");
    }

    private static void OnSyncCompleted(object? sender, SyncCompletedEventArgs e)
    {
        if (IsOfflineMode)
        {
            IsOfflineMode = false;
            if (Current.MainWindow is Window mainWindow)
            {
                _ = mainWindow.Dispatcher.InvokeAsync(() =>
                    mainWindow.Title = "Gestione Preventivi - EdilPaint");
            }
        }

        var viewModel = MainVm;
        if (viewModel == null || _isShuttingDown || e.Result.CustomersSynced == 0)
            return;

        _ = AppShutdownManager.Track(
            "Post-sync customer refresh",
            token => viewModel.RefreshCustomersAsync(token),
            AppShutdownManager.ShutdownToken);
    }

    private static Task RunStartupPdfGenerationAsync(CancellationToken token)
    {
        return AppShutdownManager.Track("Startup PDF restore", async operationToken =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), operationToken);

                if (operationToken.IsCancellationRequested || _isShuttingDown || MainVm == null)
                    return;

                if (AppSettings.App.RestoreMissingPdfsOnStartup)
                {
                    await MainVm.GenerateInitialPdfsAsync(new Progress<string>(text =>
                    {
                        Debug.WriteLine($"[PDF Generation] {text}");
                    }), operationToken);
                }
                else
                {
                    Debug.WriteLine("[PDF Generation] Ripristino PDF mancanti all'avvio disattivato.");
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[STARTUP] PDF generation cancelled.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[STARTUP] PDF error: {ex.Message}");
            }
        }, token);
    }

    private static async Task RunFinalSyncWithTimeoutAsync()
    {
        if (SyncService is null)
            return;

        if (AppSettings.App.DatabaseCostSavingMode)
        {
            Debug.WriteLine("[SHUTDOWN] Final sync skipped: modalita' risparmio DB attiva.");
            return;
        }

        if (SyncService.IsSyncRunning)
        {
            Debug.WriteLine("[SHUTDOWN] Final sync skipped: sync gia' in corso.");
            return;
        }

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(ShutdownSyncTimeoutSeconds));
        Task<SyncResult> syncTask;
        try
        {
            Debug.WriteLine("[SHUTDOWN] Final sync best-effort start.");
            syncTask = Task.Run(() => SyncService.SyncAllAsync(
                force: true,
                cancellationToken: timeoutCts.Token,
                waitForCurrentRun: false,
                includeCustomers: false));

            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(ShutdownSyncTimeoutSeconds));
            var completed = await Task.WhenAny(syncTask, timeoutTask);
            if (completed != syncTask)
            {
                timeoutCts.Cancel();
                Debug.WriteLine($"[SHUTDOWN] Final sync skipped after {ShutdownSyncTimeoutSeconds}s timeout.");
                _ = ObserveBackgroundSyncFailureAsync(syncTask);
                return;
            }

            var result = await syncTask;
            if (result.AlreadyRunning)
                Debug.WriteLine("[SHUTDOWN] Final sync skipped: sync already running.");
            else if (!string.IsNullOrWhiteSpace(result.Error))
                Debug.WriteLine($"[SHUTDOWN] Final sync returned error: {result.Error}");
            else
                Debug.WriteLine("[SHUTDOWN] Final sync completed.");
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine($"[SHUTDOWN] Final sync skipped after {ShutdownSyncTimeoutSeconds}s timeout.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SHUTDOWN] Sync error: {ex.Message}");
        }
    }

    private static async Task ObserveBackgroundSyncFailureAsync(Task<SyncResult> syncTask)
    {
        try
        {
            await syncTask;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SHUTDOWN] Final sync background error after timeout: {ex.Message}");
        }
    }

    private void CloseRemainingWindows()
    {
        foreach (Window window in Windows.Cast<Window>().ToArray())
        {
            try
            {
                if (ReferenceEquals(window, MainWindow))
                    continue;

                if (window.IsVisible)
                    window.Close();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SHUTDOWN] Window close error: {ex.Message}");
            }
        }
    }

    private static void StartPeriodicSyncIfEnabled()
    {
        if (AppSettings.App.DatabaseCostSavingMode)
        {
            Debug.WriteLine("[STARTUP] Periodic sync disabled: modalita' risparmio DB attiva.");
            return;
        }

        _syncTimer = new System.Timers.Timer(PeriodicSyncInterval.TotalMilliseconds);
        _syncTimer.Elapsed += OnPeriodicSyncElapsed;
        _syncTimer.AutoReset = true;
        _syncTimer.Start();

        Debug.WriteLine($"[STARTUP] Periodic sync started (every {PeriodicSyncInterval.TotalMinutes:F0} minutes)");
    }

    private static async void OnPeriodicSyncElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (_isShuttingDown)
            return;

        try
        {
            Debug.WriteLine("[PeriodicSync] Running scheduled sync...");
            await AppShutdownManager.Track(
                "Periodic sync",
                async token => await SyncService.SyncAllAsync(
                    cancellationToken: token,
                    includeCustomers: false),
                _shutdownCts?.Token ?? CancellationToken.None);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PeriodicSync] Sync error: {ex.Message}");
        }
    }

    private static async Task SetLoadingStatusAsync(LoadingWindow window, string message)
    {
        await window.Dispatcher.InvokeAsync(() =>
        {
            window.StatusText = message;
        }, System.Windows.Threading.DispatcherPriority.Send);

        await ForceUiRefreshAsync(window);
    }

    private static Task ForceUiRefreshAsync(LoadingWindow window)
    {
        return window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background).Task;
    }
}
