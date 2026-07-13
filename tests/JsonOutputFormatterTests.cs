using ExWSLC.Helpers;

namespace ExWSLC.Tests;

public class JsonOutputFormatterTests
{
    [Fact]
    public void Format_IndentsValidJson()
    {
        var formatted = JsonOutputFormatter.Format("{\"name\":\"web\",\"ports\":[80]}");

        Assert.Contains(Environment.NewLine, formatted);
        Assert.Contains("  \"name\": \"web\"", formatted);
        Assert.Contains("  \"ports\": [", formatted);
    }

    [Theory]
    [InlineData("  plain output  ", "plain output")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Format_PreservesNonJsonOutputAndNormalizesWhitespace(string input, string expected)
    {
        Assert.Equal(expected, JsonOutputFormatter.Format(input));
    }
}
