namespace EdilPaintPreventibiviGen.Services;

public sealed class QuoteConflictException : InvalidOperationException
{
    public QuoteConflictException(string quoteNumber)
        : base($"Il preventivo {quoteNumber} e' stato modificato da un altro PC. La versione locale e' stata archiviata nella cartella Conflicts: ricarica lo storico prima di riprovare.")
    {
    }
}
