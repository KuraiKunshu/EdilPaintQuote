using EdilPaintPreventibiviGen.Android.Controls;
using EdilPaintPreventibiviGen.Android.Models;
using EdilPaintPreventibiviGen.Android.Services;
using MailKit.Security;
using MimeKit;

namespace EdilPaintPreventibiviGen.Android;

public sealed class MailComposerPage : OperationPage
{
    private readonly QuoteDetail _quote;
    private readonly bool _order;
    private readonly bool _reminder;
    private readonly Entry _to = Input();
    private readonly Entry _cc = Input();
    private readonly Entry _subject = Input();
    private readonly Editor _body = new() { MinimumHeightRequest = 220, AutoSize = EditorAutoSizeOption.TextChanges };
    private readonly Label _state = new() { FontSize = 13 };
    private string _copy = "";
    private MobileSettings _settings = new();
    private bool _loaded;
    private bool _sent;
    private bool _registered;
    private bool _sendAttempted;
    private string? _pdf;

    public MailComposerPage(string connectionString, QuoteDetail quote, bool order = false, bool reminder = false)
        : base(connectionString, order ? "Email ordine" : reminder ? "Sollecito" : "Invia preventivo")
    {
        _quote = quote;
        _order = order;
        _reminder = reminder;
        Form.Add(Field("Destinatario", _to));
        Form.Add(Field("In copia", _cc));
        Form.Add(Field("Oggetto", _subject));
        Form.Add(Field("Messaggio", _body));
        Form.Add(_state);
        Form.Add(Action("Invia email", SendAsync));
        Form.Add(Action("Apri nell'app email", async () =>
        {
            EnsureReady();
            if (!Email.Default.IsComposeSupported) throw new InvalidOperationException("Nessuna app email disponibile. Configura l'invio SMTP.");
            var message = new EmailMessage
            {
                To = Addresses(_to.Text), Cc = Copies(), Subject = _subject.Text, Body = _body.Text, BodyFormat = EmailBodyFormat.PlainText
            };
            if (!_order) message.Attachments = [new EmailAttachment(await PdfAsync())];
            await Email.Default.ComposeAsync(message);
            _state.Text = "Email aperta. Dopo l'invio, registra l'operazione con il pulsante qui sotto.";
        }, true));
        Form.Add(Action(order ? "Registra ordine inviato" : reminder ? "Registra sollecito inviato" : "Registra invio effettuato", async () =>
        {
            EnsureReady();
            if (!await DisplayAlertAsync("Conferma invio", "Confermi di aver inviato questa email?", "Conferma", "Annulla")) return;
            await RegisterAsync();
        }, true));
        Form.Add(Action("Configura email", async () => await Navigation.PushAsync(new MobileSettingsPage(ConnectionString)), true));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RunAsync(async () =>
        {
            _settings = await MobileSettings.LoadAsync();
            CompanyContact company = await Database.GetCompanyContactAsync(ConnectionString);
            _copy = string.IsNullOrWhiteSpace(_settings.SenderEmail) ? company.Email : _settings.SenderEmail;
            if (_loaded) return;
            if (_order)
            {
                if (_quote.MaterialsOrderedByCustomer) throw new InvalidOperationException("Ordine a carico del cliente.");
                var suppliers = await Database.GetSuppliersAsync(ConnectionString);
                _to.Text = suppliers.FirstOrDefault(x => x.BusinessName.Equals(_quote.SupplierName, StringComparison.OrdinalIgnoreCase))?.Email ?? "";
            }
            else _to.Text = !string.IsNullOrWhiteSpace(_quote.CustomerEmail) ? _quote.CustomerEmail : _quote.ReferenceEmail;
            _cc.Text = _copy;
            _subject.Text = FormatTemplate(_order ? _settings.OrderSubject : _settings.QuoteSubject);
            _body.Text = _reminder ? $"Buongiorno,\n\nchiediamo un riscontro sul preventivo {_quote.QuoteNumber}.\n\nCordiali saluti" : FormatTemplate(_order ? _settings.OrderBody : _settings.QuoteBody);
            _state.Text = _order ? "Copia all'azienda inclusa" : "PDF del preventivo allegato";
            _loaded = true;
        });
    }

    private void EnsureReady()
    {
        if (!_loaded) throw new InvalidOperationException("Caricamento email non completato.");
        if (Addresses(_to.Text).Count == 0) throw new InvalidOperationException("Inserisci il destinatario.");
        if (_order && string.IsNullOrWhiteSpace(_copy)) throw new InvalidOperationException("Configura l'email mittente per ricevere la copia dell'ordine.");
    }

