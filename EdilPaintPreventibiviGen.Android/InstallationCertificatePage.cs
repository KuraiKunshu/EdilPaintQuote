using EdilPaintPreventibiviGen.Android.Controls;
using EdilPaintPreventibiviGen.Android.Models;
using EdilPaintPreventibiviGen.Android.Services;

namespace EdilPaintPreventibiviGen.Android;

public sealed class InstallationCertificatePage : OperationPage
{
    public InstallationCertificatePage(string connectionString, QuoteDetail quote) : base(connectionString, "Certificato di posa")
    {
        var recipients = new List<(string Label, string Name, string Address)>();
        if (!string.IsNullOrWhiteSpace(quote.CustomerName))
            recipients.Add(($"Cliente · {quote.CustomerName.Trim()}", quote.CustomerName.Trim(), quote.CustomerAddress));
        if (!string.IsNullOrWhiteSpace(quote.ReferenceName))
            recipients.Add(($"Riferimento · {quote.ReferenceName.Trim()}", quote.ReferenceName.Trim(), quote.ReferenceAddress));

        var recipient = new Picker { ItemsSource = recipients.Select(option => option.Label).ToList() };
        var address = Input();
        recipient.SelectedIndexChanged += (_, _) =>
            address.Text = recipient.SelectedIndex >= 0 ? recipients[recipient.SelectedIndex].Address : string.Empty;
        if (recipients.Count > 0) recipient.SelectedIndex = 0;
        var site = Input(quote.SiteName);
        var date = new DatePicker { Date = DateTime.Today, MaximumDate = DateTime.Today };
        Form.Add(Heading($"Certificato preventivo n. {quote.QuoteNumber}"));
        Form.Add(Field("Intestatario del certificato", recipient));
        Form.Add(Field("Indirizzo intestatario", address));
        Form.Add(Field("Indirizzo cantiere, se diverso", site));
        Form.Add(Field("Data completamento lavori", date));
        Form.Add(Action("Genera e condividi PDF", async () =>
        {
            if (recipient.SelectedIndex < 0)
                throw new InvalidOperationException("Seleziona l’intestatario del certificato.");
            if (quote.Materials.Count == 0)
                throw new InvalidOperationException("Aggiungi almeno un materiale al preventivo.");
            string recipientAddress = address.Text?.Trim() ?? string.Empty;
            string workSite = site.Text?.Trim() ?? string.Empty;
            if (recipientAddress.Length == 0 && workSite.Length == 0)
                throw new InvalidOperationException("Indica l’indirizzo dell’intestatario o del cantiere.");
            string recipientName = recipients[recipient.SelectedIndex].Name;
            if (!await DisplayAlertAsync("Certificato di posa", "Confermi il completamento dei lavori e la posa a regola d'arte dei materiali elencati?", "Conferma", "Annulla")) return;
            var company = await Database.GetCompanyContactAsync(ConnectionString);
            var lines = MobileDocumentContent.InstallationCertificate(quote, company, recipientName, recipientAddress, workSite, date.Date ?? DateTime.Today);
            string path = await MobileDocumentService.CreateAsync("Certificato-" + quote.QuoteNumber, lines);
            await Share.Default.RequestAsync(new ShareFileRequest("Certificato di posa", new ShareFile(path, "application/pdf")));
        }));
    }
}
