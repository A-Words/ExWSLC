using ExWSLC.Helpers;
using ExWSLC.Models;
using ExWSLC.Services;
using Moq;

namespace ExWSLC.Tests;

public class NetworkOperationTests
{
    [Fact]
    public void ConnectArguments_PreserveRepeatedValuesAsSeparateLiteralArguments()
    {
        var spec = new NetworkConnectionSpec("container", "network", "172.30.0.10",
            ["api", "literal; $(echo x)"], ["key=value with spaces", "second=a=b"]);
        Assert.Equal(["network", "connect", "--ip", "172.30.0.10", "--network-alias", "api",
            "--network-alias", "literal; $(echo x)", "--driver-opt", "key=value with spaces", "--driver-opt", "second=a=b", "network", "container"],
            WslcContainerRuntime.BuildConnectNetworkArguments(spec));
        Assert.Equal(["network", "connect", "net", "id"], WslcContainerRuntime.BuildConnectNetworkArguments(new("id", "net")));
    }

    [Theory]
    [InlineData("127.1")] [InlineData("0x7f000001")] [InlineData("::1")] [InlineData("1.2.3.4/24")]
    [InlineData("256.1.1.1")] [InlineData("1.2.3.4 --session other")] [InlineData("")]
    public void ConnectArguments_RejectInvalidIpv4(string address) =>
        Assert.Throws<ArgumentException>(() => WslcContainerRuntime.BuildConnectNetworkArguments(new("id", "net", address)));

    [Theory]
    [InlineData("--session")] [InlineData(" --help")] [InlineData("bad\0name")] [InlineData("")]
    public void PositionalTargets_CannotBecomeOptions(string target)
    {
        Assert.Throws<ArgumentException>(() => WslcContainerRuntime.BuildConnectNetworkArguments(new("id", target)));
        Assert.Throws<ArgumentException>(() => WslcContainerRuntime.BuildCreateNetworkArguments(new() { Name = target }));
    }

    [Fact]
    public void ConnectArguments_RejectEmptyAliasesAndDuplicateDriverKeys()
    {
        Assert.Throws<ArgumentException>(() => WslcContainerRuntime.BuildConnectNetworkArguments(new("id", "net", Aliases: [" "])));
        Assert.Throws<ArgumentException>(() => WslcContainerRuntime.BuildConnectNetworkArguments(new("id", "net", DriverOptions: ["a=1", "a=2"])));
        Assert.Throws<ArgumentException>(() => WslcContainerRuntime.BuildConnectNetworkArguments(new("id", "net", DriverOptions: ["missing-value"])));
    }

    [Fact]
    public void CreateArguments_MapIpamSeparatelyFromDriverOptionsAndLabels()
    {
        var spec = new NetworkCreateSpec { Name = "test", Subnet = "172.30.0.0/24", Gateway = "172.30.0.1", IpRange = "172.30.0.128/25" };
        spec.DriverOptions.Add("com.docker.network.driver.mtu=1400");
        spec.Labels.Add("owner=test team");
        Assert.Equal(["network", "create", "--driver", "bridge", "--subnet", "172.30.0.0/24", "--gateway", "172.30.0.1", "--ip-range", "172.30.0.128/25",
            "--opt", "com.docker.network.driver.mtu=1400", "--label", "owner=test team", "test"], WslcContainerRuntime.BuildCreateNetworkArguments(spec));
        NetworkOptions.Validate(new NetworkCreateSpec { Name = "v6", Subnet = "fd00::/64", Gateway = "fd00::1", IpRange = "fd00::/80" });
    }

    [Theory]
    [InlineData(null, "172.30.0.1", null)]
    [InlineData("172.30.0.0/33", null, null)]
    [InlineData("172.30.0.1/24", null, null)]
    [InlineData("172.30.0.0/24", "172.31.0.1", null)]
    [InlineData("172.30.0.0/24", "fd00::1", null)]
    [InlineData("172.30.0.0/24", null, "172.30.0.0/16")]
    [InlineData("172.30.0.0/24", null, "172.31.0.0/25")]
    [InlineData("172.30.0.0/24", null, "172.30.0.129/25")]
    public void CreateArguments_RejectInconsistentIpam(string? subnet, string? gateway, string? range) =>
        Assert.Throws<ArgumentException>(() => WslcContainerRuntime.BuildCreateNetworkArguments(new() { Name = "net", Subnet = subnet, Gateway = gateway, IpRange = range }));

