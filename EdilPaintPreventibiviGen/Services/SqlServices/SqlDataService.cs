using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public partial class SqlDataService : IDataService
{
    private readonly AppSettingsService _appSettings;

    public SqlDataService(AppSettingsService appSettings)
    {
        _appSettings = appSettings;
    }

    public bool CanSynchronize => true;
}

public class CostAllocations
{
    public List<CostAllocationItem> OurCosts { get; set; } = new();
    public List<CostAllocationItem> PartnerCosts { get; set; } = new();
    public List<CostAllocationItem> AdditionalCosts { get; set; } = new();
}
