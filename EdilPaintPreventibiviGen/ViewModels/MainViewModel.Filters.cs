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
    #region Filters & Search
    public void ApplyCustomerFilter(string text)
    {
        _customerSearchText = text;
        FilteredCustomers.Clear();
        foreach (var c in AllCustomers.Where(c => c.ContainsText(text)))
            FilteredCustomers.Add(c);
    }

    public void DeleteCustomer(Customer customer) => _ = DeleteCustomerAsync(customer);

    public async Task DeleteCustomerAsync(Customer customer)
    {
        BeginSharedDataMutation();
        try
        {
            await DatabaseOperationCoordinator.EnsureInteractiveDatabaseReadyAsync(
                _dataService,
                $"Eliminazione cliente {customer.BusinessName}");
            await DatabaseOperationCoordinator.Gate.WaitAsync();
            try
            {
                await _dataService.DeleteCustomerAsync(CloneCustomerForPersistence(customer));
            }
            finally
            {
                DatabaseOperationCoordinator.Gate.Release();
            }
            AllCustomers.Remove(customer);
            _allCustomers.Remove(customer);
            ApplyCustomerFilter(_customerSearchText);
            ApplySecondCustomerFilter(_secondCustomerSearchText);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossibile eliminare il cliente.\n\n{ex.Message}",
                "Errore eliminazione", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            EndSharedDataMutation();
        }
    }

    public void ApplySecondCustomerFilter(string text)
    {
        _secondCustomerSearchText = text;
        FilteredSecondCustomers.Clear();
        foreach (var c in AllCustomers.Where(c => c.ContainsText(text)))
            FilteredSecondCustomers.Add(c);
    }

    public void ApplyLaborFilter(string text)
    {
        _laborSearchText = text;
        FilteredLabors.Clear();
        foreach (var l in AllCatalogLabors.Where(l =>
                     string.IsNullOrWhiteSpace(text) || l.Name.Contains(text, StringComparison.OrdinalIgnoreCase)))
            FilteredLabors.Add(l);
    }

    public void ApplyMaterialFilter(string text) => _ = ApplyMaterialFilterAsync(text);

    public async Task ApplyMaterialFilterAsync(string text, CancellationToken cancellationToken = default)
    {
        var matches = MaterialCatalogSearch.Find(_personalMaterials, text, cancellationToken);
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            AllCatalogMaterials.Clear();
            foreach (var match in matches)
                AllCatalogMaterials.Add(match);
        });
    }
    #endregion
}

