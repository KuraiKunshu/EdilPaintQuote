using System.Globalization;

namespace EdilPaintPreventibiviGen.Android.Models;

public sealed class DashboardSnapshot
{
    private static readonly CultureInfo ItalianCulture = CultureInfo.GetCultureInfo("it-IT");

    public int TotalQuotes { get; init; }
    public int DraftQuotes { get; init; }
    public int SentOpenQuotes { get; init; }
    public int ConfirmedQuotes { get; init; }
    public int PendingOrders { get; init; }
    public int Customers { get; init; }
    public double ConfirmedValue { get; init; }
    public IReadOnlyList<QuoteSummary> RecentQuotes { get; init; } = [];

    public string ConfirmedValueDisplay => ConfirmedValue.ToString("C", ItalianCulture);
}

public sealed class QuoteEventRecord
{
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string DeviceName { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public string DateDisplay => CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("it-IT"));
    public string DeviceDisplay => string.IsNullOrWhiteSpace(DeviceName) ? "Dispositivo non indicato" : DeviceName;
}

public sealed class CostAllocationItem
{
    public string Description { get; set; } = string.Empty;
    public double Amount { get; set; }
    public string Notes { get; set; } = string.Empty;

    public string AmountDisplay => Amount.ToString("C", CultureInfo.GetCultureInfo("it-IT"));

    public CostAllocationItem Clone() => new()
    {
        Description = Description,
        Amount = Amount,
        Notes = Notes
    };
}

public sealed class CostAllocationSnapshot
{
    public List<CostAllocationItem> OurCosts { get; set; } = [];
    public List<CostAllocationItem> PartnerCosts { get; set; } = [];
    public List<CostAllocationItem> AdditionalCosts { get; set; } = [];
}

public sealed class SupplierRecord
{
    public int Id { get; init; }
    public string BusinessName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public double SupplierDiscount { get; init; }

    public override string ToString() => BusinessName;
}

public enum SupplierOrderSortMode
{
    OrderDateDescending,
    OrderDateAscending,
    ExpectedDeliveryAscending,
    ExpectedDeliveryDescending,
    CustomerAscending,
    CustomerDescending,
    Status
}

public sealed record SupplierOrderSortOption(SupplierOrderSortMode Mode, string Label)
{
    public override string ToString() => Label;
}

