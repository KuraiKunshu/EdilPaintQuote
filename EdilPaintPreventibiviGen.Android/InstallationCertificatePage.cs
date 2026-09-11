using EdilPaintPreventibiviGen.Android.Controls;
using EdilPaintPreventibiviGen.Android.Models;
using EdilPaintPreventibiviGen.Android.Services;

namespace EdilPaintPreventibiviGen.Android;

public sealed class InstallationCertificatePage : OperationPage
{
    public InstallationCertificatePage(string connectionString, QuoteDetail quote) : base(connectionString, "Certificato di posa")
    {
        var site = Input(quote.SiteName);
        var date = new DatePicker { Date = DateTime.Today, MaximumDate = DateTime.Today };
        Form.Add(Heading(quote.CustomerName));
        Form.Add(Field("Cantiere", site));
        Form.Add(Field("Data completamento lavori", date));
        Form.Add(Action("Genera e condividi PDF", async () =>
        {
            if (string.IsNullOrWhiteSpace(site.Text) || quote.Materials.Count == 0)
                throw new InvalidOperationException("Indica il cantiere e almeno un materiale nel preventivo.");
            if (!await DisplayAlertAsync("Certificato di posa", "Confermi il completamento dei lavori e la posa a regola d'arte dei materiali elencati?", "Conferma", "Annulla")) return;
            var company = await Database.GetCompanyContactAsync(ConnectionString);
            var lines = MobileDocumentContent.InstallationCertificate(quote, company, site.Text.Trim(), date.Date ?? DateTime.Today);
            string path = await MobileDocumentService.CreateAsync("Certificato-" + quote.QuoteNumber, lines);
            await Share.Default.RequestAsync(new ShareFileRequest("Certificato di posa", new ShareFile(path, "application/pdf")));
        }));
    }
}
