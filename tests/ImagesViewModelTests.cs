using ExWSLC.Models;
using ExWSLC.Services;
using ExWSLC.ViewModels;
using Moq;

namespace ExWSLC.Tests;

public class ImagesViewModelTests
{
    [Fact]
    public async Task BuildImage_UsesBuildFieldsWithoutChangingArchiveFields()
    {
        var runtime = new Mock<IContainerRuntime>();
        runtime.Setup(value => value.BuildImageAsync(
                "C:\\src\\app",
                "example/app:test",
                "Dockerfile.dev",
                It.IsAny<IProgress<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult(false, 1, string.Empty, "expected failure", "wslc image build"));
        using var workspace = CreateWorkspace(runtime.Object);
        var viewModel = new ImagesViewModel(workspace)
        {
            BuildContextPath = "C:\\src\\app",
            BuildImageTag = "example/app:test",
            DockerfilePath = "Dockerfile.dev",
            ArchivePath = "C:\\archives\\image.tar",
            ImportImageName = "imported:image"
        };

        await viewModel.BuildImageCommand.ExecuteAsync(null);

        runtime.VerifyAll();
        Assert.Equal("C:\\archives\\image.tar", viewModel.ArchivePath);
        Assert.Equal("imported:image", viewModel.ImportImageName);
        Assert.Equal("expected failure", viewModel.OperationOutput);
        Assert.Equal(string.Empty, viewModel.ImageInspectOutput);
    }

    [Fact]
    public async Task ImageInspect_FormatsJsonWithoutOverwritingOperationOutput()
    {
        var runtime = new Mock<IContainerRuntime>();
        runtime.Setup(value => value.InspectImageAsync("nginx:latest", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult(true, 0, "{\"Id\":\"sha256:abc\"}", string.Empty, "wslc image inspect"));
        using var workspace = CreateWorkspace(runtime.Object);
        var viewModel = new ImagesViewModel(workspace)
        {
            SelectedImage = new ImageSummary("sha256:abc", "nginx", "latest", "1024", "0"),
            OperationOutput = "pull completed"
        };
        Assert.False(viewModel.HasImageInspectOutput);

        await viewModel.InspectImageCommand.ExecuteAsync(null);

        Assert.Equal("pull completed", viewModel.OperationOutput);
        Assert.True(viewModel.HasImageInspectOutput);
        Assert.Contains(Environment.NewLine, viewModel.ImageInspectOutput);
        Assert.Contains("\"Id\": \"sha256:abc\"", viewModel.ImageInspectOutput);

        viewModel.SelectedImage = new ImageSummary("sha256:def", "alpine", "latest", "512", "0");
        Assert.False(viewModel.HasImageInspectOutput);
    }

    [Fact]
    public async Task Refresh_PreservesSelectedImageByIdentity()
    {
        var original = new ImageSummary("sha256:abc", "nginx", "latest", "1024", "0");
        var refreshed = new ImageSummary("sha256:abc", "nginx", "latest", "2048", "1");
        var runtime = CreateRefreshRuntime([refreshed]);
        using var workspace = CreateWorkspace(runtime.Object);
        workspace.Images.Add(original);
        var viewModel = new ImagesViewModel(workspace) { ImageSearchText = "nginx", SelectedImage = original };

        await workspace.RefreshAllAsync();

        Assert.Same(refreshed, viewModel.SelectedImage);
        Assert.Equal("2048", viewModel.SelectedImage.Size);
    }

    [Fact]
    public async Task PushImage_UsesSelectedImageInsteadOfPullReference()
    {
        var runtime = new Mock<IContainerRuntime>();
        runtime.Setup(value => value.PushImageAsync(
                "nginx:latest",
                It.IsAny<IProgress<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult(true, 0, "pushed", string.Empty, "wslc image push"));
        using var workspace = CreateWorkspace(runtime.Object);
        var viewModel = new ImagesViewModel(workspace)
        {
            SelectedImage = new ImageSummary("sha256:abc", "nginx", "latest", "1024", "0"),
            ImageReference = "ubuntu:latest"
        };

        await viewModel.PushImageCommand.ExecuteAsync(null);

        runtime.VerifyAll();
        Assert.Equal("pushed", viewModel.OperationOutput);
    }

    private static RuntimeWorkspace CreateWorkspace(IContainerRuntime runtime)
    {
        var capabilities = new Mock<IRuntimeCapabilityService>();
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(value => value.Current).Returns(new AppSettings());
        var interaction = new Mock<IUserInteractionService>();
        interaction.Setup(value => value.ShowErrorAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        return new RuntimeWorkspace(runtime, capabilities.Object, settings.Object, new ImmediateTaskService(), interaction.Object);
    }

    private static Mock<IContainerRuntime> CreateRefreshRuntime(IReadOnlyList<ImageSummary> images)
    {
        var runtime = new Mock<IContainerRuntime>();
        runtime.Setup(value => value.GetContainersAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        runtime.Setup(value => value.GetImagesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(images);
        runtime.Setup(value => value.GetNetworksAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        runtime.Setup(value => value.GetVolumesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        runtime.Setup(value => value.GetStatsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        return runtime;
    }

    private sealed class ImmediateTaskService : ITaskService
    {
        public IReadOnlyList<RuntimeTaskItem> Tasks => [];
        public event EventHandler? TasksChanged { add { } remove { } }
        public Task<OperationResult> RunAsync(
            string title,
            Func<IProgress<string>, CancellationToken, Task<OperationResult>> operation,
            CancellationToken cancellationToken = default) =>
            operation(new InlineProgress(), cancellationToken);
        public void ClearCompleted() { }

        private sealed class InlineProgress : IProgress<string>
        {
            public void Report(string value) { }
        }
    }
}
