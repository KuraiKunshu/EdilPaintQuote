using System.Collections.ObjectModel;
using EdilPaintPreventibiviGen.Android.Controls;
using EdilPaintPreventibiviGen.Android.Models;

namespace EdilPaintPreventibiviGen.Android;

public sealed class QuoteLineOrderPage : OperationPage
{
    public QuoteLineOrderPage(QuoteDraft draft) : base("", "Ordine delle voci")
    {
        Form.Add(Heading("Materiali"));
        AddList(draft.Materials);
        Form.Add(Heading("Lavorazioni"));
        AddList(draft.Labors);
    }

    private void AddList(ObservableCollection<QuoteLine> items)
    {
        var list = new VerticalStackLayout { Spacing = 6 };
        Form.Add(list);
        void Render()
        {
            list.Clear();
            foreach (var item in items)
            {
                var row = new Grid { ColumnDefinitions = { new(GridLength.Star), new(48), new(48) }, ColumnSpacing = 6 };
                row.Add(new Label { Text = $"N.{item.Quantity} {item.Name}", VerticalOptions = LayoutOptions.Center });
                var up = Action("\u2191", () => { Move(-1); return Task.CompletedTask; }, true);
                var down = Action("\u2193", () => { Move(1); return Task.CompletedTask; }, true);
                SemanticProperties.SetDescription(up, "Sposta su " + item.Name);
                SemanticProperties.SetDescription(down, "Sposta giu " + item.Name);
                up.IsEnabled = items.IndexOf(item) > 0;
                down.IsEnabled = items.IndexOf(item) < items.Count - 1;
                row.Add(up, 1);
                row.Add(down, 2);
                list.Add(row);
                void Move(int delta)
                {
                    int index = items.IndexOf(item);
                    int next = index + delta;
                    if (index >= 0 && next >= 0 && next < items.Count)
                    {
                        items.Move(index, next);
                        for (int i = 0; i < items.Count; i++) items[i].SortOrder = i;
                        Render();
                    }
                }
            }
        }
        Render();
    }
}
