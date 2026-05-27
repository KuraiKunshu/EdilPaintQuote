using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using EdilPaintPreventibiviGen.Views;

namespace EdilPaintPreventibiviGen.ViewModels;
public partial class MainViewModel
{
    #region History
    public async Task LoadHistorySummariesAsync(int count, CancellationToken cancellationToken = default)
    {
        try
        {
            var service = new QuoteHistoryService(App.DataService, StoragePathService.Instance);
            var summaries = await service.LoadTopSummariesAsync(count);

            if (cancellationToken.IsCancellationRequested)
                return;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                HistorySummaries.Clear();
                foreach (var s in summaries)
                    HistorySummaries.Add(s);
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LoadHistorySummaries] Error: {ex.Message}");
        }
    }

    public async Task<List<QuoteHistorySummary>> SearchHistorySummariesAsync(string searchText, int take, CancellationToken cancellationToken = default)
    {
        try
        {
            var service = new QuoteHistoryService(App.DataService, StoragePathService.Instance);
            var results = await service.SearchSummariesAsync(searchText, take);

            if (cancellationToken.IsCancellationRequested)
                return new List<QuoteHistorySummary>();

            return results;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SearchHistorySummaries] Error: {ex.Message}");
            return new List<QuoteHistorySummary>();
        }
    }

    private static byte[] ReadPdfBytesWithRetry(string pdfPath, int maxAttempts = 3, int delayMs = 200)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                if (File.Exists(pdfPath))
                    return File.ReadAllBytes(pdfPath);
            }
            catch (IOException)
            {
                Debug.WriteLine($"[ReadPdf] Tentativo {i + 1}/{maxAttempts} fallito per: {pdfPath}");
            }

            if (i < maxAttempts - 1)
                System.Threading.Thread.Sleep(delayMs);
        }

        return [];
    }

    private async Task<bool> SaveToHistoryAsync(string pdfPath, bool isNewEntry = false)
    {
        byte[] pdfBytes = ReadPdfBytesWithRetry(pdfPath);

        var entry = new QuoteHistoryEntry
        {
            QuoteNumber = QuoteNumber,
            Date = DateTime.Now,
            CustomerName = SelectedCustomer?.BusinessName ?? "Sconosciuto",
            ReferenceName = IsSecondCustomerEnabled ? (SelectedSecondCustomer?.BusinessName ?? "") : "",
            PdfPath = pdfPath,
            PaymentTerms = PaymentTerms,
            IvaType = IvaType,
            Notes = string.Empty,
            Materials = Materials.ToList(),
            Labors = Labors.ToList(),
            Imponibile = Imponibile,
            MaterialDiscount = MaterialDiscount,
            LaborDiscount = LaborDiscount,
            Total = TotaleGenerale,
            Status = QuoteStatus.Finalizzato,
            LastModifiedUtc = DateTime.UtcNow,
            IsJointVenture = IsJointVenture,
            PartnerCompanyName = PartnerCompanyName,
            OurCosts = OurCosts.ToList(),
            PartnerCosts = PartnerCosts.ToList(),
            AdditionalCosts = AdditionalCosts.ToList(),
            PdfFile = pdfBytes.Length == 0 ? null : new StoredFile
            {
                FileName = Path.GetFileName(pdfPath) ?? "preventivo.pdf",
                ContentType = "application/pdf",
                Content = pdfBytes,
                ImportedAt = DateTime.Now
            },
            Attachments = AttachedImages.Select(a => new StoredFile
            {
                FileName = a.FileName,
                ContentType = a.ContentType,
                Content = a.Content,
                ImportedAt = DateTime.Now
            }).ToList()
        };

        if (isNewEntry)
        {
            History.Insert(0, entry);
            return await SaveSingleHistoryEntrySafeAsync(entry);
        }
        else
        {
            var existingEntry = History.FirstOrDefault(x => x.QuoteNumber == QuoteNumber);
            if (existingEntry != null)
            {
                entry.Date = existingEntry.Date;
                entry.Status = existingEntry.Status;
                entry.Notes = existingEntry.Notes;

                existingEntry.CustomerName = entry.CustomerName;
                existingEntry.ReferenceName = entry.ReferenceName;
                existingEntry.PdfPath = entry.PdfPath;
                existingEntry.PaymentTerms = entry.PaymentTerms;
                existingEntry.IvaType = entry.IvaType;
                existingEntry.Notes = entry.Notes;
                existingEntry.Materials = entry.Materials;
                existingEntry.Labors = entry.Labors;
                existingEntry.Imponibile = entry.Imponibile;
                existingEntry.MaterialDiscount = entry.MaterialDiscount;
                existingEntry.LaborDiscount = entry.LaborDiscount;
                existingEntry.Total = entry.Total;
                existingEntry.PdfFile = entry.PdfFile;
                existingEntry.Attachments = entry.Attachments;
                existingEntry.IsJointVenture = entry.IsJointVenture;
                existingEntry.PartnerCompanyName = entry.PartnerCompanyName;
                existingEntry.OurCosts = entry.OurCosts;
                existingEntry.PartnerCosts = entry.PartnerCosts;
                existingEntry.AdditionalCosts = entry.AdditionalCosts;

                return await SaveSingleHistoryEntrySafeAsync(entry);
            }
            else
            {
                // Preventivo non in memoria (altro PC): recupera la data originale dal DB
                return await SaveWithOriginalDateAsync(entry);
            }
        }

    }
    
    private async Task<bool> SaveWithOriginalDateAsync(QuoteHistoryEntry entry)
    {
        try
        {
            var existing = await _dataService.GetQuoteByNumberAsync(entry.QuoteNumber);
            if (existing != null)
                entry.Date = existing.Date;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SaveWithOriginalDate] Errore recupero data originale: {ex.Message}");
        }

        History.Insert(0, entry);
        return await SaveSingleHistoryEntrySafeAsync(entry);
    }
    
    private async Task<bool> SaveSingleHistoryEntrySafeAsync(QuoteHistoryEntry entry)
    {
        int attempts = 0;
        while (_isSavingQuoteHistory && attempts < 10)
        {
            await Task.Delay(200);
            attempts++;
        }

        if (_isSavingQuoteHistory)
        {
            Debug.WriteLine("[SAVE HISTORY] Salvataggio giÃ  in corso dopo attesa, skip.");
            return false;
        }

        _isSavingQuoteHistory = true;

        try
        {
            await _dataService.SaveQuoteAsync(entry);
            return true;
        }
        catch (System.Text.Json.JsonException jsonEx)
        {
            Debug.WriteLine($"[SAVE HISTORY] Errore JSON: {jsonEx.Message}");
            try
            {
                var fallback = CloneEntryWithoutPdfBytes(entry);
                await _dataService.SaveQuoteAsync(fallback);
                return true;
            }
            catch (Exception retryEx)
            {
                MessageBox.Show($"Errore durante il salvataggio dello storico.\n\n{retryEx.Message}",
                    "Errore salvataggio storico", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore durante il salvataggio dello storico.\n\n{ex.Message}",
                "Errore salvataggio storico", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally
        {
            _isSavingQuoteHistory = false;
        }
    }

    private static QuoteHistoryEntry CloneEntryWithoutPdfBytes(QuoteHistoryEntry entry)
    {
        return new QuoteHistoryEntry
        {
            QuoteNumber = entry.QuoteNumber,
            Date = entry.Date,
            CustomerName = entry.CustomerName,
            ReferenceName = entry.ReferenceName,
            PdfPath = entry.PdfPath,
            PaymentTerms = entry.PaymentTerms,
            IvaType = entry.IvaType,
            Notes = entry.Notes,
            Materials = entry.Materials,
            Labors = entry.Labors,
            Imponibile = entry.Imponibile,
            MaterialDiscount = entry.MaterialDiscount,
            LaborDiscount = entry.LaborDiscount,
            Total = entry.Total,
            Status = entry.Status,
            LastModifiedUtc = entry.LastModifiedUtc,
            SyncHash = entry.SyncHash,
            IsJointVenture = entry.IsJointVenture,
            PartnerCompanyName = entry.PartnerCompanyName,
            OurCosts = entry.OurCosts,
            PartnerCosts = entry.PartnerCosts,
            AdditionalCosts = entry.AdditionalCosts,
            PdfFile = entry.PdfFile == null ? null : new StoredFile
            {
                FileName = entry.PdfFile.FileName,
                ContentType = entry.PdfFile.ContentType,
                Content = [],
                ImportedAt = entry.PdfFile.ImportedAt
            },
            Attachments = entry.Attachments.Select(a => new StoredFile
            {
                FileName = a.FileName,
                ContentType = a.ContentType,
                Content = [],
                ImportedAt = a.ImportedAt
            }).ToList()
        };
    }

    #endregion
}

