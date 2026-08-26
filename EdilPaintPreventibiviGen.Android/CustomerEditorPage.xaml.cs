using System.Globalization;
using System.Net.Mail;
using EdilPaintPreventibiviGen.Android.Models;
using EdilPaintPreventibiviGen.Android.Services;

namespace EdilPaintPreventibiviGen.Android;

public partial class CustomerEditorPage : ContentPage
{
    private static readonly CultureInfo ItalianCulture = CultureInfo.GetCultureInfo("it-IT");
    private readonly string _connectionString;
    private readonly MobileDatabaseService _databaseService = new();
    private readonly CustomerRecord _customer;
    private readonly Action<CustomerRecord>? _onSaved;
    private bool _isSaving;

    public CustomerEditorPage(
        string connectionString,
        CustomerRecord? customer = null,
        Action<CustomerRecord>? onSaved = null)
    {
        InitializeComponent();
        _connectionString = connectionString;
        _customer = customer?.Clone() ?? new CustomerRecord();
        _onSaved = onSaved;

        bool isEdit = _customer.Id > 0;
        HeaderTitleLabel.Text = isEdit ? "Modifica cliente" : "Nuovo cliente";
        HeaderSubtitleLabel.Text = isEdit
            ? "Aggiorna dati anagrafici e sconti"
            : "Dati anagrafici e sconti predefiniti";
        SaveButton.Text = isEdit ? "Salva modifiche" : "Crea cliente";

        BusinessNameEntry.Text = _customer.BusinessName;
        AddressEntry.Text = _customer.Address;
        PhoneEntry.Text = _customer.Phone;
        EmailEntry.Text = _customer.Email;
        MaterialDiscountEntry.Text = _customer.MaterialDiscount.ToString("0.##", ItalianCulture);
        LaborDiscountEntry.Text = _customer.LaborDiscount.ToString("0.##", ItalianCulture);
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (_isSaving)
            return;

        string businessName = BusinessNameEntry.Text?.Trim() ?? string.Empty;
        string email = EmailEntry.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(businessName))
        {
            await DisplayAlertAsync("Cliente", "Inserisci la ragione sociale.", "OK");
            BusinessNameEntry.Focus();
            return;
        }

        if (!string.IsNullOrWhiteSpace(email) && !MailAddress.TryCreate(email, out _))
        {
            await DisplayAlertAsync("Cliente", "L'indirizzo email non è valido.", "OK");
            EmailEntry.Focus();
            return;
        }

        if (!TryParsePercentage(MaterialDiscountEntry.Text, out double materialDiscount) ||
            !TryParsePercentage(LaborDiscountEntry.Text, out double laborDiscount))
        {
            await DisplayAlertAsync("Cliente", "Gli sconti devono essere numeri compresi tra 0 e 100.", "OK");
            return;
        }

        _customer.BusinessName = businessName;
        _customer.Address = AddressEntry.Text?.Trim() ?? string.Empty;
        _customer.Phone = PhoneEntry.Text?.Trim() ?? string.Empty;
        _customer.Email = email;
        _customer.MaterialDiscount = materialDiscount;
        _customer.LaborDiscount = laborDiscount;

        try
        {
            SetBusy(true);
            CustomerRecord saved = await _databaseService.SaveCustomerAsync(_connectionString, _customer);
            _onSaved?.Invoke(saved.Clone());
            await DisplayAlertAsync("Cliente salvato", saved.BusinessName, "OK");
            await Navigation.PopAsync();
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(
                "Salvataggio non riuscito",
                MobileDatabaseService.GetUserMessage(exception),
                "OK");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static bool TryParsePercentage(string? text, out double value)
    {
        bool parsed = double.TryParse(text, NumberStyles.Number, ItalianCulture, out value) ||
                      double.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
        return parsed && value >= 0 && value <= 100;
    }

    private void SetBusy(bool isBusy)
    {
        _isSaving = isBusy;
        Busy.IsVisible = isBusy;
        Busy.IsRunning = isBusy;
        SaveButton.IsEnabled = !isBusy;
    }
}
