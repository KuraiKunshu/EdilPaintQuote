using System.Globalization;
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
        if (!TryReadCompletionDate(out var completionDate))
        {
            MessageBox.Show("Seleziona una data di fine lavori valida.",
                "Certificato corretta posa", MessageBoxButton.OK, MessageBoxImage.Information);
            DpCompletionDate.Focus();
            return;
        }

        CompletionDate = completionDate;
        WorkSite = TxtWorkSite.Text.Trim();
        DialogResult = true;
    }

    private bool TryReadCompletionDate(out DateTime completionDate)
    {
        string typedDate = DpCompletionDate.Text.Trim();
        if (!string.IsNullOrWhiteSpace(typedDate) &&
            DateTime.TryParse(
                typedDate,
                CultureInfo.GetCultureInfo("it-IT"),
                DateTimeStyles.AssumeLocal,
                out completionDate))
        {
            completionDate = completionDate.Date;
            DpCompletionDate.SelectedDate = completionDate;
            return true;
        }

        if (DpCompletionDate.SelectedDate.HasValue)
        {
            completionDate = DpCompletionDate.SelectedDate.Value.Date;
            return true;
        }

        completionDate = default;
        return false;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            DialogResult = false;
    }
}
