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
        try
        {
            await _dataService.DeleteCustomerAsync(customer.BusinessName);
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
        if (string.IsNullOrWhiteSpace(text) || text.Length < 3)
        {
            await Application.Current.Dispatcher.InvokeAsync(() => AllCatalogMaterials.Clear());
            return;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var personalMatches = _personalMaterials
                .Where(m => m.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
                .Select(p => new VeluxResult
                {
                    Id = "LOCAL_" + p.Name,
                    Label = $"[Locale] {p.Name} - EUR {p.UnitPrice:N2}",
                    Value = p.Name
                })
                .ToList();

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                AllCatalogMaterials.Clear();
                foreach (var p in personalMatches)
                    AllCatalogMaterials.Add(p);
            });

            if (!App.AppSettings.App.UseVeluxLogin)
                return;

            var veluxResults = await _veluxService.SearchProductsAsync(text, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Debug.WriteLine($"[SEARCH] Velux: {veluxResults.Count} | Locali: {personalMatches.Count}");

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                for (int i = 0; i < veluxResults.Count; i++)
                    AllCatalogMaterials.Insert(i, veluxResults[i]);
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ApplyMaterialFilter] Errore: {ex.Message}");
        }
    }
    #endregion
}