    private async Task SendAsync()
    {
        EnsureReady();
        if (_sent || _sendAttempted) throw new InvalidOperationException("Invio gia' eseguito o con esito da verificare. Controlla la posta prima di ripetere l'invio.");
        if (string.IsNullOrWhiteSpace(_settings.Username) || string.IsNullOrWhiteSpace(_settings.Password))
            throw new InvalidOperationException("Configura prima utente e password SMTP.");
        if (!await DisplayAlertAsync("Invia email", $"Inviare a {_to.Text}?", "Invia", "Annulla")) return;
        using var message = new MimeMessage();
        var from = MailboxAddress.Parse(string.IsNullOrWhiteSpace(_settings.SenderEmail) ? _settings.Username : _settings.SenderEmail);
        from.Name = _settings.SenderName;
        message.From.Add(from);
        foreach (string address in Addresses(_to.Text)) message.To.Add(MailboxAddress.Parse(address));
        foreach (string address in Copies()) message.Cc.Add(MailboxAddress.Parse(address));
        message.Subject = _subject.Text ?? "";
        var builder = new BodyBuilder { TextBody = _body.Text ?? "" };
        if (!_order) builder.Attachments.Add(await PdfAsync());
        message.Body = builder.ToMessageBody();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var client = new MailKit.Net.Smtp.SmtpClient();
        await client.ConnectAsync(_settings.SmtpServer, _settings.Port,
            _settings.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, timeout.Token);
        await client.AuthenticateAsync(_settings.Username, _settings.Password, timeout.Token);
        _sendAttempted = true;
        try { await client.SendAsync(message, timeout.Token); }
        catch
        {
            _state.Text = "Esito invio da verificare nella posta. Nessun nuovo invio automatico.";
            throw;
        }
        _sent = true;
        _state.Text = "Email inviata. Registrazione in corso...";
        try { await RegisterAsync(); }
        catch
        {
            _state.Text = "Email inviata, registrazione non riuscita. Usa Registra invio senza reinviare l'email.";
            throw;
        }
    }

    private async Task RegisterAsync()
    {
        if (_registered) return;
        QuoteDetail latest = await Database.GetQuoteAsync(ConnectionString, _quote.QuoteNumber);
        if (latest.Revision != _quote.Revision && !await DisplayAlertAsync("Preventivo modificato",
            "Il preventivo e' cambiato da quando hai preparato l'email. Registrare comunque l'invio effettuato?", "Registra", "Annulla")) return;
        if (_order)
        {
            if (latest.MaterialsOrderedByCustomer || !string.Equals(latest.SupplierName, _quote.SupplierName, StringComparison.Ordinal))
                throw new InvalidOperationException("L'assegnazione dell'ordine e' cambiata: verifica l'invio e aggiorna l'ordine manualmente.");
            await Database.RegisterSupplierOrderSentAsync(ConnectionString, _quote.QuoteNumber, latest.Revision);
        }
        else if (_reminder) await Database.RegisterQuoteReminderAsync(ConnectionString, _quote.QuoteNumber, latest.Revision);
        else await Database.RegisterQuoteSentAsync(ConnectionString, _quote.QuoteNumber, string.Join("; ", Addresses(_to.Text).Concat(Copies())), latest.Revision);
        _registered = true;
        _state.Text = "Invio registrato";
    }

    private async Task<string> PdfAsync() => _pdf ??= await MobileDocumentService.CreateAsync("Preventivo-" + _quote.QuoteNumber,
        MobileDocumentContent.Quote(_quote, await Database.GetCompanyContactAsync(ConnectionString)));

    private List<string> Copies() => Addresses(_cc.Text).Concat(_order ? Addresses(_copy) : [])
        .Except(Addresses(_to.Text), StringComparer.OrdinalIgnoreCase).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static List<string> Addresses(string? value) => string.IsNullOrWhiteSpace(value) ? [] :
        InternetAddressList.Parse(value.Replace(';', ',')).Mailboxes.Select(x => x.Address).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private string FormatTemplate(string template) => template
        .Replace("{QuoteNumber}", _quote.QuoteNumber, StringComparison.OrdinalIgnoreCase)
        .Replace("{OrderReference}", string.IsNullOrWhiteSpace(_quote.ReferenceName) ? _quote.CustomerName : _quote.ReferenceName, StringComparison.OrdinalIgnoreCase)
        .Replace("{CustomerName}", _quote.CustomerName, StringComparison.OrdinalIgnoreCase)
        .Replace("{ReferenceName}", _quote.ReferenceName, StringComparison.OrdinalIgnoreCase)
        .Replace("{SupplierName}", _quote.SupplierName, StringComparison.OrdinalIgnoreCase)
        .Replace("{Date}", _quote.Date.ToString("dd/MM/yyyy"), StringComparison.OrdinalIgnoreCase)
        .Replace("{Total}", _quote.TotalDisplay, StringComparison.OrdinalIgnoreCase)
        .Replace("{Materials}", MobileMailService.BuildMaterialsList(_quote.Materials), StringComparison.OrdinalIgnoreCase);
}
