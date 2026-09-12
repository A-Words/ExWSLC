using System.IO;
using System.Text.Json;
using ExWSLC.Helpers;
using ExWSLC.Models;
using ExWSLC.Services;
using Moq;

namespace ExWSLC.Tests;

public class HostLoopbackDiagnosticsTests
{
    internal const string ContainerId = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    [Theory]
    [InlineData("", "host.wslc.internal", CapabilitySupport.Unknown)]
    [InlineData("# session:\n#   hostLoopback: none", "host.wslc.internal", CapabilitySupport.Unknown)]
    [InlineData("session:\n  hostLoopback: default", "host.wslc.internal", CapabilitySupport.Unknown)]
    [InlineData("session:\n  hostLoopback: null", "host.wslc.internal", CapabilitySupport.Unknown)]
    [InlineData("session:\n  hostLoopback: none", "", CapabilitySupport.Unsupported)]
    [InlineData("session: {hostLoopback: 'windows.example.internal'}", "windows.example.internal", CapabilitySupport.Supported)]
    [InlineData("session:\n  hostLoopback: \"custom.internal\" # comment", "custom.internal", CapabilitySupport.Supported)]
    [InlineData("session: {hostLoopback: 'null'}", "null", CapabilitySupport.Supported)]
    [InlineData("session: {hostLoopback: None}", "None", CapabilitySupport.Supported)]
    public void Settings_ParseNativeDefaultsDisabledAndCustomDomains(string yaml, string host, CapabilitySupport support)
    {
        var result = HostLoopbackSettingsReader.Parse(yaml);
        Assert.Equal(host, result.HostName);
        Assert.Equal(support, result.Support);
    }

    [Theory]
    [InlineData("session: {hostLoopback: '$(whoami).internal'}")]
    [InlineData("session: {hostLoopback: 'host.internal; touch /tmp/pwn'}")]
    [InlineData("session: {hostLoopback: [host.internal]}")]
    [InlineData("session: {hostLoopback: host.internal, hostLoopback: none}")]
    [InlineData("session: {hostLoopback: host.internal}\n---\nsession: {hostLoopback: none}")]
    [InlineData("session: {<<: {hostLoopback: none}}")]
    [InlineData("<<: {session: {hostLoopback: none}}")]
    [InlineData("session: [")]
    public void Settings_RejectAmbiguityAndUnsafeTargetsWithoutEchoingValues(string yaml) =>
        Assert.Equal(HostLoopbackConfiguration.Unknown, HostLoopbackSettingsReader.Parse(yaml));

    [Theory]
    [InlineData("host..")]
    [InlineData("host.internal/path")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("-option")]
    [InlineData("user@host")]
    [InlineData("host\nsecret")]
    public void TargetValidator_RejectsNonDnsInput(string host) => Assert.False(HostLoopbackTargetValidator.IsHostName(host));

