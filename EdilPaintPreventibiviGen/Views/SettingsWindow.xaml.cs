using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using EdilPaintPreventibiviGen.Services;
using Microsoft.Win32;

namespace EdilPaintPreventibiviGen.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        LoadSettings();
        PreviewKeyDown += SettingsWindow_PreviewKeyDown;
    }

    private void LoadSettings()
    {
        var app = App.AppSettings.App;
        var pdf = App.AppSettings.PdfStorage;
        var database = App.AppSettings.Database;

        TxtDatabaseConnectionString.Text = database.ConnectionString;
        TxtDatabaseServer.Text = database.Server;
        TxtDatabaseName.Text = database.Database;
        TxtDatabaseUsername.Text = database.Username;
        TxtDatabasePassword.Password = database.Password;
        ChkGeneratePdf.IsChecked = app.GeneratePDF;
        ChkSilentStartup.IsChecked = app.IsSilentStartup;
        ChkUseVeluxLogin.IsChecked = app.UseVeluxLogin;
        TxtHistoryResultLimit.Text = app.NumberOfQuote.ToString(CultureInfo.InvariantCulture);
        TxtTempPath.Text = app.TempPath;

        TxtPdfRootPath.Text = pdf.RootPath;
        TxtHistorySubFolder.Text = pdf.HistorySubFolder ?? string.Empty;
        TxtCustomerFolderPattern.Text = pdf.CustomerFolderPattern ?? string.Empty;
        TxtPdfFileNamePattern.Text = pdf.PdfFileNamePattern ?? string.Empty;

        if (database.RequiresCredentialReset)
        {
            Loaded += (_, _) => MessageBox.Show(
                "Le credenziali SQL salvate appartengono a un altro utente Windows o a un altro PC. Inseriscile nuovamente e salva.",
                "Credenziali SQL da reinserire",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtPdfRootPath.Text))
        {
            MessageBox.Show(
                "Inserisci la cartella principale dei PDF.",
                "Impostazioni non valide",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(TxtHistoryResultLimit.Text, out int historyResultLimit) || historyResultLimit < 1)
        {
            MessageBox.Show(
                "Il numero di risultati nello storico deve essere maggiore di zero.",
                "Impostazioni non valide",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        string databaseConnectionString = TxtDatabaseConnectionString.Text.Trim();
        string databaseServer = TxtDatabaseServer.Text.Trim();
        string databaseName = TxtDatabaseName.Text.Trim();
        string databaseUsername = TxtDatabaseUsername.Text.Trim();
        string databasePassword = TxtDatabasePassword.Password;

        try
        {
            var app = App.AppSettings.App;
            var pdf = App.AppSettings.PdfStorage;
            var database = App.AppSettings.Database;

            database.ConnectionString = databaseConnectionString;
            database.Server = databaseServer;
            database.Database = databaseName;
            database.Username = databaseUsername;
            database.Password = databasePassword;
            database.RequiresCredentialReset = false;

            if (database.IsConfigured)
                _ = database.BuildConnectionString();
            app.GeneratePDF = ChkGeneratePdf.IsChecked == true;
            app.IsSilentStartup = ChkSilentStartup.IsChecked == true;
            app.UseVeluxLogin = ChkUseVeluxLogin.IsChecked == true;
            app.NumberOfQuote = historyResultLimit;
            app.TempPath = TxtTempPath.Text.Trim();

            pdf.RootPath = TxtPdfRootPath.Text.Trim();
            pdf.HistorySubFolder = EmptyToNull(TxtHistorySubFolder.Text);
            pdf.CustomerFolderPattern = EmptyToNull(TxtCustomerFolderPattern.Text);
            pdf.PdfFileNamePattern = EmptyToNull(TxtPdfFileNamePattern.Text);

            App.AppSettings.Save();

            MessageBox.Show(
                "Impostazioni salvate. Riavvia l'applicazione se hai modificato la connessione al database.",
                "Impostazioni",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Impossibile salvare le impostazioni.\n\n{ex.Message}",
                "Errore salvataggio",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnBrowsePdfRootClick(object sender, RoutedEventArgs e)
        => BrowseFolder(TxtPdfRootPath);

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
}
