using System.Globalization;
using System.Windows;
using EdilPaintPreventibiviGen.Converters;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class FlexibleDoubleConverterTests
{
    private readonly FlexibleDoubleConverter _converter = new();

    [Theory]
    [InlineData("12,50", 12.5)]
    [InlineData("12.50", 12.5)]
    [InlineData("1.234,50", 1234.5)]
    [InlineData("1,234.50", 1234.5)]
    public void ConvertBackAcceptsItalianAndInternationalDecimals(string text, double expected)
    {
        object value = _converter.ConvertBack(text, typeof(double), null!, CultureInfo.InvariantCulture);

        Assert.Equal(expected, Assert.IsType<double>(value));
    }

    [Fact]
    public void ConvertUsesItalianDecimalSeparator()
    {
        object value = _converter.Convert(12.5d, typeof(string), null!, CultureInfo.InvariantCulture);

        Assert.Equal("12,50", value);
    }

    [Fact]
    public void ConvertBackRejectsAnInvalidValue()
    {
        object value = _converter.ConvertBack("12,5,0", typeof(double), null!, CultureInfo.InvariantCulture);

        Assert.Same(DependencyProperty.UnsetValue, value);
    }
}
