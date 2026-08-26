namespace EdilPaintPreventibiviGen.Android.Services;

public sealed class DatabaseWriteConflictException : InvalidOperationException
{
    public DatabaseWriteConflictException(string message) : base(message)
    {
    }
}
