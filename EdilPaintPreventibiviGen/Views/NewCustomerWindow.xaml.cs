using System.Globalization;
using System.Windows;
using System.Windows.Input;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Views;

public partial class NewCustomerWindow : Window
{
    public Customer? NewCustomer { get; private set; }

    private readonly Customer? _existingCustomer;

    public NewCustomerWindow(Customer? existingCustomer = null)
    {
        InitializeComponent();

        _existingCustomer = existingCustomer;

        if (_existingCustomer != null)
        {
            Title = "Modifica Cliente";
            TxtWindowTitle.Text = "Modifica cliente";
            BtnSave.Content = "Salva modifiche";

            TxtBusiness.Text = _existingCustomer.BusinessName;
            TxtAddress.Text = _existingCustomer.Address;
            TxtEmail.Text = _existingCustomer.Email;
            TxtPhone.Text = _existingCustomer.Phone;
            TxtDiscountMat.Text = _existingCustomer.MaterialDiscount.ToString(CultureInfo.InvariantCulture);
            TxtDiscountLab.Text = _existingCustomer.LaborDiscount.ToString(CultureInfo.InvariantCulture);
        }

        PreviewKeyDown += NewCustomerWindow_PreviewKeyDown;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtBusiness.Text))
        {
            MessageBox.Show("Inserire almeno la Ragione Sociale o il Cognome.");
            return;
        }

        if (!TryParseDiscount(TxtDiscountMat.Text, out var materialDiscount))
        {
            MessageBox.Show("Il valore dello sconto materiale non è valido.");
            return;
        }

        if (!TryParseDiscount(TxtDiscountLab.Text, out var laborDiscount))
        {
            MessageBox.Show("Il valore dello sconto manodopera non è valido.");
            return;
        }

        if (_existingCustomer != null)
        {
            _existingCustomer.BusinessName = TxtBusiness.Text;
            _existingCustomer.Address = TxtAddress.Text;
            _existingCustomer.Email = TxtEmail.Text;
            _existingCustomer.Phone = TxtPhone.Text;
            _existingCustomer.MaterialDiscount = materialDiscount;
            _existingCustomer.LaborDiscount = laborDiscount;

            NewCustomer = _existingCustomer;
        }
        else
        {
            NewCustomer = new Customer
            {
                BusinessName = TxtBusiness.Text,
                Address = TxtAddress.Text,
                Email = TxtEmail.Text,
                Phone = TxtPhone.Text,
                MaterialDiscount = materialDiscount,
                LaborDiscount = laborDiscount
            };
        }

        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static bool TryParseDiscount(string? text, out double value)
    {
        text = (text ?? string.Empty).Trim().Replace(',', '.');

        return double.TryParse(
            text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);
    }

    private void NewCustomerWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }
}