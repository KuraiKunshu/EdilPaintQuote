using EdilPaintPreventibiviGen.Android.Models;
using EdilPaintPreventibiviGen.Android.Services;

namespace EdilPaintPreventibiviGen.Android;

public partial class QuoteDetailPage : ContentPage
{
    private readonly string _connectionString;
    private QuoteDetail _detail;
    private readonly MobileDatabaseService _database = new();
    private bool _appeared;
    private bool _busy;

    public QuoteDetailPage(string connectionString, QuoteDetail detail)
    {
        InitializeComponent();
        _connectionString = connectionString;
        _detail = detail;
        Render();
    }

    private void Render()
    {
        BindingContext = _detail;
        CustomerNotesPanel.IsVisible = _detail.HasCustomerNotes;
        NotesPanel.IsVisible = _detail.HasNotes;
        OperationsInfo.Text = $"Ordine: {_detail.MaterialStatusDisplay}\n{_detail.SupplierDisplay}\nOrdinato: {_detail.MaterialOrderDateDisplay} | Consegna: {_detail.ExpectedDeliveryDateDisplay}\nGuadagno reale: {_detail.RealProfitDisplay}\nCollaborazione: {_detail.CollaborationDisplay}\nSolleciti: {_detail.ReminderDisplay}";
        EventsInfo.Text = string.Join("\n", _detail.Events.OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => $"{x.CreatedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm} - {x.Description}"));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_appeared) { _appeared = true; return; }
        await RunAsync(ReloadAsync);
    }

    private async Task ReloadAsync()
    {
        _detail = await _database.GetQuoteAsync(_connectionString, _detail.QuoteNumber);
        Render();
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        try { await action(); }
        catch (Exception ex) { await DisplayAlertAsync("Preventivo", MobileDatabaseService.GetUserMessage(ex), "OK"); }
        finally { _busy = false; }
    }

    private async void OnActionsClicked(object? sender, EventArgs e) => await RunAsync(async () =>
    {
        string? action = await DisplayActionSheetAsync("Preventivo " + _detail.QuoteNumber, "Annulla", null,
            "Apri PDF", "Condividi PDF", "Invia email", "Cambia stato", "Duplica", "Ordine materiali",
            "Guadagno reale", "Allegati", "Invia sollecito", "Costi collaborazione PDF", "Certificato di posa", "Elimina");
        switch (action)
        {
            case "Apri PDF":
            case "Condividi PDF":
                string pdf = await MobileDocumentService.CreateAsync("Preventivo-" + _detail.QuoteNumber,
                    MobileDocumentContent.Quote(_detail, await _database.GetCompanyContactAsync(_connectionString)));
                if (action == "Apri PDF")
                {
                    if (!await Launcher.Default.OpenAsync(new OpenFileRequest("Preventivo", new ReadOnlyFile(pdf, "application/pdf"))))
                        throw new InvalidOperationException("Installa un lettore PDF oppure usa Condividi PDF.");
                }
                else await Share.Default.RequestAsync(new ShareFileRequest("Preventivo", new ShareFile(pdf, "application/pdf")));
                break;
            case "Invia email": await Navigation.PushAsync(new MailComposerPage(_connectionString, _detail)); break;
            case "Invia sollecito": await Navigation.PushAsync(new MailComposerPage(_connectionString, _detail, reminder: true)); break;
            case "Ordine materiali": await Navigation.PushAsync(new SupplierOrderEditorPage(_connectionString, _detail.QuoteNumber)); break;
            case "Guadagno reale": await Navigation.PushAsync(new RealProfitPage(_connectionString, _detail)); break;
            case "Allegati": await Navigation.PushAsync(new AttachmentsPage(_connectionString, _detail)); break;
            case "Certificato di posa": await Navigation.PushAsync(new InstallationCertificatePage(_connectionString, _detail)); break;
            case "Duplica": await Navigation.PushAsync(new QuoteEditorPage(_connectionString, _detail, duplicate: true)); break;
            case "Cambia stato":
                var options = QuoteStatusOptions.Editable.ToList();
                string? status = await DisplayActionSheetAsync("Stato", "Annulla", null, options.Select(x => x.ToString()).ToArray());
                var selected = options.FirstOrDefault(x => x.ToString() == status);
                if (selected?.Value != null)
                {
                    await _database.UpdateQuoteStatusAsync(_connectionString, _detail.QuoteNumber, selected.Value.Value, _detail.Revision);
                    await ReloadAsync();
                }
                break;
            case "Costi collaborazione PDF":
                if (!_detail.IsJointVenture) throw new InvalidOperationException("Attiva la collaborazione in Modifica preventivo.");
                string costs = await MobileDocumentService.CreateAsync("Costi-" + _detail.QuoteNumber, MobileDocumentContent.Collaboration(_detail));
                await Share.Default.RequestAsync(new ShareFileRequest("Costi riservati", new ShareFile(costs, "application/pdf")));
                break;
            case "Elimina":
                if (await DisplayAlertAsync("Elimina preventivo", "Eliminare " + _detail.QuoteNumber + " da tutti i dispositivi?", "Elimina", "Annulla"))
                {
                    await _database.DeleteQuoteAsync(_connectionString, _detail.QuoteNumber, _detail.Revision);
                    await Navigation.PopToRootAsync();
                }
                break;
        }
    });

    private async void OnEditClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(new QuoteEditorPage(_connectionString, _detail));
}
