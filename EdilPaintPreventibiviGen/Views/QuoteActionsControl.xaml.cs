using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;

namespace EdilPaintPreventibiviGen.Views;

public partial class QuoteActionsControl : UserControl
{
    public static readonly DependencyProperty QuoteNumberProperty = DependencyProperty.Register(
        nameof(QuoteNumber), typeof(string), typeof(QuoteActionsControl), new PropertyMetadata(string.Empty, OnQuoteChanged));
    public static readonly DependencyProperty ScheduleEntryProperty = DependencyProperty.Register(
        nameof(ScheduleEntry), typeof(WorkScheduleEntry), typeof(QuoteActionsControl), new PropertyMetadata(null));
    public static readonly DependencyProperty ShowDetailsProperty = DependencyProperty.Register(
        nameof(ShowDetails), typeof(bool), typeof(QuoteActionsControl), new PropertyMetadata(true));
    private static readonly DependencyPropertyKey IsBusyPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsBusy), typeof(bool), typeof(QuoteActionsControl), new PropertyMetadata(false));
    public static readonly DependencyProperty IsBusyProperty = IsBusyPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey IsMenuOpenPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsMenuOpen), typeof(bool), typeof(QuoteActionsControl), new PropertyMetadata(false));
    public static readonly DependencyProperty IsMenuOpenProperty = IsMenuOpenPropertyKey.DependencyProperty;

    public string QuoteNumber { get => (string)GetValue(QuoteNumberProperty); set => SetValue(QuoteNumberProperty, value); }
    public WorkScheduleEntry? ScheduleEntry { get => (WorkScheduleEntry?)GetValue(ScheduleEntryProperty); set => SetValue(ScheduleEntryProperty, value); }
    public bool ShowDetails { get => (bool)GetValue(ShowDetailsProperty); set => SetValue(ShowDetailsProperty, value); }
    public bool IsBusy => (bool)GetValue(IsBusyProperty);
    public bool IsMenuOpen => (bool)GetValue(IsMenuOpenProperty);

    public QuoteActionsControl()
    {
        InitializeComponent();
        UpdateButtons();
    }

    private static void OnQuoteChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        => ((QuoteActionsControl)sender).UpdateButtons();

    private void UpdateButtons()
    {
        if (BtnPdf == null || BtnActions == null) return;
        BtnPdf.IsEnabled = BtnActions.IsEnabled = !IsBusy && !string.IsNullOrWhiteSpace(QuoteNumber);
    }

    private async void OnPdfClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        string number = QuoteNumber;
        await ExecuteAsync((owner, token) => QuoteDocumentActions.OpenPdfAsync(number, owner, token));
    }

    private void OnActionsClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (IsBusy || string.IsNullOrWhiteSpace(QuoteNumber)) return;
        var menu = BuildMenu();
        menu.PlacementTarget = BtnActions;
        menu.Opened += (_, _) => SetValue(IsMenuOpenPropertyKey, true);
        menu.Closed += (_, _) => SetValue(IsMenuOpenPropertyKey, false);
        BtnActions.ContextMenu = menu;
        menu.IsOpen = true;
    }

    internal ContextMenu BuildMenu()
    {
        string number = QuoteNumber;
        var capturedOwner = Window.GetWindow(this);
        var intervention = ScheduleEntry?.CreateValidatedCopy();
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = $"Preventivo {number}", IsEnabled = false });
        menu.Items.Add(new Separator());
        Add("Apri PDF", (owner, token) => QuoteDocumentActions.OpenPdfAsync(number, owner, token));
        Add("Scheda lavoro", (owner, token) => intervention == null
            ? QuoteDocumentActions.GenerateWorkSheetAsync(number, owner, token)
            : WorkScheduleActions.GenerateWorkSheetAsync(intervention, owner, token));
        Add("Cartella cliente", (owner, token) => QuoteDocumentActions.OpenCustomerFolderAsync(number, owner, token));
        if (ShowDetails)
            Add("Dettaglio nello storico", (owner, _) =>
            {
                WorkScheduleActions.OpenOrder(number, owner);
                return Task.CompletedTask;
            });
        return menu;

        void Add(string label, Func<Window, CancellationToken, Task> action)
        {
            var item = new MenuItem { Header = label };
            item.Click += async (_, e) => { e.Handled = true; await ExecuteAsync(action, capturedOwner); };
            menu.Items.Add(item);
        }
    }

    private async Task ExecuteAsync(Func<Window, CancellationToken, Task> action, Window? capturedOwner = null)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(QuoteNumber)) return;
        var owner = capturedOwner ?? Window.GetWindow(this);
        if (owner == null) return;
        using var lifetime = AppShutdownManager.CreateLinkedTokenSource();
        void Closed(object? sender, EventArgs e) => lifetime.Cancel();
        owner.Closed += Closed;
        SetValue(IsBusyPropertyKey, true);
        Cursor = Cursors.Wait;
        UpdateButtons();
        try { await action(owner, lifetime.Token); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!lifetime.IsCancellationRequested)
                MessageBox.Show(owner, ex.Message, "Documenti del preventivo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            owner.Closed -= Closed;
            Cursor = null;
            SetValue(IsBusyPropertyKey, false);
            UpdateButtons();
        }
    }
}
