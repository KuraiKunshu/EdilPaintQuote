using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;
// ... existing code ...
public interface IDataService
{
    Task InitializeAsync();
    
    // Customers
    Task<List<Customer>> GetCustomersAsync();
    Task<Customer> AddCustomerAsync(Customer customer);
    Task DeleteCustomerAsync(string businessName);
    
    // Company
    Task<Company?> GetCompanyAsync();
    Task SaveCompanyAsync(Company company, string selectedLogo);
    
    // Catalog
    Task<List<Item>> GetLaborCatalogAsync();
    Task SaveLaborCatalogAsync(IEnumerable<Item> labors);
    Task<List<Item>> GetPersonalMaterialsAsync();
    Task SavePersonalMaterialsAsync(IEnumerable<Item> materials);
    
    // Quotes
    Task<List<QuoteHistoryEntry>> GetQuotesAsync();
    Task<List<QuoteHistoryEntry>> GetQuotesAsync(int take);
    Task<List<QuoteHistorySummary>> GetQuoteSummariesAsync(int take);
    Task<List<QuoteHistorySummary>> SearchQuoteSummariesAsync(string searchText, int take);
    Task<HashSet<string>> GetAllQuoteNumbersAsync();
    Task<Dictionary<string, QuoteMetadata>> GetQuoteMetadataAsync();
    Task<List<QuoteHistoryEntry>> GetQuotesByNumbersAsync(IEnumerable<string> quoteNumbers);
    Task<QuoteHistoryEntry?> GetQuoteByNumberAsync(string quoteNumber);
    Task SaveQuoteAsync(QuoteHistoryEntry quote);
    Task DeleteQuoteAsync(string quoteNumber);
    
    // Utilities
    Task<int> GetNextQuoteNumberAsync();
    Task<bool> IsDatabaseEmptyAsync();
    Task<byte[]?> GetQuotePdfContentAsync(string quoteNumber);
    Task<List<StoredFile>> GetQuoteAttachmentsAsync(string quoteNumber);
}

public class QuoteMetadata
{
    public string QuoteNumber { get; set; } = string.Empty;
    public DateTime LastModifiedUtc { get; set; }
    public string SyncHash { get; set; } = string.Empty;
}