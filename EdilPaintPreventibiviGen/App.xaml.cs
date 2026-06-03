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

    private const int ShutdownSyncTimeoutSeconds = 5;
    private static System.Timers.Timer? _syncTimer;
    private static CancellationTokenSource? _shutdownCts;
    private static Task? _startupSyncTask;
    private static Task? _startupPdfTask;
    private static bool _isShuttingDown;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _isShuttingDown = false;
            _shutdownCts = new CancellationTokenSource();
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
            var pdfOutbox = new LocalPdfOutboxService(localDataPath);
            var attachmentOutbox = new LocalAttachmentOutboxService(localDataPath);
            var costsPdfOutbox = new LocalCostsPdfOutboxService(localDataPath);
            var quotePatchOutbox = new LocalQuotePatchOutboxService(localDataPath);
            var deletionOutbox = new LocalDeletionOutboxService(localDataPath);

            DataService = new FallbackDataService(
                sqlService, localStore, pdfOutbox, attachmentOutbox, costsPdfOutbox, quotePatchOutbox, deletionOutbox);
            SyncService = new SyncService(
                DataService, sqlService, localStore, pdfOutbox, attachmentOutbox, costsPdfOutbox, quotePatchOutbox, deletionOutbox);

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

                if (AppSettings.App.FirstStartup)
                {
                    await SetLoadingStatusAsync(loadingWindow, "2/5 - Import dati legacy...");
                    var importer = new JsonImportService(sqlService);
                    await Task.Run(() => importer.ImportAllAsync(assetsPath));
                }
                else
                {
                    await SetLoadingStatusAsync(loadingWindow, "2/5 - Database pronto");
                }

                await SetLoadingStatusAsync(loadingWindow, "3/5 - Sincronizzazione dati...");
                _startupSyncTask = RunStartupSyncAsync(shutdownToken);

                await SetLoadingStatusAsync(loadingWindow, "4/5 - Caricamento dati applicazione...");
                MainVm = new MainViewModel();
                await MainVm.InitializeAsync();

                await SetLoadingStatusAsync(loadingWindow, "5/5 - Apertura finestra principale...");
                await ForceUiRefreshAsync(loadingWindow);

                var mainWindow = new MainWindow(MainVm);
                MainWindow = mainWindow;
                mainWindow.Show();

                StartPeriodicSync();
                _startupPdfTask = RunStartupPdfGenerationAsync(shutdownToken);

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
        _isShuttingDown = true;
        _shutdownCts?.Cancel();

        if (_syncTimer != null)
        {
            _syncTimer.Stop();
            _syncTimer.Elapsed -= OnPeriodicSyncElapsed;
            _syncTimer.Dispose();
            _syncTimer = null;
        }

        RunFinalSyncWithTimeout();

        MainVm?.Dispose();
        MainVm = null;

        _shutdownCts?.Dispose();
        _shutdownCts = null;
        _startupSyncTask = null;
        _startupPdfTask = null;

        base.OnExit(e);
    }

    private static Task RunStartupSyncAsync(CancellationToken token)
    {
        return Task.Run(async () =>
        {
            try
            {
                if (token.IsCancellationRequested || _isShuttingDown)
                    return;

                var result = await SyncService.SyncAllAsync(force: true, cancellationToken: token);
                Debug.WriteLine($"[STARTUP] Sync completed: Quotes={result.QuotesSynced}, Customers={result.CustomersSynced}");
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
    }

    private static Task RunStartupPdfGenerationAsync(CancellationToken token)
    {
        return Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);

                if (token.IsCancellationRequested || _isShuttingDown || MainVm == null)
                    return;

                if (AppSettings.App.GeneratePDF)
                {
                    bool hasMissing = await MainVm.HasMissingPdfsAsync();
                    if (token.IsCancellationRequested || _isShuttingDown)
                        return;

                    if (!hasMissing)
                    {
                        Debug.WriteLine("[PDF Generation] Tutti i PDF presenti, skip.");
                        return;
                    }

                    await MainVm.GenerateInitialPdfsAsync(new Progress<string>(text =>
                    {
                        Debug.WriteLine($"[PDF Generation] {text}");
                    }));
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

    private static void RunFinalSyncWithTimeout()
    {
        if (SyncService is null)
            return;

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(ShutdownSyncTimeoutSeconds));
            var finalSyncTask = Task.Run(() => SyncService.SyncAllAsync(force: true, cancellationToken: timeoutCts.Token));
            if (!finalSyncTask.Wait(TimeSpan.FromSeconds(ShutdownSyncTimeoutSeconds)))
            {
                timeoutCts.Cancel();
                Debug.WriteLine($"[SHUTDOWN] Final sync skipped after {ShutdownSyncTimeoutSeconds}s timeout.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SHUTDOWN] Sync error: {ex.Message}");
        }
    }

    private static void StartPeriodicSync()
    {
        _syncTimer = new System.Timers.Timer(TimeSpan.FromMinutes(5).TotalMilliseconds);
        _syncTimer.Elapsed += OnPeriodicSyncElapsed;
        _syncTimer.AutoReset = true;
        _syncTimer.Start();

        Debug.WriteLine("[STARTUP] Periodic sync started (every 5 minutes)");
    }

    private static async void OnPeriodicSyncElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (_isShuttingDown)
            return;

        try
        {
            Debug.WriteLine("[PeriodicSync] Running scheduled sync...");
            await SyncService.SyncAllAsync(cancellationToken: _shutdownCts?.Token ?? CancellationToken.None);
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
