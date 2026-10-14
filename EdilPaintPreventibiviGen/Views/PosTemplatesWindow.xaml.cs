using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;

namespace EdilPaintPreventibiviGen.Views;

public partial class PosTemplatesWindow : Window
{
    private readonly PosService _service;
    private List<PosWorkItem> _templates;
    private PosWorkItem? _editor, _selected;
    private string _baseline = "";
    private bool _busy, _selecting;
    private PosSheetKind _kind;
    private readonly CancellationTokenSource _lifetime = AppShutdownManager.CreateLinkedTokenSource();

    public PosTemplatesWindow(PosService service, List<PosWorkItem> templates)
    {
        _service = service; _templates = templates;
        InitializeComponent();
        Helpers.WindowResizeBehavior.PreventMaximizedState(this);
        RefreshList();
        Closed += (_, _) => { _lifetime.Cancel(); _lifetime.Dispose(); };
    }
    private bool Dirty => _editor != null && JsonSerializer.Serialize(_editor) != _baseline;
    private bool DiscardAllowed() => !Dirty || MessageBox.Show(this,
        "Ci sono modifiche al testo standard non salvate. Vuoi scartarle?", "Testi standard POS",
        MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    private void RefreshList()
    {
        _selecting = true;
        TemplateList.ItemsSource = _templates.Where(x => x.Kind == _kind && x.Name.Contains(SearchBox.Text.Trim(), StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        TemplateList.SelectedItem = _selected;
        _selecting = false;
    }
    private void Edit(PosWorkItem? selected)
    {
        _selected = selected; _editor = selected == null ? null : PosDraftRules.Copy(selected);
        _baseline = _editor == null ? "" : JsonSerializer.Serialize(_editor);
        EditorPanel.DataContext = _editor; EditorPanel.IsEnabled = _editor != null;
        WorkFields.Visibility = WorkOrganization.Visibility = _kind == PosSheetKind.Work ? Visibility.Visible : Visibility.Collapsed;
        ProceduresLabel.Text = _kind == PosSheetKind.Work ? "Procedure operative" : "Istruzioni operative · prima, durante e dopo l’uso";
        var links = _templates.Where(x => x.Kind == PosSheetKind.Equipment).Select(x => new PosWorkChoice
        { Sheet = x, IsSelected = _editor?.EquipmentIds.Contains(x.Id) == true }).ToList();
        links.AddRange((_editor?.EquipmentIds ?? []).Where(id => links.All(x => x.Sheet.Id != id)).Select(id => new PosWorkChoice
        { Sheet = new() { Id = id, Kind = PosSheetKind.Equipment, Name = "Attrezzatura non più attiva" }, IsSelected = true,
            Notice = "Collegamento conservato: sostituiscilo con una scheda attiva per i nuovi POS." }));
        foreach (var link in links) link.PropertyChanged += (_, _) =>
        { if (_editor != null) _editor.EquipmentIds = links.Where(x => x.IsSelected).Select(x => x.Sheet.Id).ToList(); };
        EquipmentLinks.ItemsSource = links;
        SaveButton.IsEnabled = _editor != null; ArchiveButton.IsEnabled = _editor is { Revision: > 0 };
    }
    private void OnTemplateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selecting) return;
        var selected = TemplateList.SelectedItem as PosWorkItem;
        if (!DiscardAllowed()) { _selecting = true; TemplateList.SelectedItem = _selected; _selecting = false; return; }
        Edit(selected);
    }
    private void OnSearchChanged(object sender, TextChangedEventArgs e) { if (TemplateList != null) RefreshList(); }
    private void OnKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TemplateList == null || _selecting || !ReferenceEquals(e.Source, CatalogKind)) return;
        if (!DiscardAllowed()) { _selecting = true; CatalogKind.SelectedIndex = (int)_kind; _selecting = false; return; }
        _kind = (PosSheetKind)CatalogKind.SelectedIndex; Edit(null); RefreshList();
    }
    private void OnNewClick(object sender, RoutedEventArgs e)
    {
        if (!DiscardAllowed()) return;
        _selecting = true; TemplateList.SelectedItem = null; _selecting = false;
        Edit(new PosWorkItem { Kind = _kind });
        StatusText.Text = "Inserisci nome e testi della scheda POS. Puoi completare i testi anche in seguito.";
    }
    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        if (!DiscardAllowed()) return;
        await RunAsync(async () => { _templates = await _service.LoadTemplatesAsync(_lifetime.Token); Edit(null); RefreshList(); StatusText.Text = "Catalogo aggiornato dal database."; });
    }
    private async Task<bool> SaveAsync(bool archive = false)
    {
        if (_editor == null || _busy) return false;
        var requested = PosDraftRules.Copy(_editor); requested.IsArchived = archive;
        return await RunAsync(async () =>
        {
            var saved = await _service.SaveTemplateAsync(requested, _lifetime.Token);
            _templates.RemoveAll(x => x.Id == saved.Id);
            if (!saved.IsArchived) _templates.Add(saved);
            Edit(saved.IsArchived ? null : saved); RefreshList();
            StatusText.Text = archive ? "Modello archiviato. I POS esistenti conservano la scheda." : "Testo standard salvato sul database aziendale.";
        });
    }
    private async void OnSaveClick(object sender, RoutedEventArgs e) => await SaveAsync();
    private async void OnArchiveClick(object sender, RoutedEventArgs e)
    {
        if (_editor == null || MessageBox.Show(this, "Archiviare questo modello? Le schede già inserite nei POS resteranno salvate.",
            "Catalogo POS", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await SaveAsync(archive: true);
    }
    private async Task<bool> RunAsync(Func<Task> action)
    {
        _busy = true; FormPanel.IsEnabled = SaveButton.IsEnabled = ArchiveButton.IsEnabled = false;
        StatusText.Text = "Aggiornamento del catalogo POS…";
        try { await action(); return true; }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return false; }
        catch (Exception ex) { StatusText.Text = "Operazione non completata. I tuoi testi rimangono nella finestra."; MessageBox.Show(this, ex.Message, "Catalogo POS", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
        finally { _busy = false; FormPanel.IsEnabled = true; SaveButton.IsEnabled = _editor != null; ArchiveButton.IsEnabled = _editor is { Revision: > 0 }; }
    }
    private void OnClosing(object? sender, CancelEventArgs e) { if (!AppShutdownManager.IsShutdownRequested && (_busy || !DiscardAllowed())) e.Cancel = true; }
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
