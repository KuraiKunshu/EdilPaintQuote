using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;

namespace EdilPaintPreventibiviGen.Views;

public partial class WorkScheduleEditorWindow : Window
{
    private readonly CancellationTokenSource _lifetime = AppShutdownManager.CreateLinkedTokenSource();
    private readonly WorkScheduleSettings _settings;
    private WorkScheduleEntry? _original;
    private readonly bool _absence;
    private readonly bool _canEdit;
    private readonly List<WorkScheduleEmployeeChoice> _employees = [];
    private readonly Dictionary<DateTime, Guid> _newIds = [];
    private readonly Dictionary<DatePicker, string> _invalidDates = [];
    private readonly HashSet<DatePicker> _pendingDateInput = [];
    private readonly List<WorkScheduleEntry> _scheduleEntries;
    private readonly DateTime _availableFrom;
    private readonly DateTime _availableTo;
    private readonly bool _availabilityCurrent;
    private readonly DependencyPropertyDescriptor _documentBusyDescriptor =
        DependencyPropertyDescriptor.FromProperty(QuoteActionsControl.IsBusyProperty, typeof(QuoteActionsControl));
    private ICollectionView? _employeeView;
    private bool _initializing = true;
    private bool _clearingCrew;
    private bool _presetChanged;
    private bool _busy;
    private bool _closed;
    private bool _applyingPreset;
    public bool HasChangesSaved { get; private set; }

    public WorkScheduleEditorWindow(WorkScheduleSnapshot snapshot, WorkScheduleOrder? order = null,
        WorkScheduleEntry? entry = null, bool absence = false, DateTime? date = null)
    {
        _settings = snapshot.Settings.CreateValidatedCopy();
        _original = entry?.CreateValidatedCopy();
        _absence = entry?.Kind == WorkScheduleEntryKind.Absence || entry == null && absence;
        _canEdit = snapshot.IsCurrent && App.WorkSchedule != null;
        _scheduleEntries = snapshot.Entries.Where(item => item.Id != entry?.Id).Select(item => item.CreateValidatedCopy()).ToList();
        _availableFrom = snapshot.From.Date;
        _availableTo = snapshot.To.Date;
        _availabilityCurrent = snapshot.IsCurrent;
        InitializeComponent();
        _documentBusyDescriptor.AddValueChanged(CommonQuoteActions, OnDocumentBusyChanged);
        CommonQuoteActions.CompletionRequested += OnCompletionRequested;
        CommonQuoteActions.WorkCompleted += OnWorkCompleted;
        CommonQuoteActions.CanComplete = _canEdit;
        Helpers.WindowResizeBehavior.PreventMaximizedState(this);
        Closing += OnClosing;
        Closed += OnClosed;
        PreviewKeyDown += OnPreviewKeyDown;

        TxtTitle.Text = _absence ? (entry == null ? "Segna assenza" : "Assenza squadra") :
            entry == null ? "Programma intervento" : entry.Status == WorkScheduleEntryStatus.Completed ? "Intervento finito" : "Intervento programmato";
        Title = TxtTitle.Text;
        OrderPanel.Visibility = _absence ? Visibility.Collapsed : Visibility.Visible;
        AbsencePanel.Visibility = _absence ? Visibility.Visible : Visibility.Collapsed;
        StatusPanel.Visibility = _absence ? Visibility.Collapsed : Visibility.Visible;
        EndDatePanel.Visibility = entry == null ? Visibility.Visible : Visibility.Collapsed;
        TxtRangeExplanation.Visibility = entry == null ? Visibility.Visible : Visibility.Collapsed;
        TxtStartDateLabel.Text = entry == null ? "Dal giorno" : "Giorno dell’intervento";
        StartDate.SelectedDate = entry?.Date ?? date ?? DateTime.Today;
        EndDate.SelectedDate = StartDate.SelectedDate;
        DataObject.AddPastingHandler(StartDate, (_, _) => MarkDateInputPending(StartDate));
        DataObject.AddPastingHandler(EndDate, (_, _) => MarkDateInputPending(EndDate));
        CmbSlot.SelectedIndex = (int)(entry?.SlotKind ?? WorkScheduleSlotKind.FullDay);
        CmbStatus.SelectedIndex = entry?.Status == WorkScheduleEntryStatus.Completed ? 1 : 0;
        TxtAbsenceReason.Text = entry?.AbsenceReason ?? "Indisponibilità";
        TxtNotes.Text = entry?.Notes ?? string.Empty;

        var orders = snapshot.Orders.ToList();
        if (entry != null && !_absence && orders.All(item => item.QuoteNumber != entry.QuoteNumber))
            orders.Add(new WorkScheduleOrder
            {
                QuoteNumber = entry.QuoteNumber, CustomerName = entry.CustomerName, SiteName = entry.SiteName,
                ReferenceName = entry.ReferenceName, MaterialStatus = entry.MaterialStatus, ExpectedDeliveryDate = entry.ExpectedDeliveryDate
            });
        CmbOrder.ItemsSource = orders.OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.QuoteNumber).ToList();
        CmbOrder.SelectedItem = orders.FirstOrDefault(item => item.QuoteNumber == (entry?.QuoteNumber ?? order?.QuoteNumber));
        CmbOrder.IsEnabled = entry == null;
        UpdateOrderDetails();
        if (entry?.HasOrderWarning == true)
        {
            OrderWarningNotice.Visibility = Visibility.Visible;
            TxtOrderWarning.Text = WorkScheduleWindow.OrderWarningText(entry);
        }

