using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using EdilPaintPreventibiviGen.Data;
using EdilPaintPreventibiviGen.Services;
using EdilPaintPreventibiviGen.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace EdilPaintPreventibiviGen;

public partial class App : Application
{
    public static IDataService DataService { get; private set; } = null!;
    public static AppSettingsService AppSettings { get; private set; } = null!;
    public static SyncService SyncService { get; private set; } = null!;
    public static MainViewModel? MainVm { get; private set; }
    public static bool IsSilentStartup { get; private set; }

    private static System.Timers.Timer? _syncTimer;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var startupWatch = Stopwatch.StartNew();

            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .Build();

            AppSettings = new AppSettingsService(configuration);
            IsSilentStartup = AppSettings.App.IsSilentStartup;

            StoragePathService.Initialize(AppSettings);
            
            // Inizializza servizi di storage
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
            var localStore = new LocalJsonStoreService(assetsPath);

            // Usa il FallbackDataService come servizio principale
            DataService = new FallbackDataService(sqlService, localStore);
            SyncService = new SyncService(DataService, sqlService, localStore);

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
                await DataService.InitializeAsync();

                if (AppSettings.App.FirstStartup)
                {
                    await SetLoadingStatusAsync(loadingWindow, "2/5 - Import dati legacy...");
                    var importer = new JsonImportService(sqlService);
                    await Task.Run(async () => await importer.ImportAllAsync(assetsPath));
                }
                else
                {
                    await SetLoadingStatusAsync(loadingWindow, "2/5 - Database pronto");
                }

                // Sincronizzazione iniziale — completamente in background, NON bloccare l'avvio
                    await SetLoadingStatusAsync(loadingWindow, "3/5 - Sincronizzazione dati...");
                    // NON aspettiamo il sync — parte in background e l'app si apre subito
                    _ = Task.Run(() => SyncService.SyncAllAsync(force: true)).ContinueWith(t =>
                    {
                        if (t.Exception != null)
                            Debug.WriteLine($"[STARTUP] Sync error: {t.Exception.Message}");
                        else
                            Debug.WriteLine($"[STARTUP] Sync completed: Quotes={t.Result.QuotesSynced}, Customers={t.Result.CustomersSynced}");
                    });

                    await SetLoadingStatusAsync(loadingWindow, "4/5 - Caricamento dati applicazione...");
                    MainVm = await Task.Run(() => new MainViewModel());

                    await SetLoadingStatusAsync(loadingWindow, "5/5 - Apertura finestra principale...");
                    await ForceUiRefreshAsync(loadingWindow);

                    var mainWindow = new MainWindow(MainVm);
                    MainWindow = mainWindow;
                    mainWindow.Show();

                    StartPeriodicSync();

                    // Genera PDF dopo un ritardo, solo dopo che il sync è finito
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // Aspetta 30 secondi — il sync dei clienti ha bisogno di tempo
                            await Task.Delay(TimeSpan.FromSeconds(30));

                            if (AppSettings.App.GeneratePDF)
                            {
                                bool hasMissing = await MainVm!.HasMissingPdfsAsync();
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
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[STARTUP] PDF error: {ex.Message}");
                        }
                    });

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
        _syncTimer?.Stop();
        _syncTimer?.Dispose();
        _syncTimer = null;

        if (SyncService != null)
        {
            try
            {
                SyncService.SyncAllAsync(force: true).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SHUTDOWN] Sync error: {ex.Message}");
            }
        }

        base.OnExit(e);
    }

    private static void StartPeriodicSync()
    {
        _syncTimer = new System.Timers.Timer(TimeSpan.FromMinutes(5).TotalMilliseconds);
        _syncTimer.Elapsed += async (s, e) =>
        {
            Debug.WriteLine("[PeriodicSync] Running scheduled sync...");
            await SyncService.SyncAllAsync(); // ← rimosso take: AppSettings.App.NumberOfQuote
        };
        _syncTimer.AutoReset = true;
        _syncTimer.Start();

        Debug.WriteLine("[STARTUP] Periodic sync started (every 5 minutes)");
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