using ExWSLC.Models;
using ExWSLC.Services;
using ExWSLC.ViewModels;
using Moq;

namespace ExWSLC.Tests;

public class ContainersViewModelInspectTests
{
    private const string FirstInspectPayload = """
        {
          "Id": "container-id",
          "Name": "web",
          "Image": "nginx:latest",
          "State": { "Status": "running", "Running": true },
          "Config": { "Cmd": ["nginx"], "Env": ["MODE=production"] },
          "HostConfig": { "NetworkMode": "bridge", "Memory": 268435456 }
        }
        """;

    [Fact]
    public async Task ConfigurationAndInspectTabs_ShareOneLazyCachedInspection()
    {
        var runtime = new Mock<IContainerRuntime>();
        runtime.Setup(value => value.InspectContainerAsync("container-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Success(FirstInspectPayload));
        using var workspace = CreateWorkspace(runtime.Object);
        var viewModel = CreateViewModel(workspace);

        runtime.Verify(value => value.InspectContainerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        viewModel.SelectedDetailTabIndex = 4;
        await WaitForAsync(() => viewModel.InspectDetails is not null);

        Assert.Equal("nginx", viewModel.InspectDetails!.Config.DisplayCommand);
        Assert.Equal("MODE", Assert.Single(viewModel.InspectDetails.EnvironmentVariables).Key);
        Assert.False(viewModel.IsInspectDetailsLoading);

        viewModel.SelectedDetailTabIndex = 5;
        await Task.Delay(30, TestContext.Current.CancellationToken);

        runtime.Verify(value => value.InspectContainerAsync("container-id", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains("\"Name\": \"web\"", viewModel.InspectOutput);
    }

    [Fact]
    public async Task InspectCommand_BypassesTheCacheAndReplacesConfiguration()
    {
        const string refreshedPayload = """{ "Id": "container-id", "Name": "web", "Config": { "Cmd": ["sleep", "60"] } }""";
        var runtime = new Mock<IContainerRuntime>();
        var inspectCount = 0;
        runtime.Setup(value => value.InspectContainerAsync("container-id", It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(Success(
                Interlocked.Increment(ref inspectCount) == 1 ? FirstInspectPayload : refreshedPayload)));
        using var workspace = CreateWorkspace(runtime.Object);
        var viewModel = CreateViewModel(workspace);

        viewModel.SelectedDetailTabIndex = 5;
        await WaitForAsync(() => viewModel.InspectDetails is not null);
        await viewModel.InspectContainerCommand.ExecuteAsync(null);

        Assert.Equal(2, Volatile.Read(ref inspectCount));
        Assert.Equal("sleep 60", viewModel.InspectDetails!.Config.DisplayCommand);
        Assert.NotNull(viewModel.InspectDetailsUpdatedAt);
    }

    [Fact]
    public async Task InspectTab_IgnoresStaleResultsAfterContainerChanges()
    {
        var runtime = new Mock<IContainerRuntime>();
        var firstInspect = new TaskCompletionSource<OperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Setup(value => value.InspectContainerAsync("container-id", It.IsAny<CancellationToken>()))
            .Returns(firstInspect.Task);
        runtime.Setup(value => value.InspectContainerAsync("second-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Success("""{ "Id": "second-id", "Name": "worker", "Config": { "Cmd": ["worker"] } }"""));
        using var workspace = CreateWorkspace(runtime.Object);
        var viewModel = CreateViewModel(workspace);

        viewModel.SelectedDetailTabIndex = 5;
        await WaitForAsync(() => viewModel.IsInspectDetailsLoading);
        viewModel.SelectedContainer = CreateContainer("second-id", "worker");
        viewModel.SelectedDetailTabIndex = 5;
        await WaitForAsync(() => viewModel.InspectDetails?.Id == "second-id");

        firstInspect.SetResult(Success(FirstInspectPayload));
        await Task.Delay(30, TestContext.Current.CancellationToken);

        Assert.Equal("second-id", viewModel.InspectDetails!.Id);
        Assert.Equal(string.Empty, viewModel.InspectDetailsError);
    }

    private static ContainersViewModel CreateViewModel(RuntimeWorkspace workspace) => new(workspace)
    {
        SelectedContainer = CreateContainer("container-id", "web")
    };

    private static ContainerSummary CreateContainer(string id, string name) =>
        new(id, name, "nginx:latest", "running", "Up", "8080:80", "now");

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

        throw new TimeoutException("The expected asynchronous inspect state was not reached.");
    }
}
