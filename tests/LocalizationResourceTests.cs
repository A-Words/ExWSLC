using System.IO;
using System.Text.RegularExpressions;

namespace ExWSLC.Tests;

public class LocalizationResourceTests
{
    private static readonly Regex DynamicResourcePattern = new(
        "\\{DynamicResource\\s+([A-Za-z_][A-Za-z0-9_]*)\\}",
        RegexOptions.Compiled);
    private static readonly Regex ResourceKeyPattern = new(
        "x:Key=\"([A-Za-z_][A-Za-z0-9_]*)\"",
        RegexOptions.Compiled);
    private static readonly HashSet<string> ThemeResourceKeys =
    [
        "AccentFillColorDefaultBrush",
        "DefaultControlFocusVisualStyle",
        "SegoeFluentIcons"
    ];

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void EveryLocalizedDynamicResourceExistsInEachLanguage(string language)
    {
        var sourceDirectory = GetSourceDirectory();
        var usedKeys = Directory.EnumerateFiles(sourceDirectory, "*.xaml", SearchOption.AllDirectories)
            .SelectMany(path => DynamicResourcePattern.Matches(File.ReadAllText(path)).Select(match => match.Groups[1].Value))
            .Where(key => !ThemeResourceKeys.Contains(key))
            .ToHashSet(StringComparer.Ordinal);
        var dictionaryPath = Path.Combine(sourceDirectory, "Resources", $"Strings.{language}.xaml");
        var dictionaryText = File.ReadAllText(dictionaryPath);
        var definedKeys = ResourceKeyPattern.Matches(dictionaryText)
            .Select(match => match.Groups[1].Value)
            .ToArray();

        var missingKeys = usedKeys.Except(definedKeys, StringComparer.Ordinal).Order().ToArray();
        var duplicateKeys = definedKeys.GroupBy(key => key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .Order()
            .ToArray();

        Assert.True(missingKeys.Length == 0, $"Missing {language} resources: {string.Join(", ", missingKeys)}");
        Assert.True(duplicateKeys.Length == 0, $"Duplicate {language} resources: {string.Join(", ", duplicateKeys)}");
    }

    private static string GetSourceDirectory() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "src"));
}
