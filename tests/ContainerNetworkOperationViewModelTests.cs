using System.Text.Json;
using ExWSLC.Models;
using ExWSLC.Services;
using ExWSLC.ViewModels;
using Moq;

namespace ExWSLC.Tests;

public class ContainerNetworkOperationViewModelTests
{
    [Fact]
    public async Task NetworkCreation_MapsTrimmedIpFieldsAndPreservesDriverOptions()
    {
        using var fixture = new Fixture();
        fixture.Runtime.Setup(x => x.CreateNetworkAsync(It.IsAny<NetworkCreateSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(Success());
        var viewModel = new NetworksViewModel(fixture.Workspace)
        {
            NetworkName = " new ", NetworkSubnet = " 172.30.0.0/24 ", NetworkGateway = "172.30.0.1",
            NetworkIpRange = "172.30.0.128/25", NetworkOptions = "mtu=1400", NetworkLabels = "owner=test"
        };
        await viewModel.CreateNetworkCommand.ExecuteAsync(null);
        fixture.Runtime.Verify(x => x.CreateNetworkAsync(It.Is<NetworkCreateSpec>(s => s.Name == "new" && s.Subnet == "172.30.0.0/24" &&
            s.Gateway == "172.30.0.1" && s.IpRange == "172.30.0.128/25" && s.DriverOptions.Single() == "mtu=1400" && s.Labels.Single() == "owner=test"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task NetworkCreation_InvalidOrUnsupportedIpFieldsKeepInputsAndReportError(bool unsupported)
    {
        using var fixture = new Fixture();
        if (unsupported) fixture.Workspace.Capabilities = new();
        var viewModel = new NetworksViewModel(fixture.Workspace)
        {
            NetworkName = "new", NetworkSubnet = "172.30.0.0/24", NetworkGateway = unsupported ? "172.30.0.1" : "172.31.0.1"
        };
        await viewModel.CreateNetworkCommand.ExecuteAsync(null);
        Assert.Equal("new", viewModel.NetworkName);
        Assert.NotEmpty(viewModel.OperationOutput);
        fixture.Interaction.Verify(x => x.ShowErrorAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        fixture.Runtime.Verify(x => x.CreateNetworkAsync(It.IsAny<NetworkCreateSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void InventoryRefresh_DoesNotDisableParameterEditingButBlocksMutation()
    {
        using var fixture = new Fixture();
        fixture.ViewModel.ConnectionNetworkName = "private";
        fixture.Workspace.IsBusy = true;
        Assert.True(fixture.ViewModel.CanEditNetworkConnection);
        Assert.False(fixture.ViewModel.ConnectNetworkCommand.CanExecute(null));
        fixture.Workspace.IsBusy = false;
        Assert.True(fixture.ViewModel.ConnectNetworkCommand.CanExecute(null));
    }

    [Fact]
    public async Task NetworkInspection_RejectsAnotherContainersPayload()
    {
        using var fixture = new Fixture();
        var details = fixture.ViewModel.NetworkDetails;
        fixture.Runtime.Setup(x => x.InspectContainerAsync("one", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Success() with { Output = """{"Id":"different","HostConfig":{"NetworkMode":"bridge"}}""" });
        await fixture.ViewModel.RefreshNetworkDetailsCommand.ExecuteAsync(null);
        Assert.Same(details, fixture.ViewModel.NetworkDetails);
        Assert.True(fixture.ViewModel.HasNetworkDetailsError);
    }

    [Fact]
    public async Task Connect_RefreshesInventoryAndDetailsWithoutLosingPorts()
    {
        using var fixture = new Fixture();
        fixture.Runtime.Setup(x => x.ConnectNetworkAsync(It.IsAny<NetworkConnectionSpec>(), It.IsAny<CancellationToken>()))
            .Callback(() => fixture.Attached = true).ReturnsAsync(Success());
        fixture.ViewModel.ConnectionNetworkName = "private";
        fixture.ViewModel.ConnectionIpv4 = "172.30.0.10";
        await fixture.ViewModel.ConnectNetworkCommand.ExecuteAsync(null);
        Assert.Single(fixture.ViewModel.NetworkDetails!.Networks);
        Assert.Single(fixture.ViewModel.NetworkDetails.PortBindings);
        Assert.False(fixture.ViewModel.IsNetworkDetailsStale);
        Assert.Empty(fixture.ViewModel.AvailableConnectionNetworks);
        fixture.Runtime.Verify(x => x.GetNetworksAsync(It.IsAny<CancellationToken>()), Times.Once);
        fixture.Runtime.Verify(x => x.InspectContainerAsync("one", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Disconnect_ConfirmationCapturesTargetAndBlocksConcurrentConnect()
    {
        using var fixture = new Fixture(attached: true);
        var confirmation = new TaskCompletionSource<bool>();
        fixture.Interaction.Setup(x => x.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(confirmation.Task);
        fixture.Runtime.Setup(x => x.DisconnectNetworkAsync(It.IsAny<NetworkDisconnectionSpec>(), It.IsAny<CancellationToken>()))
            .Callback(() => fixture.Attached = false).ReturnsAsync(Success());
        var operation = fixture.ViewModel.DisconnectNetworkCommand.ExecuteAsync(fixture.ViewModel.NetworkDetails!.Networks[0]);
        Assert.True(fixture.ViewModel.IsNetworkOperationInProgress);
        fixture.ViewModel.Select(Container("two"));
        fixture.ViewModel.SelectedDetailTabIndex = 2;
        await fixture.ViewModel.ConnectNetworkCommand.ExecuteAsync(null);
        confirmation.SetResult(true);
        await operation;
        fixture.Runtime.Verify(x => x.DisconnectNetworkAsync(It.Is<NetworkDisconnectionSpec>(s => s.ContainerId == "one" && s.NetworkName == "private"), It.IsAny<CancellationToken>()), Times.Once);
        fixture.Runtime.Verify(x => x.ConnectNetworkAsync(It.IsAny<NetworkConnectionSpec>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal("two", fixture.ViewModel.SelectedContainer!.Id);
        fixture.ViewModel.Select(Container("one"));
        fixture.ViewModel.SelectedDetailTabIndex = 2;
        Assert.Empty(fixture.ViewModel.NetworkDetails!.Networks);
        fixture.Runtime.Verify(x => x.InspectContainerAsync("one", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task DeclinedDisconnect_DoesNotMutateOrRefresh()
    {
        using var fixture = new Fixture(attached: true);
        fixture.Interaction.Setup(x => x.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);
        await fixture.ViewModel.DisconnectNetworkCommand.ExecuteAsync(fixture.ViewModel.NetworkDetails!.Networks[0]);
        fixture.Runtime.Verify(x => x.DisconnectNetworkAsync(It.IsAny<NetworkDisconnectionSpec>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Runtime.Verify(x => x.GetNetworksAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.False(fixture.ViewModel.IsNetworkOperationInProgress);
    }

    [Fact]
    public async Task FailedConnect_PreservesValidDetailsAndSurfacesRuntimeConflict()
    {
        using var fixture = new Fixture();
        var details = fixture.ViewModel.NetworkDetails;
        fixture.Runtime.Setup(x => x.ConnectNetworkAsync(It.IsAny<NetworkConnectionSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult(false, 1, "", "address already in use", ""));
        fixture.ViewModel.ConnectionNetworkName = "private";
        await fixture.ViewModel.ConnectNetworkCommand.ExecuteAsync(null);
        Assert.Same(details, fixture.ViewModel.NetworkDetails);
        Assert.Contains("address already in use", fixture.ViewModel.NetworkOperationMessage);
        Assert.False(fixture.ViewModel.IsNetworkDetailsStale);
        fixture.Runtime.Verify(x => x.GetNetworksAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SuccessfulConnect_FailedInspectionShowsStaleSnapshotUntilRetry()
    {
        using var fixture = new Fixture();
        var details = fixture.ViewModel.NetworkDetails;
        fixture.Runtime.SetupSequence(x => x.InspectContainerAsync("one", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult(false, 1, "", "inspect failed", ""))
            .ReturnsAsync(Payload(true));
        fixture.Runtime.Setup(x => x.ConnectNetworkAsync(It.IsAny<NetworkConnectionSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(Success());
        fixture.ViewModel.ConnectionNetworkName = "private";
        await fixture.ViewModel.ConnectNetworkCommand.ExecuteAsync(null);
        Assert.Same(details, fixture.ViewModel.NetworkDetails);
        Assert.True(fixture.ViewModel.IsNetworkDetailsStale);
        Assert.Equal("inspect failed", fixture.ViewModel.NetworkDetailsError);
        Assert.False(fixture.ViewModel.ConnectNetworkCommand.CanExecute(null));
        await fixture.ViewModel.RefreshNetworkDetailsCommand.ExecuteAsync(null);
        Assert.False(fixture.ViewModel.IsNetworkDetailsStale);
        Assert.Single(fixture.ViewModel.NetworkDetails!.Networks);
    }

    [Fact]
    public async Task Cancellation_RefreshesBecauseRuntimeMayHaveAppliedMutation()
    {
        using var fixture = new Fixture();
        fixture.Runtime.Setup(x => x.ConnectNetworkAsync(It.IsAny<NetworkConnectionSpec>(), It.IsAny<CancellationToken>()))
            .Returns<NetworkConnectionSpec, CancellationToken>(async (_, token) =>
            {
                fixture.Attached = true;
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Success();
            });
        fixture.ViewModel.ConnectionNetworkName = "private";
        var operation = fixture.ViewModel.ConnectNetworkCommand.ExecuteAsync(null);
        fixture.Workspace.CancelCurrentOperationCommand.Execute(null);
        await operation;
        Assert.Single(fixture.ViewModel.NetworkDetails!.Networks);
        Assert.False(fixture.ViewModel.IsNetworkOperationInProgress);
        Assert.False(fixture.Workspace.IsBusy);
        Assert.NotEmpty(fixture.ViewModel.NetworkOperationMessage);
    }

    [Theory]
    [InlineData("missing")] [InlineData("attached")] [InlineData("unsupported")] [InlineData("host")] [InlineData("invalid-ip")]
    public async Task InvalidConnectionStates_DoNotReachRuntime(string scenario)
    {
        using var fixture = new Fixture(attached: scenario == "attached");
        if (scenario == "missing") fixture.Workspace.Networks.Clear();
        if (scenario == "unsupported") fixture.Workspace.Capabilities = new();
        if (scenario == "host") fixture.ViewModel.NetworkDetails = fixture.ViewModel.NetworkDetails! with { NetworkMode = "host" };
        fixture.ViewModel.ConnectionNetworkName = scenario == "missing" ? "absent" : "private";
        if (scenario == "invalid-ip") fixture.ViewModel.ConnectionIpv4 = "not-an-ip";
        await fixture.ViewModel.ConnectNetworkCommand.ExecuteAsync(null);
        Assert.NotEmpty(fixture.ViewModel.NetworkOperationMessage);
        fixture.Runtime.Verify(x => x.ConnectNetworkAsync(It.IsAny<NetworkConnectionSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ContainerSummary Container(string id) => new(id, id, "image", "running", "Up", "", "");
    private static OperationResult Success() => new(true, 0, "", "", "");
    private static OperationResult Payload(bool attached) => Success() with
    {
        Output = JsonSerializer.Serialize(new
        {
            HostConfig = new { NetworkMode = "bridge" },
            Ports = new Dictionary<string, object> { ["80/tcp"] = new[] { new { HostIp = "127.0.0.1", HostPort = "8080" } } },
            NetworkSettings = new { Networks = attached ? new Dictionary<string, object> { ["private"] = new { NetworkID = "net", IPAddress = "172.30.0.10", Aliases = new[] { "api" } } } : [] }
        })
    };

    private sealed class Fixture : IDisposable
    {
        public bool Attached { get; set; }
        public Mock<IContainerRuntime> Runtime { get; } = new();
        public Mock<IUserInteractionService> Interaction { get; } = new();
        public RuntimeWorkspace Workspace { get; }
        public TestViewModel ViewModel { get; }
        public Fixture(bool attached = false)
        {
            Attached = attached;
            var networks = new[] { new NetworkSummary("net", "private", "bridge", "local", "172.30.0.0/24", "172.30.0.1") };
            Runtime.Setup(x => x.GetNetworksAsync(It.IsAny<CancellationToken>())).ReturnsAsync(networks);
            Runtime.Setup(x => x.GetContainersAsync(It.IsAny<CancellationToken>())).ReturnsAsync([Container("one"), Container("two")]);
            Runtime.Setup(x => x.GetImagesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Runtime.Setup(x => x.GetVolumesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Runtime.Setup(x => x.GetStatsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Runtime.Setup(x => x.InspectContainerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(() => Task.FromResult(Payload(Attached)));
            var settings = new Mock<ISettingsService>();
            settings.SetupGet(x => x.Current).Returns(new AppSettings());
            var tasks = new Mock<ITaskService>();
            tasks.Setup(x => x.RunAsync(It.IsAny<string>(), It.IsAny<Func<IProgress<string>, CancellationToken, Task<OperationResult>>>(), It.IsAny<CancellationToken>()))
                .Returns<string, Func<IProgress<string>, CancellationToken, Task<OperationResult>>, CancellationToken>((_, operation, token) => operation(new Progress<string>(), token));
            Workspace = new(Runtime.Object, Mock.Of<IRuntimeCapabilityService>(), settings.Object, tasks.Object, Interaction.Object)
            { Capabilities = NetworkOperationTests.AllCapabilities(CapabilitySupport.Supported) };
            foreach (var network in networks) Workspace.Networks.Add(network);
            ViewModel = new(Workspace);
            ViewModel.Select(Container("one"));
            ViewModel.SelectedDetailTabIndex = 2;
        }
        public void Dispose() => Workspace.Dispose();
    }

    private sealed class TestViewModel(RuntimeWorkspace workspace) : ContainersViewModel(workspace)
    {
        public void Select(ContainerSummary container)
        {
            IsDesignMode = true;
            SelectedContainer = container;
            SelectedDetailTabIndex = 1;
            IsDesignMode = false;
        }
    }
}