public sealed class SupplierOrderSummary
{
    public int QuoteId { get; init; }
    public string QuoteNumber { get; init; } = string.Empty;
    public DateTime Date { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string ReferenceName { get; init; } = string.Empty;
    public string SiteName { get; init; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public bool MaterialsOrderedByCustomer { get; set; }
    public DateTime? MaterialOrderDate { get; set; }
    public DateTime? ExpectedDeliveryDate { get; set; }
    public string MaterialStatus { get; set; } = string.Empty;
    public long Revision { get; set; }

    public string CustomerReferenceDisplay => string.IsNullOrWhiteSpace(ReferenceName)
        ? CustomerName
        : $"{CustomerName} - Rif. {ReferenceName}";
    public string QuoteDisplay => $"Prev. {QuoteNumber} del {Date:dd/MM/yyyy}";
    public string SiteDisplay => string.IsNullOrWhiteSpace(SiteName) ? "Cantiere non indicato" : SiteName;
    public string SupplierDisplay => MaterialsOrderedByCustomer
        ? "Materiali ordinati dal cliente"
        : string.IsNullOrWhiteSpace(SupplierName) ? "Fornitore da indicare" : SupplierName;
    public string OrderDateDisplay => MaterialOrderDate?.ToLocalTime().ToString("dd/MM/yyyy") ?? "Da ordinare";
    public string DeliveryDateDisplay => ExpectedDeliveryDate?.ToLocalTime().ToString("dd/MM/yyyy") ?? "Non indicata";
    public string StatusDisplay => string.IsNullOrWhiteSpace(MaterialStatus) ? "DA ORDINARE" : MaterialStatus.Trim().ToUpperInvariant();
    public string StatusColor => SupplierOrderStatusOptions.GetColor(MaterialStatus);
    public string StatusTextColor => SupplierOrderStatusOptions.GetTextColor(MaterialStatus);
}

public sealed class SupplierOrderUpdate
{
    public string QuoteNumber { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public bool MaterialsOrderedByCustomer { get; set; }
    public DateTime? MaterialOrderDate { get; set; }
    public DateTime? ExpectedDeliveryDate { get; set; }
    public string MaterialStatus { get; set; } = string.Empty;
    public long ExpectedRevision { get; set; }
}

public static class SupplierOrderStatusOptions
{
    public static IReadOnlyList<string> All { get; } =
    [
        "DA ORDINARE",
        "ORDINATO",
        "DA RITIRARE",
        "IN MAGAZZINO",
        "CONSEGNATO",
        "NON DISPONIBILE"
    ];

    public static string GetColor(string? status) => Normalize(status) switch
    {
        "ORDINATO" => "#DBEAFE",
        "DA RITIRARE" => "#FEF3C7",
        "IN MAGAZZINO" => "#EDE9FE",
        "CONSEGNATO" => "#DCFCE7",
        "NON DISPONIBILE" => "#FEE2E2",
        _ => "#F3F4F6"
    };

    public static string GetTextColor(string? status) => Normalize(status) switch
    {
        "ORDINATO" => "#1D4ED8",
        "DA RITIRARE" => "#92400E",
        "IN MAGAZZINO" => "#6D28D9",
        "CONSEGNATO" => "#166534",
        "NON DISPONIBILE" => "#991B1B",
        _ => "#4B5563"
    };

    public static string Normalize(string? status)
    {
        string normalized = status?.Trim().ToUpperInvariant() ?? string.Empty;
        return All.Contains(normalized, StringComparer.OrdinalIgnoreCase) ? normalized : "DA ORDINARE";
    }
}

public static class SupplierOrderSortService
{
    public static IReadOnlyList<SupplierOrderSortOption> Options { get; } =
    [
        new(SupplierOrderSortMode.OrderDateDescending, "Data ordine - più recenti"),
        new(SupplierOrderSortMode.OrderDateAscending, "Data ordine - meno recenti"),
        new(SupplierOrderSortMode.ExpectedDeliveryAscending, "Consegna - più vicina"),
        new(SupplierOrderSortMode.ExpectedDeliveryDescending, "Consegna - più lontana"),
        new(SupplierOrderSortMode.CustomerAscending, "Cliente - A/Z"),
        new(SupplierOrderSortMode.CustomerDescending, "Cliente - Z/A"),
        new(SupplierOrderSortMode.Status, "Stato ordine")
    ];

    public static IReadOnlyList<SupplierOrderSummary> Sort(
        IEnumerable<SupplierOrderSummary> orders,
        SupplierOrderSortMode mode)
    {
        IOrderedEnumerable<SupplierOrderSummary> sorted = mode switch
        {
            SupplierOrderSortMode.OrderDateAscending => orders
                .OrderBy(OrderDateMissing)
                .ThenBy(order => order.MaterialOrderDate)
                .ThenByDescending(order => order.Date),
            SupplierOrderSortMode.ExpectedDeliveryAscending => orders
                .OrderBy(DeliveryDateMissing)
                .ThenBy(order => order.ExpectedDeliveryDate)
                .ThenByDescending(order => order.MaterialOrderDate),
            SupplierOrderSortMode.ExpectedDeliveryDescending => orders
                .OrderBy(DeliveryDateMissing)
                .ThenByDescending(order => order.ExpectedDeliveryDate)
                .ThenByDescending(order => order.MaterialOrderDate),
            SupplierOrderSortMode.CustomerAscending => orders
                .OrderBy(order => order.CustomerReferenceDisplay, StringComparer.CurrentCultureIgnoreCase),
            SupplierOrderSortMode.CustomerDescending => orders
                .OrderByDescending(order => order.CustomerReferenceDisplay, StringComparer.CurrentCultureIgnoreCase),
            SupplierOrderSortMode.Status => orders
                .OrderBy(order => GetStatusRank(order.MaterialStatus))
                .ThenBy(DeliveryDateMissing)
                .ThenBy(order => order.ExpectedDeliveryDate),
            _ => orders
                .OrderBy(OrderDateMissing)
                .ThenByDescending(order => order.MaterialOrderDate)
                .ThenByDescending(order => order.Date)
        };

        return sorted.ToArray();
    }

    private static int OrderDateMissing(SupplierOrderSummary order) => order.MaterialOrderDate.HasValue ? 0 : 1;
    private static int DeliveryDateMissing(SupplierOrderSummary order) => order.ExpectedDeliveryDate.HasValue ? 0 : 1;
    private static int GetStatusRank(string? status) => SupplierOrderStatusOptions.Normalize(status) switch
    {
        "DA ORDINARE" => 0,
        "ORDINATO" => 1,
        "DA RITIRARE" => 2,
        "IN MAGAZZINO" => 3,
        "CONSEGNATO" => 4,
        "NON DISPONIBILE" => 5,
        _ => 6
    };
}

public sealed record CompanyContact(string Name, string Email, string Address = "", string VatNumber = "");
