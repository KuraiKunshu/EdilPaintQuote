namespace EdilPaintPreventibiviGen.Services;

public sealed class DatabaseWritePendingException : Exception
{
    public DatabaseWritePendingException(string message) : base(message)
    {
    }
}
