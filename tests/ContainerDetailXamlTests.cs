using System.IO;

namespace ExWSLC.Tests;

public class ContainerDetailXamlTests
{
    [Fact]
    public void ContainerInspectOutput_UsesOneWayBindingForReadOnlyViewModelProperty()
    {
        var xamlPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "src",
            "Views",
            "Pages",
            "Containers",
            "ContainerDetailView.xaml"));

        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("Text=\"{Binding InspectOutput, Mode=OneWay}\"", xaml);
        Assert.DoesNotContain("Text=\"{Binding InspectOutput}\"", xaml);
    }
}
