using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

internal static class WorkSheetContextLoader
{
    internal static async Task<WorkSheetContext?> CreateContextAsync(
        Window owner,
        IDataService dataService,
        QuoteHistoryEntry quote,
        IEnumerable<Item> laborCatalog,
        IEnumerable<Customer> customers,
        WorkSheetOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var photoLoading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PhotoLoadingWindow? window = null;
        string status = "Caricamento delle fotografie del sopralluogo…";
        var progress = new Progress<string>(message =>
        {
            status = message;
            photoLoading.TrySetResult();
            window?.SetStatus(message);
        });
        var operation = TrelloWorkSheetService.CreateContextAsync(
            dataService, quote, laborCatalog, customers, options, cancellation.Token, progress);
        try
        {
            // Fast cached loads and sheets without photographs need no extra dialog.
            await Task.WhenAny(operation, photoLoading.Task);
            if (!operation.IsCompleted)
            {
                await Task.WhenAny(operation, Task.Delay(600, cancellation.Token));
                cancellation.Token.ThrowIfCancellationRequested();
                if (!operation.IsCompleted)
                {
                    var dialog = new PhotoLoadingWindow(status, cancellation.Cancel) { Owner = owner };
                    window = dialog;
                    dialog.Loaded += async (_, _) =>
                    {
                        try { await operation; }
                        catch { /* The awaited operation below reports errors to the caller. */ }
                        finally { dialog.Finish(); }
                    };
                    var previousCursor = Mouse.OverrideCursor;
                    try
                    {
                        Mouse.OverrideCursor = null;
                        dialog.ShowDialog();
                    }
                    finally { Mouse.OverrideCursor = previousCursor; }
                }
            }
            var context = await operation;
            cancellation.Token.ThrowIfCancellationRequested();
            context.PhotosPerPage = App.AppSettings?.WorkSheet.GetEffectivePhotosPerPage() ?? 1;
            return context;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private sealed class PhotoLoadingWindow : Window
    {
        private readonly TextBlock _status;
        private readonly Button _cancel;
        private readonly Action _requestCancellation;
        private bool _cancelRequested;
        private bool _finished;

        internal PhotoLoadingWindow(string status, Action requestCancellation)
        {
            _requestCancellation = requestCancellation;
            Title = "Preparazione scheda lavoro";
            Width = 440;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Cursor = Cursors.Arrow;
            var panel = new StackPanel { Margin = new Thickness(24) };
            panel.Children.Add(new TextBlock
            {
                Text = "Preparazione delle fotografie",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });
            _status = new TextBlock { Text = status, TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(_status);
            panel.Children.Add(new TextBlock
            {
                Text = "Il primo caricamento può richiedere qualche minuto.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 16)
            });
            panel.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 8 });
            _cancel = new Button
            {
                Content = "Annulla",
                IsCancel = true,
                MinWidth = 100,
                Padding = new Thickness(12, 6, 12, 6),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0)
            };
            _cancel.Click += (_, _) => RequestCancellation();
            panel.Children.Add(_cancel);
            Content = panel;
            Closing += (_, args) =>
            {
                if (_finished) return;
                args.Cancel = true;
                RequestCancellation();
            };
        }

        internal void SetStatus(string status)
        {
            if (!_cancelRequested) _status.Text = status;
        }

        private void RequestCancellation()
        {
            if (_cancelRequested) return;
            _cancelRequested = true;
            _cancel.IsEnabled = false;
            _status.Text = "Annullamento in corso…";
            _requestCancellation();
        }

        internal void Finish()
        {
            if (_finished) return;
            _finished = true;
            Close();
        }
    }
}
