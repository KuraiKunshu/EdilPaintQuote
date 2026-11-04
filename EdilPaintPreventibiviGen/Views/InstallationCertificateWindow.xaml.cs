using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Views;

public partial class InstallationCertificateWindow : Window
{
    public DateTime CompletionDate { get; private set; } = DateTime.Today;
    public string WorkSite { get; private set; } = string.Empty;
    public string RecipientAddress { get; private set; } = string.Empty;
    internal InstallationCertificateRecipient Recipient => (InstallationCertificateRecipient)RecipientChoice.SelectedItem;
    public IReadOnlyList<Item> SelectedMaterials { get; private set; } = [];
    public InstallationCertificateSelection Selection { get; }
    private readonly string _savedWorkSite;

    internal InstallationCertificateWindow(QuoteHistoryEntry quote, InstallationCertificateSelection selection, IEnumerable<Customer> customers)
    {
        Selection = selection;
        _savedWorkSite = quote.SiteName?.Trim() ?? string.Empty;
        InitializeComponent();
        EdilPaintPreventibiviGen.Helpers.WindowResizeBehavior.PreventMaximizedState(this);
        MaterialChoices.ItemsSource = Selection.Materials;
        RecipientChoice.ItemsSource = InstallationCertificateRecipient.CreateOptions(quote, customers);
        RecipientChoice.SelectedIndex = 0;

        TxtTitle.Text = $"Certificato preventivo n. {quote.QuoteNumber}";
        TxtSubtitle.Text = string.IsNullOrWhiteSpace(quote.ReferenceName)
            ? quote.CustomerName
            : $"{quote.CustomerName} - Rif. {quote.ReferenceName}";
        DpCompletionDate.SelectedDate = DateTime.Today;

        Loaded += (_, _) => RecipientChoice.Focus();
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

        string recipientAddress = TxtRecipientAddress.Text.Trim();
        string workSite = TxtWorkSite.Text.Trim();
        if (workSite.Length == 0)
            workSite = _savedWorkSite.Length > 0 ? _savedWorkSite
                : Recipient.ReferenceName.Length > 0 ? Recipient.WorkSiteFallback : recipientAddress;
        if (workSite.Length == 0)
        {
            MessageBox.Show(this, "Indica il cantiere presso cui sono stati eseguiti i lavori.",
                "Certificato corretta posa", MessageBoxButton.OK, MessageBoxImage.Information);
            TxtWorkSite.Focus();
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
        WorkSite = workSite;
        RecipientAddress = recipientAddress;
        DialogResult = true;
    }

    private void OnRecipientChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RecipientChoice.SelectedItem is InstallationCertificateRecipient recipient)
            TxtRecipientAddress.Text = recipient.Address;
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
