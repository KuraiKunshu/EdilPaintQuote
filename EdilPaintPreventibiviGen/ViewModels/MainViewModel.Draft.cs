using System.Diagnostics;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;

namespace EdilPaintPreventibiviGen.ViewModels;

public partial class MainViewModel
{
    private readonly LocalDraftService _draftService = new(LocalApplicationDataService.GetDataDirectoryPath());

    public async Task<QuoteHistoryEntry?> LoadDraftAsync(CancellationToken cancellationToken = default)
    {
        return await _draftService.LoadAsync(cancellationToken);
    }

    public async Task SaveDraftAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_isGeneratingPdf || _isGeneratingCostsPdf)
                return;

            if (!HasDraftContent())
            {
                await _draftService.DeleteAsync();
                return;
            }

            await _draftService.SaveAsync(CreateDraftEntry(), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Draft] Salvataggio bozza non riuscito: {ex.Message}");
        }
    }

    public Task DiscardDraftAsync() => _draftService.DeleteAsync();

    public void ApplyDraft(QuoteHistoryEntry draft)
    {
        ResetQuote();

        QuoteNumber = string.IsNullOrWhiteSpace(draft.QuoteNumber)
            ? _companyData.Counter.ToString()
            : draft.QuoteNumber;

        SelectedCustomer = AllCustomers.FirstOrDefault(c =>
            c.BusinessName.Equals(draft.CustomerName, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(draft.ReferenceName))
        {
            IsSecondCustomerEnabled = true;
            SelectedSecondCustomer = AllCustomers.FirstOrDefault(c =>
                c.BusinessName.Equals(draft.ReferenceName, StringComparison.OrdinalIgnoreCase));
        }

        PaymentTerms = draft.PaymentTerms;
        IvaType = !string.IsNullOrWhiteSpace(draft.IvaType) ? draft.IvaType : IvaType;
        MaterialDiscount = draft.MaterialDiscount;
        LaborDiscount = draft.LaborDiscount;
        IsJointVenture = draft.IsJointVenture;
        PartnerCompanyName = draft.PartnerCompanyName;

        Materials.Clear();
        foreach (var item in draft.Materials)
            Materials.Add(CloneItem(item));

        Labors.Clear();
        foreach (var item in draft.Labors)
            Labors.Add(CloneItem(item));

        AttachedImages.Clear();
        foreach (var attachment in draft.Attachments)
        {
            AttachedImages.Add(new SelectedAttachment
            {
                FileName = attachment.FileName,
                FilePath = string.Empty,
                ContentType = attachment.ContentType,
                Content = attachment.Content
            });
        }

        OurCosts.Clear();
        foreach (var cost in draft.OurCosts)
            OurCosts.Add(CloneCost(cost));

        PartnerCosts.Clear();
        foreach (var cost in draft.PartnerCosts)
            PartnerCosts.Add(CloneCost(cost));

        AdditionalCosts.Clear();
        foreach (var cost in draft.AdditionalCosts)
            AdditionalCosts.Add(CloneCost(cost));

        _isEditingExistingQuote = false;
        _hasPersistedCurrentQuote = false;
        _loadedQuoteDate = null;
        _loadedQuoteBaseVersionUtc = default;

        UpdateItemSortOrders();
        CalculateTotals();
    }

    private QuoteHistoryEntry CreateDraftEntry()
    {
        string deviceName = DeviceNameService.GetCurrentDeviceName();
        return new QuoteHistoryEntry
        {
            QuoteNumber = QuoteNumber,
            Date = _loadedQuoteDate ?? DateTime.Now,
            CustomerName = SelectedCustomer?.BusinessName ?? string.Empty,
            ReferenceName = IsSecondCustomerEnabled ? SelectedSecondCustomer?.BusinessName ?? string.Empty : string.Empty,
            PaymentTerms = PaymentTerms,
            IvaType = IvaType,
            Materials = Materials.Select(CloneItem).ToList(),
            Labors = Labors.Select(CloneItem).ToList(),
            Imponibile = Imponibile,
            MaterialDiscount = MaterialDiscount,
            LaborDiscount = LaborDiscount,
            Total = TotaleGenerale,
            Status = QuoteStatus.Bozza,
            CreatedByDevice = deviceName,
            LastModifiedByDevice = deviceName,
            IsJointVenture = IsJointVenture,
            PartnerCompanyName = PartnerCompanyName,
            OurCosts = OurCosts.Select(CloneCost).ToList(),
            PartnerCosts = PartnerCosts.Select(CloneCost).ToList(),
            AdditionalCosts = AdditionalCosts.Select(CloneCost).ToList(),
            Attachments = AttachedImages.Select(a => new StoredFile
            {
                FileName = a.FileName,
                ContentType = a.ContentType,
                Content = a.Content,
                ImportedAt = DateTime.UtcNow
            }).ToList(),
            Events =
            [
                new QuoteEventEntry
                {
                    CreatedAtUtc = DateTime.UtcNow,
                    DeviceName = deviceName,
                    EventType = "bozza",
                    Description = "Bozza autosalvata"
                }
            ]
        };
    }

    private bool HasDraftContent()
    {
        return SelectedCustomer != null ||
               SelectedSecondCustomer != null ||
               Materials.Count > 0 ||
               Labors.Count > 0 ||
               AttachedImages.Count > 0 ||
               OurCosts.Count > 0 ||
               PartnerCosts.Count > 0 ||
               AdditionalCosts.Count > 0 ||
               !string.IsNullOrWhiteSpace(InputName) ||
               !string.IsNullOrWhiteSpace(InputDescription) ||
               InputValue != 0 ||
               InputQuantity != 1 ||
               MaterialDiscount != 0 ||
               LaborDiscount != 0 ||
               IsJointVenture;
    }

    private static Item CloneItem(Item item)
    {
        return new Item
        {
            Name = item.Name,
            Description = item.Description,
            UnitPrice = item.UnitPrice,
            Quantity = item.Quantity,
            Discount = item.Discount,
            IsSignificant = item.IsSignificant,
            SortOrder = item.SortOrder
        };
    }

    private static CostAllocationItem CloneCost(CostAllocationItem item)
    {
        return new CostAllocationItem
        {
            Description = item.Description,
            Amount = item.Amount,
            Notes = item.Notes
        };
    }
}
