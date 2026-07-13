using ExWSLC.Models;
using ExWSLC.Services;
using ExWSLC.ViewModels;
using Moq;

namespace ExWSLC.Tests;

public class ResourcesViewModelTests
{
    [Fact]
    public async Task ResourceInspectOutputsAndCreateNamesRemainIndependent()
    {
        var runtime = new Mock<IContainerRuntime>();
        runtime.Setup(value => value.InspectResourceAsync("network", "frontend", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult(true, 0, "{\"Name\":\"frontend\"}", string.Empty, "wslc network inspect"));
        runtime.Setup(value => value.InspectResourceAsync("volume", "app-data", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult(true, 0, "{\"Name\":\"app-data\"}", string.Empty, "wslc volume inspect"));
        using var workspace = CreateWorkspace(runtime.Object);
        var viewModel = new ResourcesViewModel(workspace)
        {
            SelectedNetwork = new NetworkSummary("network-id", "frontend", "bridge", "local"),
            SelectedVolume = new VolumeSummary("app-data", "local", "/var/lib/data", "4096"),
            NetworkName = "new-network",
            VolumeName = "new-volume",
            ResourceOperationOutput = "create completed"
        };

        await viewModel.InspectNetworkCommand.ExecuteAsync(null);
        var networkOutput = viewModel.NetworkInspectOutput;
        await viewModel.InspectVolumeCommand.ExecuteAsync(null);

        Assert.Equal("new-network", viewModel.NetworkName);
        Assert.Equal("new-volume", viewModel.VolumeName);
        Assert.Equal("create completed", viewModel.ResourceOperationOutput);
        Assert.Equal(networkOutput, viewModel.NetworkInspectOutput);
        Assert.Contains("\"Name\": \"app-data\"", viewModel.VolumeInspectOutput);
    }

    private static RuntimeWorkspace CreateWorkspace(IContainerRuntime runtime)
    {
        var capabilities = new Mock<IRuntimeCapabilityService>();
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(value => value.Current).Returns(new AppSettings());
        var interaction = new Mock<IUserInteractionService>();
        interaction.Setup(value => value.ShowErrorAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        return new RuntimeWorkspace(runtime, capabilities.Object, settings.Object, new TaskService(), interaction.Object);
    }
}
