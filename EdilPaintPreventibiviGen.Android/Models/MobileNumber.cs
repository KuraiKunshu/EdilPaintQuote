using System.Globalization;

namespace EdilPaintPreventibiviGen.Android.Models;

public static class MobileNumber
{
    public static bool TryParse(string? text, out double value) =>
        double.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
}
