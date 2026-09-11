using EdilPaintPreventibiviGen.Android.Controls;
using EdilPaintPreventibiviGen.Android.Models;

namespace EdilPaintPreventibiviGen.Android;

public sealed class CatalogEditorPage : OperationPage
{
    public CatalogEditorPage(string connectionString, QuoteLineKind kind, CatalogItem? original = null)
        : base(connectionString, kind == QuoteLineKind.Material ? "Materiale" : "Lavorazione")
    {
        CatalogItem item = original?.Clone() ?? new();
        var name = Input(item.Name);
        var description = new Editor { Text = item.Description, AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 100 };
        var price = Input(Format(item.UnitPrice), true);
        var significant = new Switch { IsToggled = item.IsSignificant };
        var company = new Switch { IsToggled = item.IsCompanyMaterial };
        Form.Add(Heading(item.Id == 0 ? "Nuova voce" : item.Name));
        Form.Add(Field("Nome", name));
        Form.Add(Field("Descrizione", description));
        Form.Add(Field("Prezzo unitario", price));
        if (kind == QuoteLineKind.Material)
        {
            Form.Add(Toggle("Bene significativo", significant));
            Form.Add(Toggle("Materiale aziendale", company));
        }
        Form.Add(Action("Salva", async () =>
        {
            item.Name = name.Text ?? "";
            item.Description = description.Text ?? "";
            item.UnitPrice = Number(price, "Prezzo");
            item.IsSignificant = significant.IsToggled;
            item.IsCompanyMaterial = company.IsToggled;
            await Database.SaveCatalogItemAsync(ConnectionString, kind, item, original: original);
            await Navigation.PopAsync();
        }));
        if (item.Id > 0)
            Form.Add(Action("Elimina voce", async () =>
            {
                if (!await DisplayAlertAsync("Elimina dal catalogo", item.Name, "Elimina", "Annulla")) return;
                await Database.DeleteCatalogItemAsync(ConnectionString, kind, item.Id, original: original);
                await Navigation.PopAsync();
            }, true));
    }
}
