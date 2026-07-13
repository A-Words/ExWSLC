using ExWSLC.Helpers;

namespace ExWSLC.Tests;

public class JsonFoldingStrategyTests
{
    [Fact]
    public void CreateFoldings_ReturnsSortedObjectAndArrayRegions()
    {
        const string json = """
            {
              "object": {
                "values": [
                  1,
                  2
                ]
              }
            }
            """;

        var foldings = JsonFoldingStrategy.CreateFoldings(json);

        Assert.Equal(3, foldings.Count);
        Assert.Equal(foldings.OrderBy(folding => folding.StartOffset), foldings);
        Assert.All(foldings, folding =>
        {
            Assert.Equal("…", folding.Name);
            Assert.False(folding.DefaultClosed);
            Assert.Contains(json[folding.StartOffset - 1], new[] { '{', '[' });
            Assert.Contains(json[folding.EndOffset], new[] { '}', ']' });
        });
    }

    [Fact]
    public void CreateFoldings_IgnoresDelimitersInsideStringsAndEscapedQuotes()
    {
        const string json = """
            {
              "text": "escaped quote: \" } ] { [",
              "value": 1
            }
            """;

        var folding = Assert.Single(JsonFoldingStrategy.CreateFoldings(json));

        Assert.Equal('{', json[folding.StartOffset - 1]);
        Assert.Equal('}', json[folding.EndOffset]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{\"object\":{},\"array\":[]}")]
    [InlineData("{\n  \"array\": [\n")]
    public void CreateFoldings_ReturnsNoRegionsForEmptySingleLineOrIncompleteInput(string? json)
    {
        Assert.Empty(JsonFoldingStrategy.CreateFoldings(json));
    }
}
