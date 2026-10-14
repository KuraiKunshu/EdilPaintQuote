using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;

namespace EdilPaintPreventibiviGen.Views;

public partial class PosDraftWindow : Window
{
    private readonly PosService _service;
    private readonly PosDraft _draft;
    private List<PosWorkItem> _templates;
    private List<PosWorkChoice> _works = [];
    private List<PosWorkChoice> _equipment = [];
    private ICollectionView? _equipmentView;
    private readonly List<PosEmployeeChoice> _employees;
    private ICollectionView? _workView, _employeeView;
    private string _baseline = "";
    private bool _loading = true, _busy, _allowClose;
    private readonly HashSet<DatePicker> _invalidDates = [];
    private readonly CancellationTokenSource _lifetime = AppShutdownManager.CreateLinkedTokenSource();

    public PosDraftWindow(PosService service, PosDraft draft, List<PosWorkItem> templates, EmployeeDirectorySnapshot employees)
    {
        _service = service; _draft = PosDraftRules.Copy(draft); _templates = templates;
        _employees = PosDraftRules.EmployeeChoices(employees.Employees, _draft);
        InitializeComponent();
        Helpers.WindowResizeBehavior.PreventMaximizedState(this);
        DataContext = _draft;
        QuoteText.Text = $"Lavoro collegato: {_draft.QuoteNumber}";
        EmployeeNotice.Text = employees.IsCurrent ? "Elenco dal database. La scelta è specifica per questo POS." :
            "Elenco dipendenti dalla copia locale: verifica la squadra prima di salvare.";
        SetWorks(_draft);
        _employeeView = CollectionViewSource.GetDefaultView(_employees);
        _employeeView.Filter = x => x is PosEmployeeChoice choice && Matches(choice.Employee.Name, EmployeeSearch.Text);
        EmployeeList.ItemsSource = _employeeView;
        foreach (var employee in _employees) employee.PropertyChanged += (_, _) => QueueSummary();
        DataObject.AddPastingHandler(StartPicker, (_, _) => _invalidDates.Remove(StartPicker));
        DataObject.AddPastingHandler(EndPicker, (_, _) => _invalidDates.Remove(EndPicker));
        _loading = false; _baseline = JsonSerializer.Serialize(Collect()); UpdateSummary();
        ShowSavedStatus();
        Closed += (_, _) => { _lifetime.Cancel(); _lifetime.Dispose(); };
    }
    private void SetWorks(PosDraft snapshot)
    {
        _equipment = PosDraftRules.EquipmentChoices(_templates, snapshot);
        _equipmentView = CollectionViewSource.GetDefaultView(_equipment);
        _equipmentView.Filter = x => x is PosWorkChoice choice && Matches(choice.Sheet.Name, EquipmentSearch.Text);
        EquipmentList.ItemsSource = _equipmentView;
        _works = PosDraftRules.WorkChoices(_templates, snapshot);
        _workView = CollectionViewSource.GetDefaultView(_works);
        _workView.Filter = x => x is PosWorkChoice choice && Matches(choice.Sheet.Name, WorkSearch.Text);
        WorkList.ItemsSource = _workView;
        foreach (var work in _works)
        {
            work.PropertyChanged += (_, _) =>
            {
                if (work.IsSelected) foreach (var equipment in _equipment.Where(x => work.Sheet.EquipmentIds.Contains(x.Sheet.Id))) equipment.IsSelected = true;
                UpdateSheetSelector(); QueueSummary();
            };
            work.Sheet.PropertyChanged += (_, _) => QueueSummary();
        }
        foreach (var equipment in _equipment)
        {
            equipment.PropertyChanged += (_, _) =>
            {
                if (!equipment.IsSelected) foreach (var work in _works.Where(x => x.IsSelected && x.Sheet.EquipmentIds.Contains(equipment.Sheet.Id)))
                    work.Sheet.EquipmentIds = work.Sheet.EquipmentIds.Where(id => id != equipment.Sheet.Id).ToList();
                UpdateEquipmentSelector(); UpdateEquipmentLinks(); QueueSummary();
            };
            equipment.Sheet.PropertyChanged += (_, _) => QueueSummary();
        }
        EmptyCatalogText.Visibility = _works.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSheetSelector();
        UpdateEquipmentSelector();
    }
    private void UpdateEquipmentSelector()
    {
        var id = (EquipmentSelector.SelectedItem as PosWorkChoice)?.Sheet.Id;
        var selected = _equipment.Where(x => x.IsSelected).ToList();
        EquipmentSelector.ItemsSource = selected;
        EquipmentSelector.SelectedItem = selected.FirstOrDefault(x => x.Sheet.Id == id) ?? selected.FirstOrDefault();
    }
    private void UpdateEquipmentLinks()
    {
        if (SheetSelector.SelectedItem is not PosWorkChoice work) { WorkEquipmentLinks.ItemsSource = null; return; }
        var links = _equipment.Select(x => new PosWorkChoice { Sheet = x.Sheet, IsSelected = work.Sheet.EquipmentIds.Contains(x.Sheet.Id) }).ToList();
        links.AddRange(work.Sheet.EquipmentIds.Where(id => links.All(x => x.Sheet.Id != id)).Select(id => new PosWorkChoice
        { Sheet = new() { Id = id, Kind = PosSheetKind.Equipment, Name = "Attrezzatura non più attiva · deseleziona o sostituisci" }, IsSelected = true }));
        foreach (var link in links) link.PropertyChanged += (_, _) =>
        {
            work.Sheet.EquipmentIds = links.Where(x => x.IsSelected).Select(x => x.Sheet.Id).ToList();
            if (link.IsSelected) { var equipment = _equipment.FirstOrDefault(x => x.Sheet.Id == link.Sheet.Id); if (equipment != null) equipment.IsSelected = true; }
            QueueSummary();
        };
        WorkEquipmentLinks.ItemsSource = links;
    }
    private void OnEquipmentSheetChanged(object sender, SelectionChangedEventArgs e)
    {
        EquipmentEditor.DataContext = (EquipmentSelector.SelectedItem as PosWorkChoice)?.Sheet;
        EquipmentEditor.IsEnabled = EquipmentEditor.DataContext != null;
    }
    private void OnEquipmentSearchChanged(object sender, TextChangedEventArgs e) => _equipmentView?.Refresh();
    private void UpdateSheetSelector()
    {
        var id = (SheetSelector.SelectedItem as PosWorkChoice)?.Sheet.Id;
        var selected = _works.Where(x => x.IsSelected).ToList();
        SheetSelector.ItemsSource = selected;
        SheetSelector.SelectedItem = selected.FirstOrDefault(x => x.Sheet.Id == id) ?? selected.FirstOrDefault();
    }
    private PosDraft Collect()
    {
        var snapshot = PosDraftRules.Copy(_draft);
        snapshot.Works = _works.Where(x => x.IsSelected).Select(x => PosDraftRules.Copy(x.Sheet)).ToList();
        snapshot.EquipmentSheets = _equipment.Where(x => x.IsSelected).Select(x => PosDraftRules.Copy(x.Sheet)).ToList();
        snapshot.Employees = _employees.Where(x => x.IsSelected).Select(x => PosDraftRules.Copy(x.Employee)).ToList();
        return snapshot;
    }
    private bool Dirty => _invalidDates.Count > 0 || JsonSerializer.Serialize(Collect()) != _baseline;
    private static bool Matches(string value, string search) => search.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .All(term => CultureInfo.GetCultureInfo("it-IT").CompareInfo.IndexOf(value, term, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0);
    private void OnWorkSearchChanged(object sender, TextChangedEventArgs e) => _workView?.Refresh();
    private void OnEmployeeSearchChanged(object sender, TextChangedEventArgs e) => _employeeView?.Refresh();
    private void OnWorkSelectionChanged(object sender, RoutedEventArgs e) { if (!_loading) Dispatcher.BeginInvoke(UpdateSheetSelector, DispatcherPriority.DataBind); QueueSummary(); }
    private void OnEmployeeSelectionChanged(object sender, RoutedEventArgs e) => QueueSummary();
    private void OnVerifiedChanged(object sender, RoutedEventArgs e) => QueueSummary();
    private void OnSheetChanged(object sender, SelectionChangedEventArgs e)
    {
        SheetEditor.DataContext = (SheetSelector.SelectedItem as PosWorkChoice)?.Sheet;
        SheetEditor.IsEnabled = SheetEditor.DataContext != null;
        UpdateEquipmentLinks();
    }
    private void OnFormTextChanged(object sender, TextChangedEventArgs e)
    {
        if (ReferenceEquals(e.Source, WorkSearch) || ReferenceEquals(e.Source, EmployeeSearch) || ReferenceEquals(e.Source, EquipmentSearch)) return;
        QueueSummary();
    }
    private void QueueSummary()
    {
        if (_loading || !IsInitialized) return;
        Dispatcher.BeginInvoke(() => { if (_lifetime.IsCancellationRequested) return; UpdateSummary(); if (Dirty) StatusText.Text = "Modifiche alla bozza non ancora salvate."; }, DispatcherPriority.Background);
    }
    private void UpdateSummary()
    {
        var snapshot = Collect();
        string date(DateTime? value) => value?.ToString("dd/MM/yyyy") ?? "da indicare";
        SummaryText.Text = $"{(string.IsNullOrWhiteSpace(snapshot.SiteName) ? "Cantiere da indicare" : snapshot.SiteName)}\n" +
            $"{snapshot.Works.Count} lavorazioni POS · {snapshot.EquipmentSheets.Count} attrezzature · {snapshot.Employees.Count} persone · {date(snapshot.StartDate)} – {date(snapshot.EndDate)}";
        var pending = PosDraftRules.Pending(snapshot);
        ReviewText.Text = "Lavorazioni: " + (snapshot.Works.Count == 0 ? "nessuna selezionata" : string.Join(", ", snapshot.Works.Select(x => x.Name))) +
            "\nAttrezzature: " + (snapshot.EquipmentSheets.Count == 0 ? "nessuna selezionata" : string.Join(", ", snapshot.EquipmentSheets.Select(x => x.Name))) +
            "\nSquadra: " + (snapshot.Employees.Count == 0 ? "da indicare" : string.Join(", ", snapshot.Employees.Select(x => x.Name))) +
            "\n\n" + (pending.Count == 0 ? "Campi del riepilogo compilati. La bozza resta disponibile per la revisione." :
                "Da completare o verificare:\n• " + string.Join("\n• ", pending));
    }
    private void ShowSavedStatus() => StatusText.Text = _draft.SavedAtUtc is { } date ?
        $"Bozza salvata sul database · {date.ToLocalTime():dd/MM/yyyy HH:mm}" : "Nuova bozza POS · non ancora salvata.";
    private void OnDateValidationError(object? sender, DatePickerDateValidationErrorEventArgs e)
    {
        e.ThrowException = false;
        if (sender is DatePicker picker) _invalidDates.Add(picker);
        StatusText.Text = "Data non valida. Correggila oppure usa Svuota.";
    }
    private void OnDateKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is DatePicker picker && (e.Key is Key.Back or Key.Delete || e.Key >= Key.D0 && e.Key <= Key.D9 || e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9))
            _invalidDates.Remove(picker);
    }
    private void OnDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is DatePicker { IsDropDownOpen: true } picker) _invalidDates.Remove(picker);
        QueueSummary();
    }
    private void OnClearDateClick(object sender, RoutedEventArgs e)
    {
        var picker = (sender as Button)?.Tag as string == "start" ? StartPicker : EndPicker;
        _invalidDates.Remove(picker); picker.SelectedDate = null; picker.Text = ""; QueueSummary();
    }
    private void OnRestoreStandardClick(object sender, RoutedEventArgs e)
    {
        if (SheetSelector.SelectedItem is not PosWorkChoice choice) return;
        var template = _templates.FirstOrDefault(x => x.Id == choice.Sheet.Id);
        if (template == null) { MessageBox.Show(this, "Questo modello non è più attivo nel catalogo.", "Scheda POS"); return; }
        if (MessageBox.Show(this, "Sostituire i testi di questa scheda con il modello standard caricato?", "Scheda POS",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var sheet = choice.Sheet;
        sheet.Name = template.Name; sheet.Description = template.Description; sheet.Phases = template.Phases;
        sheet.Equipment = template.Equipment; sheet.Risks = template.Risks; sheet.Revision = template.Revision; sheet.Verified = false;
        sheet.Category = template.Category; sheet.Dpi = template.Dpi; sheet.Procedures = template.Procedures;
        sheet.Measures = template.Measures; sheet.Organization = template.Organization; sheet.Coordination = template.Coordination;
        sheet.EquipmentIds = template.EquipmentIds.ToList();
        foreach (var equipment in _equipment.Where(x => sheet.EquipmentIds.Contains(x.Sheet.Id))) equipment.IsSelected = true;
        UpdateEquipmentLinks();
        QueueSummary();
    }
    private async void OnCatalogClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        new PosTemplatesWindow(_service, PosDraftRules.Copy(_templates)) { Owner = this }.ShowDialog();
        _busy = true; CatalogButton.IsEnabled = SaveButton.IsEnabled = false;
        try
        {
            var latest = await _service.LoadTemplatesAsync(_lifetime.Token);
            var snapshot = Collect(); _templates = latest;
            _loading = true; SetWorks(snapshot); _loading = false; UpdateSummary();
            StatusText.Text = "Catalogo aggiornato. Le schede già selezionate conservano i loro testi.";
        }
        catch (Exception ex) { _loading = false; MessageBox.Show(this, ex.Message, "Catalogo POS", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { _busy = false; CatalogButton.IsEnabled = SaveButton.IsEnabled = true; }
    }
    private async Task<bool> SaveAsync()
    {
        if (_busy) return false;
        Keyboard.ClearFocus();
        if (_invalidDates.Count > 0) { MessageBox.Show(this, "Correggi le date non valide oppure usa Svuota. Gli altri campi possono restare incompleti.", "Bozza POS"); return false; }
        var requested = Collect();
        _busy = true; FormPanel.IsEnabled = CatalogButton.IsEnabled = SaveButton.IsEnabled = false;
        StatusText.Text = "Salvataggio della bozza POS…";
        try
        {
            var saved = await _service.SaveDraftAsync(requested, _lifetime.Token);
            _draft.Id = saved.Id; _draft.Revision = saved.Revision; _draft.SavedAtUtc = saved.SavedAtUtc;
            _baseline = JsonSerializer.Serialize(Collect()); UpdateSummary(); ShowSavedStatus();
            return true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return false; }
        catch (Exception ex) { StatusText.Text = "Bozza non salvata. Le tue modifiche rimangono nella finestra."; MessageBox.Show(this, ex.Message, "Bozza POS", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
        finally { _busy = false; FormPanel.IsEnabled = CatalogButton.IsEnabled = SaveButton.IsEnabled = true; }
    }
    private async void OnSaveClick(object sender, RoutedEventArgs e) => await SaveAsync();
    private void OnReviewClick(object sender, RoutedEventArgs e) { UpdateSummary(); ReviewSection.IsExpanded = true; ReviewSection.BringIntoView(); }
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || AppShutdownManager.IsShutdownRequested) return;
        if (_busy) { e.Cancel = true; return; }
        if (!Dirty) return;
        e.Cancel = true;
        var answer = MessageBox.Show(this, "Salvare la bozza POS prima di chiudere?", "Bozza POS", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel) return;
        if (answer == MessageBoxResult.Yes && !await SaveAsync()) return;
        _allowClose = true; _ = Dispatcher.BeginInvoke(Close);
    }
}
