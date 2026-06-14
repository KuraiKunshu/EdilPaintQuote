using System.Windows;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;

namespace EdilPaintPreventibiviGen.Views;

public partial class QuoteSendWindow : Window
{
    public QuoteSendInfo Result { get; private set; } = new();
    public string PrimaryEmailRecipient => TxtRecipient.Text.Trim();
    public string EmailCcRecipients => TxtCcRecipients.Text.Trim();
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
        EdilPaintPreventibiviGen.Helpers.WindowResizeBehavior.PreventMaximizedState(this);
        TxtQuoteTitle.Text = $"Preventivo n. {summary.QuoteNumber}";
        TxtQuoteSubtitle.Text = string.IsNullOrWhiteSpace(summary.ReferenceName)
            ? summary.CustomerName
            : $"{summary.CustomerName} - Rif. {summary.ReferenceName}";
        string rawRecipients = string.IsNullOrWhiteSpace(summary.SentRecipient)
            ? defaultRecipient
            : summary.SentRecipient;
        var recipientSplit = EmailAddressParser.SplitPrimaryAndCopies(rawRecipients);
        TxtRecipient.Text = string.IsNullOrWhiteSpace(recipientSplit.PrimaryRecipient)
            ? rawRecipients.Trim()
            : recipientSplit.PrimaryRecipient;
        TxtCcRecipients.Text = EmailAddressParser.Join(recipientSplit.CopyRecipients);
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
        var recipients = EmailAddressParser.ExtractEmails(TxtRecipient.Text);
        var copyRecipients = EmailAddressParser.ExtractEmails(TxtCcRecipients.Text);
        if (recipients.Count == 0)
        {
            MessageBox.Show(
                "Inserisci un destinatario email valido prima di procedere.",
                "Invio preventivo",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            TxtRecipient.Focus();
            return;
        }

        string primaryRecipient = recipients[0];
        var normalizedCopies = recipients
            .Skip(1)
            .Concat(copyRecipients)
            .Where(x => !string.Equals(x, primaryRecipient, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        TxtRecipient.Text = primaryRecipient;
        TxtCcRecipients.Text = EmailAddressParser.Join(normalizedCopies);

        Result = new QuoteSendInfo
        {
            SentAtUtc = DateTime.UtcNow,
            Method = "Email",
            Recipient = EmailAddressParser.Join(new[] { primaryRecipient }.Concat(normalizedCopies)),
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
