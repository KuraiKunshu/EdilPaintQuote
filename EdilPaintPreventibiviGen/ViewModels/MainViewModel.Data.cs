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
    #region Data Loading & Saving
    public Task InitializeAsync() => LoadDataAsync();

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

                    if (_companyData.Logo_index < Logos.Count)
                    {
                        _selectedLogo = Logos[_companyData.Logo_index];
                        OnPropertyChanged(nameof(SelectedLogo));
                    }
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
        }
    }

    private void SaveCompanyData() => _ = SaveCompanyDataAsync();

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
        catch (Exception ex) { Debug.WriteLine($"[SAVE PERSONAL MATERIALS] Error: {ex.Message}"); }
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
            existing.BusinessName = updated.BusinessName;
            existing.Address = updated.Address;
            existing.Email = updated.Email;
            existing.Phone = updated.Phone;
            existing.MaterialDiscount = updated.MaterialDiscount;
            existing.LaborDiscount = updated.LaborDiscount;
        }

        var existingInAll = _allCustomers.FirstOrDefault(c => ReferenceEquals(c, updated))
            ?? _allCustomers.FirstOrDefault(c =>
                c.BusinessName.Equals(originalBusinessName, StringComparison.OrdinalIgnoreCase))
            ?? _allCustomers.FirstOrDefault(c =>
                c.BusinessName.Equals(updated.BusinessName, StringComparison.OrdinalIgnoreCase));

        if (existingInAll != null)
        {
            existingInAll.BusinessName = updated.BusinessName;
            existingInAll.Address = updated.Address;
            existingInAll.Email = updated.Email;
            existingInAll.Phone = updated.Phone;
            existingInAll.MaterialDiscount = updated.MaterialDiscount;
            existingInAll.LaborDiscount = updated.LaborDiscount;
        }

        OnPropertyChanged(nameof(SelectedCustomer));
        OnPropertyChanged(nameof(SelectedSecondCustomer));
        ApplyCustomerFilter(_customerSearchText);
        ApplySecondCustomerFilter(_secondCustomerSearchText);
        _ = UpdateCustomerSafeAsync(originalBusinessName, updated);
    }

    private async Task UpdateCustomerSafeAsync(string originalBusinessName, Customer updated)
    {
        try { await _dataService.UpdateCustomerAsync(originalBusinessName, updated); }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore durante il salvataggio del cliente.\n\n{ex.Message}",
                "Errore salvataggio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion
}

