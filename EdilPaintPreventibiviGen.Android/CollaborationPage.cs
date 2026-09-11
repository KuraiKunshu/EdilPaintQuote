using EdilPaintPreventibiviGen.Android.Controls;
using EdilPaintPreventibiviGen.Android.Models;

namespace EdilPaintPreventibiviGen.Android;

public sealed class CollaborationPage : OperationPage
{
    public CollaborationPage(string connectionString, QuoteDraft draft) : base(connectionString, "Collaborazione")
    {
        var enabled = new Switch { IsToggled = draft.IsJointVenture };
        var partner = Input(draft.PartnerCompanyName);
        var groups = new List<(List<CostAllocationItem> Target, List<(Entry Name, Entry Amount, Entry Notes)> Rows)>();
        Form.Add(Toggle("Collaborazione attiva", enabled));
        Form.Add(Field("Ditta partner", partner));
        foreach (var (title, target) in new[] { ("Nostri costi", draft.OurCosts), ("Costi partner", draft.PartnerCosts), ("Costi aggiuntivi", draft.AdditionalCosts) })
        {
            Form.Add(Heading(title));
            var container = new VerticalStackLayout { Spacing = 8 };
            var rows = new List<(Entry Name, Entry Amount, Entry Notes)>();
            groups.Add((target, rows));
            void Add(CostAllocationItem cost)
            {
                var name = Input(cost.Description);
                var amount = Input(Format(cost.Amount), true);
                var notes = Input(cost.Notes);
                var row = (name, amount, notes);
                var fields = new VerticalStackLayout { Spacing = 4 };
                fields.Add(Field("Descrizione", name));
                fields.Add(Field("Importo", amount));
                fields.Add(Field("Note interne", notes));
                fields.Add(Action("Rimuovi", () => { rows.Remove(row); container.Remove(fields); return Task.CompletedTask; }, true));
                container.Add(fields);
                rows.Add(row);
            }
            foreach (var cost in target) Add(cost);
            Form.Add(container);
            Form.Add(Action("Aggiungi voce", () => { Add(new()); return Task.CompletedTask; }, true));
        }
        Form.Add(Action("Applica al preventivo", async () =>
        {
            var updates = groups.Select(group => group.Rows.Select(row => new CostAllocationItem
            {
                Description = row.Name.Text?.Trim() ?? "", Amount = Number(row.Amount, "Importo"), Notes = row.Notes.Text ?? ""
            }).ToList()).ToList();
            draft.IsJointVenture = enabled.IsToggled;
            draft.PartnerCompanyName = partner.Text?.Trim() ?? "";
            for (int index = 0; index < groups.Count; index++)
            {
                groups[index].Target.Clear();
                groups[index].Target.AddRange(updates[index]);
            }
            await Navigation.PopAsync();
        }));
    }
}
