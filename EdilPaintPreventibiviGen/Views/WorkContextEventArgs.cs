using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Views;

public sealed class WorkContextEventArgs(string number, string title, WorkScheduleEntry? entry = null) : EventArgs
{
    public string QuoteNumber { get; } = number;
    public string Title { get; } = title;
    public WorkScheduleEntry? Entry { get; } = entry;
}
