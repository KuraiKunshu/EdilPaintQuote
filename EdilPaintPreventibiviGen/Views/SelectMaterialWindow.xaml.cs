using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.ViewModels;

namespace EdilPaintPreventibiviGen.Views;

public partial class SelectMaterialWindow : Window
{
    private readonly MainViewModel _vm;
    private Item? _editingMaterial;

    public SelectMaterialWindow(MainViewModel vm)
    {
        InitializeComponent();
        EdilPaintPreventibiviGen.Helpers.WindowResizeBehavior.PreventMaximizedState(this);
        _vm = vm;
        FilterList(string.Empty);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        => FilterList(TxtSearch.Text);

    private void FilterList(string query)
    {
        var source = _vm.PersonalMaterialsView;

        GridMaterials.ItemsSource = string.IsNullOrWhiteSpace(query)
            ? source
            : source.Where(m =>
                (!string.IsNullOrWhiteSpace(m.Name) && m.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(m.Description) && m.Description.Contains(query, StringComparison.OrdinalIgnoreCase)))
              .ToList();
    }

    private void OnMaterialSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GridMaterials.SelectedItem is Item selected)
        {
            _editingMaterial = selected;
            TxtFormTitle.Text = "Modifica materiale";
            BtnSaveMaterial.Content = "Salva modifiche";

            TxtName.Text = selected.Name;
            TxtDesc.Text = selected.Description;
            TxtPrice.Text = selected.UnitPrice.ToString(CultureInfo.InvariantCulture);
            ChkSignificant.IsChecked = selected.IsSignificant;
        }
        else
        {
            ResetInputs();
        }
    }

    private void OnSaveMaterialClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        {
            MessageBox.Show("Inserisci il nome del materiale.");
            return;
        }

        if (!double.TryParse(
                (TxtPrice.Text ?? string.Empty).Trim().Replace(',', '.'),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double price))
        {
            MessageBox.Show("Il prezzo inserito non è valido.");
            return;
        }

        if (_editingMaterial != null)
        {
            _editingMaterial.Name = TxtName.Text;
            _editingMaterial.Description = TxtDesc.Text;
            _editingMaterial.UnitPrice = price;
            _editingMaterial.IsSignificant = ChkSignificant.IsChecked ?? false;
        }
        else
        {
            _vm.AddPersonalMaterial(new Item
            {
                Name = TxtName.Text,
                Description = TxtDesc.Text,
                UnitPrice = price,
                Quantity = 1,
                IsSignificant = ChkSignificant.IsChecked ?? false
            });
        }

        _vm.SavePersonalMaterialsPublic();
        ResetInputs();
        FilterList(TxtSearch.Text);
    }

    private async void OnDeleteMaterialClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Item item)
        {
            var result = MessageBox.Show(
                $"Eliminare il materiale '{item.Name}' dall'anagrafica?",
                "Conferma eliminazione",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            await _vm.RemovePersonalMaterialAsync(item);
            ResetInputs();
            FilterList(TxtSearch.Text);
        }
    }

    private void OnResetClick(object sender, RoutedEventArgs e) => ResetInputs();

    private void ResetInputs()
    {
        _editingMaterial = null;
        TxtFormTitle.Text = "Nuovo materiale";
        BtnSaveMaterial.Content = "Salva in anagrafica";
        TxtName.Text = string.Empty;
        TxtDesc.Text = string.Empty;
        TxtPrice.Text = "0";
        ChkSignificant.IsChecked = false;
        GridMaterials.SelectedItem = null;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();
}
