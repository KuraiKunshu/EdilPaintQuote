using System.Text.Json.Serialization;

namespace EdilPaintPreventibiviGen.Models;

/// <summary>Window dimensions in DIP at the window's unscaled size.</summary>
public sealed class WindowSizePreference
{
    public double Width { get; set; }
    public double Height { get; set; }
    public bool IsMaximized { get; set; }

    [JsonIgnore]
    public bool IsValid => double.IsFinite(Width) && Width > 0
        && double.IsFinite(Height) && Height > 0;

    internal WindowSizePreference CreateCopy() => new()
    {
        Width = Width,
        Height = Height,
        IsMaximized = IsMaximized
    };
}
