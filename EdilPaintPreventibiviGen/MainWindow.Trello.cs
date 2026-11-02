using System.Diagnostics;
using System.Windows;
using EdilPaintPreventibiviGen.ViewModels;
using EdilPaintPreventibiviGen.Views;
using EdilPaintPreventibiviGen.Services;

namespace EdilPaintPreventibiviGen;

public partial class MainWindow
{
    private async void OnSelectTrelloCardClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var window = new TrelloCardsWindow(vm.TrelloCardId) { Owner = this };
        if (window.ShowDialog() != true || window.SelectedResult == null) return;
        try { await vm.SetManualTrelloAssociationAsync(window.SelectedResult, AppShutdownManager.ShutdownToken); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Associazione Trello non salvata", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnRemoveTrelloCardClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        try { await vm.SetManualTrelloAssociationAsync(null, AppShutdownManager.ShutdownToken); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Associazione Trello non rimossa", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnOpenTrelloCardClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !vm.HasTrelloCardAssociation) return;
        try
        {
            string url = vm.TrelloCardUrl;
            if (!TrelloCardsWindow.TryGetSafeTrelloUrl(url, out var uri))
            {
                var card = await App.DataService.GetTrelloCardAsync(vm.TrelloCardId, AppShutdownManager.ShutdownToken);
                if (!TrelloCardsWindow.TryGetSafeTrelloUrl(card?.Url, out uri))
                    throw new InvalidOperationException("Il collegamento Trello non è disponibile nella copia sincronizzata.");
            }
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Apri scheda Trello", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
