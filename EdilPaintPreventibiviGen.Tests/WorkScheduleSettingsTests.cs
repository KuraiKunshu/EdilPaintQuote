using EdilPaintPreventibiviGen.Models;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class WorkScheduleSettingsTests
{
    [Fact]
    public void DefaultsCoverFullDayAndTwoSeparateHalfDays()
    {
        var settings = new WorkScheduleSettings().CreateValidatedCopy();
        Assert.Equal((480, 1020), settings.GetRange(WorkScheduleSlotKind.FullDay));
        Assert.Equal((480, 720), settings.GetRange(WorkScheduleSlotKind.Morning));
        Assert.Equal((780, 1020), settings.GetRange(WorkScheduleSlotKind.Afternoon));
        Assert.True(settings.UseEmployeeAbbreviations);
    }

    [Theory]
    [InlineData("08:00", 480)]
    [InlineData("8:30", 510)]
    [InlineData(" 13:45 ", 825)]
    [InlineData("00:00", 0)]
    [InlineData("23:59", 1439)]
    public void StandardTimeInputAcceptsHourAndMinute(string text, int expected)
    {
        Assert.True(WorkScheduleSettings.TryParseTime(text, out int minutes));
        Assert.Equal(expected, minutes);
        Assert.Equal($"{expected / 60:00}:{expected % 60:00}", WorkScheduleSettings.FormatTime(minutes));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("24:00")]
    [InlineData("12:60")]
    [InlineData("8")]
    [InlineData("08:00:00")]
    public void InvalidTimeInputIsRejected(string? text)
        => Assert.False(WorkScheduleSettings.TryParseTime(text, out _));

    [Theory]
    [InlineData(-1, 600)]
    [InlineData(600, 600)]
    [InlineData(700, 600)]
    [InlineData(600, 1440)]
    public void RangesMustStayWithinOneDayAndEndAfterTheirStart(int start, int end)
        => Assert.Throws<InvalidOperationException>(() => WorkScheduleSettings.ValidateRange(start, end));

    [Fact]
    public void HalfDaysMustFitInsideFullDayWithoutOverlapping()
    {
        var settings = new WorkScheduleSettings { MorningEndMinutes = 14 * 60 };
        Assert.Throws<InvalidOperationException>(() => settings.CreateValidatedCopy());
        settings = new() { MorningStartMinutes = 7 * 60 };
        Assert.Throws<InvalidOperationException>(() => settings.CreateValidatedCopy());
        settings = new() { AfternoonEndMinutes = 18 * 60 };
        Assert.Throws<InvalidOperationException>(() => settings.CreateValidatedCopy());
    }

    [Fact]
    public void ValidatedCopyPreservesSharedRevisionAndDisplayChoice()
    {
        var original = new WorkScheduleSettings { Revision = 12, UseEmployeeAbbreviations = false };
        var copy = original.CreateValidatedCopy();
        Assert.NotSame(original, copy);
        Assert.Equal(12, copy.Revision);
        Assert.False(copy.UseEmployeeAbbreviations);
        copy.MorningEndMinutes = 11 * 60;
        Assert.Equal(12 * 60, original.MorningEndMinutes);
    }

    [Fact]
    public void CustomInterventionsRequireTheirOwnTimeRange()
        => Assert.Throws<InvalidOperationException>(() => new WorkScheduleSettings().GetRange(WorkScheduleSlotKind.Custom));
}
