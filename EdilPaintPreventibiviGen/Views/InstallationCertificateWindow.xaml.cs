using System.Windows;
using System.Windows.Input;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Views;

public partial class InstallationCertificateWindow : Window
{
    public DateTime CompletionDate { get; private set; } = DateTime.Today;
    public string WorkSite { get; private set; } = string.Empty;

    public InstallationCertificateWindow(QuoteHistorySummary summary)
    {
        InitializeComponent();
        EdilPaintPreventibiviGen.Helpers.WindowResizeBehavior.PreventMaximizedState(this);

        TxtTitle.Text = $"Certificato preventivo n. {summary.QuoteNumber}";
        TxtSubtitle.Text = string.IsNullOrWhiteSpace(summary.ReferenceName)
            ? summary.CustomerName
            : $"{summary.CustomerName} - Rif. {summary.ReferenceName}";
        DpCompletionDate.SelectedDate = DateTime.Today;

        Loaded += (_, _) => TxtWorkSite.Focus();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnGenerateClick(object sender, RoutedEventArgs e)
    {
        if (!DpCompletionDate.SelectedDate.HasValue)
        {
            MessageBox.Show("Seleziona la data di fine lavori.",
                "Certificato corretta posa", MessageBoxButton.OK, MessageBoxImage.Information);
            DpCompletionDate.Focus();
            return;
        }

        CompletionDate = DpCompletionDate.SelectedDate.Value.Date;
        WorkSite = TxtWorkSite.Text.Trim();
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            DialogResult = false;
    }
}
