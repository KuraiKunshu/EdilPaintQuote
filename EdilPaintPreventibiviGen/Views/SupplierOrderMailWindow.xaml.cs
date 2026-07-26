using System.Windows;
using System.Windows.Input;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;

namespace EdilPaintPreventibiviGen.Views;

public partial class SupplierOrderMailWindow : Window
{
    private readonly QuoteHistoryEntry _quote;
    private bool _isProcessing;

    public bool WasRegisteredAsSent { get; private set; }
    public DateTime RegisteredAtUtc { get; private set; }
    public SupplierOrderMailDraft? RegisteredDraft { get; private set; }

    public SupplierOrderMailWindow(
        QuoteHistoryEntry quote,
        SupplierOrderMailDraft draft)
    {
        InitializeComponent();
        EdilPaintPreventibiviGen.Helpers.WindowResizeBehavior.PreventMaximizedState(this);
        _quote = quote;

        TxtTitle.Text = $"Ordine materiale - {quote.QuoteNumber}";
        TxtSubtitle.Text = string.IsNullOrWhiteSpace(quote.ReferenceName)
            ? quote.CustomerName
            : $"{quote.CustomerName} - Rif. {quote.ReferenceName}";
        TxtRecipient.Text = draft.Recipient;
        TxtCcRecipients.Text = draft.CcRecipients;
        TxtSubject.Text = draft.Subject;
        TxtBody.Text = draft.Body;

        bool smtpEnabled = App.AppSettings.Mail.Enabled;
        BtnSend.Content = smtpEnabled ? "Invia email" : "Apri e registra";
        TxtActionHint.Text = smtpEnabled
            ? "Invia direttamente tramite SMTP"
            : "Apre il client email e registra l'ordine";
    }

    private async void OnOpenEmailClick(object sender, RoutedEventArgs e)
    {
        if (_isProcessing)
            return;

        var recipients = EmailAddressParser.ExtractEmails(TxtRecipient.Text);
        var copies = EmailAddressParser.ExtractEmails(TxtCcRecipients.Text);
        if (recipients.Count == 0)
        {
            MessageBox.Show(
                "Inserisci un destinatario email valido.",
                "Ordine materiale",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            TxtRecipient.Focus();
            return;
        }

        string normalizedPrimary = recipients[0];
        var normalizedCopies = recipients
            .Skip(1)
            .Concat(copies)
            .Where(x => !string.Equals(x, normalizedPrimary, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        TxtRecipient.Text = normalizedPrimary;
        TxtCcRecipients.Text = EmailAddressParser.Join(normalizedCopies);

        var draft = SupplierOrderMailService.CreateDraft(
            normalizedPrimary,
            TxtSubject.Text,
            TxtBody.Text,
            EmailAddressParser.Join(normalizedCopies));

        try
        {
            _isProcessing = true;
            BtnSend.IsEnabled = false;
            Mouse.OverrideCursor = Cursors.Wait;

            DateTime registeredAtUtc;
            string successMessage;
            if (App.AppSettings.Mail.Enabled)
            {
                var service = new SmtpEmailService(App.AppSettings.Mail);
                var result = await service.SendAsync(new SmtpEmailRequest
                {
                    Recipient = draft.Recipient,
                    CcRecipients = draft.CcRecipients,
                    Subject = draft.Subject,
                    Body = draft.Body
                });
                registeredAtUtc = result.AcceptedAtUtc;
                successMessage = "Ordine inviato e registrato.";
            }
            else
            {
                SupplierOrderMailService.OpenDraft(draft);
                registeredAtUtc = DateTime.UtcNow;
                successMessage = "Ordine aperto nel client email e registrato.";
            }

            WasRegisteredAsSent = true;
            RegisteredAtUtc = registeredAtUtc;
            RegisteredDraft = draft;

            Mouse.OverrideCursor = null;
            if (ChkPrintAfterSend.IsChecked == true)
            {
                try
                {
                    SupplierOrderMailPrintService.Print(_quote, draft, registeredAtUtc);
                }
                catch (Exception printException)
                {
                    MessageBox.Show(
                        $"L'email e stata registrata, ma non e stato possibile stampare la copia.\n\n{printException.Message}",
                        "Stampa ordine",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }

            MessageBox.Show(
                successMessage,
                "Ordine materiale",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Invio dell'ordine non riuscito.\n\n{ex.Message}",
                "Ordine materiale",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            _isProcessing = false;
            BtnSend.IsEnabled = true;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
