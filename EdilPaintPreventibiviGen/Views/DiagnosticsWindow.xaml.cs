using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Microsoft.Win32;

namespace EdilPaintPreventibiviGen.Views;

public partial class DiagnosticsWindow : Window
{
    private DiagnosticsSnapshot? _snapshot;

    public DiagnosticsWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshSnapshot();
        PreviewKeyDown += DiagnosticsWindow_PreviewKeyDown;
    }

    private void RefreshSnapshot()
    {
        _snapshot = DiagnosticsService.CreateSnapshot();

        TxtAppVersion.Text = _snapshot.AppVersion;
        TxtDatabaseStatus.Text = _snapshot.DatabaseStatus;
        TxtPdfTemplate.Text = _snapshot.PdfTemplateName;
        TxtUpdaterStatus.Text = _snapshot.UpdaterStatus;
        TxtSyncStatus.Text = _snapshot.SyncStatus;
        TxtLastSync.Text = _snapshot.LastSync;
        TxtPendingQuotePatches.Text = FormatCount(_snapshot.PendingQuotePatches);
        TxtPendingDeletes.Text = $"{_snapshot.PendingQuoteDeletes} preventivi, {_snapshot.PendingCustomerDeletes} clienti";
        TxtExecutablePath.Text = _snapshot.ExecutablePath;
        TxtSettingsPath.Text = _snapshot.SettingsPath;
        TxtLocalDataPath.Text = _snapshot.LocalDataPath;
        TxtPdfRootPath.Text = _snapshot.PdfRootPath;
        TxtUpdaterStatePath.Text = string.IsNullOrWhiteSpace(_snapshot.UpdaterStatePath)
            ? "Nessun file stato trovato"
            : _snapshot.UpdaterStatePath;
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => RefreshSnapshot();

    private void OnExportSettingsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string sourcePath = App.AppSettings.SettingsPath;
            var dialog = new SaveFileDialog
            {
                Title = "Esporta impostazioni",
                Filter = "JSON (*.json)|*.json",
                FileName = $"edilpaint-appsettings-{DateTime.Now:yyyyMMdd-HHmmss}.json"
            };

            if (dialog.ShowDialog(this) != true)
                return;

            File.Copy(sourcePath, dialog.FileName, overwrite: true);
            MessageBox.Show(this, "Impostazioni esportate correttamente.", "Diagnostica", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Impossibile esportare le impostazioni.\n\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnImportSettingsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "Importa impostazioni",
                Filter = "JSON (*.json)|*.json",
                Multiselect = false
            };

            if (dialog.ShowDialog(this) != true)
                return;

            var result = MessageBox.Show(
                this,
                "Le impostazioni correnti verranno salvate in backup e sostituite. Dopo l'import riavvia l'applicazione.",
                "Importa impostazioni",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            string targetPath = App.AppSettings.SettingsPath;
            string? directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            if (File.Exists(targetPath))
            {
                string backupPath = $"{targetPath}.backup-{DateTime.Now:yyyyMMdd-HHmmss}";
                File.Copy(targetPath, backupPath, overwrite: true);
            }

            File.Copy(dialog.FileName, targetPath, overwrite: true);
            MessageBox.Show(this, "Impostazioni importate. Riavvia l'applicazione per applicarle.", "Diagnostica", MessageBoxButton.OK, MessageBoxImage.Information);
            RefreshSnapshot();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Impossibile importare le impostazioni.\n\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnOpenSettingsFolderClick(object sender, RoutedEventArgs e)
        => OpenFolder(_snapshot?.SettingsDirectory);

    private void OnOpenDataFolderClick(object sender, RoutedEventArgs e)
        => OpenFolder(_snapshot?.LocalDataPath);

    private void OnOpenPdfFolderClick(object sender, RoutedEventArgs e)
        => OpenFolder(_snapshot?.PdfRootPath);

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void DiagnosticsWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }

    private void OpenFolder(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                MessageBox.Show(this, "La cartella non esiste o non e' configurata.", "Diagnostica", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Impossibile aprire la cartella.\n\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string FormatCount(int count) => count == 1 ? "1 elemento" : $"{count} elementi";
}
