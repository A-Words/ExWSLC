using System.IO;
using System.Text.RegularExpressions;

namespace ExWSLC.Tests;

public class XamlBindingTests
{
    [Fact]
    public void EveryReadOnlyOutputTextBox_UsesOneWayBinding()
    {
        var sourceDirectory = TestPaths.SourceDirectory;
        var readOnlyTextBoxes = Directory.EnumerateFiles(sourceDirectory, "*.xaml", SearchOption.AllDirectories)
            .SelectMany(path => Regex.Matches(File.ReadAllText(path), "<ui:TextBox[^>]*IsReadOnly=\"True\"[^>]*/>")
                .Select(match => (Path: path, Markup: match.Value)))
            .ToArray();

        Assert.NotEmpty(readOnlyTextBoxes);
        Assert.All(readOnlyTextBoxes, item =>
            Assert.Contains("Mode=OneWay", item.Markup));
    }
}
