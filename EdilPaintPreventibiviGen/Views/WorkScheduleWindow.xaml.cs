using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;

namespace EdilPaintPreventibiviGen.Views;

public partial class WorkScheduleWindow : Window
{
    private readonly CancellationTokenSource _lifetime = AppShutdownManager.CreateLinkedTokenSource();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly HashSet<QuoteActionsControl> _documentActions = [];
    private readonly DependencyPropertyDescriptor _documentBusyDescriptor =
        DependencyPropertyDescriptor.FromProperty(QuoteActionsControl.IsBusyProperty, typeof(QuoteActionsControl));
    private readonly DependencyPropertyDescriptor _documentMenuDescriptor =
        DependencyPropertyDescriptor.FromProperty(QuoteActionsControl.IsMenuOpenProperty, typeof(QuoteActionsControl));
    private WorkScheduleSnapshot _snapshot = new();
    private DateTime _weekStart = StartOfWeek(DateTime.Today);
    private bool _refreshing;
    private bool _closed;
    private bool _editorOpen;
    private string? _cacheError;
    private bool DocumentsBusy => _documentActions.Any(control => control.IsBusy || control.IsMenuOpen);

    public WorkScheduleWindow()
    {
        InitializeComponent();
        Helpers.WindowResizeBehavior.PreventMaximizedState(this);
        _refreshTimer.Tick += OnTimerTick;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
        UpdateWeekLabel();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplySnapshot(ReadCachedSnapshot(_weekStart));
        await RefreshAsync();
        if (!_closed) _refreshTimer.Start();
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        if (IsVisible && !_closed) await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_refreshing || _closed || DocumentsBusy) return;
        if (App.WorkSchedule == null)
        {
            ApplySnapshot(new WorkScheduleSnapshot { From = _weekStart, To = _weekStart.AddDays(7) });
            return;
        }
        _refreshing = true;
        BtnRefresh.IsEnabled = false;
        var requestedWeek = _weekStart;
        try
        {
            var snapshot = await App.WorkSchedule.GetLatestAsync(requestedWeek, requestedWeek.AddDays(7), _lifetime.Token);
            if (!_closed && requestedWeek == _weekStart) ApplySnapshot(snapshot);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_closed && requestedWeek == _weekStart)
            {
                ApplySnapshot(ReadCachedSnapshot(requestedWeek));
                TxtConnectionNotice.Text = _cacheError ?? $"Aggiornamento non riuscito. {ex.Message}";
                ConnectionNotice.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            _refreshing = false;
            if (!_closed)
            {
                BtnRefresh.IsEnabled = !DocumentsBusy;
                // A week change while a request was in flight needs its own fresh read.
                if (requestedWeek != _weekStart) await RefreshAsync();
            }
        }
    }

    private WorkScheduleSnapshot ReadCachedSnapshot(DateTime weekStart)
    {
        _cacheError = null;
        try
        {
            return App.WorkSchedule?.CachedSnapshot(weekStart, weekStart.AddDays(7)) ??
                new WorkScheduleSnapshot { From = weekStart, To = weekStart.AddDays(7) };
        }
        catch (Exception ex)
        {
            // A database change invalidates the service as well as its old local cache.
            _cacheError = ex.Message;
            return new WorkScheduleSnapshot { From = weekStart, To = weekStart.AddDays(7) };
        }
    }

    private void ApplySnapshot(WorkScheduleSnapshot snapshot)
    {
        // Keep a card attached while its document is being prepared or its actions menu is open.
        if (DocumentsBusy) return;
        _snapshot = snapshot;
        var conflicts = WorkScheduleConflictDetector.Find(snapshot.Entries);
        var conflictsByEntry = conflicts.SelectMany(conflict => new[]
            {
                new KeyValuePair<Guid, string>(conflict.FirstEntryId, conflict.Message),
                new KeyValuePair<Guid, string>(conflict.SecondEntryId, conflict.Message)
            }).GroupBy(pair => pair.Key).ToDictionary(group => group.Key,
                group => string.Join("\n", group.Select(pair => pair.Value).Distinct(StringComparer.Ordinal)));
        ConflictNotice.Visibility = conflictsByEntry.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        TxtConflictNotice.Text = $"{conflictsByEntry.Count} impegni con assegnazioni sovrapposte. " +
            "Apri i riquadri evidenziati e verifica squadra e orari.";
        if (ChkShowInactive.IsChecked != true && snapshot.Entries.Any(entry =>
                conflictsByEntry.ContainsKey(entry.Id) && entry.Kind == WorkScheduleEntryKind.Job && !entry.IsActiveOrder))
            TxtConflictNotice.Text += " Per consultare gli ordini non attivi, abilita il filtro sopra.";
        ItemsDays.ItemsSource = Enumerable.Range(0, 7).Select(offset =>
        {
            var date = _weekStart.AddDays(offset);
            var entries = snapshot.Entries.Where(entry => entry.Date.Date == date &&
                    (entry.Kind == WorkScheduleEntryKind.Absence || entry.IsActiveOrder || ChkShowInactive.IsChecked == true))
                .OrderBy(entry => entry.StartMinutes).ThenBy(entry => entry.Title, StringComparer.CurrentCultureIgnoreCase)
                .Select(entry => new WorkScheduleCard(entry, snapshot.Settings.UseEmployeeAbbreviations, this,
                    conflictsByEntry.GetValueOrDefault(entry.Id, string.Empty))).ToList();
            return new WorkScheduleDay
            {
                Date = date,
                WeekDay = CultureInfo.GetCultureInfo("it-IT").TextInfo.ToTitleCase(date.ToString("dddd", CultureInfo.GetCultureInfo("it-IT"))),
                DayLabel = date.ToString("dd MMM", CultureInfo.GetCultureInfo("it-IT")),
                Entries = entries,
                CountLabel = entries.Count == 0 ? "Nessun intervento" : $"{entries.Count} {(entries.Count == 1 ? "intervento" : "interventi")}",
                Background = (Brush)FindResource(date == DateTime.Today ? "SelectedTabBrush" : "SectionPanelBackgroundBrush"),
                CanEdit = snapshot.IsCurrent,
                EmptyVisibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed
            };
        }).ToList();
        RefreshOrders();
        BtnAbsence.IsEnabled = snapshot.IsCurrent;
        ConnectionNotice.Visibility = snapshot.IsCurrent ? Visibility.Collapsed : Visibility.Visible;
        TxtConnectionNotice.Text = snapshot.HasCachedData
            ? "Connessione non disponibile: consulta l’ultima copia scaricata. Per cambiare squadre e orari serve il database."
            : "Questa settimana non è disponibile nella copia locale. Connettiti al database per caricare il calendario.";
        if (!snapshot.IsCurrent && _cacheError != null) TxtConnectionNotice.Text = _cacheError;
        TxtSyncStatus.Text = snapshot.UpdatedAtUtc is { } timestamp
            ? $"{(snapshot.IsCurrent ? "Database aggiornato" : "Copia locale")} · {timestamp.ToLocalTime():dd/MM HH:mm:ss}"
            : "In attesa del database";
    }

    private void RefreshOrders()
    {
        if (ItemsOrders == null || TxtOrderSearch == null || ChkUnplannedOnly == null || DocumentsBusy) return;
        var selectedNumber = (ItemsOrders.SelectedItem as WorkScheduleOrder)?.QuoteNumber;
        var terms = TxtOrderSearch.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var comparer = CultureInfo.GetCultureInfo("it-IT").CompareInfo;
        var orders = _snapshot.Orders.Where(order =>
                (ChkUnplannedOnly.IsChecked != true || order.PlannedInterventions == 0) &&
                terms.All(term => comparer.IndexOf($"{order.CustomerName} {order.SiteName} {order.ReferenceName} {order.QuoteNumber}", term,
                    CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0))
            .OrderBy(order => order.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(order => order.QuoteNumber).ToList();
        ItemsOrders.ItemsSource = orders;
        ItemsOrders.SelectedItem = orders.FirstOrDefault(order => order.QuoteNumber == selectedNumber);
        TxtOrderCount.Text = $"{orders.Count} di {_snapshot.Orders.Count} ordini";
        TxtNoOrders.Visibility = orders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TxtNoOrders.Text = _snapshot.Orders.Count == 0 ? "Nessun ordine attivo disponibile." : "Nessun risultato per questi filtri.";
        UpdateNewButton();
    }

    private void UpdateNewButton() => BtnNewIntervention.IsEnabled = !DocumentsBusy && _snapshot.IsCurrent && ItemsOrders.SelectedItem is WorkScheduleOrder;
    private void OnOrderFilterChanged(object sender, RoutedEventArgs e) => RefreshOrders();
    private void OnCalendarFilterChanged(object sender, RoutedEventArgs e)
    {
        if (ItemsDays != null) ApplySnapshot(_snapshot);
    }
    private void OnOrderSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateNewButton();
    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void OnPreviousWeekClick(object sender, RoutedEventArgs e) => await NavigateAsync(_weekStart.AddDays(-7));
    private async void OnNextWeekClick(object sender, RoutedEventArgs e) => await NavigateAsync(_weekStart.AddDays(7));
    private async void OnTodayClick(object sender, RoutedEventArgs e) => await NavigateAsync(StartOfWeek(DateTime.Today));

    private async Task NavigateAsync(DateTime weekStart)
    {
        if (DocumentsBusy) return;
        _weekStart = weekStart;
        UpdateWeekLabel();
        ApplySnapshot(ReadCachedSnapshot(_weekStart));
        await RefreshAsync();
    }

    private void UpdateWeekLabel() => TxtWeek.Text = $"{_weekStart:dd/MM} – {_weekStart.AddDays(6):dd/MM/yyyy}";
    private static DateTime StartOfWeek(DateTime date) => date.Date.AddDays(-((7 + (int)date.DayOfWeek - (int)DayOfWeek.Monday) % 7));

    internal static string OrderWarningText(WorkScheduleEntry entry) => entry.IsOrderDeleted
        ? "Ordine eliminato: verifica intervento."
        : entry.OrderStatus switch
        {
            QuoteStatus.Finito => "Ordine finito: verifica intervento.",
            QuoteStatus.Rifiutato => "Ordine rifiutato: verifica intervento.",
            _ => "Ordine non più nell’elenco Ordini: verifica intervento."
        };

    private async void OnNewInterventionClick(object sender, RoutedEventArgs e) => await OpenEditorAsync();
    private async void OnOrderDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IsDocumentActionSource(e.OriginalSource as DependencyObject)) return;
        if (_snapshot.IsCurrent && ItemsOrders.SelectedItem is WorkScheduleOrder) await OpenEditorAsync();
    }

    private static bool IsDocumentActionSource(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is QuoteActionsControl) return true;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return false;
    }
    private async void OnAbsenceClick(object sender, RoutedEventArgs e) => await OpenEditorAsync(absence: true);
    private async void OnAddForDayClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DateTime date }) await OpenEditorAsync(date: date);
    }
    private async void OnEntryClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WorkScheduleEntry entry }) await OpenEditorAsync(entry: entry);
    }

    private async Task OpenEditorAsync(WorkScheduleEntry? entry = null, bool absence = false, DateTime? date = null)
    {
        if (_editorOpen || DocumentsBusy || _closed || AppShutdownManager.IsShutdownRequested || (entry == null && !_snapshot.IsCurrent)) return;
        var order = ItemsOrders.SelectedItem as WorkScheduleOrder;
        if (entry == null && !absence && order == null && _snapshot.Orders.Count == 0)
        {
            MessageBox.Show(this, "Non ci sono ordini attivi da programmare. Il calendario usa i lavori presenti nell’elenco Ordini.",
                "Programmazione lavori", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _editorOpen = true;
        try
        {
            var initialDate = date ?? (DateTime.Today >= _weekStart && DateTime.Today < _weekStart.AddDays(7) ? DateTime.Today : _weekStart);
            var editor = new WorkScheduleEditorWindow(_snapshot, order, entry, absence, initialDate) { Owner = this };
            editor.ShowDialog();
            if (editor.HasChangesSaved) await RefreshAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_closed)
                MessageBox.Show(this, ex.Message, "Programmazione lavori", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _editorOpen = false; }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_editorOpen || DocumentsBusy) e.Cancel = true;
    }

    private void OnQuoteActionsLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is QuoteActionsControl control && _documentActions.Add(control))
        {
            _documentBusyDescriptor.AddValueChanged(control, OnDocumentBusyChanged);
            _documentMenuDescriptor.AddValueChanged(control, OnDocumentBusyChanged);
        }
    }

    private void OnQuoteActionsUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is QuoteActionsControl control && _documentActions.Remove(control))
        {
            _documentBusyDescriptor.RemoveValueChanged(control, OnDocumentBusyChanged);
            _documentMenuDescriptor.RemoveValueChanged(control, OnDocumentBusyChanged);
        }
    }

    private void OnDocumentBusyChanged(object? sender, EventArgs e)
    {
        if (_closed) return;
        bool busy = DocumentsBusy;
        bool preparingDocument = _documentActions.Any(control => control.IsBusy);
        // Keeping the target enabled lets the open context menu continue receiving its click.
        ItemsDays.IsEnabled = !preparingDocument;
        ItemsOrders.IsEnabled = !preparingDocument;
        BtnPreviousWeek.IsEnabled = !busy;
        BtnToday.IsEnabled = !busy;
        BtnNextWeek.IsEnabled = !busy;
        BtnAbsence.IsEnabled = !busy && _snapshot.IsCurrent;
        BtnRefresh.IsEnabled = !busy && !_refreshing;
        TxtOrderSearch.IsEnabled = !busy;
        ChkUnplannedOnly.IsEnabled = !busy;
        ChkShowInactive.IsEnabled = !busy;
        UpdateNewButton();
        if (!busy)
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () =>
            {
                // Menu closure can precede MenuItem.Click: let that action mark itself busy first.
                if (!_closed && !DocumentsBusy) await RefreshAsync();
            }));
    }
    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        _refreshTimer.Stop();
        _refreshTimer.Tick -= OnTimerTick;
        foreach (var control in _documentActions)
        {
            _documentBusyDescriptor.RemoveValueChanged(control, OnDocumentBusyChanged);
            _documentMenuDescriptor.RemoveValueChanged(control, OnDocumentBusyChanged);
        }
        _documentActions.Clear();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private sealed class WorkScheduleDay
    {
        public DateTime Date { get; init; }
        public string WeekDay { get; init; } = string.Empty;
        public string DayLabel { get; init; } = string.Empty;
        public string CountLabel { get; init; } = string.Empty;
        public List<WorkScheduleCard> Entries { get; init; } = [];
        public Brush Background { get; init; } = Brushes.White;
        public bool CanEdit { get; init; }
        public Visibility EmptyVisibility { get; init; }
    }

    private sealed class WorkScheduleCard
    {
        public WorkScheduleEntry Entry { get; }
        public string Subtitle { get; }
        public string Crew { get; }
        public string Status { get; }
        public Brush Background { get; }
        public Brush BorderBrush { get; }
        public string ConflictText { get; }
        public Visibility ConflictVisibility => string.IsNullOrWhiteSpace(ConflictText) ? Visibility.Collapsed : Visibility.Visible;
        public string MaterialWarning { get; }
        public Visibility MaterialWarningVisibility => Entry.HasMaterialWarning ? Visibility.Visible : Visibility.Collapsed;
        public string OrderWarning => OrderWarningText(Entry);
        public Visibility OrderWarningVisibility => Entry.HasOrderWarning ? Visibility.Visible : Visibility.Collapsed;
        public Visibility DocumentActionsVisibility => Entry.Kind == WorkScheduleEntryKind.Job ? Visibility.Visible : Visibility.Collapsed;
        public WorkScheduleCard(WorkScheduleEntry entry, bool abbreviations, FrameworkElement resources, string conflictText = "")
        {
            Entry = entry;
            ConflictText = conflictText;
            BorderBrush = (Brush)resources.FindResource(string.IsNullOrWhiteSpace(conflictText) ? "SubtleBorderBrush" : "DangerSoftTextBrush");
            Subtitle = entry.Kind == WorkScheduleEntryKind.Absence ? "Indisponibilità squadra" :
                $"{entry.CustomerName} · {entry.QuoteNumber}";
            Crew = entry.Employees.Count == 0 ? "Squadra da assegnare" : string.Join(" · ", entry.Employees.Select(employee =>
                abbreviations && !string.IsNullOrWhiteSpace(employee.Abbreviation) ? employee.Abbreviation :
                    $"{employee.FirstName} {employee.LastName}".Trim()));
            Status = entry.Kind == WorkScheduleEntryKind.Absence ? "Assenza" : entry.Status == WorkScheduleEntryStatus.Completed ? "Intervento completato" : "Programmato";
            Background = (Brush)resources.FindResource(entry.Kind == WorkScheduleEntryKind.Absence ? "DangerSoftBackgroundBrush" :
                entry.Status == WorkScheduleEntryStatus.Completed ? "StatusConfermatoBrush" : "WhiteBrush");
            MaterialWarning = entry.ExpectedDeliveryDate?.Date > entry.Date.Date
                ? $"Materiali previsti il {entry.ExpectedDeliveryDate:dd/MM}" : $"Materiali: {entry.MaterialStatus}";
        }
    }
}
