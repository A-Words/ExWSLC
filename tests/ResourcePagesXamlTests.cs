using System.IO;

namespace ExWSLC.Tests;

public class ResourcePagesXamlTests
{
    [Fact]
    public void Navigation_UsesSeparateNetworkAndVolumePages()
    {
        var sourceDirectory = GetSourceDirectory();
        var mainWindow = File.ReadAllText(Path.Combine(sourceDirectory, "MainWindow.xaml"));

        Assert.Contains("NavNetworks", mainWindow);
        Assert.Contains("TargetPageType=\"{x:Type pages:NetworksPage}\"", mainWindow);
        Assert.Contains("NavVolumes", mainWindow);
        Assert.Contains("TargetPageType=\"{x:Type pages:VolumesPage}\"", mainWindow);
        Assert.DoesNotContain("ResourcesPage", mainWindow);
    }

    [Fact]
    public void ResourceTables_ExposeTheRequestedFields()
    {
        var pagesDirectory = Path.Combine(GetSourceDirectory(), "Views", "Pages");
        var networksPage = File.ReadAllText(Path.Combine(pagesDirectory, "NetworksPage.xaml"));
        var volumesPage = File.ReadAllText(Path.Combine(pagesDirectory, "VolumesPage.xaml"));

        Assert.Contains("DisplayDriver", networksPage);
        Assert.Contains("DisplaySubnet", networksPage);
        Assert.Contains("DisplayGateway", networksPage);
        Assert.Contains("NetworkDriver", networksPage);
        Assert.Contains("NetworkOptions", networksPage);
        Assert.Contains("NetworkLabels", networksPage);
        Assert.Contains("AutomationProperties.LabeledBy", networksPage);
        Assert.Contains("NetworkName, UpdateSourceTrigger=PropertyChanged", networksPage);
        Assert.Contains("Header=\"{DynamicResource Advanced}\"", networksPage);
        Assert.Contains("HasRefreshError", networksPage);
        Assert.Contains("HasInspectOutput", networksPage);
        Assert.Contains("HasOperationOutput", networksPage);
        Assert.Contains("MaxHeight=\"380\"", networksPage);
        Assert.DoesNotContain("DynamicResource NetworkMode", networksPage);
        Assert.Contains("RemoveNetworkCommand", networksPage);
        Assert.Contains("DisplaySize", volumesPage);
        Assert.Contains("DisplayMountpoint", volumesPage);
        Assert.Contains("VolumeDriver", volumesPage);
        Assert.Contains("VolumeOptions", volumesPage);
        Assert.Contains("VolumeLabels", volumesPage);
        Assert.Contains("RemoveVolumeCommand", volumesPage);
        Assert.DoesNotContain("InspectVolumeCommand", volumesPage);
        Assert.DoesNotContain("InspectOutput", volumesPage);
        Assert.DoesNotContain("OperationOutput", volumesPage);
        Assert.DoesNotContain("VolumeCleanupOptions", volumesPage);
        Assert.True(volumesPage.IndexOf("DynamicResource Refresh", StringComparison.Ordinal) <
                    volumesPage.IndexOf("DynamicResource Prune", StringComparison.Ordinal));
    }

    private static string GetSourceDirectory() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "src"));
}
