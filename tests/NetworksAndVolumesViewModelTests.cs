using ExWSLC.Models;
using ExWSLC.Services;
using ExWSLC.ViewModels;
using Moq;

namespace ExWSLC.Tests;

public class NetworksAndVolumesViewModelTests
{
    [Fact]
    public async Task NetworkInspectOutput_DoesNotChangeVolumeCreateName()
    {
        var runtime = CreateRuntime();
        runtime.Setup(value => value.InspectResourceAsync("network", "frontend", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult(true, 0, "{\"Name\":\"frontend\"}", string.Empty, "wslc network inspect"));
        using var workspace = CreateWorkspace(runtime.Object);
        var network = new NetworkSummary("network-id", "frontend", "bridge", "local", "172.20.0.0/16", "172.20.0.1");
        var networksViewModel = new NetworksViewModel(workspace) { NetworkName = "new-network", OperationOutput = "network operation" };
        var volumesViewModel = new VolumesViewModel(workspace) { VolumeName = "new-volume" };

        await networksViewModel.InspectNetworkCommand.ExecuteAsync(network);

        Assert.Equal("new-network", networksViewModel.NetworkName);
        Assert.Equal("new-volume", volumesViewModel.VolumeName);
        Assert.Equal("network operation", networksViewModel.OperationOutput);
        Assert.Contains("\"Name\": \"frontend\"", networksViewModel.InspectOutput);
    }

    [Fact]
    public async Task CreateCommandsTrimNamesAndUseTheCorrectRuntimeOperation()
    {
        var runtime = CreateRuntime();
        runtime.Setup(value => value.CreateNetworkAsync(It.IsAny<NetworkCreateSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult(true, 0, "created network", string.Empty, "wslc network create"));
        runtime.Setup(value => value.CreateVolumeAsync(It.IsAny<VolumeCreateSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult(true, 0, "created volume", string.Empty, "wslc volume create"));
        using var workspace = CreateWorkspace(runtime.Object);
        var networksViewModel = new NetworksViewModel(workspace)
        {
            NetworkName = "  frontend  ",
            NetworkDriver = " custom-driver ",
            NetworkOptions = "mtu=1500\r\nisolation=strict",
            NetworkLabels = "environment=development\nowner=platform team"
        };
        var volumesViewModel = new VolumesViewModel(workspace)
        {
            VolumeName = "  app-data  ",
            VolumeDriver = " vhd ",
            VolumeOptions = "size=2GB\r\ndynamic=true",
            VolumeLabels = "environment=development\nowner=platform team"
        };

        await networksViewModel.CreateNetworkCommand.ExecuteAsync(null);
        await volumesViewModel.CreateVolumeCommand.ExecuteAsync(null);

        runtime.Verify(value => value.CreateNetworkAsync(
            It.Is<NetworkCreateSpec>(spec =>
                spec.Name == "frontend" &&
                spec.Driver == "custom-driver" &&
                spec.DriverOptions.SequenceEqual(new[] { "mtu=1500", "isolation=strict" }) &&
                spec.Labels.SequenceEqual(new[] { "environment=development", "owner=platform team" })),
            It.IsAny<CancellationToken>()), Times.Once);
        runtime.Verify(value => value.CreateVolumeAsync(
            It.Is<VolumeCreateSpec>(spec =>
                spec.Name == "app-data" &&
                spec.Driver == "vhd" &&
                spec.DriverOptions.SequenceEqual(new[] { "size=2GB", "dynamic=true" }) &&
                spec.Labels.SequenceEqual(new[] { "environment=development", "owner=platform team" })),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains("created network", networksViewModel.OperationOutput);
        Assert.Empty(networksViewModel.NetworkName);
        Assert.Empty(volumesViewModel.VolumeName);
    }

    [Fact]
    public void CreateNetworkCommand_IsDisabledUntilANameIsProvided()
    {
        var runtime = CreateRuntime();
        using var workspace = CreateWorkspace(runtime.Object);
        var viewModel = new NetworksViewModel(workspace);

        Assert.False(viewModel.CreateNetworkCommand.CanExecute(null));

        viewModel.NetworkName = "frontend";

        Assert.True(viewModel.CreateNetworkCommand.CanExecute(null));
    }

    [Fact]
    public void SearchText_FiltersNetworkAndVolumeCollections()
    {
        var runtime = CreateRuntime();
        using var workspace = CreateWorkspace(runtime.Object);
        workspace.Networks.Add(new NetworkSummary("frontend-id", "frontend", "bridge", "local", string.Empty, string.Empty));
        workspace.Networks.Add(new NetworkSummary("backend-id", "backend", "bridge", "local", string.Empty, string.Empty));
        workspace.Volumes.Add(new VolumeSummary("frontend-data", "guest", "/data/frontend", "4096"));
        workspace.Volumes.Add(new VolumeSummary("backend-data", "guest", "/data/backend", "4096"));
        var networksViewModel = new NetworksViewModel(workspace);
        var volumesViewModel = new VolumesViewModel(workspace);

        networksViewModel.SearchText = "front";
        volumesViewModel.SearchText = "front";

        Assert.Equal("frontend", Assert.Single(networksViewModel.VisibleNetworks).Name);
        Assert.Equal("frontend-data", Assert.Single(volumesViewModel.VisibleVolumes).Name);
    }

    [Fact]
    public async Task RefreshFailure_IsExposedWithoutReplacingExistingNetworks()
    {
        var runtime = CreateRuntime();
        runtime.Setup(value => value.GetNetworksAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("network inventory unavailable"));
        using var workspace = CreateWorkspace(runtime.Object);
        workspace.Networks.Add(new NetworkSummary("network-id", "frontend", "bridge", "local", string.Empty, string.Empty));
        var viewModel = new NetworksViewModel(workspace);

        await workspace.RefreshAllAsync();

        Assert.True(viewModel.HasRefreshError);
        Assert.Equal("network inventory unavailable", viewModel.RefreshError);
        Assert.Single(viewModel.Networks);
    }

    [Fact]
    public async Task RemoveCommandsStopWhenConfirmationIsDeclined()
    {
        var runtime = CreateRuntime();
        using var workspace = CreateWorkspace(runtime.Object, confirm: false);
        var network = new NetworkSummary("network-id", "frontend", "bridge", "local", string.Empty, string.Empty);
        var volume = new VolumeSummary("app-data", "guest", "/var/lib/data", "4096");
        var networksViewModel = new NetworksViewModel(workspace);
        var volumesViewModel = new VolumesViewModel(workspace);

        await networksViewModel.RemoveNetworkCommand.ExecuteAsync(network);
        await volumesViewModel.RemoveVolumeCommand.ExecuteAsync(volume);

        runtime.Verify(value => value.RemoveNetworkAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        runtime.Verify(value => value.RemoveVolumeAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task VolumeDeleteAndPrune_UseDefaultRuntimeOptions()
    {
        var runtime = CreateRuntime();
        runtime.Setup(value => value.RemoveVolumeAsync("app-data", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult(true, 0, "removed", string.Empty, "wslc volume remove"));
        runtime.Setup(value => value.PruneVolumesAsync(It.IsAny<VolumePruneSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult(true, 0, "pruned", string.Empty, "wslc volume prune"));
        using var workspace = CreateWorkspace(runtime.Object);
        var volume = new VolumeSummary("app-data", "guest", "/var/lib/data", "4096");
        var viewModel = new VolumesViewModel(workspace);

        await viewModel.RemoveVolumeCommand.ExecuteAsync(volume);
        await viewModel.PruneVolumesCommand.ExecuteAsync(null);

        runtime.Verify(value => value.RemoveVolumeAsync("app-data", false, It.IsAny<CancellationToken>()), Times.Once);
        runtime.Verify(value => value.PruneVolumesAsync(
            It.Is<VolumePruneSpec>(spec =>
                !spec.All && spec.Filters.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Mock<IContainerRuntime> CreateRuntime()
    {
        var runtime = new Mock<IContainerRuntime>();
        runtime.Setup(value => value.GetContainersAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        runtime.Setup(value => value.GetImagesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        runtime.Setup(value => value.GetNetworksAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        runtime.Setup(value => value.GetVolumesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        runtime.Setup(value => value.GetStatsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        return runtime;
    }

    private static RuntimeWorkspace CreateWorkspace(IContainerRuntime runtime, bool confirm = true)
    {
        var capabilities = new Mock<IRuntimeCapabilityService>();
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(value => value.Current).Returns(new AppSettings());
        var interaction = new Mock<IUserInteractionService>();
        interaction.Setup(value => value.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(confirm);
        interaction.Setup(value => value.ShowErrorAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        return new RuntimeWorkspace(runtime, capabilities.Object, settings.Object, new TaskService(), interaction.Object);
    }
}
