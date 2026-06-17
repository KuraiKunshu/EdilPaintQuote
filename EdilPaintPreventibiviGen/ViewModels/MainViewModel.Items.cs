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
        SavePersonalMaterials();
    }

    public void RemovePersonalMaterial(Item item)
    {
        _personalMaterials.Remove(item);
        SavePersonalMaterials();
    }

    public void SavePersonalMaterialsPublic() => SavePersonalMaterials();

    public void AddMaterial()
    {
        if (string.IsNullOrWhiteSpace(InputName))
            return;

        var newItem = new Item
        {
            Name = InputName.Trim(),
            Description = InputDescription,
            UnitPrice = InputValue,
            Quantity = InputQuantity,
            IsSignificant = IsSignificant,
            SortOrder = Materials.Count
        };

        Materials.Add(newItem);

        bool isVeluxMaterial = SelectedCatalogMaterial != null &&
            !SelectedCatalogMaterial.Id.StartsWith("LOCAL_", StringComparison.OrdinalIgnoreCase);
        var existingCatalogMaterial = _personalMaterials.FirstOrDefault(m =>
            m.Name.Equals(newItem.Name, StringComparison.OrdinalIgnoreCase));

        if (existingCatalogMaterial != null)
        {
            UpdateExistingLocalMaterialFromVelux(existingCatalogMaterial, newItem, isVeluxMaterial);
        }
        else if (
            MessageBox.Show(
                $"Il materiale '{newItem.Name}' non è ancora presente nell'anagrafica.\n\nVuoi aggiungerlo ai materiali locali?",
                "Nuovo materiale",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _personalMaterials.Add(new Item
            {
                Name = newItem.Name,
                Description = newItem.Description,
                UnitPrice = newItem.UnitPrice,
                Quantity = 1,
                IsSignificant = newItem.IsSignificant,
                SortOrder = _personalMaterials.Count
            });

            SavePersonalMaterials();
        }

        ResetInputs();
    }

    private void UpdateExistingLocalMaterialFromVelux(
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
        SavePersonalMaterials();
    }

    public void AddLabor()
    {
        if (string.IsNullOrWhiteSpace(InputName))
            return;

        Labors.Add(new Item
        {
            Name = InputName,
            Description = InputDescription,
            UnitPrice = InputValue,
            Quantity = InputQuantity,
            IsSignificant = IsSignificant,
            SortOrder = Labors.Count
        });

        ResetInputs();
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

        if (uuid.StartsWith("LOCAL_"))
        {
            string nameToFind = uuid.Substring(6);
            var localItem = _personalMaterials.FirstOrDefault(m => m.Name == nameToFind);

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
                return;
            }
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
    #endregion
}

