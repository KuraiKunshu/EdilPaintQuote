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
            : FindCatalogMaterial(SelectedCatalogMaterial);
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
            UnitOfMeasure = InputUnitOfMeasure,
            IsSignificant = IsSignificant,
            SortOrder = Materials.Count
        };

        Materials.Add(newItem);

        if (existingCatalogMaterial == null &&
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
                UnitOfMeasure = newItem.UnitOfMeasure,
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
            UnitOfMeasure = InputUnitOfMeasure,
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
        InputQuantity = 1;
        InputUnitOfMeasure = "pz";
        SelectedCatalogLabor = null;
        SelectedCatalogMaterial = null;
        OnPropertyChanged(string.Empty);
    }

    private void ApplyCatalogMaterial(CatalogMaterialOption selection)
    {
        Item? item = FindCatalogMaterial(selection);
        if (item == null)
            return;
        InputName = item.Name;
        InputDescription = item.Description;
        InputValue = item.UnitPrice;
        InputUnitOfMeasure = item.UnitOfMeasure;
        IsSignificant = item.IsSignificant;
    }

    private Item? FindCatalogMaterial(CatalogMaterialOption selection)
    {
        if (selection.Item.PersistentId > 0)
            return _personalMaterials.FirstOrDefault(item => item.PersistentId == selection.Item.PersistentId);
        return _personalMaterials.FirstOrDefault(item => ReferenceEquals(item, selection.Item));
    }
    #endregion
}

