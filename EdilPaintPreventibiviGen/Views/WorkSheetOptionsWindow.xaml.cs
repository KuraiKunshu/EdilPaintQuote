using System.Globalization;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Views;

public partial class WorkSheetOptionsWindow : Window
{
    private readonly List<WorkSheetEmployeeSelection> _employees = [];
    private ICollectionView? _employeeView;
    private bool _clearingEmployees;
    private string? _invalidDateText;
    public WorkSheetOptions Options { get; private set; } = new();

    public WorkSheetOptionsWindow(IEnumerable<EmployeeSettingsModel> employees, string quoteNumber)
    {
        InitializeComponent();
        Helpers.WindowResizeBehavior.PreventMaximizedState(this);
        TxtQuote.Text = $"Preventivo {quoteNumber}";
        _employees.AddRange(employees.Select(employee => new WorkSheetEmployeeSelection
        {
            DisplayName = $"{employee.FirstName} {employee.LastName}".Trim()
        }).OrderBy(employee => employee.DisplayName, StringComparer.CurrentCultureIgnoreCase));
        _employeeView = CollectionViewSource.GetDefaultView(_employees);
        _employeeView.Filter = item => item is WorkSheetEmployeeSelection employee &&
            EmployeeMatches(employee, TxtEmployeeSearch.Text, ChkSelectedOnly.IsChecked == true);
        ItemsEmployees.ItemsSource = _employeeView;
        foreach (var employee in _employees)
            employee.PropertyChanged += (_, _) =>
            {
                if (!_clearingEmployees)
                    RefreshEmployees(refreshFilter: ChkSelectedOnly.IsChecked == true);
            };
        DataObject.AddPastingHandler(InterventionDatePicker, (_, _) => ClearDateError());
        TxtNoEmployees.Visibility = _employees.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshEmployees();
    }

    internal static bool EmployeeMatches(WorkSheetEmployeeSelection employee, string search, bool selectedOnly)
        => (!selectedOnly || employee.IsSelected) && search.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(term => CultureInfo.GetCultureInfo("it-IT").CompareInfo.IndexOf(employee.DisplayName, term,
                CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0);

    private void RefreshEmployees(bool refreshFilter = true)
    {
        if (_employeeView == null) return;
        if (refreshFilter) _employeeView.Refresh();
        var selected = _employees.Where(employee => employee.IsSelected).ToArray();
        TxtSelectedCount.Text = $"Squadra selezionata · {selected.Length}";
        TxtSelectedNames.Text = selected.Length == 0 ? "Nessuno selezionato: spazio libero nel PDF."
            : string.Join(", ", selected.Select(employee => employee.DisplayName));
        TxtEmployeeResults.Text = $"{_employeeView.Cast<object>().Count()} di {_employees.Count} dipendenti";
        TxtNoResults.Visibility = _employees.Count > 0 && _employeeView.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnEmployeeSearchChanged(object sender, TextChangedEventArgs e) => RefreshEmployees();
    private void OnSelectedOnlyChanged(object sender, RoutedEventArgs e) => RefreshEmployees();
    private void OnClearEmployeeSearchClick(object sender, RoutedEventArgs e) => TxtEmployeeSearch.Clear();
    private void OnClearEmployeesClick(object sender, RoutedEventArgs e)
    {
        _clearingEmployees = true;
        try
        {
            foreach (var employee in _employees) employee.IsSelected = false;
        }
        finally
        {
            _clearingEmployees = false;
        }
        RefreshEmployees();
    }

    private void OnGenerateClick(object sender, RoutedEventArgs e)
    {
        // DatePicker may silently revert invalid typed text to the previous date on losing focus.
        // Keep the rejected input until the user corrects it or explicitly clears the field.
        if (_invalidDateText != null)
        {
            MessageBox.Show(this, $"La data '{_invalidDateText}' non è valida. Correggila oppure premi Svuota.",
                "Data intervento", MessageBoxButton.OK, MessageBoxImage.Warning);
            InterventionDatePicker.Focus();
            return;
        }
        DateTime? date = null;
        if (!string.IsNullOrWhiteSpace(InterventionDatePicker.Text))
        {
            if (!DateTime.TryParse(InterventionDatePicker.Text, CultureInfo.GetCultureInfo("it-IT"),
                    DateTimeStyles.None, out var parsed))
            {
                MessageBox.Show(this, "Inserisci una data valida oppure lascia il campo vuoto.", "Data intervento",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                InterventionDatePicker.Focus();
                return;
            }
            date = parsed.Date;
        }
        Options = new WorkSheetOptions
        {
            EmployeeNames = _employees.Where(employee => employee.IsSelected).Select(employee => employee.DisplayName).ToArray(),
            InterventionDate = date,
            AdditionalNotes = TxtAdditionalNotes.Text.Trim()
        };
        DialogResult = true;
    }

    private void OnDateValidationError(object sender, DatePickerDateValidationErrorEventArgs e)
    {
        e.ThrowException = false;
        _invalidDateText = e.Text;
        TxtDateError.Text = "Data non valida. Correggila o premi Svuota prima di generare il PDF.";
        TxtDateError.Visibility = Visibility.Visible;
    }

    private void ClearDateError()
    {
        _invalidDateText = null;
        if (TxtDateError != null) TxtDateError.Visibility = Visibility.Collapsed;
    }

    private void OnDateEdited(object sender, TextCompositionEventArgs e) => ClearDateError();
    private void OnDateKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Back or Key.Delete) ClearDateError();
    }
    private void OnDateSelected(object sender, SelectionChangedEventArgs e) => ClearDateError();
    private void OnDateCalendarClosed(object sender, RoutedEventArgs e)
    {
        if (InterventionDatePicker.SelectedDate != null) ClearDateError();
    }

    private void OnClearDateClick(object sender, RoutedEventArgs e)
    {
        ClearDateError();
        InterventionDatePicker.SelectedDate = null;
        InterventionDatePicker.Text = string.Empty;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }
}

public sealed class WorkSheetEmployeeSelection : INotifyPropertyChanged
{
    private bool _isSelected;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
