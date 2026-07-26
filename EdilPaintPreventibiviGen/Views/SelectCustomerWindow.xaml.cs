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
    private readonly bool _suppliersOnly;
    private string _searchText = string.Empty;
    #endregion

    #region Constructor
    public SelectCustomerWindow(MainViewModel vm, bool suppliersOnly = false)
    {
        InitializeComponent();
        EdilPaintPreventibiviGen.Helpers.WindowResizeBehavior.PreventMaximizedState(this);

        _vm = vm;
        _suppliersOnly = suppliersOnly;

        ConfigureMode();
        RefreshResults();
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
            _searchText = tb.Text;
            RefreshResults();
            tb.CaretIndex = tb.Text.Length;
        }
    }

    private void OnInsertClick(object sender, RoutedEventArgs e)
    {
        if (GridResults.SelectedItem is Customer c)
        {
            if (_suppliersOnly && !c.IsSupplier)
            {
                MessageBox.Show(
                    "Contrassegna prima il contatto come fornitore.",
                    "Fornitore non abilitato",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            SelectedResult = c;
            DialogResult = true;
            Close();
        }
    }
    private async void OnDeleteCustomerClick(object sender, RoutedEventArgs e)
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
            await _vm.DeleteCustomerAsync(customer);
            RefreshResults();
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
                RefreshResults();
            }
        }
    }

    private void OnSupplierFlagClick(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox || checkBox.DataContext is not Customer customer)
            return;

        customer.IsSupplier = checkBox.IsChecked == true;
        _vm.UpdateCustomer(customer.BusinessName, customer);

        if (_suppliersOnly && ChkShowAllCustomers.IsChecked != true)
        {
            Dispatcher.BeginInvoke(RefreshResults);
        }
    }

    private void OnShowAllCustomersChanged(object sender, RoutedEventArgs e)
    {
        RefreshResults();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ConfigureMode()
    {
        if (!_suppliersOnly)
            return;

        Title = "Seleziona fornitore";
        TxtHeaderTitle.Text = "Anagrafica fornitori";
        TxtHeaderSubtitle.Text = "Seleziona o modifica un fornitore esistente";
        TxtSearchLabel.Text = "Ricerca veloce fornitore";
        BtnSelect.Content = "Seleziona fornitore";
        ChkShowAllCustomers.Visibility = Visibility.Visible;
    }

    private void RefreshResults()
    {
        IEnumerable<Customer> customers = _vm.AllCustomers;
        if (_suppliersOnly && ChkShowAllCustomers.IsChecked != true)
            customers = customers.Where(customer => customer.IsSupplier);

        GridResults.ItemsSource = customers
            .Where(customer => customer.ContainsText(_searchText))
            .OrderBy(customer => customer.BusinessName)
            .ToList();
    }
    #endregion
}
