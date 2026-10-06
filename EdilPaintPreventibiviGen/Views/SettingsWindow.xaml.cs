using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Microsoft.Win32;

namespace EdilPaintPreventibiviGen.Views;

public partial class SettingsWindow : Window
{
    private readonly ObservableCollection<WindowMaterialRuleEditor> _windowMaterialRuleEditors = [];
    private readonly ObservableCollection<EmployeeSettingsModel> _employees = [];
    private List<EmployeeSettingsModel> _originalEmployees = [];
    private readonly CancellationTokenSource _employeeLoadCts = AppShutdownManager.CreateLinkedTokenSource();
    private bool _employeeListCurrent;
    private WorkScheduleSettings _originalScheduleSettings = new();
    private bool _scheduleSettingsCurrent;
    private bool _scheduleSettingsLoaded;
    private bool _isRefreshingShared;
    private CancellationTokenSource? _sharedRefreshCts;
    private Task _sharedRefreshTask = Task.CompletedTask;
    private readonly DispatcherTimer _sharedSettingsTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private bool _isSaving;
    private readonly string _displayedCatalogIdentity;
    private bool _catalogIdsCompatible;
    private bool _updatingAutomaticUpdatesControl;

    public IReadOnlyList<Item> LaborCatalog { get; }
    public IReadOnlyList<Item> CompanyMaterialCatalog { get; }
    public IReadOnlyList<WindowMaterialCalculationModeOption> WindowMaterialCalculationModes { get; } =
    [
        new(WindowMaterialRuleSettingsModel.PerimeterCalculationMode, "Perimetro finestra"),
        new(WindowMaterialRuleSettingsModel.FixedPerWindowCalculationMode, "Quantità fissa per finestra")
    ];

    public SettingsWindow() : this(Array.Empty<Item>(), Array.Empty<Item>())
    {
    }

    public SettingsWindow(IEnumerable<Item> laborCatalog, IEnumerable<Item> companyMaterials)
    {
        _displayedCatalogIdentity = App.AppSettings.Database.GetCatalogIdentity();
        LaborCatalog = CreateCatalogSnapshot(laborCatalog, companyMaterialsOnly: false);
        CompanyMaterialCatalog = CreateCatalogSnapshot(companyMaterials, companyMaterialsOnly: true);
        InitializeComponent();
        EdilPaintPreventibiviGen.Helpers.WindowResizeBehavior.PreventMaximizedState(this);
        CmbDatabaseProvider.ItemsSource = DatabaseSettingsModel.AvailableProviders;
        CmbPdfTemplate.ItemsSource = PdfTemplateSettingsModel.AvailableTemplates;
        ItemsWindowMaterialRules.ItemsSource = _windowMaterialRuleEditors;
        ItemsEmployees.ItemsSource = _employees;
        TxtNoCompanyMaterials.Visibility = CompanyMaterialCatalog.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        CmbDefaultVat.ItemsSource = new[] { "22%", "10%", "RC 10%+22%", "esclusa" };
        BtnCompanyProfile.IsEnabled = App.DataService != null;
        LoadSettings();
        Loaded += async (_, _) =>
        {
            await RefreshSelectedSharedSettingsAsync();
            if (!_employeeLoadCts.IsCancellationRequested) _sharedSettingsTimer.Start();
        };
        _sharedSettingsTimer.Tick += async (_, _) => await RefreshSelectedSharedSettingsAsync(automatic: true);
        SettingsTabs.SelectionChanged += OnSettingsTabSelectionChanged;
        Closing += (_, args) => { if (_isSaving) args.Cancel = true; };
        Closed += (_, _) => { _sharedSettingsTimer.Stop(); _employeeLoadCts.Cancel(); };
        PreviewKeyDown += SettingsWindow_PreviewKeyDown;
    }

