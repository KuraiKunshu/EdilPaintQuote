using System.Windows;
using EdilPaintPreventibiviGen.Models;
using System.Windows.Input;
using System.Globalization;

namespace EdilPaintPreventibiviGen.Views;

public partial class EditItemWindow : Window
{
    private readonly Item _item;
    private readonly string _originalName;
    public bool Success { get; private set; } = false;

    public EditItemWindow(Item item)
    {
        InitializeComponent();
        EdilPaintPreventibiviGen.Helpers.WindowResizeBehavior.PreventMaximizedState(this);
        _item = item;
        _originalName = item.Name?.Trim() ?? string.Empty;
        
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
            string newName = TxtName.Text.Trim();
            if (!string.Equals(_originalName, newName, StringComparison.OrdinalIgnoreCase))
                _item.PersistentId = 0;

            _item.Name = newName;
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
