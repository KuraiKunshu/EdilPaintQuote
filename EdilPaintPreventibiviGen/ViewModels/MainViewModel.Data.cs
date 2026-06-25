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
    private const string DefaultLogoFileName = "Edilpaint.png";

    #region Data Loading & Saving
    public Task InitializeAsync() => LoadDataAsync();

    public async Task RefreshSharedDataAsync(CancellationToken cancellationToken = default)
    {
        if (!await _sharedDataRefreshLock.WaitAsync(0, cancellationToken))
            return;

        try
        {
            var customers = await _dataService.GetCustomersAsync(cancellationToken);
            var labors = await _dataService.GetLaborCatalogAsync();
            var personalMaterials = await _dataService.GetPersonalMaterialsAsync();
            Guid? selectedCustomerId = SelectedCustomer?.SyncId;
            Guid? selectedReferenceId = SelectedSecondCustomer?.SyncId;
            Guid? selectedBillingId = SelectedBillingCustomer?.SyncId;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _allCustomers = customers.ToList();
                AllCustomers.Clear();
                foreach (var customer in _allCustomers)
                    AllCustomers.Add(customer);

                _selectedCustomer = FindCustomerById(selectedCustomerId);
                _selectedSecondCustomer = FindCustomerById(selectedReferenceId);
                _selectedBillingCustomer = FindCustomerById(selectedBillingId);
                CustomerBorderBrush = GetCustomerSelectionBrush(_selectedCustomer != null);
                SecondCustomerBorderBrush = GetCustomerSelectionBrush(_selectedSecondCustomer != null);
                OnPropertyChanged(nameof(SelectedCustomer));
                OnPropertyChanged(nameof(SelectedSecondCustomer));
                OnPropertyChanged(nameof(SelectedBillingCustomer));
                ApplyCustomerFilter(_customerSearchText);
                ApplySecondCustomerFilter(_secondCustomerSearchText);

                _allCatalogLabors = labors.ToList();
                AllCatalogLabors.Clear();
                foreach (var labor in _allCatalogLabors)
                    AllCatalogLabors.Add(labor);
                ApplyLaborFilter(_laborSearchText);

                _personalMaterials = personalMaterials;
            });
        }
        finally
        {
            _sharedDataRefreshLock.Release();
        }
    }

    private Customer? FindCustomerById(Guid? syncId) => syncId.HasValue
        ? AllCustomers.FirstOrDefault(x => x.SyncId == syncId.Value)
        : null;

    private async Task LoadDataAsync()
    {
        try
        {
            string assetsPath = GetAssetsPath();
            LoadSignificantMaterialsConfig(assetsPath);

            var company = await _dataService.GetCompanyAsync();
            var customers = await _dataService.GetCustomersAsync();
            var labors = await _dataService.GetLaborCatalogAsync();
            var personalMaterials = await _dataService.GetPersonalMaterialsAsync();

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (company != null)
                {
                    _companyData = company;
                    QuoteNumber = _companyData.Counter.ToString();
                    PaymentTerms = _companyData.Termini_pagamento;

                    Debug.WriteLine($"Loghi caricati: {string.Join(" | ", _companyData.Logo)}");
                    Logos.Clear();
                    foreach (var l in _companyData.Logo)
                        Logos.Add(Path.GetFileName(l));

                    SelectDefaultLogo();
                }

                _allCustomers = customers.ToList();
                AllCustomers.Clear();
                foreach (var customer in _allCustomers)
                    AllCustomers.Add(customer);

                FilteredSecondCustomers.Clear();
                foreach (var customer in _allCustomers)
                    FilteredSecondCustomers.Add(customer);

                _allCatalogLabors = labors.ToList();
                AllCatalogLabors.Clear();
                foreach (var labor in _allCatalogLabors)
                    AllCatalogLabors.Add(labor);

                _personalMaterials = personalMaterials;
            });
        }
        catch (Exception ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show("Errore durante il caricamento dati: " + ex.Message);
            });
        }
    }

    public void SaveLaborsJson() => _ = SaveLaborsAsync();

    private async Task SaveLaborsAsync()
    {
        try
        {
            await _dataService.SaveLaborCatalogAsync(AllCatalogLabors);
            _allCatalogLabors = AllCatalogLabors.ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SaveLaborsAsync] Error: {ex.Message}");
            MessageBox.Show(ex.Message, "Catalogo non salvato", MessageBoxButton.OK, MessageBoxImage.Warning);
            await RefreshSharedDataAsync();
        }
    }

    private void SaveCompanyData() => _ = SaveCompanyDataAsync();

    private void SelectDefaultLogo()
    {
        string defaultLogo = Logos.FirstOrDefault(logo => string.Equals(logo, DefaultLogoFileName, StringComparison.OrdinalIgnoreCase))
            ?? Logos.FirstOrDefault(logo => logo.Contains("edilpaint", StringComparison.OrdinalIgnoreCase))
            ?? Logos.FirstOrDefault()
            ?? string.Empty;

        if (_selectedLogo == defaultLogo)
            return;

        _selectedLogo = defaultLogo;
        OnPropertyChanged(nameof(SelectedLogo));
    }

    private async Task SaveCompanyDataAsync()
    {
        try
        {
            _companyData.Logo_index = Math.Max(0, Logos.IndexOf(SelectedLogo));
            if (int.TryParse(QuoteNumber, out int counter))
                _companyData.Counter = counter;

            Debug.WriteLine($"[SAVE COMPANY] Logos in memoria: {string.Join(" | ", Logos)}");
            Debug.WriteLine($"[SAVE COMPANY] SelectedLogo: {SelectedLogo}");
            Debug.WriteLine($"[SAVE COMPANY] _companyData.Logo prima del save: {string.Join(" | ", _companyData.Logo)}");

            await _dataService.SaveCompanyAsync(_companyData, SelectedLogo);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SAVE COMPANY] Error: {ex.Message}");
        }
    }

    private void SavePersonalMaterials() => _ = SavePersonalMaterialsAsync();

    private async Task SavePersonalMaterialsAsync()
    {
        try { await _dataService.SavePersonalMaterialsAsync(_personalMaterials); }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SAVE PERSONAL MATERIALS] Error: {ex.Message}");
            MessageBox.Show(ex.Message, "Materiali non salvati", MessageBoxButton.OK, MessageBoxImage.Warning);
            await RefreshSharedDataAsync();
        }
    }

    public void AddNewCustomer(Customer c) => _ = AddNewCustomerAsync(c);

    private async Task AddNewCustomerAsync(Customer c)
    {
        try
        {
            var savedCustomer = await _dataService.AddCustomerAsync(c);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                AllCustomers.Add(savedCustomer);
                _allCustomers.Add(savedCustomer);
                ApplyCustomerFilter(_customerSearchText);
                ApplySecondCustomerFilter(_secondCustomerSearchText);
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore durante il salvataggio del cliente.\n\n{ex.Message}",
                "Errore salvataggio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void UpdateCustomer(Customer updated) => UpdateCustomer(updated.BusinessName, updated);

    public void UpdateCustomer(string originalBusinessName, Customer updated)
    {
        var existing = AllCustomers.FirstOrDefault(c => ReferenceEquals(c, updated))
            ?? AllCustomers.FirstOrDefault(c =>
                c.BusinessName.Equals(originalBusinessName, StringComparison.OrdinalIgnoreCase))
            ?? AllCustomers.FirstOrDefault(c =>
                c.BusinessName.Equals(updated.BusinessName, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            updated.SyncId = existing.SyncId;
        }

        _ = UpdateCustomerSafeAsync(originalBusinessName, updated);
    }

    private async Task UpdateCustomerSafeAsync(string originalBusinessName, Customer updated)
    {
        try
        {
            var saved = await _dataService.UpdateCustomerAsync(originalBusinessName, updated);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ApplySavedCustomerToCollections(originalBusinessName, saved);
                OnPropertyChanged(nameof(SelectedCustomer));
                OnPropertyChanged(nameof(SelectedSecondCustomer));
                ApplyCustomerFilter(_customerSearchText);
                ApplySecondCustomerFilter(_secondCustomerSearchText);
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore durante il salvataggio del cliente.\n\n{ex.Message}",
                "Errore salvataggio", MessageBoxButton.OK, MessageBoxImage.Error);
            await RefreshSharedDataAsync();
        }
    }

    private void ApplySavedCustomerToCollections(string originalBusinessName, Customer saved)
    {
        ApplySavedCustomer(AllCustomers, originalBusinessName, saved);
        ApplySavedCustomer(_allCustomers, originalBusinessName, saved);
    }

    private static void ApplySavedCustomer(IList<Customer> customers, string originalBusinessName, Customer saved)
    {
        var existing = customers.FirstOrDefault(c => c.SyncId != Guid.Empty && c.SyncId == saved.SyncId)
            ?? customers.FirstOrDefault(c => c.BusinessName.Equals(originalBusinessName, StringComparison.OrdinalIgnoreCase))
            ?? customers.FirstOrDefault(c => c.BusinessName.Equals(saved.BusinessName, StringComparison.OrdinalIgnoreCase));

        if (existing == null)
        {
            customers.Add(saved);
            return;
        }

        existing.SyncId = saved.SyncId;
        existing.BusinessName = saved.BusinessName;
        existing.Address = saved.Address;
        existing.Email = saved.Email;
        existing.Phone = saved.Phone;
        existing.MaterialDiscount = saved.MaterialDiscount;
        existing.LaborDiscount = saved.LaborDiscount;
        existing.LastModifiedUtc = saved.LastModifiedUtc;
        existing.BaseVersionUtc = saved.BaseVersionUtc;
        existing.HasPendingDatabaseWrite = saved.HasPendingDatabaseWrite;
    }

    #endregion
}

