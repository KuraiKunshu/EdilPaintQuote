using System.ComponentModel;
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
        nameof(ScheduleEntry), typeof(WorkScheduleEntry), typeof(QuoteActionsControl), new PropertyMetadata(null, OnQuoteChanged));
    public static readonly DependencyProperty CanCompleteProperty = DependencyProperty.Register(
        nameof(CanComplete), typeof(bool), typeof(QuoteActionsControl), new PropertyMetadata(true, OnQuoteChanged));
    public static readonly DependencyProperty ShowDetailsProperty = DependencyProperty.Register(
        nameof(ShowDetails), typeof(bool), typeof(QuoteActionsControl), new PropertyMetadata(true));
    public static readonly DependencyProperty UseLargeButtonsProperty = DependencyProperty.Register(
        nameof(UseLargeButtons), typeof(bool), typeof(QuoteActionsControl), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey IsBusyPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsBusy), typeof(bool), typeof(QuoteActionsControl), new PropertyMetadata(false));
    public static readonly DependencyProperty IsBusyProperty = IsBusyPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey IsMenuOpenPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsMenuOpen), typeof(bool), typeof(QuoteActionsControl), new PropertyMetadata(false));
    public static readonly DependencyProperty IsMenuOpenProperty = IsMenuOpenPropertyKey.DependencyProperty;

    public string QuoteNumber { get => (string)GetValue(QuoteNumberProperty); set => SetValue(QuoteNumberProperty, value); }
    public WorkScheduleEntry? ScheduleEntry { get => (WorkScheduleEntry?)GetValue(ScheduleEntryProperty); set => SetValue(ScheduleEntryProperty, value); }
    public bool ShowDetails { get => (bool)GetValue(ShowDetailsProperty); set => SetValue(ShowDetailsProperty, value); }
    public bool UseLargeButtons { get => (bool)GetValue(UseLargeButtonsProperty); set => SetValue(UseLargeButtonsProperty, value); }
    public bool CanComplete { get => (bool)GetValue(CanCompleteProperty); set => SetValue(CanCompleteProperty, value); }
    public bool IsBusy => (bool)GetValue(IsBusyProperty);
    public bool IsMenuOpen => (bool)GetValue(IsMenuOpenProperty);
    public event EventHandler<CancelEventArgs>? CompletionRequested;
    public event EventHandler<WorkScheduleCompletedEventArgs>? WorkCompleted;

    public QuoteActionsControl()
    {
        InitializeComponent();
        UpdateButtons();
    }

    private static void OnQuoteChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        => ((QuoteActionsControl)sender).UpdateButtons();

    private void UpdateButtons()
    {
        if (BtnPdf == null || BtnActions == null || BtnComplete == null) return;
        BtnPdf.IsEnabled = BtnActions.IsEnabled = !IsBusy && !string.IsNullOrWhiteSpace(QuoteNumber);
        bool savedJob = ScheduleEntry is { Kind: WorkScheduleEntryKind.Job, Revision: > 0 };
        BtnComplete.Visibility = savedJob ? Visibility.Visible : Visibility.Collapsed;
        BtnComplete.IsEnabled = savedJob && CanComplete && !IsBusy;
        BtnComplete.Content = ScheduleEntry?.Status == WorkScheduleEntryStatus.Completed ? "PDF costi" : "Finito e costi";
    }

    private async void OnCompleteClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (ScheduleEntry is not { Kind: WorkScheduleEntryKind.Job, Revision: > 0 } entry || !CanComplete) return;
        await ExecuteAsync((owner, token) => CompleteAsync(entry.CreateValidatedCopy(), owner, token));
    }

    private async Task CompleteAsync(WorkScheduleEntry entry, Window owner, CancellationToken token)
    {
        if (!CanComplete) return;
        var request = new CancelEventArgs();
        CompletionRequested?.Invoke(this, request);
        if (request.Cancel) return;
        var completed = await WorkScheduleActions.CompleteAndGenerateCostsAsync(entry, owner, token);
        if (completed == null) return;
        ScheduleEntry = completed;
        WorkCompleted?.Invoke(this, new WorkScheduleCompletedEventArgs(completed));
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
        if (intervention is { Kind: WorkScheduleEntryKind.Job, Revision: > 0 })
        {
            var finish = Add(intervention.Status == WorkScheduleEntryStatus.Completed ? "Rigenera PDF costi" : "Finito e PDF costi",
                (owner, token) => CompleteAsync(intervention, owner, token));
            finish.IsEnabled = CanComplete;
        }
        if (ShowDetails)
            Add("Dettaglio nello storico", (owner, _) =>
            {
                WorkScheduleActions.OpenOrder(number, owner);
                return Task.CompletedTask;
            });
        return menu;

        MenuItem Add(string label, Func<Window, CancellationToken, Task> action)
        {
            var item = new MenuItem { Header = label };
            item.Click += async (_, e) => { e.Handled = true; await ExecuteAsync(action, capturedOwner); };
            menu.Items.Add(item);
            return item;
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

public sealed class WorkScheduleCompletedEventArgs(WorkScheduleEntry entry) : EventArgs
{
    public WorkScheduleEntry Entry { get; } = entry;
}
