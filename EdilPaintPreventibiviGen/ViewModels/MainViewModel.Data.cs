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
            if (Volatile.Read(ref _sharedDataMutationsInProgress) > 0)
                return;

            long mutationVersion = Volatile.Read(ref _sharedDataMutationVersion);
            var snapshot = await Task.Run(async () =>
            {
                var customers = await _dataService.GetCustomersAsync(cancellationToken);
                var labors = await _dataService.GetLaborCatalogAsync();
                var personalMaterials = await _dataService.GetPersonalMaterialsAsync();
                return (customers, labors, personalMaterials);
            }, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _sharedDataMutationsInProgress) > 0 ||
                Volatile.Read(ref _sharedDataMutationVersion) != mutationVersion)
            {
                return;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                return;

            await dispatcher.InvokeAsync(() =>
            {
                if (Volatile.Read(ref _sharedDataMutationsInProgress) > 0 ||
                    Volatile.Read(ref _sharedDataMutationVersion) != mutationVersion)
                {
                    return;
                }

                Customer? selectedCustomer = SelectedCustomer;
                Customer? selectedReference = SelectedSecondCustomer;
                Customer? selectedBilling = SelectedBillingCustomer;

                if (MergeCustomers(snapshot.customers))
                {
                    _selectedCustomer = FindRefreshedCustomer(selectedCustomer);
                    _selectedSecondCustomer = FindRefreshedCustomer(selectedReference);
                    _selectedBillingCustomer = FindRefreshedCustomer(selectedBilling);
                    CustomerBorderBrush = GetCustomerSelectionBrush(_selectedCustomer != null);
                    SecondCustomerBorderBrush = GetCustomerSelectionBrush(_selectedSecondCustomer != null);
                    OnPropertyChanged(nameof(SelectedCustomer));
                    OnPropertyChanged(nameof(SelectedSecondCustomer));
                    OnPropertyChanged(nameof(SelectedBillingCustomer));

                    if (FilteredCustomers.Count > 0 || !string.IsNullOrWhiteSpace(_customerSearchText))
                    {
                        SynchronizeCollection(
                            FilteredCustomers,
                            AllCustomers.Where(customer => customer.ContainsText(_customerSearchText)).ToList());
                    }

                    if (FilteredSecondCustomers.Count > 0 || !string.IsNullOrWhiteSpace(_secondCustomerSearchText))
                    {
                        SynchronizeCollection(
                            FilteredSecondCustomers,
                            AllCustomers.Where(customer => customer.ContainsText(_secondCustomerSearchText)).ToList());
                    }
                }

                if (MergeCatalogLabors(snapshot.labors) &&
                    (FilteredLabors.Count > 0 || !string.IsNullOrWhiteSpace(_laborSearchText)))
                {
                    SynchronizeCollection(
                        FilteredLabors,
                        AllCatalogLabors.Where(labor =>
                            string.IsNullOrWhiteSpace(_laborSearchText) ||
                            labor.Name.Contains(_laborSearchText, StringComparison.OrdinalIgnoreCase)).ToList());
                }

                MergeItemList(_personalMaterials, snapshot.personalMaterials);
            }, System.Windows.Threading.DispatcherPriority.Background, cancellationToken);
        }
        finally
        {
            _sharedDataRefreshLock.Release();
        }
    }

    private Customer? FindRefreshedCustomer(Customer? previous)
    {
        if (previous == null)
            return null;
        if (AllCustomers.Contains(previous))
            return previous;
        if (previous.SyncId != Guid.Empty)
            return AllCustomers.FirstOrDefault(customer => customer.SyncId == previous.SyncId);

        var sameContent = AllCustomers
            .Where(customer => CustomersHaveSameVisibleContent(customer, previous))
            .Take(2)
            .ToList();
        if (sameContent.Count == 1)
            return sameContent[0];

        var sameName = AllCustomers
            .Where(customer => customer.BusinessName.Equals(
                previous.BusinessName,
                StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();
        return sameName.Count == 1 ? sameName[0] : null;
    }

    private bool MergeCustomers(IReadOnlyList<Customer> incoming)
    {
        var byId = AllCustomers
            .Where(customer => customer.SyncId != Guid.Empty)
            .GroupBy(customer => customer.SyncId)
            .ToDictionary(group => group.Key, group => group.First());
        var byName = AllCustomers
            .Where(customer => !string.IsNullOrWhiteSpace(customer.BusinessName))
            .GroupBy(customer => customer.BusinessName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var desired = new List<Customer>(incoming.Count);
        var used = new HashSet<Customer>();
        bool changed = false;

        foreach (var source in incoming)
        {
            Customer? target = null;
            if (source.SyncId != Guid.Empty &&
                byId.TryGetValue(source.SyncId, out var idMatch) &&
                !used.Contains(idMatch))
            {
                target = idMatch;
            }

            if (target == null &&
                !string.IsNullOrWhiteSpace(source.BusinessName) &&
                byName.TryGetValue(source.BusinessName, out var nameMatches))
            {
                target = nameMatches.FirstOrDefault(candidate =>
                    !used.Contains(candidate) &&
                    (source.SyncId == Guid.Empty || candidate.SyncId == Guid.Empty) &&
                    CustomersHaveSameVisibleContent(candidate, source))
                    ?? nameMatches.FirstOrDefault(candidate =>
                        !used.Contains(candidate) &&
                        (source.SyncId == Guid.Empty || candidate.SyncId == Guid.Empty));
            }

            if (target == null)
            {
                target = source;
                changed = true;
            }
            else
            {
                changed |= ApplyCustomerSnapshot(target, source);
            }

            used.Add(target);
            desired.Add(target);
        }

        changed |= SynchronizeCollection(AllCustomers, desired);
        _allCustomers = AllCustomers.ToList();
        return changed;
    }

    private bool MergeCatalogLabors(IReadOnlyList<Item> incoming)
    {
        var desired = MergeItems(AllCatalogLabors, incoming, out bool changed);
        changed |= SynchronizeCollection(AllCatalogLabors, desired);
        _allCatalogLabors = AllCatalogLabors.ToList();
        return changed;
    }

    private static void MergeItemList(List<Item> current, IReadOnlyList<Item> incoming)
    {
        var desired = MergeItems(current, incoming, out bool changed);
        if (!changed && current.Count == desired.Count &&
            current.Zip(desired).All(pair => ReferenceEquals(pair.First, pair.Second)))
        {
            return;
        }

        current.Clear();
        current.AddRange(desired);
    }

    private static List<Item> MergeItems(
        IEnumerable<Item> current,
        IReadOnlyList<Item> incoming,
        out bool changed)
    {
        var currentItems = current.ToList();
        var byId = currentItems
            .Where(item => item.PersistentId > 0)
            .GroupBy(item => item.PersistentId)
            .ToDictionary(group => group.Key, group => group.First());
        var byName = currentItems
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var desired = new List<Item>(incoming.Count);
        var used = new HashSet<Item>();
        changed = false;
        foreach (var source in incoming)
        {
            Item? target = null;
            if (source.PersistentId > 0 &&
                byId.TryGetValue(source.PersistentId, out var idMatch) &&
                !used.Contains(idMatch))
            {
                target = idMatch;
            }

            if (target == null &&
                !string.IsNullOrWhiteSpace(source.Name) &&
                byName.TryGetValue(source.Name, out var nameMatches))
            {
                target = nameMatches.FirstOrDefault(candidate =>
                    !used.Contains(candidate) &&
                    (source.PersistentId <= 0 || candidate.PersistentId <= 0) &&
                    ItemsHaveSameVisibleContent(candidate, source))
                    ?? nameMatches.FirstOrDefault(candidate =>
                        !used.Contains(candidate) &&
                        (source.PersistentId <= 0 || candidate.PersistentId <= 0));
            }

            if (target == null)
            {
                target = source;
                changed = true;
            }
            else
            {
                changed |= ApplyItemSnapshot(target, source);
            }

            used.Add(target);
            desired.Add(target);
        }

        if (currentItems.Count != desired.Count)
            changed = true;

        return desired;
    }

    private static bool CustomersHaveSameVisibleContent(Customer left, Customer right) =>
        string.Equals(left.BusinessName, right.BusinessName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Address, right.Address, StringComparison.Ordinal) &&
        string.Equals(left.Email, right.Email, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Phone, right.Phone, StringComparison.Ordinal) &&
        left.MaterialDiscount.Equals(right.MaterialDiscount) &&
        left.LaborDiscount.Equals(right.LaborDiscount) &&
        left.SupplierDiscount.Equals(right.SupplierDiscount) &&
        left.IsSupplier == right.IsSupplier;

    private static bool ItemsHaveSameVisibleContent(Item left, Item right) =>
        string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Description, right.Description, StringComparison.Ordinal) &&
        left.UnitPrice.Equals(right.UnitPrice) &&
        left.Quantity == right.Quantity &&
        left.Discount.Equals(right.Discount) &&
        left.IsSignificant == right.IsSignificant &&
        left.IsCompanyMaterial == right.IsCompanyMaterial &&
        left.SortOrder == right.SortOrder;

    private static bool ApplyCustomerSnapshot(Customer target, Customer source)
    {
        bool changed = false;
        if (source.SyncId != Guid.Empty || target.SyncId == Guid.Empty)
            SetIfDifferent(target.SyncId, source.SyncId, value => target.SyncId = value, ref changed);
        SetIfDifferent(target.BusinessName, source.BusinessName, value => target.BusinessName = value, ref changed);
        SetIfDifferent(target.Address, source.Address, value => target.Address = value, ref changed);
        SetIfDifferent(target.Email, source.Email, value => target.Email = value, ref changed);
        SetIfDifferent(target.Phone, source.Phone, value => target.Phone = value, ref changed);
        SetIfDifferent(target.MaterialDiscount, source.MaterialDiscount, value => target.MaterialDiscount = value, ref changed);
        SetIfDifferent(target.LaborDiscount, source.LaborDiscount, value => target.LaborDiscount = value, ref changed);
        SetIfDifferent(target.SupplierDiscount, source.SupplierDiscount, value => target.SupplierDiscount = value, ref changed);
        SetIfDifferent(target.IsSupplier, source.IsSupplier, value => target.IsSupplier = value, ref changed);
        SetIfDifferent(target.LastModifiedUtc, source.LastModifiedUtc, value => target.LastModifiedUtc = value, ref changed);
        SetIfDifferent(target.BaseVersionUtc, source.BaseVersionUtc, value => target.BaseVersionUtc = value, ref changed);
        SetIfDifferent(target.HasPendingDatabaseWrite, source.HasPendingDatabaseWrite, value => target.HasPendingDatabaseWrite = value, ref changed);
        return changed;
    }

    private static bool ApplyItemSnapshot(Item target, Item source)
    {
        bool changed = false;
        if (source.PersistentId > 0 || target.PersistentId <= 0)
            SetIfDifferent(target.PersistentId, source.PersistentId, value => target.PersistentId = value, ref changed);
        SetIfDifferent(target.Name, source.Name, value => target.Name = value, ref changed);
        SetIfDifferent(target.Description, source.Description, value => target.Description = value, ref changed);
        SetIfDifferent(target.UnitPrice, source.UnitPrice, value => target.UnitPrice = value, ref changed);
        SetIfDifferent(target.Quantity, source.Quantity, value => target.Quantity = value, ref changed);
        SetIfDifferent(target.Discount, source.Discount, value => target.Discount = value, ref changed);
        SetIfDifferent(target.IsSignificant, source.IsSignificant, value => target.IsSignificant = value, ref changed);
        SetIfDifferent(target.IsCompanyMaterial, source.IsCompanyMaterial, value => target.IsCompanyMaterial = value, ref changed);
        SetIfDifferent(target.SortOrder, source.SortOrder, value => target.SortOrder = value, ref changed);
        return changed;
    }

    private static void SetIfDifferent<T>(T current, T incoming, Action<T> apply, ref bool changed)
    {
        if (EqualityComparer<T>.Default.Equals(current, incoming))
            return;

        apply(incoming);
        changed = true;
    }

    private static bool SynchronizeCollection<T>(
        ObservableCollection<T> target,
        IReadOnlyList<T> desired)
        where T : class
    {
        bool changed = false;
        var desiredSet = new HashSet<T>(desired);
        for (int index = target.Count - 1; index >= 0; index--)
        {
            if (desiredSet.Contains(target[index]))
                continue;

            target.RemoveAt(index);
            changed = true;
        }

        var currentSet = new HashSet<T>(target);
        foreach (var item in desired)
        {
            if (!currentSet.Add(item))
                continue;

            // Manteniamo stabile l'ordine corrente: riallineare migliaia di
            // elementi per una sola rinomina genererebbe migliaia di eventi Move.
            target.Add(item);
            changed = true;
        }

        return changed;
    }

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
        bool refreshAfterSave = false;
        BeginSharedDataMutation();
        try
        {
            var snapshot = AllCatalogLabors.Select(CloneCatalogItem).ToList();
            await _dataService.SaveLaborCatalogAsync(snapshot);
            ApplyPersistentIds(AllCatalogLabors, snapshot);
            _allCatalogLabors = AllCatalogLabors.ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SaveLaborsAsync] Error: {ex.Message}");
            MessageBox.Show(ex.Message, "Catalogo non salvato", MessageBoxButton.OK, MessageBoxImage.Warning);
            refreshAfterSave = true;
        }
        finally
        {
            EndSharedDataMutation();
        }

        if (refreshAfterSave)
            await RefreshSharedDataAsync();
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
        bool refreshAfterSave = false;
        BeginSharedDataMutation();
        try
        {
            var snapshot = _personalMaterials.Select(CloneCatalogItem).ToList();
            await _dataService.SavePersonalMaterialsAsync(snapshot);
            ApplyPersistentIds(_personalMaterials, snapshot);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SAVE PERSONAL MATERIALS] Error: {ex.Message}");
            MessageBox.Show(ex.Message, "Materiali non salvati", MessageBoxButton.OK, MessageBoxImage.Warning);
            refreshAfterSave = true;
        }
        finally
        {
            EndSharedDataMutation();
        }

        if (refreshAfterSave)
            await RefreshSharedDataAsync();
    }

    public async Task AddNewCustomerAsync(Customer c)
    {
        Customer? savedCustomer = null;
        BeginSharedDataMutation();
        try
        {
            var snapshot = CloneCustomerForPersistence(c);
            savedCustomer = await _dataService.AddCustomerAsync(snapshot);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ApplySavedCustomerToCollections(c.BusinessName, savedCustomer);
                SelectedCustomer = AllCustomers.FirstOrDefault(customer =>
                    customer.SyncId != Guid.Empty && customer.SyncId == savedCustomer.SyncId)
                    ?? AllCustomers.FirstOrDefault(customer =>
                        customer.BusinessName.Equals(savedCustomer.BusinessName, StringComparison.OrdinalIgnoreCase))
                    ?? savedCustomer;
                ApplyCustomerFilter(_customerSearchText);
                ApplySecondCustomerFilter(_secondCustomerSearchText);
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore durante il salvataggio del cliente.\n\n{ex.Message}",
                "Errore salvataggio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            EndSharedDataMutation();
        }

        if (savedCustomer == null)
            return;

        await RefreshSharedDataAsync();
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            SelectedCustomer = AllCustomers.FirstOrDefault(customer =>
                customer.SyncId != Guid.Empty && customer.SyncId == savedCustomer.SyncId)
                ?? AllCustomers.FirstOrDefault(customer =>
                    customer.BusinessName.Equals(savedCustomer.BusinessName, StringComparison.OrdinalIgnoreCase));
            ApplyCustomerFilter(_customerSearchText);
            ApplySecondCustomerFilter(_secondCustomerSearchText);
        });
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

        var snapshot = CloneCustomerForPersistence(updated);
        BeginSharedDataMutation();
        _ = UpdateCustomerSafeAsync(originalBusinessName, snapshot);
    }

    private async Task UpdateCustomerSafeAsync(string originalBusinessName, Customer updated)
    {
        bool refreshAfterSave = false;
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
            refreshAfterSave = true;
        }
        finally
        {
            EndSharedDataMutation();
        }

        if (refreshAfterSave)
            await RefreshSharedDataAsync();
    }

    private void BeginSharedDataMutation()
    {
        Interlocked.Increment(ref _sharedDataMutationsInProgress);
        Interlocked.Increment(ref _sharedDataMutationVersion);
    }

    private void EndSharedDataMutation()
    {
        Interlocked.Increment(ref _sharedDataMutationVersion);
        Interlocked.Decrement(ref _sharedDataMutationsInProgress);
    }

    private static Customer CloneCustomerForPersistence(Customer source) => new()
    {
        SyncId = source.SyncId,
        BusinessName = source.BusinessName,
        Address = source.Address,
        Email = source.Email,
        Phone = source.Phone,
        MaterialDiscount = source.MaterialDiscount,
        LaborDiscount = source.LaborDiscount,
        SupplierDiscount = source.SupplierDiscount,
        IsSupplier = source.IsSupplier,
        LastModifiedUtc = source.LastModifiedUtc,
        BaseVersionUtc = source.BaseVersionUtc,
        HasPendingDatabaseWrite = source.HasPendingDatabaseWrite
    };

    private static Item CloneCatalogItem(Item source) => new()
    {
        PersistentId = source.PersistentId,
        Name = source.Name,
        Description = source.Description,
        UnitPrice = source.UnitPrice,
        Quantity = source.Quantity,
        Discount = source.Discount,
        IsSignificant = source.IsSignificant,
        IsCompanyMaterial = source.IsCompanyMaterial,
        SortOrder = source.SortOrder
    };

    private static void ApplyPersistentIds(IList<Item> current, IEnumerable<Item> saved)
    {
        var used = new HashSet<Item>();
        foreach (var savedItem in saved.Where(item => item.PersistentId > 0))
        {
            var target = current.FirstOrDefault(item =>
                !used.Contains(item) && item.PersistentId == savedItem.PersistentId)
                ?? current.FirstOrDefault(item =>
                    !used.Contains(item) &&
                    item.PersistentId <= 0 &&
                    item.Name.Equals(savedItem.Name, StringComparison.OrdinalIgnoreCase));
            if (target != null)
            {
                target.PersistentId = savedItem.PersistentId;
                used.Add(target);
            }
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
        existing.SupplierDiscount = saved.SupplierDiscount;
        existing.IsSupplier = saved.IsSupplier;
        existing.LastModifiedUtc = saved.LastModifiedUtc;
        existing.BaseVersionUtc = saved.BaseVersionUtc;
        existing.HasPendingDatabaseWrite = saved.HasPendingDatabaseWrite;
    }

    #endregion
}

