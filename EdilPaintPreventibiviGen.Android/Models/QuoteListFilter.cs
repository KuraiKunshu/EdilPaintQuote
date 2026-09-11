namespace EdilPaintPreventibiviGen.Android.Models;

public sealed class QuoteListFilter
{
    public DateTime? From { get; init; }
    public DateTime? Until { get; init; }
    public bool SentOpenOnly { get; init; }
    public int Sort { get; init; }

    public IReadOnlyList<QuoteSummary> Apply(IEnumerable<QuoteSummary> quotes)
    {
        var filtered = quotes.Where(q => (!From.HasValue || q.Date.Date >= From.Value.Date) &&
            (!Until.HasValue || q.Date.Date <= Until.Value.Date) && (!SentOpenOnly ||
            q.SentAtUtc.HasValue && q.Status is not (QuoteStatus.Confermato or QuoteStatus.Finito or QuoteStatus.Rifiutato or QuoteStatus.Archiviato)));
        return (Sort switch
        {
            1 => filtered.OrderBy(q => q.Date),
            2 => filtered.OrderBy(q => q.CustomerName, StringComparer.CurrentCultureIgnoreCase),
            3 => filtered.OrderByDescending(q => q.Total),
            _ => filtered.OrderByDescending(q => q.Date)
        }).ThenBy(q => q.QuoteNumber, StringComparer.Ordinal).ToArray();
    }
}
