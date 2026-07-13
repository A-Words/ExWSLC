using System.IO;

namespace ExWSLC.Tests;

public class ContainerDetailXamlTests
{
    [Fact]
    public void ContainerInspectOutput_UsesOneWayBindingForReadOnlyViewModelProperty()
    {
        var xamlPath = Path.Combine(
            TestPaths.SourceDirectory,
            "Views",
            "Pages",
            "Containers",
            "ContainerDetailView.xaml");

        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("<controls:JsonTextViewer", xaml);
        Assert.Contains("JsonText=\"{Binding InspectOutput, Mode=OneWay}\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"{DynamicResource Inspect}\"", xaml);
        Assert.DoesNotContain("JsonText=\"{Binding InspectOutput}\"", xaml);
    }
}
