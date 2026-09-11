using EdilPaintPreventibiviGen.Android.Controls;
using EdilPaintPreventibiviGen.Android.Models;

namespace EdilPaintPreventibiviGen.Android;

public sealed class QuoteFiltersPage : OperationPage
{
    public QuoteFiltersPage(QuoteListFilter filter, System.Action<QuoteListFilter> apply) : base("", "Filtri preventivi")
    {
        var useFrom = new Switch { IsToggled = filter.From.HasValue };
        var useUntil = new Switch { IsToggled = filter.Until.HasValue };
        var from = new DatePicker { Date = filter.From ?? new DateTime(DateTime.Today.Year, 1, 1), IsEnabled = useFrom.IsToggled };
        var until = new DatePicker { Date = filter.Until ?? DateTime.Today, IsEnabled = useUntil.IsToggled };
        var open = new Switch { IsToggled = filter.SentOpenOnly };
        var sort = new Picker { ItemsSource = new[] { "Data - recenti", "Data - meno recenti", "Cliente - A/Z", "Importo - maggiore" }, SelectedIndex = filter.Sort };
        useFrom.Toggled += (_, e) => from.IsEnabled = e.Value;
        useUntil.Toggled += (_, e) => until.IsEnabled = e.Value;
        Form.Add(Toggle("Data iniziale", useFrom));
        Form.Add(from);
        Form.Add(Toggle("Data finale", useUntil));
        Form.Add(until);
        Form.Add(Toggle("Solo inviati ancora aperti", open));
        Form.Add(Field("Ordina per", sort));
        Form.Add(Action("Applica", async () =>
        {
            if (useFrom.IsToggled && useUntil.IsToggled && from.Date > until.Date) throw new InvalidOperationException("La data finale precede quella iniziale.");
            apply(new() { From = useFrom.IsToggled ? from.Date : null, Until = useUntil.IsToggled ? until.Date : null, SentOpenOnly = open.IsToggled, Sort = sort.SelectedIndex });
            await Navigation.PopAsync();
        }));
        Form.Add(Action("Azzera filtri", async () => { apply(new()); await Navigation.PopAsync(); }, true));
    }
}
