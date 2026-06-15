using System;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Linq;
using EdilPaintPreventibiviGen.ViewModels;
using EdilPaintPreventibiviGen.Views;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Microsoft.Win32;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace EdilPaintPreventibiviGen;

public partial class MainWindow : Window
{
    private CancellationTokenSource? _materialSearchCts;
    private CancellationTokenSource? _draftAutosaveCts;
    private DispatcherTimer? _draftAutosaveTimer;
    private bool _isAutosavingDraft;
    private bool _isCloseConfirmed;
    private bool _isCloseCleanupRunning;
    private bool _isInitializingMainWindowScale;
    
    #region Constructor
    public MainWindow()
    {
        InitializeComponent();
        InitializeMainWindowScale();
        var vm = new MainViewModel();
        DataContext = vm;
        Loaded += async (_, _) =>
        {
            await vm.InitializeAsync();
            await PromptDraftRecoveryAsync(vm);
            StartDraftAutosave();
        };
    }
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        InitializeMainWindowScale();
        DataContext = vm;
        Loaded += async (_, _) =>
        {
            await PromptDraftRecoveryAsync(vm);
            StartDraftAutosave();
        };
    }
    #endregion

    private void InitializeMainWindowScale()
    {
        _isInitializingMainWindowScale = true;
        try
        {
            double scale = App.AppSettings?.App.GetEffectiveMainWindowScale() ?? 1.0;
            ApplyMainWindowScale(scale);
            SldMainWindowScale.Value = scale;
            SldMainWindowScale.ValueChanged -= OnMainWindowScaleChanged;
            SldMainWindowScale.ValueChanged += OnMainWindowScaleChanged;
        }
        finally
        {
            _isInitializingMainWindowScale = false;
        }
    }

    private void ApplyMainWindowScale(double scale)
    {
        scale = Math.Clamp(scale, 0.8, 1.1);
        MainWindowScaleTransform.ScaleX = scale;
        MainWindowScaleTransform.ScaleY = scale;
        TxtMainWindowScale.Text = $"{scale:P0}";
    }

    private void OnMainWindowScaleChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        double scale = Math.Clamp(e.NewValue, 0.8, 1.1);
        ApplyMainWindowScale(scale);

        if (_isInitializingMainWindowScale || App.AppSettings == null)
            return;

        App.AppSettings.App.MainWindowScale = scale;
        App.AppSettings.Save();
    }

    private void OnResetMainWindowScaleClick(object sender, RoutedEventArgs e)
    {
        SldMainWindowScale.Value = 1.0;
    }
    
    #region Window events
    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (AppShutdownManager.IsShutdownRequested)
        {
            StopDraftAutosave();
            _materialSearchCts?.Cancel();
            return;
        }

        if (_isCloseCleanupRunning)
        {
            e.Cancel = true;
            return;
        }

        if (!_isCloseConfirmed)
        {
            var result = MessageBox.Show(
                "Sei sicuro di voler chiudere l'applicazione?",
                "Conferma chiusura",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.No)
            {
                e.Cancel = true;
                return;
            }
        }

        if (!_isCloseConfirmed)
        {
            e.Cancel = true;
            _isCloseConfirmed = true;
            _ = ShutdownAfterCleanupAsync();
            return;
        }
    }
    #endregion

    private async Task ShutdownAfterCleanupAsync()
    {
        try
        {
            await Task.Yield();
            await PrepareForCloseAsync();
            await Dispatcher.InvokeAsync(() =>
            {
                AppShutdownManager.RequestShutdown();
                Application.Current.Shutdown();
            }, DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SHUTDOWN] Close retry error: {ex.Message}");
            await Dispatcher.InvokeAsync(() =>
            {
                AppShutdownManager.RequestShutdown();
                Application.Current.Shutdown();
            }, DispatcherPriority.Background);
        }
    }

    private async Task PrepareForCloseAsync()
    {
        if (_isCloseCleanupRunning)
            return;

        _isCloseCleanupRunning = true;
        try
        {
            StopDraftAutosave();
            _materialSearchCts?.Cancel();

            if (DataContext is MainViewModel vm)
            {
                try
                {
                    CommitPendingGridEdits();
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await vm.SaveDraftAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("[SHUTDOWN] Draft autosave skipped after timeout.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SHUTDOWN] Draft autosave error: {ex.Message}");
                }
            }

            AppShutdownManager.RequestShutdown();
        }
        finally
        {
            _materialSearchCts?.Dispose();
            _materialSearchCts = null;
            _isCloseCleanupRunning = false;
        }
    }

    private void StartDraftAutosave()
    {
        StopDraftAutosave();
        _draftAutosaveCts = AppShutdownManager.CreateLinkedTokenSource();
        _draftAutosaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(25)
        };
        _draftAutosaveTimer.Tick += OnDraftAutosaveTick;
        _draftAutosaveTimer.Start();
    }

    private void StopDraftAutosave()
    {
        _draftAutosaveCts?.Cancel();
        _draftAutosaveCts?.Dispose();
        _draftAutosaveCts = null;

        if (_draftAutosaveTimer != null)
        {
            _draftAutosaveTimer.Stop();
            _draftAutosaveTimer.Tick -= OnDraftAutosaveTick;
            _draftAutosaveTimer = null;
        }
    }

    private async void OnDraftAutosaveTick(object? sender, EventArgs e)
    {
        var token = _draftAutosaveCts?.Token ?? CancellationToken.None;
        await AutosaveDraftAsync(token);
    }

    private async Task AutosaveDraftAsync(CancellationToken cancellationToken)
    {
        if (_isAutosavingDraft ||
            AppShutdownManager.IsShutdownRequested ||
            cancellationToken.IsCancellationRequested ||
            DataContext is not MainViewModel vm)
            return;

        _isAutosavingDraft = true;
        try
        {
            CommitPendingGridEdits();
            await vm.SaveDraftAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[Draft] Autosalvataggio annullato.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Draft] Autosalvataggio non riuscito: {ex.Message}");
        }
        finally
        {
            _isAutosavingDraft = false;
        }
    }

    private void CommitPendingGridEdits()
    {
        try
        {
            CommitFocusedTextBinding();

            foreach (var grid in GetEditableDataGrids())
            {
                grid.CommitEdit(DataGridEditingUnit.Cell, true);
                grid.CommitEdit(DataGridEditingUnit.Row, true);
            }

            (DataContext as MainViewModel)?.CalculateTotals();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CommitPendingGridEdits] Commit modifiche griglia non riuscito: {ex.Message}");
        }
    }

    private IEnumerable<DataGrid> GetEditableDataGrids()
    {
        yield return GridMaterials;
        yield return GridLabors;
        yield return GridOurCosts;
        yield return GridPartnerCosts;
        yield return GridAdditionalCosts;
    }

    private static void CommitFocusedTextBinding()
    {
        if (Keyboard.FocusedElement is TextBox textBox)
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    private async Task PromptDraftRecoveryAsync(MainViewModel vm)
    {
        var draft = await vm.LoadDraftAsync();
        if (draft == null)
            return;

        string savedAt = draft.LastModifiedUtc == default
            ? "data sconosciuta"
            : draft.LastModifiedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");

        var result = MessageBox.Show(
            $"Ho trovato una bozza autosalvata del {savedAt}. Vuoi recuperarla?",
            "Bozza disponibile",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            vm.ApplyDraft(draft);
        }
        else
        {
            await vm.DiscardDraftAsync();
            vm.ResetQuote();
        }
    }
    
    #region On drag & drop
    private Point _dragStartPoint;

    private void DataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    private void DataGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        Point currentPosition = e.GetPosition(null);
        Vector diff = _dragStartPoint - currentPosition;

        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (sender is not DataGrid)
            return;

        if (FindVisualParent<DataGridRow>((DependencyObject)e.OriginalSource) is not DataGridRow row)
            return;

        if (row.Item is not Item draggedItem)
            return;

        DragDrop.DoDragDrop(row, draggedItem, DragDropEffects.Move);
    }

    private void DataGrid_Drop(object sender, DragEventArgs e)
    {
        if (sender is not DataGrid dataGrid)
            return;

        if (e.Data.GetDataPresent(typeof(Item)) is false)
            return;

        if (e.Data.GetData(typeof(Item)) is not Item draggedItem)
            return;

        if (DataContext is not MainViewModel vm)
            return;

        bool isMaterialsGrid = ReferenceEquals(dataGrid.ItemsSource, vm.Materials);
        bool isLaborsGrid = ReferenceEquals(dataGrid.ItemsSource, vm.Labors);

        if (!isMaterialsGrid && !isLaborsGrid)
            return;

        var items = isMaterialsGrid ? vm.Materials : vm.Labors;

        Point dropPosition = e.GetPosition(dataGrid);
        var target = GetItemFromPoint(dataGrid, dropPosition);

        if (target == null || ReferenceEquals(draggedItem, target))
            return;

        int oldIndex = items.IndexOf(draggedItem);
        int newIndex = items.IndexOf(target);

        if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex)
            return;

        items.Move(oldIndex, newIndex);
        vm.UpdateItemSortOrders();
        vm.CalculateTotals();
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent)
                return parent;

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    private static Item? GetItemFromPoint(DataGrid dataGrid, Point point)
    {
        var element = dataGrid.InputHitTest(point) as DependencyObject;
        var row = FindVisualParent<DataGridRow>(element);
        return row?.Item as Item;
    }

    #endregion
    
    
    #region Image Handlers
    private async void OnAddImageClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Tutti i file (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            if (DataContext is MainViewModel vm)
            {
                foreach (string file in dialog.FileNames)
                {
                    await vm.AddAttachmentFromPathAsync(file);
                }
            }
        }
    }

    private void OnRemoveImageClick(object sender, RoutedEventArgs e)
    {
        if (LstImages.SelectedItem is SelectedAttachment selectedAttachment)
        {
            (DataContext as MainViewModel)?.RemoveAttachment(selectedAttachment);
        }
    }

    private async void OnImageDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                if (DataContext is MainViewModel vm)
                {
                    foreach (string file in files)
                    {
                        if (!string.IsNullOrWhiteSpace(file))
                            await vm.AddAttachmentFromPathAsync(file);
                    }
                }
            }
        }
    }

    private async void OnImagePaste(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.V && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
        {
            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList();
                if (DataContext is MainViewModel vm && files != null)
                {
                    foreach (string? file in files)
                    {
                        if (!string.IsNullOrWhiteSpace(file))
                            await vm.AddAttachmentFromPathAsync(file);
                    }
                }
            }
        }
    }
    #endregion

    #region General Handlers
    private void OnSelectCustomerClick(object sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) { var win = new SelectCustomerWindow(vm) { Owner = this, Title = "Seleziona Cliente" }; if (win.ShowDialog() == true && win.SelectedResult != null) vm.SelectedCustomer = win.SelectedResult; } }
    private void OnSelectReferenceClick(object sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) { var win = new SelectCustomerWindow(vm) { Owner = this, Title = "Seleziona Riferimento" }; if (win.ShowDialog() == true && win.SelectedResult != null) vm.SelectedSecondCustomer = win.SelectedResult; } }
    private async void OnNewQuoteClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Sicuro?", "Conferma", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;

        if (DataContext is MainViewModel vm)
        {
            await vm.DiscardDraftAsync();
            vm.ResetQuote();
        }
    }
    private void OnGeneratePdfClick(object sender, RoutedEventArgs e)
    {
        CommitPendingGridEdits();
        (DataContext as MainViewModel)?.GeneratePdf();
    }

    private void OnGenerateCostsPdfClick(object sender, RoutedEventArgs e)
    {
        CommitPendingGridEdits();
        (DataContext as MainViewModel)?.GenerateCostsPdf();
    }
    private void OnOpenHistoryClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var win = new HistoryWindow(vm) { Owner = this }; 
            win.ShowDialog();
        }
    }
    private void OnNewCustomerClick(object sender, RoutedEventArgs e){
        var win = new NewCustomerWindow { Owner = this }; 
        if (win.ShowDialog() == true && win.NewCustomer != null) 
            (DataContext as MainViewModel)?.AddNewCustomer(win.NewCustomer);
    }
    private void OnEditCustomerClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.SelectedCustomer == null) return;

        string originalBusinessName = vm.SelectedCustomer.BusinessName;
        var win = new NewCustomerWindow(vm.SelectedCustomer) { Owner = this };
        if (win.ShowDialog() == true && win.NewCustomer != null)
            vm.UpdateCustomer(originalBusinessName, win.NewCustomer);
    }

    private void OnEditReferenceClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.SelectedSecondCustomer == null) return;

        string originalBusinessName = vm.SelectedSecondCustomer.BusinessName;
        var win = new NewCustomerWindow(vm.SelectedSecondCustomer) { Owner = this };
        if (win.ShowDialog() == true && win.NewCustomer != null)
            vm.UpdateCustomer(originalBusinessName, win.NewCustomer);
    }
    private void OnOpenLaborListClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var win = new SelectLaborWindow(vm) { Owner = this };
            win.ShowDialog();
        }
    }
    private void OnOpenMaterialListClick(object sender, RoutedEventArgs e){
        if (DataContext is MainViewModel vm)
        {
            var win = new SelectMaterialWindow(vm) { Owner = this };
            win.ShowDialog();
        }
    }
    private void OnOpenSettingsClick(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow { Owner = this };
        win.ShowDialog();
    }
    private void OnOpenDashboardClick(object sender, RoutedEventArgs e)
    {
        var win = new DashboardWindow { Owner = this };
        win.ShowDialog();
    }

    private void OnOpenSentOpenQuotesClick(object sender, RoutedEventArgs e)
    {
        var win = new SentOpenQuotesWindow { Owner = this };
        win.ShowDialog();
    }

    private void OnEditRowClick(object sender, RoutedEventArgs e) { if (sender is Button btn && btn.DataContext is Item item) { var win = new EditItemWindow(item) { Owner = this }; if (win.ShowDialog() == true) (DataContext as MainViewModel)?.CalculateTotals(); } }
    private void OnDeleteRowClick(object sender, RoutedEventArgs e) { if (sender is Button btn && btn.DataContext is Item item && DataContext is MainViewModel vm) { if (vm.Materials.Contains(item)) vm.Materials.Remove(item); else if (vm.Labors.Contains(item)) vm.Labors.Remove(item); vm.UpdateItemSortOrders(); vm.CalculateTotals(); } }
    private void OnLaborSearchChanged(object sender, TextChangedEventArgs e) 
    { 
        if (DataContext is MainViewModel vm && sender is ComboBox cb) 
        { 
            if (cb.SelectedItem is Item s && s.Name == cb.Text) return; 
            vm.ApplyLaborFilter(cb.Text); 
            cb.SelectedIndex = -1;
            cb.IsDropDownOpen = !string.IsNullOrWhiteSpace(cb.Text) && cb.HasItems; 
            FixComboBoxSelection(cb); 
        } 
    }

    private void OnMaterialSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not ComboBox cb)
            return;

        // Evita loop quando viene selezionato un elemento
        if (cb.SelectedItem is VeluxResult selected && selected.Label == cb.Text)
            return;

        string text = cb.Text;

        _materialSearchCts?.Cancel();
        _materialSearchCts?.Dispose();
        _materialSearchCts = AppShutdownManager.CreateLinkedTokenSource();
        var token = _materialSearchCts.Token;

        _ = RunMaterialSearchAsync(vm, cb, text, token);
    }

    private async Task RunMaterialSearchAsync(MainViewModel vm, ComboBox cb, string text, CancellationToken token)
    {
        try
        {
            await Task.Delay(150, token);
            if (token.IsCancellationRequested) return;

            await vm.ApplyMaterialFilterAsync(text, token);
            if (token.IsCancellationRequested) return;

            cb.SelectedIndex = -1;
            cb.IsDropDownOpen = vm.AllCatalogMaterials.Count > 0;
        }
        catch (OperationCanceledException) { }
    }    
    private void FixComboBoxSelection(ComboBox cb) 
    { 
        if (cb.Template.FindName("PART_EditableTextBox", cb) is TextBox tb) 
        {
            cb.Dispatcher.BeginInvoke(new Action(() => 
            { 
                tb.SelectionLength = 0; 
                tb.CaretIndex = tb.Text.Length; 
            }), System.Windows.Threading.DispatcherPriority.Input); 
        }
    }
    private void OnOpenCustomerFolderClick(object sender, RoutedEventArgs e) => (DataContext as MainViewModel)?.OpenCustomerFolder();
    private void OnOpenReferenceFolderClick(object sender, RoutedEventArgs e) => (DataContext as MainViewModel)?.OpenReferenceFolder();
    private void OnAddMaterialClick(object sender, RoutedEventArgs e) => (DataContext as MainViewModel)?.AddMaterial();
    private void OnAddLaborClick(object sender, RoutedEventArgs e) => (DataContext as MainViewModel)?.AddLabor();
    private void OnComboDropDownOpened(object sender, EventArgs e)
    {
        if (sender is ComboBox cb)
        {
            // Trova la TextBox interna e rimuovi la selezione
            var tb = cb.Template.FindName("PART_EditableTextBox", cb) as TextBox;
            if (tb != null)
            {
                tb.SelectionLength = 0;
                tb.CaretIndex = tb.Text.Length; // cursore alla fine
            }
        }
    }

    private void OnAddOurCostClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        vm.OurCosts.Add(new CostAllocationItem { Description = "Nuova voce", Amount = 0 });
        GridOurCosts.ScrollIntoView(vm.OurCosts.Last());
    }

    private void OnAddPartnerCostClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        vm.PartnerCosts.Add(new CostAllocationItem { Description = "Nuova voce", Amount = 0 });
        GridPartnerCosts.ScrollIntoView(vm.PartnerCosts.Last());
    }

    private void OnAddAdditionalCostClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        vm.AdditionalCosts.Add(new CostAllocationItem { Description = "Nuova voce", Amount = 0 });
        GridAdditionalCosts.ScrollIntoView(vm.AdditionalCosts.Last());
    }

    private void OnDeleteCostRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not CostAllocationItem item) return;
        if (DataContext is not MainViewModel vm) return;

        if (vm.OurCosts.Contains(item)) vm.OurCosts.Remove(item);
        else if (vm.PartnerCosts.Contains(item)) vm.PartnerCosts.Remove(item);
        else if (vm.AdditionalCosts.Contains(item)) vm.AdditionalCosts.Remove(item);
    }
    #endregion
}
