using EdilPaintPreventibiviGen.Android.Controls;
using EdilPaintPreventibiviGen.Android.Models;
using EdilPaintPreventibiviGen.Android.Services;
using Rule = EdilPaintPreventibiviGen.Models.AutomaticWindowMaterialRule;

namespace EdilPaintPreventibiviGen.Android;

public sealed class AutomaticMaterialSettingsPage : OperationPage
{
    public AutomaticMaterialSettingsPage(string connectionString) : base(connectionString, "Regole materiali") { }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RunAsync(async () =>
        {
            var settings = await MobileSettings.LoadAsync();
            Form.Clear();
            var prefixes = Input(string.Join(", ", settings.WindowPrefixes));
            Form.Add(Field("Prefissi prodotti finestra", prefixes));
            Form.Add(Action("Salva prefissi", async () =>
            {
                settings.WindowPrefixes = (prefixes.Text ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                await settings.SaveAsync();
                await DisplayAlertAsync("Regole", "Prefissi salvati", "OK");
            }, true));
            Form.Add(Action("Aggiungi regola", async () => await Navigation.PushAsync(new AutomaticRuleEditorPage(ConnectionString, new Rule { RuleId = Guid.NewGuid().ToString("N") }))));
            foreach (var rule in settings.MaterialRules)
                Form.Add(Action($"{(rule.Enabled ? "Attiva" : "Disattiva")} - {rule.LaborNameSnapshot}\n{rule.MaterialNameSnapshot}", async () => await Navigation.PushAsync(new AutomaticRuleEditorPage(ConnectionString, rule)), true));
        });
    }
}

internal sealed class AutomaticRuleEditorPage : OperationPage
{
    public AutomaticRuleEditorPage(string connectionString, Rule original) : base(connectionString, "Regola materiale")
    {
        var rule = System.Text.Json.JsonSerializer.Deserialize<Rule>(System.Text.Json.JsonSerializer.Serialize(original))!;
        var enabled = new Switch { IsToggled = rule.Enabled };
        var windows = new Switch { IsToggled = rule.IsWindowAutomation };
        var labor = new Label { Text = rule.LaborNameSnapshot };
        var material = new Label { Text = rule.MaterialNameSnapshot };
        var mode = new Picker { ItemsSource = new[] { "Perimetro", "Quantita fissa" }, SelectedIndex = rule.Mode == "FixedPerWindow" ? 1 : 0 };
        var amount = Input(rule.Parameter.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("it-IT")), true);
        Form.Add(Toggle("Regola attiva", enabled));
        Form.Add(Toggle("Calcolo per finestra", windows));
        Form.Add(Field("Lavorazione", labor));
        Form.Add(Action("Scegli lavorazione", async () => await Navigation.PushAsync(new CatalogPickerPage(ConnectionString, QuoteLineKind.Labor, item =>
        { rule.LaborCatalogItemId = item.Id; rule.LaborNameSnapshot = item.Name; labor.Text = item.Name; })), true));
        Form.Add(Field("Materiale aziendale", material));
        Form.Add(Action("Scegli materiale", async () => await Navigation.PushAsync(new CatalogPickerPage(ConnectionString, QuoteLineKind.Material, item =>
        { rule.MaterialCatalogItemId = item.Id; rule.MaterialNameSnapshot = item.Name; material.Text = item.Name; }, companyOnly: true)), true));
        Form.Add(Field("Calcolo", mode));
        Form.Add(Field("Moltiplicatore perimetro / quantita per unita", amount));
        Form.Add(Action("Salva regola", async () =>
        {
            if (string.IsNullOrWhiteSpace(rule.LaborNameSnapshot) || string.IsNullOrWhiteSpace(rule.MaterialNameSnapshot)) throw new InvalidOperationException("Seleziona lavorazione e materiale.");
            rule.Enabled = enabled.IsToggled;
            rule.IsWindowAutomation = windows.IsToggled;
            rule.Mode = mode.SelectedIndex == 0 ? "Perimeter" : "FixedPerWindow";
            rule.Parameter = (decimal)Number(amount, "Quantita", 1000000);
            if (rule.Parameter <= 0) throw new InvalidOperationException("La quantita deve essere maggiore di zero.");
            var settings = await MobileSettings.LoadAsync();
            settings.MaterialRules.RemoveAll(x => x.RuleId == rule.RuleId);
            settings.MaterialRules.Add(rule);
            await settings.SaveAsync();
            await Navigation.PopAsync();
        }));
        Form.Add(Action("Elimina regola", async () =>
        {
            if (!await DisplayAlertAsync("Elimina regola", rule.MaterialNameSnapshot, "Elimina", "Annulla")) return;
            var settings = await MobileSettings.LoadAsync();
            settings.MaterialRules.RemoveAll(x => x.RuleId == rule.RuleId);
            await settings.SaveAsync();
            await Navigation.PopAsync();
        }, true));
    }
}
