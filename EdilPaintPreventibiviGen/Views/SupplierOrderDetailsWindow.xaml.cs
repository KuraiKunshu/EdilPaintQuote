using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using EdilPaintPreventibiviGen.ViewModels;

namespace EdilPaintPreventibiviGen.Views;

public partial class SupplierOrderDetailsWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly Func<QuoteHistorySummary, Task<bool>> _saveOrder;
    private readonly Func<QuoteHistorySummary, Window, Task> _prepareMail;
    private readonly Dictionary<DatePicker, string> _invalidDates = [];
    private readonly DependencyPropertyDescriptor _documentBusyDescriptor =
        DependencyPropertyDescriptor.FromProperty(QuoteActionsControl.IsBusyProperty, typeof(QuoteActionsControl));
    private bool _busy;
    private bool _closed;

    public QuoteHistorySummary Order { get; }

    public SupplierOrderDetailsWindow(
        MainViewModel vm,
        QuoteHistorySummary original,
        IReadOnlyList<string> materialStatusOptions,
        Func<QuoteHistorySummary, Task<bool>> saveOrder,
        Func<QuoteHistorySummary, Window, Task> prepareMail)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(materialStatusOptions);
        ArgumentNullException.ThrowIfNull(saveOrder);
        ArgumentNullException.ThrowIfNull(prepareMail);
        _vm = vm;
        _saveOrder = saveOrder;
        _prepareMail = prepareMail;
        Order = CreateDraft(original);
        InitializeComponent();
        DataContext = Order;
        CmbMaterialStatus.ItemsSource = materialStatusOptions;
        Title = $"Ordine {Order.QuoteNumber}";
        Helpers.WindowResizeBehavior.PreventMaximizedState(this);
        DataObject.AddPastingHandler(OrderDate, (_, _) => _invalidDates.Remove(OrderDate));
        DataObject.AddPastingHandler(DeliveryDate, (_, _) => _invalidDates.Remove(DeliveryDate));
        _documentBusyDescriptor.AddValueChanged(CommonQuoteActions, OnDocumentBusyChanged);
        Closing += OnClosing;
        Closed += OnClosed;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private static QuoteHistorySummary CreateDraft(QuoteHistorySummary original) => new()
    {
        QuoteNumber = original.QuoteNumber,
        Date = original.Date,
        CustomerName = original.CustomerName,
        ReferenceName = original.ReferenceName,
        SiteName = original.SiteName,
        BillingCustomerName = original.BillingCustomerName,
        PdfPath = original.PdfPath,
        Total = original.Total,
        IvaType = original.IvaType,
        MaterialDiscount = original.MaterialDiscount,
        LaborDiscount = original.LaborDiscount,
        Status = original.Status,
        Notes = original.Notes,
        SyncStatus = original.SyncStatus,
        IsJointVenture = original.IsJointVenture,
        PartnerCompanyName = original.PartnerCompanyName,
        CreatedByDevice = original.CreatedByDevice,
        LastModifiedByDevice = original.LastModifiedByDevice,
        SentAtUtc = original.SentAtUtc,
        SentMethod = original.SentMethod,
        SentRecipient = original.SentRecipient,
        SentByDevice = original.SentByDevice,
        LastReminderAtUtc = original.LastReminderAtUtc,
        ReminderCount = original.ReminderCount,
        LastReminderByDevice = original.LastReminderByDevice,
        SupplierName = original.SupplierName,
        MaterialsOrderedByCustomer = original.MaterialsOrderedByCustomer,
        MaterialOrderDate = original.MaterialOrderDate,
        ExpectedDeliveryDate = original.ExpectedDeliveryDate,
        MaterialStatus = original.MaterialStatus
    };

    private bool CommitPendingEdits()
    {
        try
        {
            if (Keyboard.FocusedElement is TextBox focusedTextBox)
                focusedTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            TxtSupplier.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            CmbMaterialStatus.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();

            // Read both values before applying either, so an invalid date cannot partially commit the pair.
            var orderDate = ReadOptionalDate(OrderDate, "Data ordine");
            var deliveryDate = ReadOptionalDate(DeliveryDate, "Consegna prevista");
            OrderDate.SelectedDate = Order.MaterialOrderDate = orderDate;
            DeliveryDate.SelectedDate = Order.ExpectedDeliveryDate = deliveryDate;
            if (Order.MaterialsOrderedByCustomer)
                SupplierOrderAssignmentService.ApplyCustomerOrderChoice(Order, orderedByCustomer: true);
            ErrorPanel.Visibility = Visibility.Collapsed;
            return true;
        }
        catch (InvalidOperationException ex)
        {
            ShowError(ex.Message);
            return false;
        }
    }

    private DateTime? ReadOptionalDate(DatePicker picker, string label)
    {
        if (_invalidDates.TryGetValue(picker, out var invalid))
            throw new InvalidOperationException($"{label}: «{invalid}» non è una data valida. Usa il formato gg/mm/aaaa oppure lascia il campo vuoto.");

        picker.ApplyTemplate();
        string text = picker.Template.FindName("PART_TextBox", picker) is DatePickerTextBox editor
            ? editor.Text
            : picker.Text;
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!DateTime.TryParse(text, CultureInfo.GetCultureInfo("it-IT"), DateTimeStyles.None, out var date))
            throw new InvalidOperationException($"{label}: inserisci una data valida nel formato gg/mm/aaaa oppure lascia il campo vuoto.");
        return date.Date;
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_busy || CommonQuoteActions.IsBusy || !CommitPendingEdits()) return;
        try
        {
            SetBusy(true, "Salvataggio dell’ordine…");
            if (await _saveOrder(Order))
            {
                SetBusy(false);
                DialogResult = true;
            }
            else
                ShowError("L’ordine non è stato salvato. Puoi correggere le informazioni e riprovare.");
        }
        catch (Exception ex)
        {
            ShowError($"Salvataggio dell’ordine non riuscito. {ex.Message}");
        }
        finally
        {
            if (!_closed) SetBusy(false);
        }
    }

    private async void OnPrepareOrderMailClick(object sender, RoutedEventArgs e)
    {
        if (_busy || CommonQuoteActions.IsBusy || !CommitPendingEdits()) return;
        try
        {
            SetBusy(true, "Preparazione dell’email…");
            await _prepareMail(Order, this);
        }
        catch (Exception ex)
        {
            ShowError($"Preparazione dell’email non riuscita. {ex.Message}");
        }
        finally
        {
            if (!_closed) SetBusy(false);
        }
    }

    private void OnSelectSupplierClick(object sender, RoutedEventArgs e)
    {
        if (_busy || CommonQuoteActions.IsBusy) return;
        var selector = new SelectCustomerWindow(_vm, suppliersOnly: true)
        {
            Owner = this,
            Title = "Seleziona fornitore"
        };
        if (selector.ShowDialog() != true || selector.SelectedResult == null) return;
        Order.MaterialsOrderedByCustomer = false;
        Order.SupplierName = selector.SelectedResult.BusinessName;
    }

    private void OnMaterialsOrderedByCustomerClick(object sender, RoutedEventArgs e)
        => SupplierOrderAssignmentService.ApplyCustomerOrderChoice(Order, ChkOrderedByCustomer.IsChecked == true);

    private void SetBusy(bool busy, string message = "")
    {
        _busy = busy;
        bool interactionBusy = busy || CommonQuoteActions.IsBusy;
        EditableFields.IsEnabled = !interactionBusy;
        BtnSave.IsEnabled = BtnPrepareMail.IsEnabled = BtnCancel.IsEnabled = !interactionBusy;
        CommonQuoteActions.IsEnabled = !busy;
        TxtBusy.Text = busy ? message : CommonQuoteActions.IsBusy ? "Preparazione dei documenti…" : string.Empty;
        TxtBusy.Visibility = interactionBusy ? Visibility.Visible : Visibility.Collapsed;
        Cursor = interactionBusy ? Cursors.Wait : null;
        if (busy) ErrorPanel.Visibility = Visibility.Collapsed;
    }

    private void OnDocumentBusyChanged(object? sender, EventArgs e)
    {
        if (!_closed) SetBusy(_busy, TxtBusy.Text);
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
    }

    private void OnDateEdited(object sender, TextCompositionEventArgs e)
    {
        if (sender is DatePicker picker) _invalidDates.Remove(picker);
    }

    private void OnDateKeyDown(object sender, KeyEventArgs e)
    {
        if ((e.Key is Key.Back or Key.Delete) && sender is DatePicker picker) _invalidDates.Remove(picker);
    }

    private void OnCalendarClosed(object sender, RoutedEventArgs e)
    {
        if (sender is DatePicker { SelectedDate: not null } picker) _invalidDates.Remove(picker);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _busy || CommonQuoteActions.IsBusy) return;
        e.Handled = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (!_busy && !CommonQuoteActions.IsBusy) Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_busy || CommonQuoteActions.IsBusy) e.Cancel = true;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        _documentBusyDescriptor.RemoveValueChanged(CommonQuoteActions, OnDocumentBusyChanged);
    }
}
