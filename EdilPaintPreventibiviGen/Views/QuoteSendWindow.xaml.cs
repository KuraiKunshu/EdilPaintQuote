using System.Windows;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;

namespace EdilPaintPreventibiviGen.Views;

public partial class QuoteSendWindow : Window
{
    public QuoteSendInfo Result { get; private set; } = new();
    public string EmailSubject => TxtSubject.Text.Trim();
    public string EmailBody => TxtBody.Text;
    public bool ShouldOpenWhatsApp => ChkSendWhatsApp.IsChecked == true;
    public string WhatsAppPhone => TxtWhatsAppPhone.Text.Trim();
    public string WhatsAppMessage => TxtWhatsAppMessage.Text.Trim();

    public QuoteSendWindow(
        QuoteHistorySummary summary,
        string defaultRecipient = "",
        string defaultSubject = "",
        string defaultBody = "",
        string defaultWhatsAppPhone = "",
        string defaultWhatsAppMessage = "")
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
        TxtWhatsAppPhone.Text = defaultWhatsAppPhone;
        TxtWhatsAppMessage.Text = string.IsNullOrWhiteSpace(defaultWhatsAppMessage)
            ? BuildDefaultWhatsAppMessage(summary)
            : defaultWhatsAppMessage;

        UpdateMethodUi();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtRecipient.Text))
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
            SentAtUtc = DateTime.UtcNow,
            Method = "Email",
            Recipient = TxtRecipient.Text.Trim(),
            DeviceName = DeviceNameService.GetCurrentDeviceName()
        };

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void UpdateMethodUi()
    {
        EmailFieldsPanel.Visibility = Visibility.Visible;
        BtnSave.Content = App.AppSettings.Mail.Enabled
            ? "Invia email"
            : "Salva invio";
        TxtActionHint.Text = App.AppSettings.Mail.Enabled
            ? "Allega il PDF e invia tramite SMTP"
            : "Registra l'invio nello storico";
    }

    private static string BuildDefaultWhatsAppMessage(QuoteHistorySummary summary) =>
        $"Buongiorno, abbiamo inviato via email il preventivo n. {summary.QuoteNumber}. Cordiali saluti.";
}
