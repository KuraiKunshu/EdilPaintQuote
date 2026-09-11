#if MOBILE_SMOKE_TEST
using EdilPaintPreventibiviGen.Android.Controls;
using EdilPaintPreventibiviGen.Android.Models;
using EdilPaintPreventibiviGen.Android.Services;

namespace EdilPaintPreventibiviGen.Android.Diagnostics;

public sealed class SmokeTestPage : OperationPage
{
    public SmokeTestPage() : base("Host=127.0.0.1;Database=smoketest;Username=smoketest;Password=not-a-secret;Timeout=2", "Test locale")
    {
        var quote = new QuoteDetail
        {
            Id = 1, QuoteNumber = "TEST-001", Date = DateTime.Today,
            CustomerName = "Cliente di prova con ragione sociale molto lunga",
            CustomerAddress = "Via di prova 12", ReferenceName = "Riferimento cantiere di prova",
            SiteName = "Cantiere di prova", Status = QuoteStatus.Confermato,
            MaterialStatus = "ORDINATO", MaterialOrderDate = DateTime.Today,
            CustomerNotes = "Nota pubblica di prova", Notes = "Nota interna riservata",
            PaymentTerms = "Acconto 30%, saldo a fine lavori", IvaType = "22%", Imponibile = 1850, Total = 2257,
            Materials = [new() { Name = "GGL MK04 finestra per tetti", Description = "Dimensioni 78 x 98 cm. Finitura bianca.", Quantity = 3, UnitPrice = 500 }],
            Labors = [new() { Name = "Posa in opera e finitura interna", Quantity = 2, UnitPrice = 175 }],
            Events = [new() { Description = "Preventivo creato per test", CreatedAtUtc = DateTime.UtcNow }]
        };
        Form.Add(Action("Dettaglio", async () => await Navigation.PushAsync(new QuoteDetailPage(ConnectionString, quote))));
        Form.Add(Action("Guadagno reale", async () => await Navigation.PushAsync(new RealProfitPage(ConnectionString, quote))));
        Form.Add(Action("Collaborazione", async () => await Navigation.PushAsync(new CollaborationPage(ConnectionString, QuoteDraft.FromDetail(quote)))));
        Form.Add(Action("Catalogo", async () => await Navigation.PushAsync(new CatalogEditorPage(ConnectionString, QuoteLineKind.Material, new CatalogItem { Id = 1, Name = "Materiale test", UnitPrice = 100 }))));
        Form.Add(Action("Voce preventivo", async () => await Navigation.PushAsync(new QuoteLineEditorPage(ConnectionString, QuoteLineKind.Material, _ => { }, quote.Materials[0]))));
        Form.Add(Action("Impostazioni email", async () => await Navigation.PushAsync(new MobileSettingsPage(ConnectionString))));
        Form.Add(Action("Accesso", async () => await Navigation.PushAsync(new MainPage())));
        Form.Add(Action("Serializzazione", async () =>
        {
            var input = new RealProfitInput { QuoteRevenue = 1000, Workers = 2, Days = 1, HoursPerDay = 8, HourlyCost = 20 };
            var snapshot = new RealProfitSnapshot { Input = input, Result = RealProfitCalculator.Calculate(input) };
            string json = System.Text.Json.JsonSerializer.Serialize(snapshot);
            var copy = System.Text.Json.JsonSerializer.Deserialize<RealProfitSnapshot>(json)!;
            if (copy.Result.Profit != 680) throw new InvalidOperationException("Snapshot non valido");
            await DisplayAlertAsync("Snapshot verificato", "Calcolo e serializzazione corretti", "OK");
        }));
        Form.Add(Action("PDF di prova", async () =>
        {
            var lines = MobileDocumentContent.Quote(quote, new CompanyContact("EdilPaint", "test@example.com", "Indirizzo azienda", "00000000000")).ToList();
            lines.AddRange(Enumerable.Range(1, 80).Select(i => new DocumentLine($"Riga lunga di prova {i}: materiale con descrizione dettagliata per verificare impaginazione e passaggio alla pagina successiva.")));
            string path = await MobileDocumentService.CreateAsync("SmokeTest", lines);
            await DisplayAlertAsync("PDF creato", Path.GetFileName(path), "OK");
        }));
    }
}
#endif
