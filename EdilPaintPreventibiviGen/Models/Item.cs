using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace EdilPaintPreventibiviGen.Models;

public class Item : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _description = string.Empty;
    private double _unitPrice;
    private int _quantity = 1;
    private double _discount;
    private bool _isSignificant;
    private int _sortOrder;

    public string Name { get => _name; set { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalPrice)); } }
    public string Description { get => _description; set { _description = value; OnPropertyChanged(); } }
    public double UnitPrice { get => _unitPrice; set { _unitPrice = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalPrice)); } }
    public int Quantity { get => _quantity; set { _quantity = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalPrice)); } }
    public double Discount { get => _discount; set { _discount = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalPrice)); } }
    public bool IsSignificant { get => _isSignificant; set { _isSignificant = value; OnPropertyChanged(); } }
    public int SortOrder { get => _sortOrder; set { _sortOrder = value; OnPropertyChanged(); } }

    public double TotalPrice => (UnitPrice * Quantity) * (1 - Math.Clamp(Discount, 0, 100) / 100);
    
    
    
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}