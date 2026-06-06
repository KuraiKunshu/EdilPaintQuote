using System;
using System.Globalization;
using System.Linq;
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
        CmbPdfTemplate.ItemsSource = PdfTemplateSettingsModel.AvailableTemplates;
        LoadSettings();
        PreviewKeyDown += SettingsWindow_PreviewKeyDown;
    }

    private void LoadSettings()
    {
        var app = App.AppSettings.App;
        var pdf = App.AppSettings.PdfStorage;
        var template = App.AppSettings.PdfTemplate;
        var database = App.AppSettings.Database;
        var mail = App.AppSettings.Mail;

        TxtDatabaseConnectionString.Text = database.ConnectionString;
        TxtDatabaseServer.Text = database.Server;
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
        ChkGeneratePdf.IsChecked = app.GeneratePDF;
        ChkRestoreMissingPdfsOnStartup.IsChecked = app.RestoreMissingPdfsOnStartup;
        ChkSilentStartup.IsChecked = app.IsSilentStartup;
        ChkUseVeluxLogin.IsChecked = app.UseVeluxLogin;
        TxtHistoryResultLimit.Text = app.NumberOfQuote.ToString(CultureInfo.InvariantCulture);
        TxtTempPath.Text = app.TempPath;
        TxtDeviceName.Text = app.GetEffectiveDeviceName();

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
        bool mailEnabled = ChkMailEnabled.IsChecked == true;
        string mailSmtpServer = TxtMailSmtpServer.Text.Trim();
        string mailUsername = TxtMailUsername.Text.Trim();
        string mailPassword = TxtMailPassword.Password;
        string mailSenderEmail = TxtMailSenderEmail.Text.Trim();
        string mailSenderName = TxtMailSenderName.Text.Trim();
        string mailSubject = TxtMailSubject.Text.Trim();
        string mailBody = TxtMailBody.Text;

        if (!int.TryParse(TxtMailPort.Text, out int mailPort) || mailPort <= 0 || mailPort > 65535)
        {
            MessageBox.Show(
                "La porta SMTP deve essere un numero valido tra 1 e 65535.",
                "Impostazioni non valide",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            var app = App.AppSettings.App;
            var pdf = App.AppSettings.PdfStorage;
            var template = App.AppSettings.PdfTemplate;
            var database = App.AppSettings.Database;
            var mail = App.AppSettings.Mail;

            database.ConnectionString = databaseConnectionString;
            database.Server = databaseServer;
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
            mail.RequiresCredentialReset = false;
            mail.Normalize();

            if (database.IsConfigured)
                _ = database.BuildConnectionString();
            if (mail.Enabled)
                mail.ValidateForSend();
            app.GeneratePDF = ChkGeneratePdf.IsChecked == true;
            app.RestoreMissingPdfsOnStartup = ChkRestoreMissingPdfsOnStartup.IsChecked == true;
            app.IsSilentStartup = ChkSilentStartup.IsChecked == true;
            app.UseVeluxLogin = ChkUseVeluxLogin.IsChecked == true;
            app.NumberOfQuote = historyResultLimit;
            app.TempPath = TxtTempPath.Text.Trim();
            app.DeviceName = string.IsNullOrWhiteSpace(TxtDeviceName.Text)
                ? Environment.MachineName
                : TxtDeviceName.Text.Trim();

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
}
