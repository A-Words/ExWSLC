using System.Globalization;
using ExWSLC.Models;

namespace ExWSLC.Tests;

public class ImageSummaryTests
{
    [Theory]
    [InlineData("161308134", "161.31 MB")]
    [InlineData("1000", "1 KB")]
    [InlineData("999", "999 B")]
    [InlineData("", "-")]
    [InlineData("already formatted", "already formatted")]
    public void DisplaySize_FormatsRawBytesAndPreservesText(string size, string expected)
    {
        var image = new ImageSummary("id", "repository", "latest", size, string.Empty);

        Assert.Equal(expected, image.DisplaySize);
    }

    [Fact]
    public void DisplayCreated_FormatsUnixTimestampInLocalTime()
    {
        const long unixSeconds = 1782264118;
        var expected = DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
            .ToLocalTime()
            .ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
        var image = new ImageSummary("id", "repository", "latest", string.Empty, unixSeconds.ToString(CultureInfo.InvariantCulture));

        Assert.Equal(expected, image.DisplayCreated);
    }

    [Fact]
    public void DisplayCreated_PreservesRelativeText()
    {
        var image = new ImageSummary("id", "repository", "latest", string.Empty, "3 weeks ago");

        Assert.Equal("3 weeks ago", image.DisplayCreated);
    }
}