    private void LoadSettings()
    {
        try { SetEmployeeSnapshot(App.EmployeeDirectory?.CachedSnapshot ?? new EmployeeDirectorySnapshot([], false)); }
        catch (InvalidOperationException) { SetEmployeeSnapshot(new EmployeeDirectorySnapshot([], false)); }
        SetEmployeeEditingEnabled(false);
        SetScheduleSettingsSnapshot(new WorkScheduleSettingsSnapshot(new(), false, null));
        _scheduleSettingsLoaded = false;
        CmbDefaultVat.SelectedItem = App.AppSettings.App.GetEffectiveDefaultVatType();
        ChkWindowAutomations.IsChecked = App.AppSettings.Business.EnableWindowAutomations;
        ChkInstallationCertificate.IsChecked = App.AppSettings.Business.EnableInstallationCertificate;
        var app = App.AppSettings.App;
        var realProfit = App.AppSettings.RealProfit;
        var pdf = App.AppSettings.PdfStorage;
        var template = App.AppSettings.PdfTemplate;
        var database = App.AppSettings.Database;
        var mail = App.AppSettings.Mail;

        CmbDatabaseProvider.SelectedItem = DatabaseSettingsModel.AvailableProviders.Contains(database.Provider)
            ? database.Provider
            : DatabaseSettingsModel.SqlServerProvider;
        TxtDatabaseServer.Text = database.Server;
        TxtDatabasePort.Text = database.Port?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        TxtDatabaseName.Text = database.Database;
        TxtDatabaseUsername.Text = database.Username;
        TxtDatabasePassword.Password = database.Password;
        ChkMailEnabled.IsChecked = mail.Enabled;
        TxtMailSmtpServer.Text = mail.SmtpServer;
        TxtMailPort.Text = mail.Port.ToString(CultureInfo.InvariantCulture);
        ChkMailUseSsl.IsChecked = mail.UseSsl;
        TxtMailUsername.Text = mail.Username;
        TxtMailPassword.Password = mail.Password;
        TxtMailSenderEmail.Text = mail.SenderEmail;
        TxtMailSenderName.Text = mail.SenderName;
        TxtMailSubject.Text = mail.DefaultSubject;
        TxtMailBody.Text = mail.DefaultBody;
        TxtSupplierOrderMailSubject.Text = mail.SupplierOrderSubjectTemplate;
        TxtSupplierOrderMailBody.Text = mail.SupplierOrderBodyTemplate;
        ChkGeneratePdf.IsChecked = app.GeneratePDF;
        ChkRestoreMissingPdfsOnStartup.IsChecked = app.RestoreMissingPdfsOnStartup;
        ChkDatabaseCostSavingMode.IsChecked = app.DatabaseCostSavingMode;
        ChkSilentStartup.IsChecked = app.IsSilentStartup;
        ChkUseVeluxLogin.IsChecked = app.UseVeluxLogin;
        TxtHistoryResultLimit.Text = app.NumberOfQuote.ToString(CultureInfo.InvariantCulture);
        TxtTempPath.Text = app.TempPath;
        TxtDeviceName.Text = app.GetEffectiveDeviceName();
        TxtDefaultProfitWorkers.Text = realProfit.Workers.ToString(CultureInfo.CurrentCulture);
        TxtDefaultProfitDays.Text = realProfit.Days.ToString("0.##", CultureInfo.CurrentCulture);
        TxtDefaultProfitHoursPerDay.Text = realProfit.HoursPerDay.ToString("0.##", CultureInfo.CurrentCulture);
        TxtDefaultProfitHourlyCost.Text = realProfit.HourlyCost.ToString("0.##", CultureInfo.CurrentCulture);
        TxtDefaultProfitReductionPercentage.Text = realProfit.ProfitReductionPercentage.ToString("0.##", CultureInfo.CurrentCulture);
        TxtWindowProductPrefixes.Text = string.Join(Environment.NewLine, realProfit.WindowProductPrefixes);
        _catalogIdsCompatible = string.IsNullOrWhiteSpace(realProfit.WindowMaterialCatalogIdentity) ||
            string.Equals(
                realProfit.WindowMaterialCatalogIdentity,
                _displayedCatalogIdentity,
                StringComparison.OrdinalIgnoreCase);
        BorderWindowMaterialCatalogIdentityWarning.Visibility = _catalogIdsCompatible
            ? Visibility.Collapsed
            : Visibility.Visible;
        TxtWindowMaterialCatalogIdentityWarning.Text = _catalogIdsCompatible
            ? string.Empty
            : "Queste regole provengono da un altro catalogo. Gli ID salvati non vengono usati: verifica e riseleziona le regole attive. Le regole disattivate manterranno i nomi, ma gli ID non risolti del vecchio catalogo saranno rimossi.";
        _windowMaterialRuleEditors.Clear();
        foreach (WindowMaterialRuleSettingsModel rule in realProfit.WindowMaterialRules)
        {
            _windowMaterialRuleEditors.Add(WindowMaterialRuleEditor.FromSettings(
                rule,
                LaborCatalog,
                CompanyMaterialCatalog,
                useCatalogIds: _catalogIdsCompatible));
        }
        UpdateWindowMaterialRulesEmptyState();

        TxtPdfRootPath.Text = pdf.RootPath;
        TxtHistorySubFolder.Text = pdf.HistorySubFolder ?? string.Empty;
        TxtCustomerFolderPattern.Text = pdf.CustomerFolderPattern ?? string.Empty;
        TxtPdfFileNamePattern.Text = pdf.PdfFileNamePattern ?? string.Empty;
        CmbPdfTemplate.SelectedItem = PdfTemplateSettingsModel.AvailableTemplates.Contains(template.ActiveTemplate)
            ? template.ActiveTemplate
            : "Standard";
        TxtPdfNotesTitle.Text = template.NotesTitle;
        TxtPdfFooterText.Text = template.FooterText;
        TxtPdfSignatureText.Text = template.SignatureText;
        ChkPdfShowTemplateName.IsChecked = template.ShowTemplateName;
        RefreshAutomaticUpdateStatus();

        if (database.RequiresCredentialReset)
        {
            Loaded += (_, _) => MessageBox.Show(
                "Le credenziali SQL salvate appartengono a un altro utente Windows o a un altro PC. Inseriscile nuovamente e salva.",
                "Credenziali SQL da reinserire",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        if (mail.RequiresCredentialReset)
        {
            Loaded += (_, _) => MessageBox.Show(
                "La password email salvata appartiene a un altro utente Windows o a un altro PC. Inseriscila nuovamente e salva.",
                "Password email da reinserire",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_isSaving || _isRefreshingShared) return;
        var invalidEmployee = _employees.FirstOrDefault(employee => string.IsNullOrWhiteSpace(employee.FirstName));
        if (invalidEmployee != null)
        {
            TabEmployees.IsSelected = true;
            UpdateLayout();
            FocusEmployee(invalidEmployee);
            MessageBox.Show(this, "Inserisci il nome di ogni dipendente. Il cognome è facoltativo.",
                "Dipendenti", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        List<EmployeeSettingsModel> employees;
        WorkScheduleSettings scheduleSettings;
        try
        {
            employees = _employees.Select(employee => employee.CreateValidatedCopy()).ToList();
            EmployeeChanges.ValidateUniqueAbbreviations(employees);
        }
        catch (InvalidOperationException ex)
        {
            TabEmployees.IsSelected = true;
            MessageBox.Show(this, ex.Message, "Dipendenti", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try { scheduleSettings = ReadScheduleSettings(); }
        catch (InvalidOperationException ex)
        {
            ShowValidationError(TabCalendarSettings, TxtFullDayStart, ex.Message);
            return;
        }
        bool scheduleChanged = !ScheduleSettingsEqual(_originalScheduleSettings, scheduleSettings);

        if (string.IsNullOrWhiteSpace(TxtPdfRootPath.Text))
        {
            ShowValidationError(TabPdfSettings, TxtPdfRootPath, "Inserisci la cartella principale dei PDF.");
            return;
        }

        if (!int.TryParse(TxtHistoryResultLimit.Text, out int historyResultLimit) || historyResultLimit < 1)
        {
            ShowValidationError(
                TabPdfSettings,
                TxtHistoryResultLimit,
                "Il numero di risultati nello storico deve essere maggiore di zero.");
            return;
        }

        string databaseProvider = CmbDatabaseProvider.SelectedItem?.ToString() ?? DatabaseSettingsModel.SqlServerProvider;
        string databaseServer = TxtDatabaseServer.Text.Trim();
        int? databasePort = null;
        if (!string.IsNullOrWhiteSpace(TxtDatabasePort.Text))
        {
            if (!int.TryParse(TxtDatabasePort.Text, out int parsedDatabasePort) ||
                parsedDatabasePort <= 0 ||
                parsedDatabasePort > 65535)
            {
                ShowValidationError(
                    TabDatabaseSettings,
                    TxtDatabasePort,
                    "La porta del database deve essere un numero valido tra 1 e 65535.");
                return;
            }

            databasePort = parsedDatabasePort;
        }

        string databaseName = TxtDatabaseName.Text.Trim();
        string databaseUsername = TxtDatabaseUsername.Text.Trim();
        string databasePassword = TxtDatabasePassword.Password;
        bool mailEnabled = ChkMailEnabled.IsChecked == true;
        string mailSmtpServer = TxtMailSmtpServer.Text.Trim();
        string mailUsername = TxtMailUsername.Text.Trim();
        string mailPassword = TxtMailPassword.Password;
        string mailSenderEmail = TxtMailSenderEmail.Text.Trim();
        string mailSenderName = TxtMailSenderName.Text.Trim();
        string mailSubject = TxtMailSubject.Text.Trim();
        string mailBody = TxtMailBody.Text;
        string supplierOrderMailSubject = TxtSupplierOrderMailSubject.Text.Trim();
        string supplierOrderMailBody = TxtSupplierOrderMailBody.Text;

        if (!supplierOrderMailBody.Contains("{Materials}", StringComparison.OrdinalIgnoreCase))
        {
            ShowValidationError(
                TabEmailSettings,
                TxtSupplierOrderMailBody,
                "Il testo dell'email per gli ordini fornitori deve contenere il segnaposto {Materials}.");
            return;
        }

        if (!int.TryParse(TxtDefaultProfitWorkers.Text, out int defaultProfitWorkers) ||
            defaultProfitWorkers < 1)
        {
            ShowValidationError(
                TabGeneralSettings,
                TxtDefaultProfitWorkers,
                "Il numero predefinito di operai deve essere un intero maggiore di zero.");
            return;
        }

        if (!TryParseSettingsDouble(TxtDefaultProfitDays.Text, allowZero: false, out double defaultProfitDays))
        {
            ShowValidationError(
                TabGeneralSettings,
                TxtDefaultProfitDays,
                "Il numero predefinito di giorni deve essere maggiore di zero.");
            return;
        }

        if (!TryParseSettingsDouble(
                TxtDefaultProfitHoursPerDay.Text,
                allowZero: false,
                out double defaultProfitHoursPerDay))
        {
            ShowValidationError(
                TabGeneralSettings,
                TxtDefaultProfitHoursPerDay,
                "Le ore giornaliere predefinite devono essere maggiori di zero.");
            return;
        }

        if (!TryParseSettingsDouble(
                TxtDefaultProfitHourlyCost.Text,
                allowZero: true,
                out double defaultProfitHourlyCost))
        {
            ShowValidationError(
                TabGeneralSettings,
                TxtDefaultProfitHourlyCost,
                "Il costo orario predefinito deve essere uguale o maggiore di zero.");
            return;
        }

        if (!TryParseSettingsDouble(
                TxtDefaultProfitReductionPercentage.Text,
                allowZero: true,
                out double defaultProfitReductionPercentage) ||
            defaultProfitReductionPercentage > 100)
        {
            ShowValidationError(
                TabGeneralSettings,
                TxtDefaultProfitReductionPercentage,
                "La riduzione prudenziale deve essere compresa tra 0 e 100.");
            return;
        }

        List<string> windowProductPrefixes = TxtWindowProductPrefixes.Text
            .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(prefix => prefix.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!TryBuildWindowMaterialRules(
                out List<WindowMaterialRuleSettingsModel> windowMaterialRules,
                out WindowMaterialRuleEditor? invalidRule,
                out string windowMaterialRuleError))
        {
            ShowWindowMaterialRuleValidationError(invalidRule, windowMaterialRuleError);
            return;
        }

        if (!int.TryParse(TxtMailPort.Text, out int mailPort) || mailPort <= 0 || mailPort > 65535)
        {
            ShowValidationError(
                TabEmailSettings,
                TxtMailPort,
                "La porta SMTP deve essere un numero valido tra 1 e 65535.");
            return;
        }

        if (mailEnabled && string.IsNullOrWhiteSpace(mailUsername))
        {
            ShowValidationError(
                TabEmailSettings,
                TxtMailUsername,
                "Inserisci l'utente o l'indirizzo email SMTP.");
            return;
        }

        if (mailEnabled && string.IsNullOrWhiteSpace(mailPassword))
        {
            ShowValidationError(
                TabEmailSettings,
                TxtMailPassword,
                "Inserisci la password SMTP.");
            return;
        }

        bool employeesSaved = false;
        bool calendarSaved = false;
        try
        {
            var employeeChanges = EmployeeChanges.Between(_originalEmployees, employees);
            var requestedDatabase = new DatabaseSettingsModel
            {
                Provider = DatabaseSettingsModel.NormalizeProvider(databaseProvider), Server = databaseServer,
                Port = databasePort, Database = databaseName, Username = databaseUsername, Password = databasePassword
            };
            if (requestedDatabase.IsConfigured) _ = requestedDatabase.BuildConnectionString();
            if ((employeeChanges.Count > 0 || scheduleChanged) && !string.Equals(_displayedCatalogIdentity,
                    requestedDatabase.GetCatalogIdentity(), StringComparison.Ordinal))
            {
                ShowValidationError(TabDatabaseSettings, TxtDatabaseName,
                    "Salva prima le modifiche condivise a dipendenti e calendario. Poi puoi cambiare la connessione al database e riavviare l'applicazione.");
                return;
            }
            _isSaving = true;
            IsEnabled = false;
            if (scheduleChanged && (!_scheduleSettingsCurrent || App.WorkSchedule == null))
                throw new InvalidOperationException("Ricarica gli orari dal database prima di modificarli.");
            if (employeeChanges.Count > 0)
            {
                if (!_employeeListCurrent || App.EmployeeDirectory == null)
                    throw new InvalidOperationException("Ricarica l'elenco dal database prima di modificare i dipendenti.");
                var saved = await App.EmployeeDirectory.SaveAsync(_originalEmployees, employees, _employeeLoadCts.Token);
                employeesSaved = true;
                SetEmployeeSnapshot(saved);
                SetEmployeeEditingEnabled(saved.IsCurrent);
            }
            if (scheduleChanged)
            {
                var saved = await App.WorkSchedule!.SaveSettingsAsync(scheduleSettings, _employeeLoadCts.Token);
                calendarSaved = true;
                SetScheduleSettingsSnapshot(saved);
            }

            var app = App.AppSettings.App;
            var realProfit = App.AppSettings.RealProfit;
            var pdf = App.AppSettings.PdfStorage;
            var template = App.AppSettings.PdfTemplate;
            var database = App.AppSettings.Database;
            var mail = App.AppSettings.Mail;

            database.Provider = DatabaseSettingsModel.NormalizeProvider(databaseProvider);
            database.Server = databaseServer;
            database.Port = databasePort;
            database.Database = databaseName;
            database.Username = databaseUsername;
            database.Password = databasePassword;
            database.RequiresCredentialReset = false;

            mail.Enabled = mailEnabled;
            mail.SmtpServer = mailSmtpServer;
            mail.Port = mailPort;
            mail.UseSsl = ChkMailUseSsl.IsChecked == true;
            mail.Username = mailUsername;
            mail.Password = mailPassword;
            mail.SenderEmail = mailSenderEmail;
            mail.SenderName = mailSenderName;
            mail.DefaultSubject = mailSubject;
            mail.DefaultBody = mailBody;
            mail.SupplierOrderSubjectTemplate = supplierOrderMailSubject;
            mail.SupplierOrderBodyTemplate = supplierOrderMailBody;
            mail.RequiresCredentialReset = false;
            mail.Normalize();

            if (database.IsConfigured)
                _ = database.BuildConnectionString();
            if (mail.Enabled)
                mail.ValidateForSend();
            app.DefaultVatType = CmbDefaultVat.SelectedItem?.ToString() ?? "22%";
            App.AppSettings.Business.EnableWindowAutomations = ChkWindowAutomations.IsChecked == true;
            App.AppSettings.Business.EnableInstallationCertificate = ChkInstallationCertificate.IsChecked == true;
            app.GeneratePDF = ChkGeneratePdf.IsChecked == true;
            app.RestoreMissingPdfsOnStartup = ChkRestoreMissingPdfsOnStartup.IsChecked == true;
            app.DatabaseCostSavingMode = ChkDatabaseCostSavingMode.IsChecked == true;
            app.IsSilentStartup = ChkSilentStartup.IsChecked == true;
            app.UseVeluxLogin = ChkUseVeluxLogin.IsChecked == true;
            app.NumberOfQuote = historyResultLimit;
            app.TempPath = TxtTempPath.Text.Trim();
            app.DeviceName = string.IsNullOrWhiteSpace(TxtDeviceName.Text)
                ? Environment.MachineName
                : TxtDeviceName.Text.Trim();

            realProfit.Workers = defaultProfitWorkers;
            realProfit.Days = defaultProfitDays;
            realProfit.HoursPerDay = defaultProfitHoursPerDay;
            realProfit.HourlyCost = defaultProfitHourlyCost;
            realProfit.ProfitReductionPercentage = defaultProfitReductionPercentage;
            realProfit.WindowProductPrefixes = windowProductPrefixes;
            realProfit.WindowMaterialCatalogIdentity = _displayedCatalogIdentity;
            realProfit.WindowMaterialRulesSchemaVersion = RealProfitSettingsModel.CurrentWindowMaterialRulesSchemaVersion;
            realProfit.WindowMaterialRules = windowMaterialRules;
            realProfit.Normalize();

            pdf.RootPath = TxtPdfRootPath.Text.Trim();
            pdf.HistorySubFolder = EmptyToNull(TxtHistorySubFolder.Text);
            pdf.CustomerFolderPattern = EmptyToNull(TxtCustomerFolderPattern.Text);
            pdf.PdfFileNamePattern = EmptyToNull(TxtPdfFileNamePattern.Text);
            template.ActiveTemplate = CmbPdfTemplate.SelectedItem?.ToString() ?? "Standard";
            template.NotesTitle = TxtPdfNotesTitle.Text.Trim();
            template.FooterText = TxtPdfFooterText.Text.Trim();
            template.SignatureText = TxtPdfSignatureText.Text.Trim();
            template.ShowTemplateName = ChkPdfShowTemplateName.IsChecked == true;
            template.Normalize();

            App.AppSettings.Save();

            MessageBox.Show(
                "Impostazioni salvate. Riavvia l'applicazione se hai modificato la connessione al database.",
                "Impostazioni",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            _isSaving = false;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                (employeesSaved || calendarSaved
                    ? $"Salvati nel database: {string.Join(" e ", new[] { employeesSaved ? "dipendenti" : null, calendarSaved ? "orari del calendario" : null }.Where(x => x != null))}. Non è stato possibile completare le altre modifiche."
                    : "Impossibile salvare le impostazioni.") + $"\n\n{ex.Message}",
                "Errore salvataggio",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isSaving = false;
            IsEnabled = true;
        }
    }

    private void SetEmployeeSnapshot(EmployeeDirectorySnapshot snapshot)
    {
        _employees.Clear();
        foreach (var employee in snapshot.Employees) _employees.Add(employee.CreateValidatedCopy());
        _originalEmployees = snapshot.Employees.Select(x => x.CreateValidatedCopy()).ToList();
        _employeeListCurrent = snapshot.IsCurrent;
        UpdateEmployeesEmptyState();
    }

    private void SetEmployeeEditingEnabled(bool enabled)
    {
        BtnAddEmployee.IsEnabled = enabled;
        ItemsEmployees.IsEnabled = enabled;
    }

    private Task RefreshSelectedSharedSettingsAsync(bool automatic = true)
    {
        var tab = SettingsTabs.SelectedItem as TabItem;
        return tab == TabEmployees || tab == TabCalendarSettings
            ? StartSharedTabRefreshAsync(tab, automatic) : Task.CompletedTask;
    }

    private async void OnSettingsTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != SettingsTabs || !IsLoaded || _isSaving) return;
        if (_isRefreshingShared)
        {
            _sharedRefreshCts?.Cancel();
            await _sharedRefreshTask;
        }
        if (!_employeeLoadCts.IsCancellationRequested) await RefreshSelectedSharedSettingsAsync();
    }

    private Task StartSharedTabRefreshAsync(TabItem tab, bool automatic)
    {
        if (_isRefreshingShared || _isSaving || _employeeLoadCts.IsCancellationRequested) return Task.CompletedTask;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_employeeLoadCts.Token);
        _sharedRefreshCts = cancellation;
        _sharedRefreshTask = RefreshSharedTabCoreAsync(tab, automatic, cancellation);
        return _sharedRefreshTask;
    }

    private async Task RefreshSharedTabCoreAsync(TabItem tab, bool automatic, CancellationTokenSource cancellation)
    {
        _isRefreshingShared = true;
        BtnSaveSettings.IsEnabled = false;
        BtnRefreshEmployees.IsEnabled = false;
        BtnRefreshCalendarSettings.IsEnabled = false;
        try
        {
            if (tab == TabEmployees) await RefreshEmployeesAsync(automatic, cancellation.Token);
            else await RefreshScheduleSettingsAsync(automatic, cancellation.Token);
        }
        finally
        {
            _isRefreshingShared = false;
            BtnSaveSettings.IsEnabled = true;
            BtnRefreshEmployees.IsEnabled = App.EmployeeDirectory != null;
            BtnRefreshCalendarSettings.IsEnabled = App.WorkSchedule != null;
            _sharedRefreshCts = null;
            cancellation.Dispose();
        }
    }

    private static bool EmployeeListsEqual(IEnumerable<EmployeeSettingsModel> first,
        IEnumerable<EmployeeSettingsModel> second, bool includeRevision = false)
    {
        static string Text(string? value) => value?.Trim() ?? string.Empty;
        var left = first.OrderBy(x => x.Id).ToArray();
        var right = second.OrderBy(x => x.Id).ToArray();
        return left.Length == right.Length && left.Zip(right).All(pair => pair.First.Id == pair.Second.Id &&
            Text(pair.First.FirstName) == Text(pair.Second.FirstName) &&
            Text(pair.First.LastName) == Text(pair.Second.LastName) &&
            Text(pair.First.Abbreviation).ToUpperInvariant() == Text(pair.Second.Abbreviation).ToUpperInvariant() &&
            (!includeRevision || pair.First.Revision == pair.Second.Revision));
    }

    private async Task RefreshEmployeesAsync(bool automatic, CancellationToken token)
    {
        bool modified = !EmployeeListsEqual(_originalEmployees, _employees);
        if (string.IsNullOrWhiteSpace(TxtEmployeeSyncStatus.Text))
            TxtEmployeeSyncStatus.Text = "Caricamento dei dipendenti dal database...";
        if (!automatic)
        {
            SetEmployeeEditingEnabled(false);
            TxtEmployeeSyncStatus.Text = "Caricamento dei dipendenti dal database...";
        }
        try
        {
            var directory = App.EmployeeDirectory;
            if (directory == null)
            {
                TxtEmployeeSyncStatus.Text = "Configura il database e riavvia l'applicazione per gestire i dipendenti condivisi.";
                return;
            }
            var snapshot = await directory.GetLatestAsync(token);
            if (token.IsCancellationRequested) return;
            modified = !EmployeeListsEqual(_originalEmployees, _employees);
            if (!automatic || !modified)
            {
                if (!automatic || !EmployeeListsEqual(_originalEmployees, snapshot.Employees, includeRevision: true))
                    SetEmployeeSnapshot(snapshot);
            }
            _employeeListCurrent = snapshot.IsCurrent;
            SetEmployeeEditingEnabled(snapshot.IsCurrent);
            TxtEmployeeSyncStatus.Text = snapshot.IsCurrent
                ? automatic && modified && !EmployeeListsEqual(_originalEmployees, snapshot.Employees, includeRevision: true)
                    ? "L'elenco è cambiato su un altro PC. Le tue modifiche non salvate sono conservate; ricarica l'elenco per vedere gli aggiornamenti."
                    : modified && automatic
                        ? "Modifiche non ancora salvate. L'elenco condiviso viene controllato automaticamente."
                        : "Elenco aggiornato dal database. Le modifiche salvate saranno disponibili su tutti i PC."
                : "Database non disponibile: visualizzi l'ultimo elenco scaricato. Per modificarlo, riconnettiti e premi Ricarica elenco.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _employeeListCurrent = false;
            SetEmployeeEditingEnabled(false);
            TxtEmployeeSyncStatus.Text = ex.Message;
        }
    }

    private async void OnRefreshEmployeesClick(object sender, RoutedEventArgs e)
    {
        // Reload is explicit: never replace unsaved edits during background sync.
        if (_isRefreshingShared || _isSaving) return;
        bool modified = !EmployeeListsEqual(_originalEmployees, _employees);
        if (modified && MessageBox.Show(this, "Ricaricare l'elenco e scartare le modifiche ai dipendenti non ancora salvate?",
                "Ricarica dipendenti", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (_isRefreshingShared || _isSaving || _employeeLoadCts.IsCancellationRequested) return;
        await StartSharedTabRefreshAsync(TabEmployees, automatic: false);
    }

    private WorkScheduleSettings ReadScheduleSettings()
    {
        static int Parse(TextBox input)
        {
            if (!WorkScheduleSettings.TryParseTime(input.Text, out int minutes))
                throw new InvalidOperationException("Inserisci gli orari del calendario nel formato HH:mm, per esempio 08:00.");
            return minutes;
        }
        return new WorkScheduleSettings
        {
            Revision = _originalScheduleSettings.Revision,
            FullDayStartMinutes = Parse(TxtFullDayStart), FullDayEndMinutes = Parse(TxtFullDayEnd),
            MorningStartMinutes = Parse(TxtMorningStart), MorningEndMinutes = Parse(TxtMorningEnd),
            AfternoonStartMinutes = Parse(TxtAfternoonStart), AfternoonEndMinutes = Parse(TxtAfternoonEnd),
            UseEmployeeAbbreviations = ChkCalendarUseAbbreviations.IsChecked == true
        }.CreateValidatedCopy();
    }

    private static bool ScheduleSettingsEqual(WorkScheduleSettings first, WorkScheduleSettings second) =>
        first.FullDayStartMinutes == second.FullDayStartMinutes && first.FullDayEndMinutes == second.FullDayEndMinutes &&
        first.MorningStartMinutes == second.MorningStartMinutes && first.MorningEndMinutes == second.MorningEndMinutes &&
        first.AfternoonStartMinutes == second.AfternoonStartMinutes && first.AfternoonEndMinutes == second.AfternoonEndMinutes &&
        first.UseEmployeeAbbreviations == second.UseEmployeeAbbreviations;

    private bool HasScheduleSettingsChanges()
    {
        if (!_scheduleSettingsLoaded) return false;
        try { return !ScheduleSettingsEqual(_originalScheduleSettings, ReadScheduleSettings()); }
        catch (InvalidOperationException) { return true; }
    }

    private void SetScheduleSettingsSnapshot(WorkScheduleSettingsSnapshot snapshot)
    {
        _originalScheduleSettings = snapshot.Settings.CreateValidatedCopy();
        _scheduleSettingsCurrent = snapshot.IsCurrent;
        _scheduleSettingsLoaded = true;
        TxtFullDayStart.Text = WorkScheduleSettings.FormatTime(snapshot.Settings.FullDayStartMinutes);
        TxtFullDayEnd.Text = WorkScheduleSettings.FormatTime(snapshot.Settings.FullDayEndMinutes);
        TxtMorningStart.Text = WorkScheduleSettings.FormatTime(snapshot.Settings.MorningStartMinutes);
        TxtMorningEnd.Text = WorkScheduleSettings.FormatTime(snapshot.Settings.MorningEndMinutes);
        TxtAfternoonStart.Text = WorkScheduleSettings.FormatTime(snapshot.Settings.AfternoonStartMinutes);
        TxtAfternoonEnd.Text = WorkScheduleSettings.FormatTime(snapshot.Settings.AfternoonEndMinutes);
        ChkCalendarUseAbbreviations.IsChecked = snapshot.Settings.UseEmployeeAbbreviations;
        CalendarSettingsEditor.IsEnabled = snapshot.IsCurrent;
    }

    private async Task RefreshScheduleSettingsAsync(bool automatic, CancellationToken token)
    {
        bool modified = HasScheduleSettingsChanges();
        if (string.IsNullOrWhiteSpace(TxtCalendarSyncStatus.Text))
            TxtCalendarSyncStatus.Text = "Caricamento degli orari dal database...";
        if (!automatic)
        {
            CalendarSettingsEditor.IsEnabled = false;
            TxtCalendarSyncStatus.Text = "Caricamento degli orari dal database...";
        }
        try
        {
            if (App.WorkSchedule == null)
            {
                TxtCalendarSyncStatus.Text = "Configura il database e riavvia l'applicazione per gestire il calendario condiviso.";
                return;
            }
            var snapshot = await App.WorkSchedule.GetSettingsAsync(token);
            if (token.IsCancellationRequested) return;
            modified = HasScheduleSettingsChanges();
            bool remoteChanged = snapshot.Settings.Revision != _originalScheduleSettings.Revision;
            if (!automatic || !modified)
            {
                if (!automatic || !_scheduleSettingsLoaded || remoteChanged || !ScheduleSettingsEqual(_originalScheduleSettings, snapshot.Settings))
                    SetScheduleSettingsSnapshot(snapshot);
            }
            _scheduleSettingsCurrent = snapshot.IsCurrent;
            CalendarSettingsEditor.IsEnabled = snapshot.IsCurrent;
            TxtCalendarSyncStatus.Text = !snapshot.IsCurrent
                ? "Database non disponibile: gli ultimi orari scaricati sono consultabili. Le modifiche richiedono la connessione."
                : modified && automatic && remoteChanged
                    ? "Gli orari sono cambiati su un altro PC. Le tue modifiche non salvate sono conservate; ricarica per vedere gli aggiornamenti."
                    : modified && automatic
                        ? "Modifiche non ancora salvate. Gli orari condivisi vengono controllati automaticamente."
                        : $"Orari aggiornati dal database{(snapshot.UpdatedAtUtc is { } updated ? $" alle {updated.ToLocalTime():HH:mm:ss}" : string.Empty)}. Aggiornamento automatico ogni 10 secondi.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _scheduleSettingsCurrent = false;
            CalendarSettingsEditor.IsEnabled = false;
            TxtCalendarSyncStatus.Text = ex.Message;
        }
    }

    private async void OnRefreshCalendarSettingsClick(object sender, RoutedEventArgs e)
    {
        if (_isRefreshingShared || _isSaving) return;
        if (HasScheduleSettingsChanges() && MessageBox.Show(this,
                "Ricaricare gli orari e scartare le modifiche al calendario non ancora salvate?",
                "Ricarica orari", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (_isRefreshingShared || _isSaving || _employeeLoadCts.IsCancellationRequested) return;
        // Explicit reload of one tab must preserve unsaved edits in the other tab.
        await StartSharedTabRefreshAsync(TabCalendarSettings, automatic: false);
    }

    private void OnBrowsePdfRootClick(object sender, RoutedEventArgs e)
        => BrowseFolder(TxtPdfRootPath);

    private void OnAddEmployeeClick(object sender, RoutedEventArgs e)
    {
        var employee = new EmployeeSettingsModel();
        _employees.Add(employee);
        UpdateEmployeesEmptyState();
        UpdateLayout();
        FocusEmployee(employee);
    }

    private void OnRemoveEmployeeClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EmployeeSettingsModel employee })
            _employees.Remove(employee);
        UpdateEmployeesEmptyState();
    }

    private void UpdateEmployeesEmptyState()
        => TxtNoEmployees.Visibility = _employees.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void FocusEmployee(EmployeeSettingsModel employee)
    {
        if (ItemsEmployees.ItemContainerGenerator.ContainerFromItem(employee) is ContentPresenter container)
        {
            container.BringIntoView();
            container.ApplyTemplate();
            if (container.ContentTemplate.FindName("TxtEmployeeFirstName", container) is TextBox textBox)
                textBox.Focus();
        }
    }

    private void OnAddWindowMaterialRuleClick(object sender, RoutedEventArgs e)
    {
        var editor = WindowMaterialRuleEditor.CreateNew();
        _windowMaterialRuleEditors.Add(editor);
        UpdateWindowMaterialRulesEmptyState();
        UpdateLayout();
        if (ItemsWindowMaterialRules.ItemContainerGenerator.ContainerFromItem(editor) is FrameworkElement container)
            container.BringIntoView();
    }

    private void OnRemoveWindowMaterialRuleClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is WindowMaterialRuleEditor editor)
        {
            _windowMaterialRuleEditors.Remove(editor);
            UpdateWindowMaterialRulesEmptyState();
        }
    }

    private void UpdateWindowMaterialRulesEmptyState()
    {
        TxtNoWindowMaterialRules.Visibility = _windowMaterialRuleEditors.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private bool TryBuildWindowMaterialRules(
        out List<WindowMaterialRuleSettingsModel> rules,
        out WindowMaterialRuleEditor? invalidRule,
        out string error)
    {
        rules = [];
        invalidRule = null;
        error = string.Empty;

        foreach (WindowMaterialRuleEditor editor in _windowMaterialRuleEditors)
        {
            bool hasSelectedLabor = editor.SelectedLabor != null &&
                LaborCatalog.Contains(editor.SelectedLabor);
            bool hasSelectedMaterial = editor.SelectedMaterial != null &&
                CompanyMaterialCatalog.Contains(editor.SelectedMaterial);

            if (editor.Enabled &&
                (!hasSelectedLabor || editor.SelectedLabor!.PersistentId <= 0))
            {
                invalidRule = editor;
                error = "Per una regola attiva devi selezionare una lavorazione con ID valido dall'elenco del catalogo.";
                return false;
            }

            if (editor.Enabled &&
                (!hasSelectedMaterial || editor.SelectedMaterial!.PersistentId <= 0))
            {
                invalidRule = editor;
                error = "Per una regola attiva devi selezionare un materiale aziendale con ID valido dall'elenco del catalogo.";
                return false;
            }

            if (!TryParseSettingsDecimal(editor.QuantityParameterText, out decimal quantityParameter))
            {
                invalidRule = editor;
                error = "Il parametro quantità deve essere un numero maggiore di zero.";
                return false;
            }

            if (!string.Equals(
                    editor.CalculationMode,
                    WindowMaterialRuleSettingsModel.PerimeterCalculationMode,
                    StringComparison.Ordinal) &&
                !string.Equals(
                    editor.CalculationMode,
                    WindowMaterialRuleSettingsModel.FixedPerWindowCalculationMode,
                    StringComparison.Ordinal))
            {
                invalidRule = editor;
                error = "Seleziona un tipo di calcolo valido.";
                return false;
            }

            WindowMaterialRuleSettingsModel rule = editor.CreateSettingsRule(
                quantityParameter,
                discardUnresolvedCatalogIds: !_catalogIdsCompatible);
            rule.Normalize();

            if (rules.Any(existing => AreIdenticalRules(existing, rule)))
            {
                invalidRule = editor;
                error = $"Esiste già una regola identica per “{rule.LaborName}” e “{rule.MaterialName}”.";
                return false;
            }

            rules.Add(rule);
        }

        return true;
    }

    private void ShowWindowMaterialRuleValidationError(
        WindowMaterialRuleEditor? invalidRule,
        string message)
    {
        SettingsTabs.SelectedItem = TabGeneralSettings;
        UpdateLayout();
        if (invalidRule != null &&
            ItemsWindowMaterialRules.ItemContainerGenerator.ContainerFromItem(invalidRule) is FrameworkElement container)
        {
            container.BringIntoView();
        }
        else
        {
            ItemsWindowMaterialRules.BringIntoView();
        }

        MessageBox.Show(
            message,
            "Impostazioni non valide",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        ItemsWindowMaterialRules.Focus();
    }

    private static bool AreIdenticalRules(
        WindowMaterialRuleSettingsModel left,
        WindowMaterialRuleSettingsModel right)
    {
        bool sameLabor = left.LaborCatalogId.HasValue && right.LaborCatalogId.HasValue
            ? left.LaborCatalogId == right.LaborCatalogId
            : string.Equals(left.LaborName, right.LaborName, StringComparison.OrdinalIgnoreCase);
        bool sameMaterial = left.MaterialCatalogId.HasValue && right.MaterialCatalogId.HasValue
            ? left.MaterialCatalogId == right.MaterialCatalogId
            : string.Equals(left.MaterialName, right.MaterialName, StringComparison.OrdinalIgnoreCase);
        return sameLabor &&
               sameMaterial &&
               left.IsWindowAutomation == right.IsWindowAutomation &&
               (!left.IsWindowAutomation ||
                string.Equals(left.CalculationMode, right.CalculationMode, StringComparison.OrdinalIgnoreCase)) &&
               left.QuantityParameter == right.QuantityParameter;
    }

    private static IReadOnlyList<Item> CreateCatalogSnapshot(
        IEnumerable<Item> source,
        bool companyMaterialsOnly)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source
            .Where(item =>
                item != null &&
                !string.IsNullOrWhiteSpace(item.Name) &&
                (!companyMaterialsOnly || item.IsCompanyMaterial))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.PersistentId)
            .ToArray();
    }

    private void OnBrowseTempPathClick(object sender, RoutedEventArgs e)
        => BrowseFolder(TxtTempPath);

    private static void BrowseFolder(System.Windows.Controls.TextBox target)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Seleziona cartella",
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(target.Text))
            dialog.InitialDirectory = target.Text;

        if (dialog.ShowDialog() == true)
            target.Text = dialog.FolderName;
    }

    private void OnClearVeluxSessionClick(object sender, RoutedEventArgs e)
    {
        try
        {
            VeluxSessionStorage.Clear();
            MessageBox.Show(
                "Sessione Velux rimossa. Il login verra richiesto alla prossima ricerca dopo il riavvio dell'app.",
                "Sessione Velux",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Impossibile rimuovere la sessione Velux.\n\n{ex.Message}",
                "Errore sessione Velux",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnRunUpdaterClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string? scriptPath = UpdaterLauncherService.ResolveUpdaterScriptPath();
            if (string.IsNullOrWhiteSpace(scriptPath))
            {
                MessageBox.Show(
                    this,
                    "Script updater non trovato. Metti Update-EdilPaint.ps1 nella cartella updater accanto al programma o in tools/updater nel progetto.",
                    "Aggiornamento",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            UpdaterLauncherService.RefreshUpdaterScriptFromBundledCopy(scriptPath);

            string settingsPath = Path.Combine(Path.GetDirectoryName(scriptPath)!, "updater-settings.json");
            if (!File.Exists(settingsPath))
            {
                MessageBox.Show(
                    this,
                    $"File updater-settings.json non trovato accanto allo script.\n\nPercorso atteso:\n{settingsPath}",
                    "Aggiornamento",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                this,
                $"Verrà avviato l'updater e l'applicazione verrà chiusa.\n\nScript:\n{scriptPath}\n\nContinuare?",
                "Aggiorna programma",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            UpdaterLauncherService.StartUpdater(scriptPath);
            AppShutdownManager.RequestShutdown();
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Impossibile avviare l'aggiornamento.\n\n{ex.Message}",
                "Aggiornamento",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void OnAutomaticUpdatesChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingAutomaticUpdatesControl)
            return;

        try
        {
            if (ChkAutomaticUpdates.IsChecked == true)
            {
                string? scriptPath = GetConfiguredUpdaterScriptPath();
                if (string.IsNullOrWhiteSpace(scriptPath))
                    return;

                AutomaticUpdateStatus status = await Task.Run(
                    () => UpdaterAutoUpdateService.Enable(scriptPath));
                TxtAutomaticUpdatesStatus.Text = status.Description;
            }
            else
            {
                await Task.Run(UpdaterAutoUpdateService.Disable);
                TxtAutomaticUpdatesStatus.Text = "Disattivati su questo PC.";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Impossibile modificare gli aggiornamenti automatici.\n\n{ex.Message}",
                "Aggiornamenti automatici",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            RefreshAutomaticUpdateStatus();
        }
    }

    private string? GetConfiguredUpdaterScriptPath()
    {
        string? scriptPath = UpdaterLauncherService.ResolveUpdaterScriptPath();
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            MessageBox.Show(
                this,
                "Script updater non trovato. Prima installa o configura l'updater, poi potrai attivare gli aggiornamenti automatici.",
                "Aggiornamenti automatici",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return null;
        }

        string settingsPath = Path.Combine(Path.GetDirectoryName(scriptPath)!, "updater-settings.json");
        if (File.Exists(settingsPath))
            return scriptPath;

        MessageBox.Show(
            this,
            $"File updater-settings.json non trovato accanto allo script.\n\nPercorso atteso:\n{settingsPath}",
            "Aggiornamenti automatici",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return null;
    }

    public void SelectDatabaseTab() => TabDatabaseSettings.IsSelected = true;

    private async void OnCompanyProfileClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var company = await App.DataService.GetCompanyAsync() ?? new Company();
            new CompanyProfileWindow(company) { Owner = this }.ShowDialog();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Dati aziendali", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void RefreshAutomaticUpdateStatus()
    {
        if (CompanyInstallationService.IsGenericInstallation)
        {
            ChkAutomaticUpdates.IsEnabled = false;
            TxtAutomaticUpdatesStatus.Text = "Aggiornamenti tramite pacchetto della propria azienda.";
            return;
        }
        _updatingAutomaticUpdatesControl = true;
        try
        {
            AutomaticUpdateStatus status = UpdaterAutoUpdateService.GetStatus();
            ChkAutomaticUpdates.IsChecked = status.IsEnabled;
            TxtAutomaticUpdatesStatus.Text = status.Description;
        }
        finally
        {
            _updatingAutomaticUpdatesControl = false;
        }
    }

    private async void OnPreviewPdfTemplateClick(object sender, RoutedEventArgs e)
    {
        var template = new PdfTemplateSettingsModel
        {
            ActiveTemplate = CmbPdfTemplate.SelectedItem?.ToString() ?? "Standard",
            NotesTitle = TxtPdfNotesTitle.Text.Trim(),
            FooterText = TxtPdfFooterText.Text.Trim(),
            SignatureText = TxtPdfSignatureText.Text.Trim(),
            ShowTemplateName = ChkPdfShowTemplateName.IsChecked == true
        };

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var previewService = new PdfTemplatePreviewService(App.DataService);
            string previewPath = await previewService.GenerateQuotePreviewAsync(template);
            PdfTemplatePreviewService.OpenPreview(previewPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Impossibile generare l'anteprima del template.\n\n{ex.Message}",
                "Anteprima template",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private void SettingsWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }

    private static string? EmptyToNull(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void ShowValidationError(TabItem tab, Control control, string message)
    {
        SettingsTabs.SelectedItem = tab;
        UpdateLayout();
        control.BringIntoView();
        MessageBox.Show(
            message,
            "Impostazioni non valide",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        control.Focus();
        if (control is TextBox textBox)
            textBox.SelectAll();
    }

    private static bool TryParseSettingsDouble(string? text, bool allowZero, out double value)
    {
        string normalized = (text ?? string.Empty).Trim().Replace(',', '.');
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
            !double.IsFinite(value))
        {
            return false;
        }

        return allowZero ? value >= 0 : value > 0;
    }

    private static bool TryParseSettingsDecimal(string? text, out decimal value)
    {
        string normalized = (text ?? string.Empty).Trim().Replace(',', '.');
        return decimal.TryParse(
                   normalized,
                   NumberStyles.Number,
                   CultureInfo.InvariantCulture,
                   out value) &&
               value > 0;
    }
}

public sealed record WindowMaterialCalculationModeOption(string Value, string Label);

public sealed class WindowMaterialRuleEditor : INotifyPropertyChanged
{
    private bool _enabled = true;
    private bool _isWindowAutomation = true;
    private int? _laborCatalogId;
    private string _laborName = string.Empty;
    private Item? _selectedLabor;
    private int? _materialCatalogId;
    private string _materialName = string.Empty;
    private Item? _selectedMaterial;
    private string _calculationMode = WindowMaterialRuleSettingsModel.PerimeterCalculationMode;
    private string _quantityParameterText = "1";

    public bool Enabled
    {
        get => _enabled;
        set => SetField(ref _enabled, value);
    }

    public bool IsWindowAutomation
    {
        get => _isWindowAutomation;
        set
        {
            if (!SetField(ref _isWindowAutomation, value))
                return;

            OnPropertyChanged(nameof(CalculationHint));
        }
    }

    public string LaborName
    {
        get => _laborName;
        set
        {
            string normalized = value ?? string.Empty;
            if (_laborName == normalized)
                return;

            _laborName = normalized;
            if (_selectedLabor != null &&
                !string.Equals(_selectedLabor.Name, normalized, StringComparison.Ordinal))
            {
                _selectedLabor = null;
                _laborCatalogId = null;
                OnPropertyChanged(nameof(SelectedLabor));
                OnPropertyChanged(nameof(LaborCatalogInfo));
            }
            else if (_selectedLabor == null)
            {
                _laborCatalogId = null;
                OnPropertyChanged(nameof(LaborCatalogInfo));
            }

            OnPropertyChanged();
        }
    }

    public Item? SelectedLabor
    {
        get => _selectedLabor;
        set
        {
            if (ReferenceEquals(_selectedLabor, value))
                return;

            _selectedLabor = value;
            _laborCatalogId = value?.PersistentId > 0 ? value.PersistentId : null;
            if (value != null)
            {
                _laborName = value.Name;
                OnPropertyChanged(nameof(LaborName));
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(LaborCatalogInfo));
        }
    }

    public int? LaborCatalogId => _laborCatalogId;

    public string MaterialName
    {
        get => _materialName;
        set
        {
            string normalized = value ?? string.Empty;
            if (_materialName == normalized)
                return;

            _materialName = normalized;
            if (_selectedMaterial != null &&
                !string.Equals(_selectedMaterial.Name, normalized, StringComparison.Ordinal))
            {
                _selectedMaterial = null;
                _materialCatalogId = null;
                OnPropertyChanged(nameof(SelectedMaterial));
                OnPropertyChanged(nameof(MaterialCatalogInfo));
            }
            else if (_selectedMaterial == null)
            {
                _materialCatalogId = null;
                OnPropertyChanged(nameof(MaterialCatalogInfo));
            }

            OnPropertyChanged();
        }
    }

    public Item? SelectedMaterial
    {
        get => _selectedMaterial;
        set
        {
            if (ReferenceEquals(_selectedMaterial, value))
                return;

            _selectedMaterial = value;
            _materialCatalogId = value?.PersistentId > 0 ? value.PersistentId : null;
            if (value != null)
            {
                _materialName = value.Name;
                OnPropertyChanged(nameof(MaterialName));
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(MaterialCatalogInfo));
        }
    }

    public int? MaterialCatalogId => _materialCatalogId;

    public string CalculationMode
    {
        get => _calculationMode;
        set => SetField(ref _calculationMode, value ?? WindowMaterialRuleSettingsModel.PerimeterCalculationMode);
    }

    public string QuantityParameterText
    {
        get => _quantityParameterText;
        set => SetField(ref _quantityParameterText, value ?? string.Empty);
    }

    public string CalculationHint => IsWindowAutomation
        ? "Perimetro: unità di materiale per ogni metro. Quantità fissa: unità per finestra. " +
          "Il numero di finestre elaborate è determinato dalla quantità della lavorazione."
        : "Automazione generica: parametro × quantità della lavorazione. " +
          "Non richiede un prodotto finestra nel preventivo.";

    public string LaborCatalogInfo => _selectedLabor != null
        ? _selectedLabor.PersistentId > 0
            ? $"ID catalogo {_selectedLabor.PersistentId}"
            : "Selezionata dal catalogo · ID non ancora assegnato"
        : _laborCatalogId.HasValue
            ? $"ID catalogo {_laborCatalogId.Value} non trovato: seleziona di nuovo"
            : "Digita per cercare, poi seleziona la lavorazione dall'elenco";

    public string MaterialCatalogInfo => _selectedMaterial != null
        ? _selectedMaterial.PersistentId > 0
            ? $"ID catalogo {_selectedMaterial.PersistentId} · costo {_selectedMaterial.UnitPrice:N2} €"
            : $"Selezionato dal catalogo · costo {_selectedMaterial.UnitPrice:N2} €"
        : _materialCatalogId.HasValue
            ? $"ID catalogo {_materialCatalogId.Value} non trovato: seleziona di nuovo"
            : "Digita per cercare, poi seleziona il materiale dall'elenco";

    public static WindowMaterialRuleEditor CreateNew() => new();

    public WindowMaterialRuleSettingsModel CreateSettingsRule(
        decimal quantityParameter,
        bool discardUnresolvedCatalogIds = false) => new()
    {
        Enabled = Enabled,
        IsWindowAutomation = IsWindowAutomation,
        LaborCatalogId = SelectedLabor?.PersistentId > 0
            ? SelectedLabor.PersistentId
            : discardUnresolvedCatalogIds
                ? null
                : LaborCatalogId,
        LaborName = SelectedLabor?.Name.Trim() ?? LaborName.Trim(),
        MaterialCatalogId = SelectedMaterial?.PersistentId > 0
            ? SelectedMaterial.PersistentId
            : discardUnresolvedCatalogIds
                ? null
                : MaterialCatalogId,
        MaterialName = SelectedMaterial?.Name.Trim() ?? MaterialName.Trim(),
        CalculationMode = IsWindowAutomation
            ? CalculationMode
            : WindowMaterialRuleSettingsModel.FixedPerWindowCalculationMode,
        QuantityParameter = quantityParameter
    };

    public static WindowMaterialRuleEditor FromSettings(
        WindowMaterialRuleSettingsModel rule,
        IReadOnlyList<Item> laborCatalog,
        IReadOnlyList<Item> materialCatalog,
        bool useCatalogIds = true)
    {
        Item? selectedLabor = useCatalogIds
            ? ResolveCatalogItem(laborCatalog, rule.LaborCatalogId, rule.LaborName)
            : null;
        Item? selectedMaterial = useCatalogIds
            ? ResolveCatalogItem(materialCatalog, rule.MaterialCatalogId, rule.MaterialName)
            : null;

        return new WindowMaterialRuleEditor
        {
            _enabled = rule.Enabled,
            _isWindowAutomation = rule.IsWindowAutomation,
            _laborCatalogId = rule.LaborCatalogId,
            _laborName = selectedLabor?.Name ?? rule.LaborName,
            _selectedLabor = selectedLabor,
            _materialCatalogId = rule.MaterialCatalogId,
            _materialName = selectedMaterial?.Name ?? rule.MaterialName,
            _selectedMaterial = selectedMaterial,
            _calculationMode = rule.CalculationMode,
            _quantityParameterText = rule.QuantityParameter.ToString("0.###", CultureInfo.CurrentCulture)
        };
    }

    private static Item? ResolveCatalogItem(
        IReadOnlyList<Item> catalog,
        int? catalogId,
        string? snapshotName)
    {
        if (catalogId > 0)
            return catalog.FirstOrDefault(item => item.PersistentId == catalogId.Value);

        string name = snapshotName?.Trim() ?? string.Empty;
        if (name.Length == 0)
            return null;

        Item[] exactMatches = catalog
            .Where(item => string.Equals(item.Name.Trim(), name, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return exactMatches.Length == 1 ? exactMatches[0] : null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
