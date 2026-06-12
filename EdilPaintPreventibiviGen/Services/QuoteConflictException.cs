namespace EdilPaintPreventibiviGen.Services;

public sealed class QuoteConflictException : InvalidOperationException
{
    public QuoteConflictException(string quoteNumber)
        : base($"Il preventivo {quoteNumber} ha una versione piu' recente nel database. La versione locale e' stata archiviata nella cartella Conflicts: ricarica lo storico prima di riprovare.")
    {
    }
}
