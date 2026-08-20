using CharterTrip.Core.Services;

namespace CharterTrip.Tests;

public class TimeTextTests
{
    [Theory]
    [InlineData("9:00 AM", 540)]
    [InlineData("10:00 AM", 600)]
    [InlineData("12:00 PM", 720)]   // noon, not midnight
    [InlineData("4:00 PM", 960)]
    [InlineData("11:45 PM", 1425)]
    [InlineData("4pm", 960)]
    [InlineData("  8:30 am  ", 510)]
    public void Parses_clock_times(string text, int expected) =>
        Assert.Equal(expected, TimeText.ToMinutes(text));

    [Theory]
    [InlineData("12:00 AM", 1440)]  // the nightcap belongs at the END of the night
    [InlineData("1:00 AM", 1500)]
    [InlineData("2:30 AM", 1590)]
    public void After_midnight_sorts_to_the_end_of_the_night(string text, int expected) =>
        Assert.Equal(expected, TimeText.ToMinutes(text));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("TBD")]
    [InlineData("after dinner")]
    [InlineData("13:00 PM")]        // 13 is not a valid 12-hour clock hour
    [InlineData("4:75 PM")]
    [InlineData("16:00")]           // 24-hour clock isn't what people type here
    public void Unparseable_text_sorts_last(string? text) =>
        Assert.Equal(TimeText.Unparseable, TimeText.ToMinutes(text));

    [Fact]
    public void Midnight_sorts_after_the_late_evening()
    {
        Assert.True(TimeText.ToMinutes("12:00 AM") > TimeText.ToMinutes("11:30 PM"));
        Assert.True(TimeText.ToMinutes("TBD") > TimeText.ToMinutes("12:00 AM"));
    }
}
