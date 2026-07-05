using ExWSLC.Models;
using ExWSLC.Services;
using ExWSLC.ViewModels;
using Moq;

namespace ExWSLC.Tests;

public class MainViewModelTests
{
    [Fact]
    public void SearchText_FiltersContainersAcrossNameImageAndId()
    {
        var viewModel = CreateViewModel();
        viewModel.Containers.Add(new ContainerSummary("abc", "web", "nginx", "running", "Up", "80", "now"));
        viewModel.Containers.Add(new ContainerSummary("def", "worker", "alpine", "stopped", "Exited", "", "now"));

        viewModel.SearchText = "nginx";

        Assert.Single(viewModel.VisibleContainerItems);
        Assert.Equal("web", viewModel.VisibleContainerItems[0].Container.Name);
        viewModel.Dispose();
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void ContainerListItem_DisplaysMissingPortsAsDash(string ports)
    {
        var item = new ContainerListItem
        {
            Container = new ContainerSummary("abc", "web", "nginx", "running", "Up", ports, "now")
        };

        Assert.Equal("-", item.Ports);
    }

    [Fact]
    public void ContainerSummary_ExposesDockerStyleShortIdAndDisplayPorts()
    {
        var container = new ContainerSummary(
            "1234567890abcdef",
            "web",
            "nginx",
            "running",
            "Up",
            """[{ "ContainerPort": 80, "HostPort": 8080 }]""",
            "now");

        Assert.Equal("1234567890ab", container.ShortId);
        Assert.Equal("8080:80", container.DisplayPorts);
    }

    [Fact]
    public void ContainerListItem_FormatsStructuredPortMappings()
    {
        const string ports = """
            [
              {
                "BindingAddress": "127.0.0.1",
                "ContainerPort": 80,
                "HostPort": 8080,
                "Protocol": 6
              }
            ]
            """;
        var item = new ContainerListItem
        {
            Container = new ContainerSummary("abc", "web", "nginx", "running", "Up", ports, "now")
        };

        Assert.Equal("8080:80", item.Ports);
    }

    [Fact]
    public void ContainerListItem_FormatsWrappedPortMappings()
    {
        const string ports = """
            {
              "ports": [
                { "ContainerPort": "80", "HostPort": "8080", "Protocol": "tcp" },
                { "ContainerPort": 53, "HostPort": 5353, "Protocol": 17 }
              ]
            }
            """;
        var item = new ContainerListItem
        {
            Container = new ContainerSummary("abc", "web", "nginx", "running", "Up", ports, "now")
        };

        Assert.Equal("8080:80, 5353:53", item.Ports);
    }

    [Theory]
    [InlineData("512 KiB / 15.49 GiB", "512 KiB")]
    [InlineData("21.54 MiB / 15.49 GiB", "21.54 MiB")]
    [InlineData("1.25 GiB / 15.49 GiB", "1.25 GiB")]
    public void ContainerListItem_DisplaysUsedMemoryOnly(string memory, string expected)
    {
        var item = new ContainerListItem
        {
            Container = new ContainerSummary("abc", "web", "nginx", "running", "Up", "80", "now"),
            Stats = new ContainerStats("abc", "web", "0.00%", memory, "0 B / 0 B", "0 B / 0 B", "1")
        };

        Assert.Equal(expected, item.Memory);
    }

    [Fact]
    public async Task InitializeAsync_RecoversBusyStateWhenRefreshFails()
    {
        var runtime = new Mock<IContainerRuntime>();
        runtime.Setup(value => value.GetContainersAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("runtime failed"));
        var capabilities = new Mock<IRuntimeCapabilityService>();
        capabilities.Setup(value => value.DetectAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new RuntimeCapabilities(true, "2.9.3", "2.9.3", [], "ready"));
        var viewModel = CreateViewModel(runtime, capabilities);

        await viewModel.InitializeAsync();

        Assert.False(viewModel.IsBusy);
        Assert.Contains("runtime failed", viewModel.StatusMessage);
        viewModel.Dispose();
    }

    [Fact]
    public async Task InitializeAsync_PopulatesGuidedDashboardWithRunningContainers()
    {
        var runtime = new Mock<IContainerRuntime>();
        var containers = Enumerable.Range(1, 5)
            .Select(index => new ContainerSummary($"id-{index}", $"running-{index}", "alpine", "running", "Up", string.Empty, "now"))
            .Append(new ContainerSummary("id-stopped", "stopped", "alpine", "stopped", "Exited", string.Empty, "now"))
            .ToArray();
        runtime.Setup(value => value.GetContainersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(containers);
        runtime.Setup(value => value.GetImagesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        runtime.Setup(value => value.GetNetworksAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        runtime.Setup(value => value.GetVolumesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        runtime.Setup(value => value.GetStatsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var capabilities = new Mock<IRuntimeCapabilityService>();
        capabilities.Setup(value => value.DetectAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new RuntimeCapabilities(true, "2.9.3", "2.9.3", [], "ready"));
        var viewModel = CreateViewModel(runtime, capabilities);

        await viewModel.InitializeAsync();

        Assert.Equal(5, viewModel.RunningContainerCount);
        Assert.Equal(4, viewModel.ActiveContainers.Count);
        Assert.All(viewModel.ActiveContainers, container => Assert.True(container.IsRunning));
        viewModel.Dispose();
    }

    [Fact]
    public async Task RefreshAllCommand_PreservesSelectedContainerAcrossCollectionReplacement()
    {
        var runtime = new Mock<IContainerRuntime>();
        runtime.SetupSequence(value => value.GetContainersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ContainerSummary("id-web", "web", "nginx:latest", "running", "Up", "8080:80", "now")
            ])
            .ReturnsAsync([
                new ContainerSummary("id-web", "web", "nginx:latest", "running", "Up 5 minutes", "8080:80", "later")
            ]);
        runtime.Setup(value => value.GetImagesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        runtime.Setup(value => value.GetNetworksAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        runtime.Setup(value => value.GetVolumesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        runtime.Setup(value => value.GetStatsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var capabilities = new Mock<IRuntimeCapabilityService>();
        capabilities.Setup(value => value.DetectAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new RuntimeCapabilities(true, "2.9.3", "2.9.3", [], "ready"));
        var viewModel = CreateViewModel(runtime, capabilities);

        await viewModel.InitializeAsync();
        viewModel.SelectedContainer = viewModel.VisibleContainerItems[0].Container;
        var firstSelection = viewModel.SelectedContainer;

        await viewModel.RefreshAllCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.SelectedContainer);
        Assert.NotSame(firstSelection, viewModel.SelectedContainer);
        Assert.Equal("id-web", viewModel.SelectedContainer.Id);
        Assert.Equal("Up 5 minutes", viewModel.SelectedContainer.Status);
        viewModel.Dispose();
    }

    [Fact]
    public async Task RefreshAllCommand_DoesNotRestoreContainerWhenSelectionChangesDuringRefresh()
    {
        var runtime = new Mock<IContainerRuntime>();
        var refreshedContainers = new TaskCompletionSource<IReadOnlyList<ContainerSummary>>();
        runtime.Setup(value => value.GetContainersAsync(It.IsAny<CancellationToken>())).Returns(refreshedContainers.Task);
        runtime.Setup(value => value.GetImagesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        runtime.Setup(value => value.GetNetworksAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        runtime.Setup(value => value.GetVolumesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        runtime.Setup(value => value.GetStatsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var viewModel = CreateViewModel(runtime);
        var selectedContainer = new ContainerSummary("id-web", "web", "nginx:latest", "running", "Up", "8080:80", "now");
        viewModel.SelectedContainer = selectedContainer;

        var refresh = viewModel.RefreshAllCommand.ExecuteAsync(null);
        viewModel.SelectedContainer = null;
        refreshedContainers.SetResult([
            new ContainerSummary("id-web", "web", "nginx:latest", "running", "Up 5 minutes", "8080:80", "later")
        ]);

        await refresh;

        Assert.Null(viewModel.SelectedContainer);
        viewModel.Dispose();
    }

    private static MainViewModel CreateViewModel(Mock<IContainerRuntime>? runtime = null, Mock<IRuntimeCapabilityService>? capabilities = null)
    {
        runtime ??= new Mock<IContainerRuntime>();
        capabilities ??= new Mock<IRuntimeCapabilityService>();
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(value => value.Current).Returns(new AppSettings());
        return new MainViewModel(runtime.Object, capabilities.Object, settings.Object, new TaskService(), Mock.Of<IUserInteractionService>());
    }
}
