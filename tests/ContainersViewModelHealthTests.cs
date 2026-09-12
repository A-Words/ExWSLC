using ExWSLC.Models;
using ExWSLC.Services;
using ExWSLC.ViewModels;
using Moq;

namespace ExWSLC.Tests;

public class ContainersViewModelHealthTests
{
    [Fact]
    public async Task HealthTab_SharesInspectionCacheAndRefreshesHealth()
    {
        using var fixture = new Fixture();
        var status = "starting";
        fixture.Runtime.Setup(x => x.InspectContainerAsync("one", It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(Payload("one", status)));
        fixture.ViewModel.SelectedDetailTabIndex = 6;
        Assert.Equal(ContainerHealthStatus.Starting, fixture.ViewModel.InspectDetails!.Health.Status);
        fixture.ViewModel.SelectedDetailTabIndex = 4;
        fixture.ViewModel.SelectedDetailTabIndex = 5;
        fixture.ViewModel.SelectedDetailTabIndex = 6;
        fixture.Runtime.Verify(x => x.InspectContainerAsync("one", It.IsAny<CancellationToken>()), Times.Once);
        status = "healthy";
        await fixture.Workspace.RefreshAllAsync();
        Assert.Equal(ContainerHealthStatus.Healthy, fixture.ViewModel.InspectDetails!.Health.Status);
        fixture.Runtime.Verify(x => x.InspectContainerAsync("one", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task HealthTab_DiscardsCancelledResultWhenSelectionChanges()
    {
        using var fixture = new Fixture();
        var pending = new TaskCompletionSource<OperationResult>();
        CancellationToken firstToken = default;
        fixture.Runtime.Setup(x => x.InspectContainerAsync("one", It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((_, token) => firstToken = token).Returns(pending.Task);
        fixture.Runtime.Setup(x => x.InspectContainerAsync("two", It.IsAny<CancellationToken>())).ReturnsAsync(Payload("two", "unhealthy"));
        fixture.ViewModel.SelectedDetailTabIndex = 6;
        fixture.ViewModel.SelectWithoutFollowingLogs(Container("two"));
        Assert.True(firstToken.IsCancellationRequested);
        Assert.Null(fixture.ViewModel.InspectDetails);
        fixture.ViewModel.SelectedDetailTabIndex = 6;
        pending.SetResult(Payload("one", "healthy"));
        await Task.Delay(30, TestContext.Current.CancellationToken);
        Assert.Equal("two", fixture.ViewModel.InspectDetails!.Id);
        Assert.Equal(ContainerHealthStatus.Unhealthy, fixture.ViewModel.InspectDetails.Health.Status);
    }

    [Fact]
    public async Task HealthRefresh_ClearsOldRecordsAndReportsFailure()
    {
        using var fixture = new Fixture();
        var pending = new TaskCompletionSource<OperationResult>();
        fixture.Runtime.SetupSequence(x => x.InspectContainerAsync("one", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Payload("one", "healthy")).Returns(pending.Task);
        fixture.ViewModel.SelectedDetailTabIndex = 6;
        var refresh = fixture.ViewModel.InspectContainerCommand.ExecuteAsync(null);
        Assert.Null(fixture.ViewModel.InspectDetails);
        Assert.True(fixture.ViewModel.IsInspectDetailsLoading);
        pending.SetResult(new(false, 1, "", "inspection failed", ""));
        await refresh;
        Assert.Null(fixture.ViewModel.InspectDetails);
        Assert.True(fixture.ViewModel.HasInspectDetailsError);
        Assert.False(fixture.ViewModel.IsInspectDetailsLoading);
    }

    [Fact]
    public async Task HealthTab_LeavingAndReturningDoesNotAcceptOldRequest()
    {
        using var fixture = new Fixture();
        var first = new TaskCompletionSource<OperationResult>();
        fixture.Runtime.SetupSequence(x => x.InspectContainerAsync("one", It.IsAny<CancellationToken>()))
            .Returns(first.Task).ReturnsAsync(Payload("one", "unhealthy"));
        fixture.ViewModel.SelectedDetailTabIndex = 6;
        fixture.ViewModel.SelectedDetailTabIndex = 1;
        fixture.ViewModel.SelectedDetailTabIndex = 6;
        first.SetResult(Payload("one", "healthy"));
        await Task.Delay(30, TestContext.Current.CancellationToken);
        Assert.Equal(ContainerHealthStatus.Unhealthy, fixture.ViewModel.InspectDetails!.Health.Status);
        Assert.False(fixture.ViewModel.IsInspectDetailsLoading);
    }

    [Fact]
    public void HealthTab_RejectsMismatchedInspectIdentity()
    {
        using var fixture = new Fixture();
        fixture.Runtime.Setup(x => x.InspectContainerAsync("one", It.IsAny<CancellationToken>())).ReturnsAsync(Payload("two", "healthy"));
        fixture.ViewModel.SelectedDetailTabIndex = 6;
        Assert.Null(fixture.ViewModel.InspectDetails);
        Assert.True(fixture.ViewModel.HasInspectDetailsError);
    }

    [Theory]
    [InlineData(CapabilitySupport.Unknown)] [InlineData(CapabilitySupport.Unsupported)]
    public async Task Creation_UnsupportedOverrideShowsActionableError(CapabilitySupport support)
    {
        using var fixture = new Fixture(support);
        fixture.ViewModel.NewHealthModeIndex = 1;
        await fixture.ViewModel.RunNewContainerCommand.ExecuteAsync(null);
        Assert.NotEmpty(fixture.ViewModel.HealthValidationError);
        Assert.False(fixture.ViewModel.CanConfigureHealth);
        fixture.Runtime.Verify(x => x.RunContainerAsync(It.IsAny<ContainerCreateSpec>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Creation_SwitchingToInheritOmitsRetainedCustomFields()
    {
        using var fixture = new Fixture();
        ContainerCreateSpec? captured = null;
        fixture.Runtime.Setup(x => x.RunContainerAsync(It.IsAny<ContainerCreateSpec>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .Callback<ContainerCreateSpec, IProgress<string>?, CancellationToken>((spec, _, _) => captured = spec)
            .ReturnsAsync(new OperationResult(true, 0, "", "", ""));
        fixture.ViewModel.NewHealthModeIndex = 2;
        fixture.ViewModel.NewHealthRetries = "invalid";
        await fixture.ViewModel.RunNewContainerCommand.ExecuteAsync(null);
        Assert.NotEmpty(fixture.ViewModel.HealthValidationError);
        Assert.Null(captured);
        fixture.ViewModel.NewHealthModeIndex = 0;
        await fixture.ViewModel.RunNewContainerCommand.ExecuteAsync(null);
        Assert.Equal(HealthCheckMode.Inherit, captured!.HealthMode);
        Assert.Null(captured.HealthRetries);
    }

    private static ContainerSummary Container(string id) => new(id, id, "image", "running", "Up", "", "");
    private static OperationResult Payload(string id, string status) => new(true, 0,
        System.Text.Json.JsonSerializer.Serialize(new { Id = id, State = new { Health = new { Status = status, FailingStreak = 0, Log = new[] { new { ExitCode = 0, Output = "ready" } } } } }), "", "");

    private sealed class Fixture : IDisposable
    {
        public Mock<IContainerRuntime> Runtime { get; } = new();
        public RuntimeWorkspace Workspace { get; }
        public HealthTestViewModel ViewModel { get; }
        public Fixture(CapabilitySupport support = CapabilitySupport.Supported)
        {
            Runtime.Setup(x => x.GetContainersAsync(It.IsAny<CancellationToken>())).ReturnsAsync([Container("one")]);
            Runtime.Setup(x => x.GetImagesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Runtime.Setup(x => x.GetNetworksAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Runtime.Setup(x => x.GetVolumesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Runtime.Setup(x => x.GetStatsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Runtime.Setup(x => x.FollowLogsAsync(It.IsAny<string>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OperationResult(true, 0, "", "", ""));
            var settings = new Mock<ISettingsService>();
            settings.SetupGet(x => x.Current).Returns(new AppSettings());
            var tasks = new Mock<ITaskService>();
            tasks.Setup(x => x.RunAsync(It.IsAny<string>(), It.IsAny<Func<IProgress<string>, CancellationToken, Task<OperationResult>>>(), It.IsAny<CancellationToken>()))
                .Returns<string, Func<IProgress<string>, CancellationToken, Task<OperationResult>>, CancellationToken>((_, operation, token) => operation(new Progress<string>(), token));
            Workspace = new(Runtime.Object, Mock.Of<IRuntimeCapabilityService>(), settings.Object, tasks.Object, Mock.Of<IUserInteractionService>())
            {
                Capabilities = new RuntimeCapabilities { Features = new Dictionary<RuntimeFeature, RuntimeFeatureCapability> { [RuntimeFeature.HealthChecks] = new(support, "", "") } }
            };
            ViewModel = new HealthTestViewModel(Workspace);
            ViewModel.SelectWithoutFollowingLogs(Container("one"));
        }
        public void Dispose() => Workspace.Dispose();
    }

    // These tests exercise inspection, not async-void log following, which requires a UI dispatcher.
    private sealed class HealthTestViewModel(RuntimeWorkspace workspace) : ContainersViewModel(workspace)
    {
        public void SelectWithoutFollowingLogs(ContainerSummary container)
        {
            IsDesignMode = true;
            SelectedContainer = container;
            SelectedDetailTabIndex = 1;
            IsDesignMode = false;
        }
    }

}
