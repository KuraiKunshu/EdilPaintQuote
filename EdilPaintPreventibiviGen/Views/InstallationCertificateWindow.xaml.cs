using System.Globalization;
using System.Windows;
using System.Windows.Input;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Views;

public partial class InstallationCertificateWindow : Window
{
    public DateTime CompletionDate { get; private set; } = DateTime.Today;
    public string WorkSite { get; private set; } = string.Empty;
    public IReadOnlyList<Item> SelectedMaterials { get; private set; } = [];
    public InstallationCertificateSelection Selection { get; }

    public InstallationCertificateWindow(QuoteHistorySummary summary, IEnumerable<Item> materials)
    {
        Selection = new InstallationCertificateSelection(materials);
        InitializeComponent();
        EdilPaintPreventibiviGen.Helpers.WindowResizeBehavior.PreventMaximizedState(this);
        MaterialChoices.ItemsSource = Selection.Materials;

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

        try
        {
            SelectedMaterials = Selection.GetSelectedMaterials();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "Certificato corretta posa",
                MessageBoxButton.OK, MessageBoxImage.Information);
            MaterialChoices.Focus();
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

    private void OnSelectAllClick(object sender, RoutedEventArgs e) => Selection.SelectAll(true);

    private void OnDeselectAllClick(object sender, RoutedEventArgs e) => Selection.SelectAll(false);

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            DialogResult = false;
    }
}
