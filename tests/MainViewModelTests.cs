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

    private static MainViewModel CreateViewModel(Mock<IContainerRuntime>? runtime = null, Mock<IRuntimeCapabilityService>? capabilities = null)
    {
        runtime ??= new Mock<IContainerRuntime>();
        capabilities ??= new Mock<IRuntimeCapabilityService>();
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(value => value.Current).Returns(new AppSettings());
        return new MainViewModel(runtime.Object, capabilities.Object, settings.Object, new TaskService(), Mock.Of<IUserInteractionService>());
    }
}
