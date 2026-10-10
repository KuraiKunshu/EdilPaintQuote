using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Views;

namespace EdilPaintPreventibiviGen.Services;

/// <summary>Common document actions for every view of a saved quote.</summary>
public static class QuoteDocumentActions
{
    internal static QuotePdfDocumentService CreatePdfService() => new(App.DataService, StoragePathService.Instance,
        App.AppSettings, () => App.MainVm?.SelectedLogo ?? string.Empty,
        App.MainVm == null ? null : () => App.MainVm.AllCustomers.ToList());

    public static async Task OpenPdfAsync(string quoteNumber, Window owner, CancellationToken token = default)
    {
        var path = await CreatePdfService().GetPdfPathAsync(quoteNumber, token);
        token.ThrowIfCancellationRequested();
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    public static async Task OpenCustomerFolderAsync(string quoteNumber, Window owner, CancellationToken token = default)
    {
        var quote = await App.DataService.GetQuoteByNumberAsync(quoteNumber, token)
            ?? throw new InvalidOperationException("Preventivo non trovato nello storico.");
        if (string.IsNullOrWhiteSpace(quote.CustomerName))
            throw new InvalidOperationException("Nessun cliente associato a questo preventivo.");
        var history = new QuoteHistoryService(App.DataService, StoragePathService.Instance);
        await history.EnsureAttachmentsFolderExistsAsync(quote);
        token.ThrowIfCancellationRequested();
        StoragePathService.Instance.OpenFolder(StoragePathService.Instance.BuildCustomerPdfFolder(
            quote.CustomerName, string.IsNullOrWhiteSpace(quote.ReferenceName) ? null : quote.ReferenceName));
    }

    public static async Task GenerateWorkSheetAsync(string quoteNumber, Window owner, CancellationToken token = default)
    {
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var employees = App.EmployeeDirectory != null
                ? await App.EmployeeDirectory.GetLatestAsync(token)
                : new EmployeeDirectorySnapshot([], false);
            token.ThrowIfCancellationRequested();
            Mouse.OverrideCursor = null;
            var input = new WorkSheetOptionsWindow(employees.Employees, quoteNumber, employees.IsCurrent) { Owner = owner };
            if (input.ShowDialog() != true) return;
            token.ThrowIfCancellationRequested();
            Mouse.OverrideCursor = Cursors.Wait;
            var quote = await App.DataService.GetQuoteByNumberAsync(quoteNumber, token, includeAttachments: false)
                ?? throw new InvalidOperationException("Preventivo non trovato nello storico.");
            var catalog = await App.DataService.GetLaborCatalogAsync().WaitAsync(token);
            var customers = await App.DataService.GetCustomersAsync(token);
            var company = await App.DataService.GetCompanyAsync().WaitAsync(token) ?? new Company();
            var context = WorkSheetService.CreateContext(quote, catalog, customers, input.Options);
            context.SelectedLogo = App.MainVm?.SelectedLogo ?? string.Empty;
            if (string.IsNullOrWhiteSpace(context.SelectedLogo) && company.Logo.Count > 0)
                context.SelectedLogo = company.Logo[Math.Clamp(company.Logo_index, 0, company.Logo.Count - 1)];
            context.IsOfflineSnapshot = App.DataService is FallbackDataService { IsOfflineMode: true };
            if (context.IsOfflineSnapshot && MessageBox.Show(owner,
                    "Il PC è offline. La scheda userà i dati disponibili su questo computer. Continuare?",
                    "Scheda lavoro offline", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var expected = StoragePathService.Instance.BuildWorkSheetPdfPath(quote.CustomerName, quote.QuoteNumber, quote.ReferenceName);
            var temporaryRoot = App.AppSettings.App.GetEffectiveTempPath();
            Directory.CreateDirectory(temporaryRoot);
            var temporary = Path.Combine(temporaryRoot, $"{Guid.NewGuid():N}_{Path.GetFileName(expected)}");
            await Task.Run(() => new PdfService().GenerateWorkSheet(context, company, temporary), token);
            token.ThrowIfCancellationRequested();
            var path = temporary;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(expected)!);
                File.Copy(temporary, expected, overwrite: true);
                path = expected;
                File.Delete(temporary);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(owner, $"La scheda è stata generata. La cartella cliente non è disponibile; la copia si trova qui:\n\n{temporary}",
                    "Scheda lavoro", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            token.ThrowIfCancellationRequested();
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{Path.GetFullPath(path)}\"", UseShellExecute = true });
        }
        finally { Mouse.OverrideCursor = null; }
    }
}
