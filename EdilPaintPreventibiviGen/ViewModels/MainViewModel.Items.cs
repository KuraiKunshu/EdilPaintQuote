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
    #region Material & Labor Input
    public void AddPersonalMaterial(Item item)
    {
        _personalMaterials.Add(item);
    }

    public async Task RemovePersonalMaterialAsync(Item item)
    {
        BeginSharedDataMutation();
        try
        {
            await _dataService.DeletePersonalMaterialAsync(CloneCatalogItem(item));
            _personalMaterials.Remove(item);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Materiale non eliminato", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            EndSharedDataMutation();
        }
    }

    public async Task RemoveCatalogLaborAsync(Item item)
    {
        BeginSharedDataMutation();
        try
        {
            await _dataService.DeleteLaborCatalogItemAsync(CloneCatalogItem(item));
            AllCatalogLabors.Remove(item);
            _allCatalogLabors.RemoveAll(x => x.PersistentId == item.PersistentId ||
                x.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Lavorazione non eliminata", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            EndSharedDataMutation();
        }
    }

    public Task SavePersonalMaterialsPublicAsync() => SavePersonalMaterialsAsync();

    public async Task AddMaterialAsync()
    {
        if (string.IsNullOrWhiteSpace(InputName))
            return;

        string materialName = InputName.Trim();
        Item? selectedLocalCatalogMaterial = SelectedCatalogMaterial == null
            ? null
            : FindLocalCatalogMaterial(SelectedCatalogMaterial.Id);
        var existingCatalogMaterial = selectedLocalCatalogMaterial ?? _personalMaterials.FirstOrDefault(m =>
            m.Name.Equals(materialName, StringComparison.OrdinalIgnoreCase));
        bool selectedCatalogMaterialStillMatches =
            selectedLocalCatalogMaterial != null &&
            string.Equals(
                selectedLocalCatalogMaterial.Name.Trim(),
                materialName,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                SelectedCatalogMaterial?.Value?.Trim(),
                materialName,
                StringComparison.OrdinalIgnoreCase);

        var newItem = new Item
        {
            PersistentId = selectedCatalogMaterialStillMatches
                ? selectedLocalCatalogMaterial!.PersistentId
                : 0,
            Name = materialName,
            Description = InputDescription,
            UnitPrice = InputValue,
            Quantity = InputQuantity,
            IsSignificant = IsSignificant,
            SortOrder = Materials.Count
        };

        Materials.Add(newItem);

        bool isVeluxMaterial = SelectedCatalogMaterial != null &&
            !SelectedCatalogMaterial.Id.StartsWith("LOCAL_", StringComparison.OrdinalIgnoreCase);

        if (existingCatalogMaterial != null)
        {
            await UpdateExistingLocalMaterialFromVeluxAsync(
                existingCatalogMaterial,
                newItem,
                isVeluxMaterial);
        }
        else if (
            MessageBox.Show(
                $"Il materiale '{newItem.Name}' non è ancora presente nell'anagrafica.\n\nVuoi aggiungerlo ai materiali locali?",
                "Nuovo materiale",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            var catalogItem = new Item
            {
                Name = newItem.Name,
                Description = newItem.Description,
                UnitPrice = newItem.UnitPrice,
                Quantity = 1,
                IsSignificant = newItem.IsSignificant,
                SortOrder = _personalMaterials.Count
            };
            _personalMaterials.Add(catalogItem);

            await SavePersonalMaterialsAsync();
            if (catalogItem.PersistentId > 0)
                newItem.PersistentId = catalogItem.PersistentId;
        }

        ResetInputs();
        await SaveDraftAsync();
    }

    private async Task UpdateExistingLocalMaterialFromVeluxAsync(
        Item existingCatalogMaterial,
        Item veluxMaterial,
        bool isVeluxMaterial)
    {
        if (!isVeluxMaterial)
            return;

        if (Math.Abs(existingCatalogMaterial.UnitPrice - veluxMaterial.UnitPrice) < 0.001)
            return;

        Debug.WriteLine(
            $"[Velux] Aggiorno prezzo materiale locale '{existingCatalogMaterial.Name}': {existingCatalogMaterial.UnitPrice:N2} -> {veluxMaterial.UnitPrice:N2}");

        existingCatalogMaterial.Description = veluxMaterial.Description;
        existingCatalogMaterial.UnitPrice = veluxMaterial.UnitPrice;
        existingCatalogMaterial.IsSignificant = veluxMaterial.IsSignificant;
        await SavePersonalMaterialsAsync();
    }

    public void AddLabor()
    {
        if (string.IsNullOrWhiteSpace(InputName))
            return;

        string laborName = InputName.Trim();
        bool selectedCatalogLaborStillMatches =
            SelectedCatalogLabor != null &&
            string.Equals(
                SelectedCatalogLabor.Name?.Trim(),
                laborName,
                StringComparison.OrdinalIgnoreCase);

        Labors.Add(new Item
        {
            PersistentId = selectedCatalogLaborStillMatches
                ? SelectedCatalogLabor!.PersistentId
                : 0,
            Name = laborName,
            Description = InputDescription,
            UnitPrice = InputValue,
            Quantity = InputQuantity,
            IsSignificant = IsSignificant,
            SortOrder = Labors.Count
        });

        ResetInputs();
        _ = SaveDraftAsync();
    }

    private void ResetInputs()
    {
        InputName = "";
        InputDescription = "";
        InputValue = 0;
        SelectedCatalogLabor = null;
        SelectedCatalogMaterial = null;
        OnPropertyChanged(string.Empty);
    }

    public async Task FetchVeluxDetails(string uuid, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(uuid))
            return;

        if (cancellationToken.IsCancellationRequested)
            return;

        if (uuid.StartsWith("LOCAL_", StringComparison.OrdinalIgnoreCase))
        {
            Item? localItem = FindLocalCatalogMaterial(uuid);

            if (localItem != null)
            {
                if (SelectedCatalogMaterial?.Id != uuid)
                    return;

                InputName = localItem.Name;
                InputDescription = localItem.Description;
                InputValue = localItem.UnitPrice;
                IsSignificant = localItem.IsSignificant;

                OnPropertyChanged(nameof(InputName));
                OnPropertyChanged(nameof(InputDescription));
                OnPropertyChanged(nameof(InputValue));
                OnPropertyChanged(nameof(IsSignificant));
            }

            return;
        }

        if (!App.AppSettings.App.UseVeluxLogin)
            return;

        Item? details;
        try
        {
            details = await _veluxService.GetProductDetailsAsync(uuid, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (details != null)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            if (SelectedCatalogMaterial?.Id != uuid)
                return;

            InputName = details.Name;
            InputDescription = details.Description;
            InputValue = details.UnitPrice;
            IsSignificant = IsMaterialSignificant(details.Name);

            OnPropertyChanged(nameof(InputName));
            OnPropertyChanged(nameof(InputDescription));
            OnPropertyChanged(nameof(InputValue));
            OnPropertyChanged(nameof(IsSignificant));
        }
    }

    private Item? FindLocalCatalogMaterial(string selectionId)
    {
        const string idPrefix = "LOCAL_ID_";
        const string namePrefix = "LOCAL_NAME_";
        const string legacyPrefix = "LOCAL_";

        if (selectionId.StartsWith(idPrefix, StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(
                selectionId[idPrefix.Length..],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out int persistentId) &&
            persistentId > 0)
        {
            return _personalMaterials.FirstOrDefault(item => item.PersistentId == persistentId);
        }

        string? materialName = selectionId.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase)
            ? selectionId[namePrefix.Length..]
            : selectionId.StartsWith(legacyPrefix, StringComparison.OrdinalIgnoreCase)
                ? selectionId[legacyPrefix.Length..]
                : null;
        if (string.IsNullOrWhiteSpace(materialName))
            return null;

        Item[] exactMatches = _personalMaterials
            .Where(item => string.Equals(
                item.Name.Trim(),
                materialName.Trim(),
                StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return exactMatches.Length == 1 ? exactMatches[0] : null;
    }
    #endregion
}

