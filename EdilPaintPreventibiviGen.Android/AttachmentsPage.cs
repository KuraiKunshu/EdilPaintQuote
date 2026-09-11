using EdilPaintPreventibiviGen.Android.Controls;
using EdilPaintPreventibiviGen.Android.Models;
using EdilPaintPreventibiviGen.Android.Services;

namespace EdilPaintPreventibiviGen.Android;

public sealed class AttachmentsPage : OperationPage
{
    private readonly QuoteDetail _quote;
    private long _revision;
    private readonly VerticalStackLayout _list = new() { Spacing = 8 };

    public AttachmentsPage(string connectionString, QuoteDetail quote) : base(connectionString, "Allegati")
    {
        _quote = quote;
        _revision = quote.Revision;
        Form.Add(Heading(quote.CustomerName));
        Form.Add(Action("Aggiungi PDF o immagine", AddAsync));
        Form.Add(Action("Aggiorna", LoadAsync, true));
        Form.Add(_list);
    }

    protected override async void OnAppearing() { base.OnAppearing(); await RunAsync(LoadAsync); }

    private async Task LoadAsync()
    {
        var quote = await Database.GetQuoteAsync(ConnectionString, _quote.QuoteNumber);
        var items = await Database.GetAttachmentsAsync(ConnectionString, _quote.Id);
        _revision = quote.Revision;
        _list.Clear();
        if (items.Count == 0) _list.Add(new Label { Text = "Nessun allegato" });
        foreach (var item in items)
            _list.Add(Action($"{item.FileName}\n{item.Size / 1024d:0.#} KB - {item.ImportedAtUtc.ToLocalTime():dd/MM/yyyy}", async () =>
            {
                string? choice = await DisplayActionSheetAsync(item.FileName, "Annulla", null, "Apri", "Condividi", "Elimina");
                if (choice == "Elimina")
                {
                    if (!await DisplayAlertAsync("Elimina allegato", item.FileName, "Elimina", "Annulla")) return;
                    _revision = await Database.DeleteAttachmentAsync(ConnectionString, _quote, _revision, item);
                    await LoadAsync();
                }
                else if (choice is "Apri" or "Condividi")
                {
                    string extension = Path.GetExtension(item.FileName).ToLowerInvariant();
                    if (extension is not (".pdf" or ".jpg" or ".jpeg" or ".png" or ".webp"))
                        throw new InvalidOperationException("Formato non supportato sul telefono.");
                    byte[] bytes = await Database.GetAttachmentContentAsync(ConnectionString, _quote.Id, item.Id);
                    Directory.CreateDirectory(MobileDocumentService.ExportDirectory);
                    string path = Path.Combine(MobileDocumentService.ExportDirectory, $"allegato-{Guid.NewGuid():N}{extension}");
                    await File.WriteAllBytesAsync(path, bytes);
                    if (choice == "Condividi") await Share.Default.RequestAsync(new ShareFileRequest(item.FileName, new ShareFile(path, item.ContentType)));
                    else if (!await Launcher.Default.OpenAsync(new OpenFileRequest(item.FileName, new ReadOnlyFile(path, item.ContentType))))
                        throw new InvalidOperationException("Nessuna app disponibile per questo allegato.");
                }
            }, true));
    }

    private async Task AddAsync()
    {
        FileResult? file = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Allega PDF o immagine",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>> { [DevicePlatform.Android] = ["application/pdf", "image/jpeg", "image/png", "image/webp"] })
        });
        if (file == null) return;
        string type = Path.GetExtension(file.FileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf", ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp",
            _ => throw new InvalidOperationException("Seleziona PDF, JPG, PNG o WebP.")
        };
        await using var input = await file.OpenReadAsync();
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(chunk)) > 0)
        {
            if (buffer.Length + count > 20 * 1024 * 1024) throw new InvalidOperationException("Dimensione massima: 20 MB.");
            buffer.Write(chunk, 0, count);
        }
        _revision = await Database.AddAttachmentAsync(ConnectionString, _quote, _revision, file.FileName, type, buffer.ToArray());
        await LoadAsync();
    }
}
