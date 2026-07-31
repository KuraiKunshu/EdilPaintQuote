using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace EdilPaintPreventibiviGen.Models;

public sealed class ProfitMaterialCost
{
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public double CustomerUnitPrice { get; set; }
    public double CustomerDiscount { get; set; }

    public double CustomerTotal =>
        CustomerUnitPrice * Quantity * (1 - Math.Clamp(CustomerDiscount, 0, 100) / 100);
}

public sealed class CompanyMaterialCost : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private int _quantity = 1;
    private double _unitCost;

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public int Quantity
    {
        get => _quantity;
        set
        {
            _quantity = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Total));
        }
    }

    public double UnitCost
    {
        get => _unitCost;
        set
        {
            _unitCost = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Total));
        }
    }

    public double Total => Math.Max(0, Quantity) * Math.Max(0, UnitCost);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class RealProfitInput
{
    public double QuoteRevenue { get; set; }
    public bool ExcludeMaterials { get; set; }
    public double SupplierDiscount { get; set; }
    public int Workers { get; set; }
    public double Days { get; set; }
    public double HoursPerDay { get; set; }
    public double HourlyCost { get; set; }
    public List<ProfitMaterialCost> Materials { get; set; } = [];
    public List<CompanyMaterialCost> CompanyMaterials { get; set; } = [];
}

public sealed class RealProfitResult
{
    public double CustomerMaterialRevenue { get; init; }
    public double SupplierMaterialCost { get; init; }
    public double MaterialMargin { get; init; }
    public double LaborCost { get; init; }
    public double CompanyMaterialCost { get; init; }
    public double TotalCosts { get; init; }
    public double Profit { get; init; }
    public double ProfitPercentage { get; init; }
}
