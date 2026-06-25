using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.ViewModels;

namespace EdilPaintPreventibiviGen.Views;

public partial class SelectLaborWindow : Window
{
    private readonly MainViewModel _vm;
    private Item? _editingLabor;

    public SelectLaborWindow(MainViewModel vm)
    {
        InitializeComponent();
        EdilPaintPreventibiviGen.Helpers.WindowResizeBehavior.PreventMaximizedState(this);
        _vm = vm;
        FilterList(string.Empty);
        PreviewKeyDown += SelectLaborWindow_PreviewKeyDown;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        FilterList(TxtSearch.Text);
    }

    private void FilterList(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            GridLabors.ItemsSource = _vm.AllCatalogLabors;
            return;
        }

        GridLabors.ItemsSource = _vm.AllCatalogLabors
            .Where(l =>
                (!string.IsNullOrWhiteSpace(l.Name) && l.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(l.Description) && l.Description.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private void OnLaborSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GridLabors.SelectedItem is Item selected)
        {
            _editingLabor = selected;
            TxtFormTitle.Text = "Modifica lavorazione";
            BtnSaveLabor.Content = "Salva modifiche";

            TxtName.Text = selected.Name;
            TxtDesc.Text = selected.Description;
            TxtPrice.Text = selected.UnitPrice.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            ResetInputs();
        }
    }

    private void OnSaveLaborClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        {
            MessageBox.Show("Inserisci il nome della lavorazione.");
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

        if (_editingLabor != null)
        {
            _editingLabor.Name = TxtName.Text;
            _editingLabor.Description = TxtDesc.Text;
            _editingLabor.UnitPrice = price;
        }
        else
        {
            _vm.AllCatalogLabors.Add(new Item
            {
                Name = TxtName.Text,
                Description = TxtDesc.Text,
                UnitPrice = price,
                Quantity = 1
            });
        }

        _vm.SaveLaborsJson();
        ResetInputs();
        FilterList(TxtSearch.Text);
    }

    private async void OnDeleteLaborClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Item itemToDelete)
        {
            var result = MessageBox.Show(
                $"Eliminare la lavorazione '{itemToDelete.Name}' dall'anagrafica?",
                "Conferma eliminazione",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            await _vm.RemoveCatalogLaborAsync(itemToDelete);
            ResetInputs();
            FilterList(TxtSearch.Text);
        }
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        ResetInputs();
    }

    private void ResetInputs()
    {
        _editingLabor = null;
        TxtFormTitle.Text = "Nuova lavorazione";
        BtnSaveLabor.Content = "Salva in anagrafica";

        TxtName.Text = string.Empty;
        TxtDesc.Text = string.Empty;
        TxtPrice.Text = "0";
        GridLabors.SelectedItem = null;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SelectLaborWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }
}
