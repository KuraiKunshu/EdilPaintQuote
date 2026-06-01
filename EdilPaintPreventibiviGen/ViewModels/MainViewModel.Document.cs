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
    #region Document Logic
    public void ResetQuote()
    {
        Materials.Clear();
        Labors.Clear();
        AttachedImages.Clear();
        OurCosts.Clear();
        PartnerCosts.Clear();
        AdditionalCosts.Clear();

        _selectedCustomer = null;
        _selectedSecondCustomer = null;
        _isSecondCustomerEnabled = false;
        _isJointVenture = false;
        _partnerCompanyName = string.Empty;

        _isEditingExistingQuote = false;

        _materialDiscount = 0;
        _laborDiscount = 0;
        _ivaType = "RC 10%+22%";

        PaymentTerms = _companyData.Termini_pagamento;
        QuoteNumber = _companyData.Counter.ToString();

        OnPropertyChanged(nameof(SelectedCustomer));
        OnPropertyChanged(nameof(SelectedSecondCustomer));
        OnPropertyChanged(nameof(IsSecondCustomerEnabled));
        OnPropertyChanged(nameof(MaterialDiscount));
        OnPropertyChanged(nameof(LaborDiscount));
        OnPropertyChanged(nameof(IvaType));
        OnPropertyChanged(nameof(IsJointVenture));
        OnPropertyChanged(nameof(PartnerCompanyName));

        ResetInputs();
        CalculateTotals();
    }

    public async Task<bool> HasMissingPdfsAsync()
    {
        try
        {
            // Carica solo i summary (leggeri) invece dell'intero storico
            var summaries = await _dataService.GetQuoteSummariesAsync(int.MaxValue);
            return summaries.Any(e => !string.IsNullOrWhiteSpace(e.PdfPath) && !File.Exists(e.PdfPath));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HasMissingPdfs] Errore: {ex.Message}");
            return false;
        }
    }

    public async Task GenerateInitialPdfsAsync(IProgress<string>? progress = null)
    {
        var watch = Stopwatch.StartNew();

        try
        {
            string storageRoot = _storagePathService.GetPdfRootPath();
            if (!Directory.Exists(storageRoot))
            {
                progress?.Report("Cartella di rete non disponibile - generazione PDF saltata.");
                return;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[STARTUP][HISTORY] Errore verifica cartella: {ex.Message}");
            progress?.Report("Cartella di rete non configurata - generazione PDF saltata.");
            return;
        }

        progress?.Report("Analisi storico: caricamento elenco preventivi...");
        var historyEntries = await _quoteHistoryService.LoadAsync();

        int total = historyEntries.Count;
        int current = 0;

        foreach (var entry in historyEntries)
        {
            current++;
            progress?.Report($"Analisi storico: {current}/{total} - Preventivo {entry.QuoteNumber}");

            try
            {
                if (string.IsNullOrWhiteSpace(entry.CustomerName) || !HasMatchingCustomer(entry.CustomerName))
                    continue;

                string restoredPath = await _quoteHistoryService.EnsureOfficialPdfExistsAsync(entry);
                if (!string.IsNullOrWhiteSpace(restoredPath) && File.Exists(restoredPath))
                    continue;

                progress?.Report($"Analisi storico: {current}/{total} - PDF ufficiale non disponibile per {entry.QuoteNumber}");
                Debug.WriteLine($"[STARTUP][HISTORY] PDF ufficiale non disponibile per {entry.QuoteNumber}, rigenerazione automatica saltata.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[STARTUP][HISTORY] ERRORE su preventivo {entry.QuoteNumber}: {ex.Message}");
            }
        }

        progress?.Report("Analisi PDF storico completata.");
        Debug.WriteLine($"[STARTUP][HISTORY] Analisi completa in {watch.Elapsed}");
    }

    private bool HasMatchingCustomer(string? customerName)
    {
        if (string.IsNullOrWhiteSpace(customerName))
            return false;

        string normalizedEntryName = NormalizeMatchText(customerName);
        return AllCustomers.Any(customer =>
            NormalizeMatchText(customer.BusinessName) == normalizedEntryName);
    }

    private static string NormalizeMatchText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Trim().Replace("_", " ").ToUpperInvariant();
    }

    public void GeneratePdf() => _ = GeneratePdfAsync(!_isEditingExistingQuote);

    public async Task GeneratePdfAsync(
        bool incrementCounter = true,
        bool openAfterGeneration = true,
        DateTime? specificDate = null,
        string? forceTargetPath = null)
    {
        if (!App.AppSettings.App.GeneratePDF)
        {
            MessageBox.Show("La generazione PDF e' disabilitata nelle impostazioni.");
            return;
        }
        if (SelectedCustomer == null)
        {
            MessageBox.Show("Seleziona un cliente prima di generare il PDF.");
            return;
        }

        if (_isGeneratingPdf)
        {
            Debug.WriteLine("[GENERATEPDF] Generazione gia' in corso, richiesta ignorata.");
            return;
        }

        _isGeneratingPdf = true;

        try
        {
            if (incrementCounter && !_isEditingExistingQuote)
            {
                try
                {
                    int nextNumber = await _dataService.GetNextQuoteNumberAsync();
                    _companyData.Counter = nextNumber;
                    QuoteNumber = nextNumber.ToString();
                    OnPropertyChanged(nameof(QuoteNumber));
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Impossibile ottenere il prossimo numero preventivo dal database.\n\n{ex.Message}",
                        "Errore numerazione", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            DateTime effectiveDate = specificDate ?? DateTime.Now;

            string targetPath = !string.IsNullOrWhiteSpace(forceTargetPath)
                ? forceTargetPath
                : _storagePathService.BuildQuotePdfPath(
                    SelectedCustomer.BusinessName,
                    QuoteNumber,
                    effectiveDate,
                    IsSecondCustomerEnabled ? SelectedSecondCustomer?.BusinessName : null);

            string tempRoot = App.AppSettings.App.GetEffectiveTempPath();
            Directory.CreateDirectory(tempRoot);
            string tempFileName = Path.GetFileName(targetPath);
            string pathToGenerate = Path.Combine(tempRoot, tempFileName);
            Debug.WriteLine($"Generazione temporanea PDF: {pathToGenerate}");

            _pdfService.GenerateQuote(this, _companyData, pathToGenerate, effectiveDate);

            bool copiedToTarget = false;
            try
            {
                string? targetFolder = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(targetFolder))
                    Directory.CreateDirectory(targetFolder);

                File.Copy(pathToGenerate, targetPath, overwrite: true);
                copiedToTarget = true;
                Debug.WriteLine($"PDF copiato su destinazione: {targetPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERRORE copia PDF su share: {targetPath} -> {ex.Message}");
            }

            if (!await SaveToHistoryAsync(targetPath, incrementCounter, effectiveDate, pathToGenerate))
                return;

            try { File.Delete(pathToGenerate); }
            catch (Exception ex) { Debug.WriteLine($"[GENERATEPDF] Error deleting temporary PDF: {ex.Message}"); }

            if (!copiedToTarget)
            {
                MessageBox.Show(
                    "Il PDF e' al sicuro nel database o nella coda locale ed e' in attesa di essere ripristinato nella cartella condivisa.",
                    "Cartella condivisa non disponibile",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (!openAfterGeneration) return;

            try { Process.Start("explorer.exe", $"/select,\"{targetPath}\""); }
            catch { Process.Start("explorer.exe", targetPath); }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore durante la generazione del PDF.\n\n{ex.Message}",
                "Errore PDF", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isGeneratingPdf = false;
        }
    }

    public void LoadQuoteFromHistory(QuoteHistoryEntry entry, bool isEdit = false)
    {
        ResetQuote();

        SelectedCustomer = AllCustomers.FirstOrDefault(c => c.BusinessName == entry.CustomerName);
        CustomerBorderBrush = _selectedCustomer != null ? Brushes.Green : Brushes.Red;
        OnPropertyChanged(nameof(SelectedCustomer));

        if (!string.IsNullOrWhiteSpace(entry.ReferenceName))
        {
            IsSecondCustomerEnabled = true;
            SelectedSecondCustomer = AllCustomers.FirstOrDefault(c => c.BusinessName == entry.ReferenceName);
        }

        PaymentTerms = entry.PaymentTerms;
        IvaType = !string.IsNullOrEmpty(entry.IvaType) ? entry.IvaType : "esclusa";

        _materialDiscount = entry.MaterialDiscount;
        _laborDiscount = entry.LaborDiscount;
        OnPropertyChanged(nameof(MaterialDiscount));
        OnPropertyChanged(nameof(LaborDiscount));

        _isEditingExistingQuote = isEdit;
        if (isEdit)
            QuoteNumber = entry.QuoteNumber;

        // Collaborazione
        _isJointVenture = entry.IsJointVenture;
        _partnerCompanyName = entry.PartnerCompanyName;
        OnPropertyChanged(nameof(IsJointVenture));
        OnPropertyChanged(nameof(PartnerCompanyName));

        OurCosts.Clear();
        foreach (var c in entry.OurCosts) OurCosts.Add(c);
        PartnerCosts.Clear();
        foreach (var c in entry.PartnerCosts) PartnerCosts.Add(c);
        AdditionalCosts.Clear();
        foreach (var c in entry.AdditionalCosts) AdditionalCosts.Add(c);

        foreach (var m in entry.Materials)
        {
            Materials.Add(new Item
            {
                Name = m.Name,
                Description = m.Description,
                UnitPrice = m.UnitPrice,
                Quantity = m.Quantity,
                Discount = m.Discount,
                IsSignificant = m.IsSignificant,
                SortOrder = m.SortOrder
            });
        }

        foreach (var l in entry.Labors)
        {
            Labors.Add(new Item
            {
                Name = l.Name,
                Description = l.Description,
                UnitPrice = l.UnitPrice,
                Quantity = l.Quantity,
                Discount = l.Discount,
                IsSignificant = l.IsSignificant,
                SortOrder = l.SortOrder
            });
        }

        foreach (var attachment in entry.Attachments)
        {
            AttachedImages.Add(new SelectedAttachment
            {
                FileName = attachment.FileName,
                FilePath = string.Empty,
                ContentType = attachment.ContentType,
                Content = attachment.Content
            });
        }

        UpdateItemSortOrders();
        CalculateTotals();
    }

    public void GenerateCostsPdf()
    {
        if (!IsJointVenture)
        {
            MessageBox.Show("La collaborazione non e' attiva.", "Avviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (SelectedCustomer == null)
        {
            MessageBox.Show("Seleziona un cliente prima di generare il PDF dei costi.", "Avviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            string basePath = _storagePathService.BuildQuotePdfPath(
                SelectedCustomer.BusinessName,
                QuoteNumber,
                DateTime.Now,
                IsSecondCustomerEnabled ? SelectedSecondCustomer?.BusinessName : null);

            string costsPath = Path.Combine(
                Path.GetDirectoryName(basePath) ?? string.Empty,
                Path.GetFileNameWithoutExtension(basePath) + "_COSTI.pdf");

            string? folder = Path.GetDirectoryName(costsPath);
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

            _pdfService.GenerateCostsPdf(new CostsPdfContext
            {
                QuoteNumber = QuoteNumber,
                Date = DateTime.Now,
                CustomerName = SelectedCustomer.BusinessName,
                PartnerCompanyName = PartnerCompanyName,
                OurCosts = OurCosts.ToList(),
                PartnerCosts = PartnerCosts.ToList(),
                AdditionalCosts = AdditionalCosts.ToList(),
                Imponibile = Imponibile,
                Total = TotaleGenerale
            }, _companyData, costsPath);

            Process.Start("explorer.exe", $"/select,\"{costsPath}\"");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore generazione PDF costi:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    #endregion
}

