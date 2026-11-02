using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;

namespace EdilPaintPreventibiviGen.Views;

public partial class TrelloCardsWindow : Window
{
    private readonly CancellationTokenSource _loadCts = new();
    private List<TrelloCardRow> _cards = [];
    private bool _isLoading;
    private bool _updatingFilters;
    private string _sourceStatus = string.Empty;
    public TrelloCard? SelectedResult { get; private set; }
    public bool RemoveRequested { get; private set; }

    public TrelloCardsWindow(string currentCardId = "", bool allowRemove = false)
    {
        InitializeComponent();
        BtnRemove.Visibility = allowRemove && !string.IsNullOrWhiteSpace(currentCardId)
            ? Visibility.Visible : Visibility.Collapsed;
        TxtCurrentAssociation.Text = string.IsNullOrWhiteSpace(currentCardId)
            ? "Le schede sono consultate dalla copia sincronizzata nel database."
            : $"Scheda attualmente associata: {currentCardId}. La scelta cambia solo dopo la conferma.";
        Loaded += async (_, _) => { TxtSearch.Focus(); await LoadCardsAsync(); };
        Closed += (_, _) => { _loadCts.Cancel(); _loadCts.Dispose(); };
    }

    private async Task LoadCardsAsync()
    {
        if (_isLoading) return;
        _isLoading = true;
        BtnRefresh.IsEnabled = false;
        BtnAssociate.IsEnabled = false;
        GridCards.IsEnabled = false;
        TxtStatus.Text = "Caricamento delle schede sincronizzate…";
        try
        {
            var cards = await App.DataService.GetTrelloCardsAsync(_loadCts.Token);
            if (_loadCts.IsCancellationRequested) return;
            _cards = cards.Where(card => !card.IsDeleted).Select(card => new TrelloCardRow(card)).ToList();
            _updatingFilters = true;
            string? previousList = CmbList.SelectedItem as string;
            var lists = _cards.Select(row => row.BoardAndList).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToList();
            lists.Insert(0, "Tutte le liste");
            CmbList.ItemsSource = lists;
            CmbList.SelectedItem = previousList != null && lists.Contains(previousList) ? previousList : lists[0];
            _updatingFilters = false;
            _sourceStatus = cards.Any(card => card.IsOfflineSnapshot)
                ? "Copia locale: database non disponibile."
                : "Copia sincronizzata nel database.";
            _isLoading = false;
            RefreshResults();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Impossibile leggere le schede: {ex.Message}";
        }
        finally
        {
            _isLoading = false;
            if (!_loadCts.IsCancellationRequested)
            {
                BtnRefresh.IsEnabled = true;
                GridCards.IsEnabled = true;
                BtnAssociate.IsEnabled = GridCards.SelectedItem is TrelloCardRow;
            }
        }
    }

    private void RefreshResults()
    {
        if (GridCards == null || _isLoading || _updatingFilters) return;
        string search = TxtSearch.Text.Trim();
        string? selectedList = CmbList.SelectedItem as string;
        var rows = _cards.Where(row => ChkArchived.IsChecked == true || !row.Card.IsArchived)
            .Where(row => selectedList == null || selectedList == "Tutte le liste" || row.BoardAndList == selectedList)
            .Where(row => search.Length == 0 || new[] { row.Card.Name, row.Card.Description, row.BoardAndList, row.Card.Id }
                .Any(value => value?.Contains(search, StringComparison.CurrentCultureIgnoreCase) == true))
            .OrderBy(row => row.Card.IsArchived)
            .ThenBy(row => row.Card.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        GridCards.ItemsSource = rows;
        GridCards.UnselectAll();
        TxtStatus.Text = rows.Count == 0
            ? (_cards.Count == 0 ? "Nessuna scheda disponibile: occorre completare la prima sincronizzazione Trello." : "Nessuna scheda corrisponde ai filtri.")
            : $"{rows.Count} schede · {_sourceStatus}";
    }

    private void OnCardSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var card = (GridCards.SelectedItem as TrelloCardRow)?.Card;
        BtnAssociate.IsEnabled = card != null && !_isLoading;
        BtnOpen.IsEnabled = TryGetSafeTrelloUrl(card?.Url, out _);
        TxtPreviewTitle.Text = card?.Name ?? "Seleziona una scheda per leggerne i dati";
        TxtPreviewDetails.Text = card == null ? string.Empty
            : $"{card.BoardName} / {card.ListName}" + (card.IsArchived ? " · Archiviata" : "") +
              (card.SyncedAtUtc == default ? string.Empty : $"\nUltima sincronizzazione: {card.SyncedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}");
        TxtPreview.Text = card == null ? string.Empty : BuildPreview(card);
    }

    private void OnAssociateClick(object sender, RoutedEventArgs e)
    {
        if (_isLoading || GridCards.SelectedItem is not TrelloCardRow row || row.Card.IsDeleted) return;
        SelectedResult = row.Card;
        DialogResult = true;
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        RemoveRequested = true;
        DialogResult = true;
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        if (GridCards.SelectedItem is not TrelloCardRow row || !TryGetSafeTrelloUrl(row.Card.Url, out var uri)) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Apri Trello", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    public static bool TryGetSafeTrelloUrl(string? url, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var candidate) ||
            candidate.Scheme != Uri.UriSchemeHttps || !candidate.IsDefaultPort ||
            !string.IsNullOrEmpty(candidate.UserInfo) ||
            !(string.Equals(candidate.Host, "trello.com", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(candidate.Host, "www.trello.com", StringComparison.OrdinalIgnoreCase)) ||
            !candidate.AbsolutePath.StartsWith("/c/", StringComparison.Ordinal)) return false;
        uri = candidate;
        return true;
    }

    private static string BuildPreview(TrelloCard card)
    {
        var text = new StringBuilder(card.Description);
        if (card.DueUtc is { } due) text.AppendLine().AppendLine($"Appuntamento: {due.ToLocalTime():dd/MM/yyyy HH:mm}");
        if (string.IsNullOrWhiteSpace(card.PayloadJson)) return text.ToString();
        try
        {
            using var document = JsonDocument.Parse(card.PayloadJson);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("checklists", out var checklists) && checklists.ValueKind == JsonValueKind.Array)
            {
                foreach (var checklist in checklists.EnumerateArray())
                {
                    if (checklist.ValueKind != JsonValueKind.Object) continue;
                    text.AppendLine().AppendLine(checklist.TryGetProperty("name", out var name) ? name.GetString() : "Checklist");
                    if (!checklist.TryGetProperty("checkItems", out var items) || items.ValueKind != JsonValueKind.Array) continue;
                    foreach (var item in items.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("name", out var itemName)) continue;
                        bool completed = item.TryGetProperty("state", out var state) && state.GetString() == "complete";
                        text.Append(completed ? "☑ " : "☐ ").AppendLine(itemName.GetString());
                    }
                }
            }
        }
        catch (JsonException) { }
        return text.ToString();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadCardsAsync();
    private void OnSearchChanged(object sender, TextChangedEventArgs e) => RefreshResults();
    private void OnListChanged(object sender, SelectionChangedEventArgs e) => RefreshResults();
    private void OnArchivedChanged(object sender, RoutedEventArgs e) => RefreshResults();

    private sealed record TrelloCardRow(TrelloCard Card)
    {
        public string BoardAndList => $"{Card.BoardName} / {Card.ListName}";
        public string Appointment => Card.DueUtc?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? string.Empty;
    }
}
