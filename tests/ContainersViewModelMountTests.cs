using ExWSLC.Models;
using ExWSLC.Services;
using ExWSLC.ViewModels;
using Moq;

namespace ExWSLC.Tests;

public class ContainersViewModelMountTests
{
    private const string InspectPayload = """
        {
          "Mounts": [
            {
              "Type": "bind",
              "Source": "C:\\workspace\\data",
              "Destination": "/workspace/data",
              "ReadWrite": true
            }
          ]
        }
        """;

    [Fact]
    public async Task MountsTab_LoadsDetailsOnDemandAndCachesTheResult()
    {
        var runtime = new Mock<IContainerRuntime>();
        runtime.Setup(value => value.InspectContainerAsync("container-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Success(InspectPayload));
        using var workspace = CreateWorkspace(runtime.Object);
        var viewModel = CreateViewModel(workspace, "container-id");

        runtime.Verify(value => value.InspectContainerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        viewModel.SelectedDetailTabIndex = 3;
        await WaitForAsync(() => viewModel.MountDetails is not null);

        var mount = Assert.Single(viewModel.MountDetails!.Mounts);
        Assert.Equal(ContainerMountKind.Bind, mount.Kind);
        Assert.False(viewModel.IsMountDetailsLoading);

        viewModel.SelectedDetailTabIndex = 0;
        viewModel.SelectedDetailTabIndex = 3;
        await Task.Delay(30, TestContext.Current.CancellationToken);

        runtime.Verify(value => value.InspectContainerAsync("container-id", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MountsTab_TreatsEmptyMountArrayAsSuccessfulEmptyState()
    {
        var runtime = new Mock<IContainerRuntime>();
        runtime.Setup(value => value.InspectContainerAsync("container-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Success("""{ "Mounts": [] }"""));
        using var workspace = CreateWorkspace(runtime.Object);
        var viewModel = CreateViewModel(workspace, "container-id");

        viewModel.SelectedDetailTabIndex = 3;
        await WaitForAsync(() => viewModel.MountDetails is not null);

        Assert.False(viewModel.MountDetails!.HasMounts);
        Assert.Empty(viewModel.MountDetails.Mounts);
        Assert.False(viewModel.HasMountDetailsError);
        Assert.False(viewModel.IsMountDetailsLoading);
    }

    [Fact]
    public async Task MountsTab_ShowsARecoverablePageErrorWhenInspectFails()
    {
        var runtime = new Mock<IContainerRuntime>();
        runtime.Setup(value => value.InspectContainerAsync("container-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult(false, 1, string.Empty, "inspect failed", "wslc container inspect"));
        using var workspace = CreateWorkspace(runtime.Object);
        var viewModel = CreateViewModel(workspace, "container-id");

        viewModel.SelectedDetailTabIndex = 3;
        await WaitForAsync(() => viewModel.HasMountDetailsError);

        Assert.Equal("inspect failed", viewModel.MountDetailsError);
        Assert.Null(viewModel.MountDetails);
        Assert.False(viewModel.IsMountDetailsLoading);
    }

    [Fact]
    public async Task RefreshMountDetailsCommand_BypassesTheCache()
    {
        const string refreshedPayload = """
            {
              "Mounts": [
                {
                  "Type": "bind",
                  "Source": "C:\\refreshed-cache",
                  "Destination": "/cache",
                  "ReadWrite": false
                }
              ]
            }
            """;
        var runtime = new Mock<IContainerRuntime>();
        var inspectionCount = 0;
        runtime.Setup(value => value.InspectContainerAsync("container-id", It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(Success(
                Interlocked.Increment(ref inspectionCount) == 1 ? InspectPayload : refreshedPayload)));
        using var workspace = CreateWorkspace(runtime.Object);
        var viewModel = CreateViewModel(workspace, "container-id");

        viewModel.SelectedDetailTabIndex = 3;
        await WaitForAsync(() => viewModel.MountDetails is not null);
        Assert.Equal(@"C:\workspace\data", viewModel.MountDetails!.Mounts[0].Source);

        await viewModel.RefreshMountDetailsCommand.ExecuteAsync(null);
        await WaitForAsync(() => viewModel.MountDetails?.Mounts.FirstOrDefault()?.Source == @"C:\refreshed-cache");

        Assert.Equal(2, Volatile.Read(ref inspectionCount));
        Assert.Equal(ContainerMountAccess.ReadOnly, viewModel.MountDetails!.Mounts[0].Access);
    }

    [Fact]
    public async Task MountsTab_RetriesWhenReturningBeforeCancelledLoadCompletes()
    {
        var runtime = new Mock<IContainerRuntime>();
        var firstInspection = new TaskCompletionSource<OperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inspectionCount = 0;
        runtime.Setup(value => value.InspectContainerAsync("container-id", It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken cancellationToken) =>
                Interlocked.Increment(ref inspectionCount) == 1
                    ? firstInspection.Task.WaitAsync(cancellationToken)
                    : Task.FromResult(Success(InspectPayload)));
        using var workspace = CreateWorkspace(runtime.Object);
        var viewModel = CreateViewModel(workspace, "container-id");

        viewModel.SelectedDetailTabIndex = 3;
        await WaitForAsync(() => Volatile.Read(ref inspectionCount) == 1);
        viewModel.SelectedDetailTabIndex = 0;
        viewModel.SelectedDetailTabIndex = 3;
        await WaitForAsync(() => Volatile.Read(ref inspectionCount) == 2);
        await WaitForAsync(() => viewModel.MountDetails is not null);

        Assert.False(viewModel.IsMountDetailsLoading);
        Assert.Single(viewModel.MountDetails!.Mounts);
    }

    [Fact]
    public async Task MountsTab_ShowsCachedDetailsWhileCancelledRefreshWindsDown()
    {
        var runtime = new Mock<IContainerRuntime>();
        var cancelledRefresh = new TaskCompletionSource<OperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inspectionCount = 0;
        runtime.Setup(value => value.InspectContainerAsync("container-id", It.IsAny<CancellationToken>()))
            .Returns(() => Interlocked.Increment(ref inspectionCount) == 1
                ? Task.FromResult(Success(InspectPayload))
                : cancelledRefresh.Task);
        using var workspace = CreateWorkspace(runtime.Object);
        var viewModel = CreateViewModel(workspace, "container-id");

        viewModel.SelectedDetailTabIndex = 3;
        await WaitForAsync(() => viewModel.MountDetails is not null);

        var refresh = viewModel.RefreshMountDetailsCommand.ExecuteAsync(null);
        await WaitForAsync(() => viewModel.IsMountDetailsLoading);
        viewModel.SelectedDetailTabIndex = 0;
        viewModel.SelectedDetailTabIndex = 3;
        await WaitForAsync(() => !viewModel.IsMountDetailsLoading);

        Assert.Single(viewModel.MountDetails!.Mounts);
        Assert.Equal(2, Volatile.Read(ref inspectionCount));

        cancelledRefresh.SetResult(Success(InspectPayload));
        await refresh;
    }

    [Fact]
    public async Task MountsTab_IgnoresAStaleResultAfterContainerChanges()
    {
        const string secondPayload = """
            {
              "Mounts": [
                {
                  "Type": "bind",
                  "Source": "C:\\second-cache",
                  "Destination": "/cache",
                  "ReadWrite": true
                }
              ]
            }
            """;
        var runtime = new Mock<IContainerRuntime>();
        var firstInspection = new TaskCompletionSource<OperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Setup(value => value.InspectContainerAsync("first-id", It.IsAny<CancellationToken>()))
            .Returns(firstInspection.Task);
        runtime.Setup(value => value.InspectContainerAsync("second-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Success(secondPayload));
        using var workspace = CreateWorkspace(runtime.Object);
        var viewModel = CreateViewModel(workspace, "first-id");

        viewModel.SelectedDetailTabIndex = 3;
        await WaitForAsync(() => viewModel.IsMountDetailsLoading);
        viewModel.SelectedContainer = CreateContainer("second-id");
        viewModel.SelectedDetailTabIndex = 3;
        await WaitForAsync(() => viewModel.MountDetails?.Mounts.FirstOrDefault()?.Source == @"C:\second-cache");

        firstInspection.SetResult(Success(InspectPayload));
        await Task.Delay(30, TestContext.Current.CancellationToken);

        Assert.Equal(@"C:\second-cache", Assert.Single(viewModel.MountDetails!.Mounts).Source);
        Assert.Equal(string.Empty, viewModel.MountDetailsError);
    }

    private static ContainersViewModel CreateViewModel(RuntimeWorkspace workspace, string containerId) => new(workspace)
    {
        SelectedContainer = CreateContainer(containerId)
    };

    private static ContainerSummary CreateContainer(string id) =>
        new(id, $"container-{id}", "nginx", "Running", "Up", "8080:80", "now");

    private static OperationResult Success(string output) =>
        new(true, 0, output, string.Empty, "wslc container inspect");

    private static RuntimeWorkspace CreateWorkspace(IContainerRuntime runtime)
    {
        var capabilities = new Mock<IRuntimeCapabilityService>();
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(value => value.Current).Returns(new AppSettings());
        var interaction = new Mock<IUserInteractionService>();
        interaction.Setup(value => value.ShowErrorAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        return new RuntimeWorkspace(runtime, capabilities.Object, settings.Object, new TaskService(), interaction.Object);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition()) return;
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("The expected asynchronous mount state was not reached.");
    }
}
