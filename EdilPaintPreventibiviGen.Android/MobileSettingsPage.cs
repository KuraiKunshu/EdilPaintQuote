using EdilPaintPreventibiviGen.Android.Controls;
using EdilPaintPreventibiviGen.Android.Services;

namespace EdilPaintPreventibiviGen.Android;

public sealed class MobileSettingsPage : OperationPage
{
    private bool _loaded;
    public MobileSettingsPage(string connectionString) : base(connectionString, "Impostazioni") { }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        await RunAsync(async () =>
        {
            MobileSettings settings = await MobileSettings.LoadAsync();
            var host = Input(settings.SmtpServer);
            var port = Input(settings.Port.ToString(), true);
            var username = Input(settings.Username);
            var password = Input(settings.Password);
            password.IsPassword = true;
            var sender = Input(settings.SenderEmail);
            var name = Input(settings.SenderName);
            var quoteSubject = Input(settings.QuoteSubject);
            var quoteBody = new Editor { Text = settings.QuoteBody, MinimumHeightRequest = 120, AutoSize = EditorAutoSizeOption.TextChanges };
            var orderSubject = Input(settings.OrderSubject);
            var orderBody = new Editor { Text = settings.OrderBody, MinimumHeightRequest = 120, AutoSize = EditorAutoSizeOption.TextChanges };
            Form.Add(Heading("Account di invio"));
            Form.Add(Field("Server SMTP", host));
            Form.Add(Field("Porta (465 TLS / 587 STARTTLS)", port));
            Form.Add(Field("Utente", username));
            Form.Add(Field("Password", password));
            Form.Add(Field("Email mittente e copia ordini", sender));
            Form.Add(Field("Nome mittente", name));
            Form.Add(Heading("Preventivi"));
            Form.Add(Field("Oggetto", quoteSubject));
            Form.Add(Field("Messaggio", quoteBody));
            Form.Add(Heading("Ordini"));
            Form.Add(Field("Oggetto", orderSubject));
            Form.Add(Field("Messaggio (include {Materials})", orderBody));
            var workers = Input(Format(settings.Workers), true);
            var days = Input(Format(settings.Days), true);
            var hours = Input(Format(settings.HoursPerDay), true);
            var hourly = Input(Format(settings.HourlyCost), true);
            var reduction = Input(Format(settings.ProfitReductionPercentage), true);
            Form.Add(Heading("Guadagno reale: valori iniziali"));
            Form.Add(Field("Operai", workers));
            Form.Add(Field("Giorni", days));
            Form.Add(Field("Ore al giorno", hours));
            Form.Add(Field("Costo orario", hourly));
            Form.Add(Field("Riduzione prudenziale (%)", reduction));
            Form.Add(Action("Salva impostazioni", async () =>
            {
                if (!(orderBody.Text ?? "").Contains("{Materials}", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Il testo ordini deve contenere {Materials}.");
                settings.SmtpServer = host.Text?.Trim() ?? "";
                settings.Port = WholeNumber(port, "Porta");
                if (settings.Port is < 1 or > 65535) throw new InvalidOperationException("Porta non valida.");
                settings.Username = username.Text?.Trim() ?? "";
                settings.Password = password.Text ?? "";
                settings.SenderEmail = sender.Text?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(settings.SenderEmail) && !System.Net.Mail.MailAddress.TryCreate(settings.SenderEmail, out _))
                    throw new InvalidOperationException("Email mittente non valida.");
                settings.SenderName = name.Text ?? "EdilPaint";
                settings.QuoteSubject = quoteSubject.Text ?? "";
                settings.QuoteBody = quoteBody.Text ?? "";
                settings.OrderSubject = orderSubject.Text ?? "";
                settings.OrderBody = orderBody.Text ?? "";
                settings.Workers = WholeNumber(workers, "Operai");
                settings.Days = Number(days, "Giorni");
                settings.HoursPerDay = Number(hours, "Ore", 24);
                settings.HourlyCost = Number(hourly, "Costo orario");
                settings.ProfitReductionPercentage = Number(reduction, "Riduzione", 100);
                await settings.SaveAsync();
                await Navigation.PopAsync();
            }));
            _loaded = true;
        });
    }
}
