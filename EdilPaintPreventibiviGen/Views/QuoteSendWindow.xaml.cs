using System.Windows;
using System.Windows.Controls;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;

namespace EdilPaintPreventibiviGen.Views;

public partial class QuoteSendWindow : Window
{
    public QuoteSendInfo Result { get; private set; } = new();

    public QuoteSendWindow(QuoteHistorySummary summary)
    {
        InitializeComponent();
        TxtRecipient.Text = summary.SentRecipient;
        DpSentDate.SelectedDate = summary.SentAtUtc?.ToLocalTime().Date ?? DateTime.Today;
        if (!string.IsNullOrWhiteSpace(summary.SentMethod))
            CboMethod.Text = summary.SentMethod;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        DateTime date = DpSentDate.SelectedDate ?? DateTime.Today;
        string method = CboMethod.Text.Trim();
        if (string.IsNullOrWhiteSpace(method) && CboMethod.SelectedItem is ComboBoxItem item)
            method = item.Content?.ToString() ?? string.Empty;

        Result = new QuoteSendInfo
        {
            SentAtUtc = date.ToUniversalTime(),
            Method = string.IsNullOrWhiteSpace(method) ? "Invio" : method,
            Recipient = TxtRecipient.Text.Trim(),
            DeviceName = DeviceNameService.GetCurrentDeviceName()
        };

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
