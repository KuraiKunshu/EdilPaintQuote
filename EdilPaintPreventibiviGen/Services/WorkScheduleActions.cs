using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Views;

namespace EdilPaintPreventibiviGen.Services;

public static class WorkScheduleActions
{
    public static void OpenOrder(string quoteNumber, Window owner)
    {
        if (App.MainVm == null) throw new InvalidOperationException("La finestra principale non è disponibile.");
        new HistoryWindow(App.MainVm, quoteNumber) { Owner = owner }.ShowDialog();
    }

    internal static WorkSheetOptions CreateWorkSheetOptions(WorkScheduleEntry entry) => new()
    {
        EmployeeNames = entry.Employees.Select(x => $"{x.FirstName} {x.LastName}".Trim()).ToList(),
        InterventionDate = entry.Date.Date,
        InterventionTime = entry.TimeDisplay,
        AdditionalNotes = entry.Notes
    };

    public static async Task GenerateWorkSheetAsync(WorkScheduleEntry selectedEntry, Window owner, CancellationToken token = default)
    {
        if (selectedEntry.Kind != WorkScheduleEntryKind.Job)
            throw new InvalidOperationException("La scheda lavoro è disponibile per gli interventi collegati a un ordine.");
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var entry = selectedEntry;
            bool currentSchedule = false;
            if (App.WorkSchedule != null)
            {
                var latest = await App.WorkSchedule.GetLatestAsync(entry.Date, entry.Date.AddDays(1), token);
                currentSchedule = latest.IsCurrent;
                if (currentSchedule)
                    entry = latest.Entries.FirstOrDefault(x => x.Id == entry.Id)
                        ?? throw new InvalidOperationException("L'intervento è stato spostato o rimosso da un altro PC. Ricarica il calendario.");
            }
            token.ThrowIfCancellationRequested();
            var quote = await App.DataService.GetQuoteByNumberAsync(entry.QuoteNumber, token, includeAttachments: false)
                ?? throw new InvalidOperationException("Ordine non trovato nello storico.");
            var catalog = await App.DataService.GetLaborCatalogAsync().WaitAsync(token);
            var customers = await App.DataService.GetCustomersAsync(token);
            var company = await App.DataService.GetCompanyAsync().WaitAsync(token) ?? new Company();
            var context = WorkSheetService.CreateContext(quote, catalog, customers, CreateWorkSheetOptions(entry));
            context.IsOfflineSnapshot = !currentSchedule || App.DataService is FallbackDataService { IsOfflineMode: true };
            if (context.IsOfflineSnapshot && MessageBox.Show(owner,
                    "La programmazione non è aggiornata dal database. La scheda userà i dati disponibili su questo PC. Continuare?",
                    "Scheda lavoro offline", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            context.SelectedLogo = App.MainVm?.SelectedLogo ?? string.Empty;
            if (string.IsNullOrWhiteSpace(context.SelectedLogo) && company.Logo.Count > 0)
                context.SelectedLogo = company.Logo[Math.Clamp(company.Logo_index, 0, company.Logo.Count - 1)];

            string basePath = StoragePathService.Instance.BuildWorkSheetPdfPath(quote.CustomerName, quote.QuoteNumber, quote.ReferenceName);
            string fileName = $"{Path.GetFileNameWithoutExtension(basePath)}_{entry.Date:yyyyMMdd}_{entry.StartMinutes / 60:00}{entry.StartMinutes % 60:00}_{entry.Id.ToString("N")[..8]}.pdf";
            string destination = Path.Combine(Path.GetDirectoryName(basePath)!, fileName);
            string temporaryRoot = App.AppSettings.App.GetEffectiveTempPath();
            Directory.CreateDirectory(temporaryRoot);
            string temporary = Path.Combine(temporaryRoot, $"{Guid.NewGuid():N}_{fileName}");
            await Task.Run(() => new PdfService().GenerateWorkSheet(context, company, temporary), token);
            token.ThrowIfCancellationRequested();
            string pathToOpen = temporary;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(temporary, destination, overwrite: true);
                pathToOpen = destination;
                File.Delete(temporary);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(owner, $"La scheda è stata generata. La cartella cliente non è disponibile, quindi la copia si trova qui:\n\n{temporary}",
                    "Scheda lavoro", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            token.ThrowIfCancellationRequested();
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe", Arguments = $"/select,\"{Path.GetFullPath(pathToOpen)}\"", UseShellExecute = true
            });
        }
        finally { Mouse.OverrideCursor = null; }
    }
}
