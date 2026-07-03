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

        Assert.Single(viewModel.VisibleContainers);
        Assert.Equal("web", viewModel.VisibleContainers[0].Name);
        viewModel.Dispose();
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

    private static MainViewModel CreateViewModel(Mock<IContainerRuntime>? runtime = null, Mock<IRuntimeCapabilityService>? capabilities = null)
    {
        runtime ??= new Mock<IContainerRuntime>();
        capabilities ??= new Mock<IRuntimeCapabilityService>();
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(value => value.Current).Returns(new AppSettings());
        return new MainViewModel(runtime.Object, capabilities.Object, settings.Object, new TaskService(), Mock.Of<IUserInteractionService>());
    }
}