    [Fact]
    public void Settings_DoNotExposeUnrelatedCredentialFields()
    {
        var configuration = HostLoopbackSettingsReader.Parse("credentials: {password: secret}\nsession: {hostLoopback: custom.internal}");
        Assert.Equal("custom.internal", configuration.HostName);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(configuration));
    }

    [Fact]
    public void Settings_RejectOversizedDeepOrCyclicDocumentsBeforeBuildingTheModel()
    {
        Assert.Equal(HostLoopbackConfiguration.Unknown, HostLoopbackSettingsReader.Parse(new string(' ', 65537)));
        Assert.Equal(HostLoopbackConfiguration.Unknown, HostLoopbackSettingsReader.Parse(new string('[', 40) + new string(']', 40)));
        Assert.Equal(HostLoopbackConfiguration.Unknown, HostLoopbackSettingsReader.Parse("session: &cycle {hostLoopback: *cycle}"));
    }

    [Fact]
    public async Task Probe_DistinguishesExplicitlyUnsupportedCapabilityWithoutCallingExec()
    {
        var fixture = new Fixture();
        var capabilities = new RuntimeCapabilities { Features = new Dictionary<RuntimeFeature, RuntimeFeatureCapability>
        {
            [RuntimeFeature.HostLoopback] = new(CapabilitySupport.Unsupported, "HostConfigDisabled", "test")
        } };
        var result = await fixture.Runtime.ProbeHostLoopbackAsync(new(ContainerId, HostLoopbackConfiguration.DefaultHostName, 80), capabilities, TestContext.Current.CancellationToken);
        Assert.Equal(HostLoopbackOutcome.CapabilityDisabled, result.Outcome);
        Assert.Empty(fixture.Commands);
    }

    [Theory]
    [InlineData("EXWSLC09:DNS_FAIL", HostLoopbackOutcome.DnsFailed, false)]
    [InlineData("EXWSLC09:DNS_OK\nEXWSLC09:TCP_FAIL", HostLoopbackOutcome.TcpFailed, true)]
    [InlineData("EXWSLC09:DNS_OK\nEXWSLC09:TCP_OK", HostLoopbackOutcome.Connected, true)]
    [InlineData("EXWSLC09:TOOLS_MISSING", HostLoopbackOutcome.ToolsMissing, false)]
    [InlineData("EXWSLC09:TIMEOUT", HostLoopbackOutcome.TimedOut, false)]
    public async Task Probe_DistinguishesDnsTcpToolsAndTimeout(string markers, HostLoopbackOutcome expected, bool dns)
    {
        var fixture = new Fixture { ExecResult = Success(markers) };
        var result = await fixture.Probe();
        Assert.Equal(expected, result.Outcome);
        Assert.Equal(dns, result.DnsSucceeded);
        Assert.Equal(2, fixture.Commands.Count);
        Assert.Equal("container inspect", string.Join(' ', fixture.Commands[0].Take(2)));
        Assert.Equal("exec", fixture.Commands[1][0]);
    }

    [Fact]
    public async Task Probe_PassesCustomDomainAndPortAsSeparateArgumentsWithFixedScript()
    {
        var fixture = new Fixture { Configuration = new(CapabilitySupport.Supported, "custom.example.internal", "HostConfigCustom") };
        var result = await fixture.Probe(new(ContainerId[..12], "custom.example.internal", 65432));
        Assert.Equal(HostLoopbackOutcome.Connected, result.Outcome);
        var args = fixture.Commands[1];
        Assert.Equal(ContainerId, args[1]);
        Assert.Equal(new[] { "custom.example.internal", "65432" }, args.Skip(6).Take(2));
        Assert.DoesNotContain('\r', args[4]);
        Assert.DoesNotContain("custom.example.internal", args[4]);
        Assert.Contains("\"$1\" \"$2\"", args[4]);
        Assert.DoesNotContain("recv(", args[4]);
        Assert.DoesNotContain("send(", args[4]);
        var summary = HostLoopbackDiagnosticsFormatter.Summary(result);
        Assert.DoesNotContain("custom.example.internal", summary);
        Assert.Contains("[custom host]:65432", summary);
    }

    [Theory]
    [InlineData("--help", "host.wslc.internal", 80)]
    [InlineData(ContainerId, "host; id", 80)]
    [InlineData(ContainerId, "host.wslc.internal", 0)]
    [InlineData(ContainerId, "host.wslc.internal", 65536)]
    public async Task Probe_RejectsInvalidTargetsBeforeAnyIo(string id, string host, int port)
    {
        var fixture = new Fixture();
        Assert.Equal(HostLoopbackOutcome.InvalidTarget, (await fixture.Probe(new(id, host, port))).Outcome);
        Assert.Empty(fixture.Commands);
        fixture.Settings.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(CapabilitySupport.Unsupported, "", HostLoopbackOutcome.CapabilityDisabled)]
    [InlineData(CapabilitySupport.Unknown, "", HostLoopbackOutcome.CapabilityUnknown)]
    [InlineData(CapabilitySupport.Supported, "changed.internal", HostLoopbackOutcome.ConfigurationChanged)]
    public async Task Probe_DoesNotConnectWhenConfigurationDisablesOrChangesTheTarget(CapabilitySupport support, string host, HostLoopbackOutcome expected)
    {
        var fixture = new Fixture { Configuration = new(support, host, "") };
        Assert.Equal(expected, (await fixture.Probe()).Outcome);
        Assert.Empty(fixture.Commands);
    }

    [Fact]
    public async Task Probe_DoesNotExecAStoppedOrRemovedContainer()
    {
        var fixture = new Fixture { InspectResult = Success(Inspect(false)) };
        Assert.Equal(HostLoopbackOutcome.ContainerNotRunning, (await fixture.Probe()).Outcome);
        fixture.InspectResult = new(false, 1, "", "private missing container error", "");
        Assert.Equal(HostLoopbackOutcome.ContainerUnavailable, (await fixture.Probe()).Outcome);
        Assert.All(fixture.Commands, args => Assert.Equal("inspect", args[1]));
    }

    [Fact]
    public async Task Probe_RechecksStateIfContainerStopsDuringExec()
    {
        var fixture = new Fixture { ExecResult = new(false, 1, "", "secret exec error", "") };
        fixture.OnExec = () => fixture.InspectResult = Success(Inspect(false));
        Assert.Equal(HostLoopbackOutcome.ContainerNotRunning, (await fixture.Probe()).Outcome);
        Assert.Equal(3, fixture.Commands.Count);
    }

    [Fact]
    public async Task Probe_UsesCancellationAndNeverEchoesUnrecognizedOutput()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture();
        fixture.OnExec = cancellation.Cancel;
        var result = await fixture.Runtime.ProbeHostLoopbackAsync(new(ContainerId, HostLoopbackConfiguration.DefaultHostName, 80), new(), cancellation.Token);
        Assert.Equal(HostLoopbackOutcome.Cancelled, result.Outcome);
        fixture.OnExec = null;
        fixture.ExecResult = Success("credential=secret body\nEXWSLC09:DNS_OK\nEXWSLC09:TCP_OK");
        result = await fixture.Probe();
        Assert.Equal(HostLoopbackOutcome.RuntimeFailed, result.Outcome);
        Assert.DoesNotContain("secret", HostLoopbackDiagnosticsFormatter.Summary(result));
    }

    [Fact]
    public async Task Probe_CancellationBeforeStartNeverRunsCommands()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var fixture = new Fixture();
        var result = await fixture.Runtime.ProbeHostLoopbackAsync(new(ContainerId, HostLoopbackConfiguration.DefaultHostName, 80), new(), cancellation.Token);
        Assert.Equal(HostLoopbackOutcome.Cancelled, result.Outcome);
        Assert.Empty(fixture.Commands);
    }

    private static OperationResult Success(string output) => new(true, 0, output, "", "");
    private static string Inspect(bool running) => JsonSerializer.Serialize(new { Id = ContainerId, State = new { Running = running } });

    private sealed class Fixture
    {
        public HostLoopbackConfiguration Configuration { get; set; } = HostLoopbackConfiguration.Default;
        public OperationResult InspectResult { get; set; } = Success(Inspect(true));
        public OperationResult ExecResult { get; set; } = Success("EXWSLC09:DNS_OK\nEXWSLC09:TCP_OK");
        public Action? OnExec { get; set; }
        public Mock<IHostLoopbackSettingsReader> Settings { get; } = new(MockBehavior.Strict);
        public List<IReadOnlyList<string>> Commands { get; } = [];
        public WslcContainerRuntime Runtime { get; }

        public Fixture()
        {
            Settings.Setup(value => value.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => Configuration);
            var runner = new Mock<IProcessRunner>(MockBehavior.Strict);
            runner.Setup(value => value.ExecuteAsync("wslc.exe", It.IsAny<IReadOnlyList<string>>(), null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, IReadOnlyList<string> args, string? input, IProgress<string>? progress, CancellationToken token) =>
                {
                    Commands.Add(args);
                    if (args[0] == "exec") { OnExec?.Invoke(); return ExecResult; }
                    return InspectResult;
                });
            Runtime = new(runner.Object, Settings.Object);
        }

        public Task<HostLoopbackProbeResult> Probe(HostLoopbackProbeRequest? target = null) => Runtime.ProbeHostLoopbackAsync(
            target ?? new(ContainerId, HostLoopbackConfiguration.DefaultHostName, 80), new RuntimeCapabilities { CliVersion = "2.9.10.0" }, TestContext.Current.CancellationToken);
    }
}
