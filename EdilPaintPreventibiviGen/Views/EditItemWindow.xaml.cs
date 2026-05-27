using System.Windows;
using EdilPaintPreventibiviGen.Models;
using System.Windows.Input;
using System.Globalization;

namespace EdilPaintPreventibiviGen.Views;

public partial class EditItemWindow : Window
{
    private Item _item;
    public bool Success { get; private set; } = false;

    public EditItemWindow(Item item)
    {
        InitializeComponent();
        _item = item;
        
        TxtName.Text = _item.Name;
        TxtDescription.Text = _item.Description;
        TxtPrice.Text = _item.UnitPrice.ToString();
        TxtQty.Text = _item.Quantity.ToString();
        ChkSignificant.IsChecked = _item.IsSignificant;
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
        string priceText = TxtPrice.Text.Replace(",", ".");
        if (double.TryParse(priceText, NumberStyles.Any, CultureInfo.InvariantCulture, out double price) && 
            int.TryParse(TxtQty.Text, out int qty))
        {
            _item.Name = TxtName.Text;
            _item.Description = TxtDescription.Text;
            _item.UnitPrice = price;
            _item.Quantity = qty;
            _item.IsSignificant = ChkSignificant.IsChecked ?? false;
            
            Success = true;
            this.DialogResult = true; 
            Close();
        }
        else
        {
            MessageBox.Show("Inserisci valori numerici validi.", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        this.DialogResult = false;
        Close();
    }
}