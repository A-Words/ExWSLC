using ExWSLC.Models;
using ExWSLC.Services;
using ExWSLC.ViewModels;
using Moq;

namespace ExWSLC.Tests;

public class ContainersViewModelNetworkTests
{
    private const string InspectPayload = """
        {
          "HostConfig": { "NetworkMode": "bridge" },
          "State": { "Running": true },
          "NetworkSettings": {
            "Networks": {
              "bridge": {
                "Gateway": "172.20.0.1",
                "IPAddress": "172.20.0.2",
                "IPPrefixLen": 16,
                "MacAddress": "02:42:ac:14:00:02"
              }
            }
          },
          "Ports": { "80/tcp": [{ "HostIp": "127.0.0.1", "HostPort": "8080" }] }
        }
        """;

    [Fact]
    public async Task NetworkTab_LoadsDetailsOnDemandAndCachesTheResult()
    {
        var runtime = new Mock<IContainerRuntime>();
        runtime.Setup(value => value.InspectContainerAsync("container-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult(true, 0, InspectPayload, string.Empty, "wslc container inspect"));
        using var workspace = CreateWorkspace(runtime.Object);
        var viewModel = new ContainersViewModel(workspace)
        {
            SelectedContainer = new ContainerSummary("container-id", "web", "nginx", "Running", "Up", "8080:80", "now")
        };

        viewModel.SelectedDetailTabIndex = 2;
        await WaitForAsync(() => viewModel.NetworkDetails is not null);

        Assert.Equal("bridge", viewModel.NetworkDetails!.NetworkMode);
        Assert.False(workspace.IsBusy);
        viewModel.SelectedDetailTabIndex = 0;
        viewModel.SelectedDetailTabIndex = 2;
        await Task.Delay(30, TestContext.Current.CancellationToken);
        runtime.Verify(value => value.InspectContainerAsync("container-id", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NetworkTab_ShowsARecoverablePageErrorWhenInspectFails()
    {
        var runtime = new Mock<IContainerRuntime>();
        runtime.Setup(value => value.InspectContainerAsync("container-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult(false, 1, string.Empty, "inspect failed", "wslc container inspect"));
        using var workspace = CreateWorkspace(runtime.Object);
        var viewModel = new ContainersViewModel(workspace)
        {
            SelectedContainer = new ContainerSummary("container-id", "web", "nginx", "Running", "Up", "8080:80", "now")
        };

        viewModel.SelectedDetailTabIndex = 2;
        await WaitForAsync(() => viewModel.HasNetworkDetailsError);

        Assert.Equal("inspect failed", viewModel.NetworkDetailsError);
        Assert.Null(viewModel.NetworkDetails);
        Assert.False(workspace.IsBusy);
    }

    [Fact]
    public async Task NetworkTab_RetriesWhenReturningBeforeACancelledLoadCompletes()
    {
        var runtime = new Mock<IContainerRuntime>();
        var firstInspection = new TaskCompletionSource<OperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inspectionCount = 0;
        runtime.Setup(value => value.InspectContainerAsync("container-id", It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken cancellationToken) =>
                Interlocked.Increment(ref inspectionCount) == 1
                    ? firstInspection.Task.WaitAsync(cancellationToken)
                    : Task.FromResult(new OperationResult(true, 0, InspectPayload, string.Empty, "wslc container inspect")));
        using var workspace = CreateWorkspace(runtime.Object);
        var viewModel = new ContainersViewModel(workspace)
        {
            SelectedContainer = new ContainerSummary("container-id", "web", "nginx", "Running", "Up", "8080:80", "now")
        };

        viewModel.SelectedDetailTabIndex = 2;
        await WaitForAsync(() => Volatile.Read(ref inspectionCount) == 1);
        viewModel.SelectedDetailTabIndex = 0;
        viewModel.SelectedDetailTabIndex = 2;
        await WaitForAsync(() => Volatile.Read(ref inspectionCount) == 2);
        await WaitForAsync(() => viewModel.NetworkDetails is not null);

        Assert.Equal("bridge", viewModel.NetworkDetails!.NetworkMode);
        Assert.False(viewModel.IsNetworkDetailsLoading);
    }

    [Fact]
    public async Task NetworkTab_IgnoresErrorsFromACancelledSupersededLoad()
    {
        var runtime = new Mock<IContainerRuntime>();
        var firstInspection = new TaskCompletionSource<OperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inspectionCount = 0;
        runtime.Setup(value => value.InspectContainerAsync("container-id", It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken cancellationToken) =>
                Interlocked.Increment(ref inspectionCount) == 1
                    ? firstInspection.Task
                    : Task.FromResult(new OperationResult(true, 0, InspectPayload, string.Empty, "wslc container inspect")));
        using var workspace = CreateWorkspace(runtime.Object);
        var viewModel = new ContainersViewModel(workspace)
        {
            SelectedContainer = new ContainerSummary("container-id", "web", "nginx", "Running", "Up", "8080:80", "now")
        };

        viewModel.SelectedDetailTabIndex = 2;
        await WaitForAsync(() => Volatile.Read(ref inspectionCount) == 1);
        viewModel.SelectedDetailTabIndex = 0;
        viewModel.SelectedDetailTabIndex = 2;
        await WaitForAsync(() => viewModel.NetworkDetails is not null);

        firstInspection.SetException(new InvalidOperationException("stale inspect failure"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => firstInspection.Task);
        await Task.Delay(20, TestContext.Current.CancellationToken);

        Assert.NotNull(viewModel.NetworkDetails);
        Assert.Equal(string.Empty, viewModel.NetworkDetailsError);
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

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition()) return;
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("The expected asynchronous network state was not reached.");
    }
}