        var directory = snapshot.Employees.ToDictionary(employee => employee.Id);
        // Historical people stay visible when consulting an intervention after a directory removal.
        foreach (var employee in entry?.Employees ?? []) directory.TryAdd(employee.Id, employee);
        _employees.AddRange(directory.Values.Select(employee => new WorkScheduleEmployeeChoice
        {
            Employee = employee.CreateValidatedCopy(), IsSelected = entry?.EmployeeIds.Contains(employee.Id) == true,
            DisplayName = $"{employee.FirstName} {employee.LastName}".Trim()
        }).OrderBy(employee => employee.DisplayName, StringComparer.CurrentCultureIgnoreCase));
        _employeeView = CollectionViewSource.GetDefaultView(_employees);
        _employeeView.Filter = item => item is WorkScheduleEmployeeChoice employee && EmployeeMatches(employee);
        ItemsEmployees.ItemsSource = _employeeView;
        foreach (var employee in _employees)
            employee.PropertyChanged += (_, change) =>
            {
                if (!_clearingCrew && change.PropertyName == nameof(WorkScheduleEmployeeChoice.IsSelected))
                    RefreshEmployees(ChkSelectedOnly.IsChecked == true);
            };

        if (entry != null)
        {
            TxtStartTime.Text = WorkScheduleSettings.FormatTime(entry.StartMinutes);
            TxtEndTime.Text = WorkScheduleSettings.FormatTime(entry.EndMinutes);
        }
        else ApplyPreset();
        UpdateTimeFields();
        RefreshEmployees();
        ExistingActions.Visibility = !_absence || entry != null ? Visibility.Visible : Visibility.Collapsed;
        CommonQuoteActions.Visibility = _absence ? Visibility.Collapsed : Visibility.Visible;
        CommonQuoteActions.ScheduleEntry = _absence ? null : _original;
        BtnDelete.Visibility = entry == null ? Visibility.Collapsed : Visibility.Visible;
        BtnDelete.IsEnabled = _canEdit;
        BtnSave.IsEnabled = _canEdit;
        EditableFields.IsEnabled = _canEdit;
        ReadOnlyNotice.Visibility = _canEdit ? Visibility.Collapsed : Visibility.Visible;
        if (!_canEdit) BtnCancel.Content = "Chiudi";
        if (_absence) BtnSave.Content = entry == null ? "Salva assenza" : "Salva modifiche";
        else if (entry != null) BtnSave.Content = "Salva modifiche";
        _initializing = false;
        UpdateAvailability();
    }

    private WorkScheduleSlotKind SelectedSlot => (WorkScheduleSlotKind)Math.Max(0, CmbSlot.SelectedIndex);

    private void ApplyPreset()
    {
        if (SelectedSlot == WorkScheduleSlotKind.Custom) return;
        var range = _settings.GetRange(SelectedSlot);
        _applyingPreset = true;
        try
        {
            TxtStartTime.Text = WorkScheduleSettings.FormatTime(range.Start);
            TxtEndTime.Text = WorkScheduleSettings.FormatTime(range.End);
        }
        finally { _applyingPreset = false; }
    }

    private void UpdateTimeFields()
    {
        if (TxtStartTime == null || TxtEndTime == null || TxtTimeExplanation == null) return;
        bool custom = SelectedSlot == WorkScheduleSlotKind.Custom;
        TxtStartTime.IsReadOnly = !custom;
        TxtEndTime.IsReadOnly = !custom;
        TxtTimeExplanation.Text = custom
            ? "Inserisci le ore nel formato HH:mm. Gli orari liberi restano indipendenti dagli orari standard."
            : "Orari standard condivisi, modificabili in Impostazioni → Calendario lavori. Gli aggiornamenti si applicano anche agli interventi futuri programmati.";
    }

    private void OnSlotChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        _presetChanged = true;
        ApplyPreset();
        UpdateTimeFields();
        UpdateAvailability();
    }

    private void OnDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is DatePicker picker) _pendingDateInput.Remove(picker);
        if (_initializing) return;
        if (SelectedSlot != WorkScheduleSlotKind.Custom)
        {
            _presetChanged = true;
            ApplyPreset();
        }
        UpdateAvailability();
    }

    private void OnTimeChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initializing && !_applyingPreset) UpdateAvailability();
    }

    private void UpdateAvailability()
    {
        if (_initializing || _closed || TxtAvailabilityNotice == null) return;
        var neutral = (Brush)FindResource("MutedTextBrush");
        bool invalidDate = _invalidDates.ContainsKey(StartDate) || _pendingDateInput.Contains(StartDate) ||
            _original == null && (_invalidDates.ContainsKey(EndDate) || _pendingDateInput.Contains(EndDate));
        bool datesValid = DateTime.TryParse(StartDate.Text, CultureInfo.GetCultureInfo("it-IT"), DateTimeStyles.None, out var start);
        var end = start;
        if (_original == null)
            datesValid &= DateTime.TryParse(EndDate.Text, CultureInfo.GetCultureInfo("it-IT"), DateTimeStyles.None, out end);
        if (invalidDate || !datesValid || end.Date < start.Date ||
            !WorkScheduleSettings.TryParseTime(TxtStartTime.Text, out int from) ||
            !WorkScheduleSettings.TryParseTime(TxtEndTime.Text, out int to) || to <= from)
        {
            TxtAvailabilityNotice.Text = "Completa date e orari validi per vedere gli impegni dei dipendenti.";
            foreach (var employee in _employees)
                employee.SetAvailability("Disponibilità da verificare", neutral, "Completa date e orari dell’intervento.");
            return;
        }
        start = start.Date;
        end = end.Date;
        bool fullCoverage = _availabilityCurrent && start >= _availableFrom && end < _availableTo;
        TxtAvailabilityNotice.Text = fullCoverage
            ? "Disponibilità nella settimana caricata. Il database ricontrolla gli impegni al salvataggio, comprese le modifiche degli altri PC."
            : "Verifica parziale: il periodo supera la settimana caricata oppure stai usando una copia locale. Gli impegni mancanti saranno controllati nel database al salvataggio.";
        var conflicts = _scheduleEntries.Where(item => item.ReservesEmployees(DateTime.Today) && item.Date.Date >= start && item.Date.Date <= end &&
                item.StartMinutes < to && item.EndMinutes > from)
            .OrderBy(item => item.Date).ThenBy(item => item.StartMinutes)
            .SelectMany(item => item.EmployeeIds.Select(id => (Id: id, Entry: item)))
            .GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.Select(item => item.Entry).ToList());
        foreach (var employee in _employees)
        {
            if (conflicts.TryGetValue(employee.Employee.Id, out var commitments))
            {
                var first = commitments[0];
                var prefix = first.Kind == WorkScheduleEntryKind.Absence ? "Assente" : "Occupato";
                var extra = commitments.Count == 1 ? string.Empty : commitments.Count == 2 ? " · +1 altro impegno" :
                    $" · +{commitments.Count - 1} altri impegni";
                string details = string.Join("\n", commitments.Take(10).Select(item =>
                    $"{item.Date:dd/MM/yyyy} · {item.TimeDisplay} · {item.Title}"));
                if (commitments.Count > 10) details += $"\nAltri {commitments.Count - 10} impegni.";
                employee.SetAvailability($"{prefix} · {first.Date:dd/MM} {first.TimeDisplay}{extra}",
                    (Brush)FindResource("DangerSoftTextBrush"), details);
            }
            else
                employee.SetAvailability(fullCoverage ? "Disponibile" : "Nessun impegno noto · da verificare",
                    fullCoverage ? (Brush)FindResource("SuccessGreenBrush") : neutral,
                    fullCoverage ? "Nessun impegno sovrapposto nel calendario scaricato. Verifica definitiva al salvataggio." :
                        "Il calendario disponibile non copre tutto il periodo o non è aggiornato. Verifica definitiva al salvataggio.");
        }
    }

    private void OnOrderChanged(object sender, SelectionChangedEventArgs e) => UpdateOrderDetails();
    private void UpdateOrderDetails()
    {
        if (TxtOrderDetails == null) return;
        if (CommonQuoteActions != null)
            CommonQuoteActions.QuoteNumber = _absence ? string.Empty :
                _original?.QuoteNumber ?? (CmbOrder.SelectedItem as WorkScheduleOrder)?.QuoteNumber ?? string.Empty;
        if (CmbOrder.SelectedItem is not WorkScheduleOrder order)
        {
            TxtOrderDetails.Text = "Seleziona l’ordine da collegare all’intervento.";
            return;
        }
        var detail = $"{order.CustomerName} · Ordine {order.QuoteNumber}";
        if (!string.IsNullOrWhiteSpace(order.ReferenceName)) detail += $" · Riferimento: {order.ReferenceName}";
        if (!string.IsNullOrWhiteSpace(order.MaterialStatus)) detail += $"\nMateriali: {order.MaterialStatus}";
        if (order.ExpectedDeliveryDate is { } delivery) detail += $" · Consegna prevista: {delivery:dd/MM/yyyy}";
        TxtOrderDetails.Text = detail;
    }

    private bool EmployeeMatches(WorkScheduleEmployeeChoice employee)
    {
        if (ChkSelectedOnly.IsChecked == true && !employee.IsSelected) return false;
        var text = $"{employee.DisplayName} {employee.Employee.Abbreviation}";
        return TxtEmployeeSearch.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(term =>
            CultureInfo.GetCultureInfo("it-IT").CompareInfo.IndexOf(text, term, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0);
    }

    private void RefreshEmployees(bool refreshFilter = true)
    {
        if (_employeeView == null) return;
        if (refreshFilter) _employeeView.Refresh();
        var selected = _employees.Where(employee => employee.IsSelected).ToList();
        TxtSelectedCount.Text = $"Squadra · {selected.Count} {(selected.Count == 1 ? "persona" : "persone")}";
        TxtSelectedNames.Text = selected.Count == 0 ? (_absence ? "Seleziona almeno una persona per registrare l’assenza." : "Squadra da assegnare: puoi programmarla anche in seguito.") :
            string.Join(" · ", selected.Select(employee => employee.DisplayName));
        TxtEmployeeResults.Text = $"{_employeeView.Cast<object>().Count()} di {_employees.Count}";
        TxtNoEmployees.Visibility = _employeeView.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        TxtNoEmployees.Text = _employees.Count == 0 ? "Nessun dipendente disponibile. Aggiungili in Impostazioni → Dipendenti." : "Nessun risultato. Modifica la ricerca o il filtro.";
    }

    private void OnEmployeeSearchChanged(object sender, TextChangedEventArgs e) => RefreshEmployees();
    private void OnSelectedOnlyChanged(object sender, RoutedEventArgs e) => RefreshEmployees();
    private void OnClearSearchClick(object sender, RoutedEventArgs e) => TxtEmployeeSearch.Clear();
    private void OnClearCrewClick(object sender, RoutedEventArgs e)
    {
        _clearingCrew = true;
        try { foreach (var employee in _employees) employee.IsSelected = false; }
        finally { _clearingCrew = false; }
        RefreshEmployees();
    }

    private DateTime ReadDate(DatePicker picker, string label)
    {
        if (_invalidDates.TryGetValue(picker, out var invalid))
            throw new InvalidOperationException($"{label}: la data '{invalid}' non è valida. Correggila prima di salvare.");
        if (!DateTime.TryParse(picker.Text, CultureInfo.GetCultureInfo("it-IT"), DateTimeStyles.None, out var date))
            throw new InvalidOperationException($"{label}: inserisci una data valida.");
        return date.Date;
    }

    private List<WorkScheduleEntry> BuildEntries()
    {
        var start = ReadDate(StartDate, "Giorno iniziale");
        var end = _original == null ? ReadDate(EndDate, "Giorno finale") : start;
        if (end < start) throw new InvalidOperationException("Il giorno finale deve essere uguale o successivo a quello iniziale.");
        if ((end - start).TotalDays >= 366) throw new InvalidOperationException("Programma un periodo di massimo un anno alla volta.");
        if (!WorkScheduleSettings.TryParseTime(TxtStartTime.Text, out int from) || !WorkScheduleSettings.TryParseTime(TxtEndTime.Text, out int to))
            throw new InvalidOperationException("Inserisci orari validi nel formato HH:mm, per esempio 08:30 e 17:00.");
        WorkScheduleSettings.ValidateRange(from, to);
        var order = CmbOrder.SelectedItem as WorkScheduleOrder;
        if (!_absence && order == null) throw new InvalidOperationException("Seleziona un ordine attivo da programmare.");
        var employees = _employees.Where(employee => employee.IsSelected).Select(employee => employee.Employee).ToList();
        var result = new List<WorkScheduleEntry>();
        foreach (var date in Enumerable.Range(0, (int)(end - start).TotalDays + 1).Select(offset => start.AddDays(offset)))
        {
            if (!_newIds.TryGetValue(date, out var id)) _newIds[date] = id = Guid.NewGuid();
            var entry = new WorkScheduleEntry
            {
                Id = _original?.Id ?? id, Revision = _original?.Revision ?? 0, Date = date,
                Kind = _absence ? WorkScheduleEntryKind.Absence : WorkScheduleEntryKind.Job,
                SlotKind = SelectedSlot,
                SettingsRevision = _original != null && !_presetChanged ? _original.SettingsRevision : _settings.Revision,
                StartMinutes = from, EndMinutes = to, QuoteNumber = _absence ? string.Empty : order!.QuoteNumber,
                CustomerName = order?.CustomerName ?? string.Empty, SiteName = order?.SiteName ?? string.Empty,
                ReferenceName = order?.ReferenceName ?? string.Empty, MaterialStatus = order?.MaterialStatus ?? string.Empty,
                ExpectedDeliveryDate = order?.ExpectedDeliveryDate,
                OrderStatus = _original?.OrderStatus, IsOrderDeleted = _original?.IsOrderDeleted ?? false,
                IsOrderActive = _original == null ? true : _original.IsOrderActive,
                AbsenceReason = TxtAbsenceReason.Text.Trim(), Notes = TxtNotes.Text.Trim(),
                Status = !_absence && CmbStatus.SelectedIndex == 1 ? WorkScheduleEntryStatus.Completed : WorkScheduleEntryStatus.Planned,
                EmployeeIds = employees.Select(employee => employee.Id).ToList(), Employees = employees.Select(employee => employee.CreateValidatedCopy()).ToList()
            };
            result.Add(entry.CreateValidatedCopy());
        }
        return result;
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_busy || CommonQuoteActions.IsBusy || !_canEdit || App.WorkSchedule == null) return;
        try
        {
            var entries = BuildEntries();
            SetBusy(true, entries.Count == 1 ? "Salvataggio…" : $"Salvataggio di {entries.Count} interventi…");
            await App.WorkSchedule.SaveEntriesAsync(entries, _lifetime.Token);
            HasChangesSaved = true;
            SetBusy(false);
            DialogResult = true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { if (!_closed) SetBusy(false); }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_busy || CommonQuoteActions.IsBusy || !_canEdit || _original == null || App.WorkSchedule == null) return;
        if (MessageBox.Show(this, "Eliminare questo intervento dal calendario? L’ordine rimane disponibile.", "Elimina intervento",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            SetBusy(true, "Eliminazione…");
            await App.WorkSchedule.DeleteEntryAsync(_original.Id, _original.Revision, _lifetime.Token);
            HasChangesSaved = true;
            SetBusy(false);
            DialogResult = true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { if (!_closed) SetBusy(false); }
    }

    private void SetBusy(bool busy, string message = "")
    {
        _busy = busy;
        bool interactionBusy = busy || CommonQuoteActions.IsBusy;
        EditableFields.IsEnabled = !interactionBusy && _canEdit;
        BtnSave.IsEnabled = !interactionBusy && _canEdit;
        BtnDelete.IsEnabled = !interactionBusy && _canEdit;
        BtnCancel.IsEnabled = !interactionBusy;
        CommonQuoteActions.IsEnabled = !busy;
        TxtBusy.Text = busy ? message : CommonQuoteActions.IsBusy ? "Preparazione documenti…" : string.Empty;
        if (busy) ErrorPanel.Visibility = Visibility.Collapsed;
    }

    private void OnDocumentBusyChanged(object? sender, EventArgs e)
    {
        if (!_closed) SetBusy(_busy, TxtBusy.Text);
    }

    private void OnCompletionRequested(object? sender, CancelEventArgs e)
    {
        if (_original == null || !_canEdit) { e.Cancel = true; return; }
        try
        {
            var draft = BuildEntries().Single();
            if (draft.Date != _original.Date || draft.SlotKind != _original.SlotKind ||
                draft.StartMinutes != _original.StartMinutes || draft.EndMinutes != _original.EndMinutes ||
                draft.Notes != _original.Notes || !draft.EmployeeIds.ToHashSet().SetEquals(_original.EmployeeIds) ||
                _original.Status == WorkScheduleEntryStatus.Completed && draft.Status != _original.Status)
                throw new InvalidOperationException("Salva le modifiche prima di segnare l’intervento come finito o generare il PDF dei costi.");
        }
        catch (Exception ex) { e.Cancel = true; ShowError(ex.Message); }
    }

    private void OnWorkCompleted(object? sender, WorkScheduleCompletedEventArgs e)
    {
        _original = e.Entry.CreateValidatedCopy();
        HasChangesSaved = true;
        CmbStatus.SelectedIndex = 1;
        TxtTitle.Text = Title = "Intervento finito";
        ErrorPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        if (_closed) return;
        TxtError.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
        ErrorPanel.BringIntoView();
    }

    private void OnDateValidationError(object sender, DatePickerDateValidationErrorEventArgs e)
    {
        e.ThrowException = false;
        if (sender is DatePicker picker) _invalidDates[picker] = e.Text;
        UpdateAvailability();
    }
    private void OnDateEdited(object sender, TextCompositionEventArgs e)
    {
        if (sender is DatePicker picker) MarkDateInputPending(picker);
    }
    private void OnDateKeyDown(object sender, KeyEventArgs e)
    {
        if ((e.Key is Key.Back or Key.Delete) && sender is DatePicker picker) MarkDateInputPending(picker);
    }
    private void OnCalendarClosed(object sender, RoutedEventArgs e)
    {
        if (sender is DatePicker { SelectedDate: not null } picker)
        {
            _invalidDates.Remove(picker);
            _pendingDateInput.Remove(picker);
        }
        UpdateAvailability();
    }

    private void MarkDateInputPending(DatePicker picker)
    {
        _invalidDates.Remove(picker);
        _pendingDateInput.Add(picker);
        UpdateAvailability();
    }

    private void OnDateFocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is DatePicker picker && !_invalidDates.ContainsKey(picker)) _pendingDateInput.Remove(picker);
        UpdateAvailability();
    }
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_busy && !CommonQuoteActions.IsBusy) Close();
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_busy || CommonQuoteActions.IsBusy) e.Cancel = true;
    }
    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        _documentBusyDescriptor.RemoveValueChanged(CommonQuoteActions, OnDocumentBusyChanged);
        CommonQuoteActions.CompletionRequested -= OnCompletionRequested;
        CommonQuoteActions.WorkCompleted -= OnWorkCompleted;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (!_busy && !CommonQuoteActions.IsBusy) Close();
    }
    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }
}

public sealed class WorkScheduleEmployeeChoice : INotifyPropertyChanged
{
    private bool _isSelected;
    private string _availabilityText = "Disponibilità da verificare";
    private string _availabilityDetails = string.Empty;
    private Brush _availabilityBrush = Brushes.Gray;
    public EmployeeSettingsModel Employee { get; init; } = new();
    public string DisplayName { get; init; } = string.Empty;
    public string AvailabilityText => _availabilityText;
    public string AvailabilityDetails => _availabilityDetails;
    public Brush AvailabilityBrush => _availabilityBrush;

    internal void SetAvailability(string text, Brush brush, string details)
    {
        if (_availabilityText != text)
        {
            _availabilityText = text;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AvailabilityText)));
        }
        if (_availabilityDetails != details)
        {
            _availabilityDetails = details;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AvailabilityDetails)));
        }
        if (!ReferenceEquals(_availabilityBrush, brush))
        {
            _availabilityBrush = brush;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AvailabilityBrush)));
        }
    }
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
