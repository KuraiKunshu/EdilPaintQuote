using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.ViewModels;

namespace EdilPaintPreventibiviGen.Views;

public partial class SelectCustomerWindow : Window
{
    #region Fields
    public Customer? SelectedResult { get; private set; }
    private readonly MainViewModel _vm;
    #endregion

    #region Constructor
    public SelectCustomerWindow(MainViewModel vm)
    {
        InitializeComponent();

        _vm = vm;
        _vm.ApplyCustomerFilter(string.Empty);

        GridResults.ItemsSource = _vm.FilteredCustomers;
        Loaded += SelectCustomerWindow_Loaded;
        PreviewKeyDown += SelectCustomerWindow_PreviewKeyDown;
        Closed += SelectCustomerWindow_Closed;
    }
    #endregion

    #region Window Chrome
    private void SelectCustomerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        TxtSearch.Focus();
        TxtSearch.CaretIndex = TxtSearch.Text.Length;
    }

    private void SelectCustomerWindow_Closed(object? sender, System.EventArgs e)
    {
        _vm.ApplyCustomerFilter(string.Empty);
        TxtSearch.Text = string.Empty;
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

    private void SelectCustomerWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }
    #endregion

    #region Handlers
    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            _vm.ApplyCustomerFilter(tb.Text);
            tb.CaretIndex = tb.Text.Length;
        }
    }

    private void OnInsertClick(object sender, RoutedEventArgs e)
    {
        if (GridResults.SelectedItem is Customer c)
        {
            SelectedResult = c;
            DialogResult = true;
            Close();
        }
    }
    private void OnDeleteCustomerClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not Customer customer)
            return;

        var result = MessageBox.Show(
            $"Sei sicuro di voler eliminare il cliente:\n\n{customer.BusinessName}?\n\nL'operazione non è reversibile.",
            "Conferma eliminazione",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            _vm.DeleteCustomer(customer);
            _vm.ApplyCustomerFilter(TxtSearch.Text);
        }
    }

    private void OnEditCustomerClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Customer customerToEdit)
        {
            string originalBusinessName = customerToEdit.BusinessName;
            var editWin = new NewCustomerWindow(customerToEdit)
            {
                Owner = this
            };

            if (editWin.ShowDialog() == true)
            {
                if (editWin.NewCustomer != null)
                    _vm.UpdateCustomer(originalBusinessName, editWin.NewCustomer);
                _vm.ApplyCustomerFilter(TxtSearch.Text);
            }
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
    #endregion
}
