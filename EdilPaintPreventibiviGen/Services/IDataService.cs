using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;
// ... existing code ...
public interface IDataService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    bool CanSynchronize { get; }
    
    // Customers
    Task<List<Customer>> GetCustomersAsync(CancellationToken cancellationToken = default);
    Task<Customer> AddCustomerAsync(Customer customer, CancellationToken cancellationToken = default);
    Task<Customer> UpdateCustomerAsync(string originalBusinessName, Customer customer);
    Task DeleteCustomerAsync(Customer customer);
    
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
    Task<List<QuoteHistorySummary>> GetQuoteSummariesAsync(int take, CancellationToken cancellationToken = default);
    Task<List<QuoteHistorySummary>> SearchQuoteSummariesAsync(
        string searchText,
        int take,
        CancellationToken cancellationToken = default);
    Task<HashSet<string>> GetAllQuoteNumbersAsync();
    Task<Dictionary<string, QuoteMetadata>> GetQuoteMetadataAsync(CancellationToken cancellationToken = default);
    Task<List<QuoteHistoryEntry>> GetQuotesByNumbersAsync(IEnumerable<string> quoteNumbers, CancellationToken cancellationToken = default);
    Task<QuoteHistoryEntry?> GetQuoteByNumberAsync(string quoteNumber);
    Task SaveQuoteAsync(QuoteHistoryEntry quote, CancellationToken cancellationToken = default);
    Task DeleteQuoteAsync(string quoteNumber);
    Task UpdateQuoteNotesAsync(string quoteNumber, string notes, CancellationToken cancellationToken = default);
    Task UpdateQuoteStatusAsync(string quoteNumber, QuoteStatus status, CancellationToken cancellationToken = default);
    Task UpdateQuoteSendInfoAsync(string quoteNumber, QuoteSendInfo sendInfo, CancellationToken cancellationToken = default);
    Task RegisterQuoteReminderAsync(string quoteNumber, QuoteReminderInfo reminderInfo, CancellationToken cancellationToken = default);
    
    // Utilities
    Task<int> GetNextQuoteNumberAsync();
    Task<bool> IsDatabaseEmptyAsync();
}

public class QuoteMetadata
{
    public string QuoteNumber { get; set; } = string.Empty;
    public DateTime LastModifiedUtc { get; set; }
    public string SyncHash { get; set; } = string.Empty;
}
