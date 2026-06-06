using System.Windows;
using System.Windows.Controls;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;

namespace EdilPaintPreventibiviGen.Views;

public partial class QuoteSendWindow : Window
{
    public QuoteSendInfo Result { get; private set; } = new();
    public string EmailSubject => TxtSubject.Text.Trim();
    public string EmailBody => TxtBody.Text;

    public QuoteSendWindow(
        QuoteHistorySummary summary,
        string defaultRecipient = "",
        string defaultSubject = "",
        string defaultBody = "")
    {
        InitializeComponent();
        TxtQuoteTitle.Text = $"Preventivo n. {summary.QuoteNumber}";
        TxtQuoteSubtitle.Text = string.IsNullOrWhiteSpace(summary.ReferenceName)
            ? summary.CustomerName
            : $"{summary.CustomerName} - Rif. {summary.ReferenceName}";
        TxtRecipient.Text = string.IsNullOrWhiteSpace(summary.SentRecipient)
            ? defaultRecipient
            : summary.SentRecipient;
        TxtSubject.Text = defaultSubject;
        TxtBody.Text = defaultBody;
        DpSentDate.SelectedDate = summary.SentAtUtc?.ToLocalTime().Date ?? DateTime.Today;
        if (!string.IsNullOrWhiteSpace(summary.SentMethod))
            CboMethod.Text = summary.SentMethod;

        UpdateMethodUi();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        DateTime date = DpSentDate.SelectedDate ?? DateTime.Today;
        string method = CboMethod.Text.Trim();
        if (string.IsNullOrWhiteSpace(method) && CboMethod.SelectedItem is ComboBoxItem item)
            method = item.Content?.ToString() ?? string.Empty;

        if (IsEmailMethod(method) && string.IsNullOrWhiteSpace(TxtRecipient.Text))
        {
            MessageBox.Show(
                "Inserisci un destinatario email prima di procedere.",
                "Invio preventivo",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            TxtRecipient.Focus();
            return;
        }

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

    private void OnMethodChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        UpdateMethodUi();
    }

    private void OnMethodLostFocus(object sender, RoutedEventArgs e)
    {
        UpdateMethodUi();
    }

    private void UpdateMethodUi()
    {
        bool isEmail = IsEmailMethod(GetSelectedMethod());
        EmailFieldsPanel.Visibility = isEmail ? Visibility.Visible : Visibility.Collapsed;
        BtnSave.Content = isEmail && App.AppSettings.Mail.Enabled
            ? "Invia email"
            : "Salva invio";
        TxtActionHint.Text = isEmail && App.AppSettings.Mail.Enabled
            ? "Allega il PDF e invia tramite SMTP"
            : "Registra l'invio nello storico";
    }

    private string GetSelectedMethod()
    {
        string method = CboMethod.Text.Trim();
        if (string.IsNullOrWhiteSpace(method) && CboMethod.SelectedItem is ComboBoxItem item)
            method = item.Content?.ToString() ?? string.Empty;

        return method;
    }

    private static bool IsEmailMethod(string method) =>
        method.Equals("Email", StringComparison.OrdinalIgnoreCase) ||
        method.Equals("E-mail", StringComparison.OrdinalIgnoreCase);
}
