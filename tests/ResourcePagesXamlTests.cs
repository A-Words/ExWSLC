using System.IO;
using System.Text.RegularExpressions;

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
        var dialogsDirectory = Path.Combine(GetSourceDirectory(), "Views", "Dialogs");
        var networksPage = File.ReadAllText(Path.Combine(pagesDirectory, "NetworksPage.xaml"));
        var volumesPage = File.ReadAllText(Path.Combine(pagesDirectory, "VolumesPage.xaml"));
        var networkCreateDialog = File.ReadAllText(Path.Combine(dialogsDirectory, "NetworkCreateDialogContent.xaml"));
        var volumeCreateDialog = File.ReadAllText(Path.Combine(dialogsDirectory, "VolumeCreateDialogContent.xaml"));

        Assert.Contains("DisplayDriver", networksPage);
        Assert.Contains("<ui:DataGrid", networksPage);
        Assert.Contains("DisplaySubnet", networksPage);
        Assert.Contains("DisplayGateway", networksPage);
        Assert.Contains("NetworkDriver", networkCreateDialog);
        Assert.Contains("NetworkOptions", networkCreateDialog);
        Assert.Contains("NetworkLabels", networkCreateDialog);
        Assert.Contains("AutomationProperties.LabeledBy", networkCreateDialog);
        Assert.Contains("NetworkName, UpdateSourceTrigger=PropertyChanged", networkCreateDialog);
        Assert.Contains("Header=\"{DynamicResource Advanced}\"", networkCreateDialog);
        Assert.Contains("HasRefreshError", networksPage);
        Assert.Contains("HasInspectOutput", networksPage);
        Assert.Contains("HasOperationOutput", networksPage);
        Assert.Contains("MaxHeight=\"380\"", networksPage);
        Assert.DoesNotContain("DynamicResource NetworkMode", networksPage);
        Assert.Contains("RemoveNetworkCommand", networksPage);
        Assert.Contains("DisplaySize", volumesPage);
        Assert.Contains("<ui:DataGrid", volumesPage);
        Assert.Contains("DisplayMountpoint", volumesPage);
        Assert.Contains("VolumeDriver", volumeCreateDialog);
        Assert.Contains("VolumeOptions", volumeCreateDialog);
        Assert.Contains("VolumeLabels", volumeCreateDialog);
        Assert.Contains("RemoveVolumeCommand", volumesPage);
        Assert.DoesNotContain("InspectVolumeCommand", volumesPage);
        Assert.DoesNotContain("InspectOutput", volumesPage);
        Assert.DoesNotContain("OperationOutput", volumesPage);
        Assert.DoesNotContain("VolumeCleanupOptions", volumesPage);
        Assert.True(volumesPage.IndexOf("DynamicResource Refresh", StringComparison.Ordinal) <
                    volumesPage.IndexOf("DynamicResource Prune", StringComparison.Ordinal));
    }

    [Fact]
    public void ResourcePages_UseTheSharedFluentShellAndDialogHost()
    {
        var sourceDirectory = GetSourceDirectory();
        var pagesDirectory = Path.Combine(sourceDirectory, "Views", "Pages");
        var pages = new[]
        {
            File.ReadAllText(Path.Combine(pagesDirectory, "Containers", "ContainerListView.xaml")),
            File.ReadAllText(Path.Combine(pagesDirectory, "ImagesPage.xaml")),
            File.ReadAllText(Path.Combine(pagesDirectory, "NetworksPage.xaml")),
            File.ReadAllText(Path.Combine(pagesDirectory, "VolumesPage.xaml"))
        };

        Assert.All(pages, page =>
        {
            Assert.Contains("ResourcePageTitleStyle", page);
            Assert.Contains("ResourceToolbarStyle", page);
            Assert.Contains("ResourceSurfaceStyle", page);
            Assert.Contains("AutomationProperties.Name", page);
        });

        var mainWindow = File.ReadAllText(Path.Combine(sourceDirectory, "MainWindow.xaml"));
        Assert.Contains("ContentDialogHost", mainWindow);
        Assert.Contains("IsDisableSiblingsEnabled=\"True\"", mainWindow);
    }

    [Fact]
    public void FluentControls_DoNotFallBackToNativeVariantsOrDetachedStyles()
    {
        var sourceDirectory = GetSourceDirectory();
        var xamlSources = Directory.GetFiles(sourceDirectory, "*.xaml", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();
        var combinedXaml = string.Join(Environment.NewLine, xamlSources);

        Assert.DoesNotMatch(new Regex(@"<MenuItem(?:\s|>)"), combinedXaml);
        Assert.DoesNotMatch(new Regex(@"<DataGrid\s"), combinedXaml);
        Assert.DoesNotContain("DetailCardButtonStyle", combinedXaml);

        var tasksPage = File.ReadAllText(Path.Combine(sourceDirectory, "Views", "Pages", "TasksPage.xaml"));
        var containerDetail = File.ReadAllText(Path.Combine(sourceDirectory, "Views", "Pages", "Containers", "ContainerDetailView.xaml"));
        Assert.Contains("TargetType=\"ui:ProgressRing\" BasedOn=\"{StaticResource {x:Type ui:ProgressRing}}\"", tasksPage);
        Assert.Contains("TargetType=\"ui:SymbolIcon\" BasedOn=\"{StaticResource {x:Type ui:SymbolIcon}}\"", containerDetail);
    }

    private static string GetSourceDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var sourceDirectory = Path.Combine(directory.FullName, "src");
            if (File.Exists(Path.Combine(sourceDirectory, "ExWSLC.csproj"))) return sourceDirectory;
        }

        throw new DirectoryNotFoundException("Could not locate the ExWSLC source directory.");
    }
}