    [Theory]
    [InlineData(CapabilitySupport.Unknown)] [InlineData(CapabilitySupport.Unsupported)]
    public async Task Runtime_UnsupportedOperationsNeverInvokeRunner(CapabilitySupport support)
    {
        var runner = new Mock<IProcessRunner>(MockBehavior.Strict);
        var capability = new Mock<IRuntimeCapabilityService>();
        capability.Setup(x => x.DetectAsync(It.IsAny<CancellationToken>())).ReturnsAsync(AllCapabilities(support));
        var runtime = new WslcContainerRuntime(runner.Object, capability.Object);
        await Assert.ThrowsAsync<ArgumentException>(() => runtime.ConnectNetworkAsync(new("id", "net"), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => runtime.DisconnectNetworkAsync(new("id", "net"), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => runtime.CreateNetworkAsync(new() { Name = "net", Subnet = "172.30.0.0/24" }, TestContext.Current.CancellationToken));
        runner.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(RuntimeFeature.NetworkConnectIp)] [InlineData(RuntimeFeature.NetworkConnectAlias)]
    [InlineData(RuntimeFeature.NetworkConnectDriverOptions)] [InlineData(RuntimeFeature.NetworkCreateIpRange)]
    public async Task Runtime_EachOptionalFeatureHasItsOwnGate(RuntimeFeature missing)
    {
        var runner = new Mock<IProcessRunner>(MockBehavior.Strict);
        var features = AllCapabilities(CapabilitySupport.Supported).Features.ToDictionary();
        features[missing] = RuntimeFeatureCapability.NotChecked;
        var capability = new Mock<IRuntimeCapabilityService>();
        capability.Setup(x => x.DetectAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new RuntimeCapabilities { Features = features });
        var runtime = new WslcContainerRuntime(runner.Object, capability.Object);
        if (missing == RuntimeFeature.NetworkCreateIpRange)
            await Assert.ThrowsAsync<ArgumentException>(() => runtime.CreateNetworkAsync(new() { Name = "net", Subnet = "172.30.0.0/24", IpRange = "172.30.0.128/25" }, TestContext.Current.CancellationToken));
        else
            await Assert.ThrowsAsync<ArgumentException>(() => runtime.ConnectNetworkAsync(new("id", "net", "172.30.0.10", ["api"], ["a=b"]), TestContext.Current.CancellationToken));
        runner.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Runtime_DisconnectUsesCapturedPositionalsAndCancellationPreventsExecution()
    {
        var runner = new Mock<IProcessRunner>();
        runner.Setup(x => x.ExecuteAsync("wslc.exe", It.IsAny<IReadOnlyList<string>>(), null, null, It.IsAny<CancellationToken>())).ReturnsAsync(new OperationResult(true, 0, "", "", ""));
        var capabilities = new Mock<IRuntimeCapabilityService>();
        capabilities.Setup(x => x.DetectAsync(It.IsAny<CancellationToken>())).ReturnsAsync(AllCapabilities(CapabilitySupport.Supported));
        var runtime = new WslcContainerRuntime(runner.Object, capabilities.Object);
        await runtime.DisconnectNetworkAsync(new("id", "net"), TestContext.Current.CancellationToken);
        runner.Verify(x => x.ExecuteAsync("wslc.exe", It.Is<IReadOnlyList<string>>(a => a.SequenceEqual(new[] { "network", "disconnect", "net", "id" })), null, null, It.IsAny<CancellationToken>()), Times.Once);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.ConnectNetworkAsync(new("id", "net"), cancelled.Token));
        Assert.Single(runner.Invocations);
    }

    internal static RuntimeCapabilities AllCapabilities(CapabilitySupport support) => new()
    {
        Features = Enum.GetValues<RuntimeFeature>().ToDictionary(feature => feature, _ => new RuntimeFeatureCapability(support, "", ""))
    };
}
