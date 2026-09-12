using System.IO;
using System.Xml.Linq;
using ExWSLC.Models;
using ExWSLC.Services;
using Moq;

namespace ExWSLC.Tests;

public class RuntimeCapabilityServiceTests
{
    [Fact]
    public async Task DetectAsync_SeparatesClientServiceAndBundledPackageVersions()
    {
        var fixture = new Fixture();
        var result = await fixture.Service.DetectAsync(TestContext.Current.CancellationToken);

        Assert.Equal("2.9.10.0", result.CliVersion);
        Assert.Equal("2.9.10", result.ServiceVersion);
        var project = XDocument.Load(Path.Combine(TestPaths.SourceDirectory, "ExWSLC.csproj"));
        Assert.Equal(project.Descendants("WslContainersSdkVersion").Single().Value, result.SdkPackageVersion);
        Assert.Equal(CapabilitySupport.Supported, result.CliAvailability);
        Assert.Equal(CapabilitySupport.Supported, result.SdkAvailability);
        Assert.Equal(CapabilitySupport.Supported, result.ServiceAvailability);
        Assert.True(result.IsAvailable);
        Assert.DoesNotContain(fixture.Commands, command => command is "version" or "--version");
        Assert.All(fixture.Commands, command => Assert.True(command.StartsWith("version") || command.EndsWith("--help")));
        fixture.Sdk.Verify(value => value.InstallMissingComponentsAsync(
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("--version")]
    public async Task DetectAsync_AcceptsVerifiedLegacyVersionText(string fallback)
    {
        var fixture = new Fixture();
        fixture.Responses["version --format json"] = Failure();
        fixture.Responses["version"] = Failure();
        fixture.Responses[fallback] = Success("wslc 2.9.3.0\r\n");

        var result = await fixture.Service.DetectAsync(TestContext.Current.CancellationToken);

        Assert.Equal("2.9.3.0", result.CliVersion);
        Assert.True(result.IsAvailable);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"Server":{"Version":"9.9.9"}}""")]
    [InlineData("""{"Client":null}""")]
    [InlineData("""{"Client":{"Version":99}}""")]
    [InlineData("""{"Client":{"Version":"unexpected"}}""")]
    public async Task DetectAsync_InvalidVersionPayloadDoesNotUseServerVersionOrArbitraryText(string payload)
    {
        var fixture = new Fixture();
        fixture.Responses["version --format json"] = Success(payload);
        fixture.Responses["version"] = Success("Copyright Microsoft\nunknown preview version");
        fixture.Responses["--version"] = Failure();

        var result = await fixture.Service.DetectAsync(TestContext.Current.CancellationToken);

        Assert.Empty(result.CliVersion);
        Assert.Equal(CapabilitySupport.Supported, result.CliAvailability);
        Assert.Equal(CapabilitySupport.Supported, result[RuntimeFeature.ContainerCopy].Support);
        Assert.Equal("RuntimeDetectionIncomplete", result.MessageKey);
        Assert.True(result.IsAvailable);
    }

    [Fact]
    public async Task DetectAsync_OnlyFailureToStartEveryProbeMarksCliUnavailable()
    {
        var fixture = new Fixture();
        foreach (var command in fixture.Responses.Keys.ToArray())
            fixture.Responses[command] = Failure(-1);

        var result = await fixture.Service.DetectAsync(TestContext.Current.CancellationToken);

        Assert.False(result.IsAvailable);
        Assert.Equal(CapabilitySupport.Unsupported, result.CliAvailability);
        Assert.Equal(CapabilitySupport.Supported, result.SdkAvailability);
        Assert.Equal("2.9.10", result.ServiceVersion);
        Assert.All(result.Features.Values, capability => Assert.Equal(CapabilitySupport.Unknown, capability.Support));
        Assert.Equal("CliUnavailable", result.MessageKey);
    }

    [Fact]
    public async Task DetectAsync_RunningCliWithFailedProbesStillAllowsInventory()
    {
        var fixture = new Fixture();
        foreach (var command in fixture.Responses.Keys.ToArray())
            fixture.Responses[command] = Failure(1);

        var result = await fixture.Service.DetectAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsAvailable);
        Assert.Equal(CapabilitySupport.Supported, result.CliAvailability);
        Assert.Empty(result.CliVersion);
        Assert.All(result.Features.Values, capability => Assert.Equal(CapabilitySupport.Unknown, capability.Support));
    }

    [Theory]
    [InlineData(false, "Usage: wslc image build [OPTIONS]\n  --help  help\n  --secret  secret", "CapabilityProbeFailed")]
    [InlineData(true, "", "CapabilityHelpUnrecognized")]
    [InlineData(true, "The --secret option might work.", "CapabilityHelpUnrecognized")]
    [InlineData(true, "Usage: wslc image build [OPTIONS]\n  --secret  secret", "CapabilityHelpUnrecognized")]
    [InlineData(true, "Usage: wslc container create [OPTIONS]\n  --help  help\n  --secret  secret", "CapabilityHelpUnrecognized")]
    public async Task DetectAsync_HelpFailureOrUnrecognizedOutputIsUnknown(bool success, string output, string reason)
    {
        var fixture = new Fixture();
        fixture.Responses["image build --help"] = new OperationResult(success, success ? 0 : 1, output, "", "");

        var result = await fixture.Service.DetectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CapabilitySupport.Unknown, result[RuntimeFeature.BuildSecret].Support);
        Assert.Equal(reason, result[RuntimeFeature.BuildSecret].ReasonKey);
        Assert.Equal("wslc image build --help", result[RuntimeFeature.BuildSecret].Source);
    }

    [Fact]
    public async Task DetectAsync_RecognizesLocalizedHelpAndAllIndependentlyAdvertisedFeatures()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.DetectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Enum.GetValues<RuntimeFeature>().Length, result.Features.Count);
        foreach (var feature in Enum.GetValues<RuntimeFeature>())
        {
            var expected = feature is RuntimeFeature.NativeRestart or RuntimeFeature.Events
                ? CapabilitySupport.Unsupported
                : CapabilitySupport.Supported;
            Assert.Equal(expected, result[feature].Support);
        }
        Assert.Contains("wslc --help", result[RuntimeFeature.Events].Source);
        Assert.Contains("wslc system --help", result[RuntimeFeature.Events].Source);
        Assert.Equal("CapabilityNotAdvertised", result[RuntimeFeature.NativeRestart].ReasonKey);
    }

    [Theory]
    [InlineData(RuntimeFeature.BuildSecret, "--secret")]
    [InlineData(RuntimeFeature.BuildOutput, "--output")]
    [InlineData(RuntimeFeature.BuildProgress, "--progress")]
    [InlineData(RuntimeFeature.BuildPull, "--pull")]
    public async Task DetectAsync_BuildFlagsAreIndependentAndRequireExactOptionRows(RuntimeFeature feature, string option)
    {
        var fixture = new Fixture();
        var flags = new[] { "--secret", "--output", "--progress", "--pull" };
        fixture.Responses["image build --help"] = Success(Help("image build", flags.Where(flag => flag != option).ToArray()) +
            $"\n  {option}-other  a different option\n  note  documentation mentions {option}\n");

        var result = await fixture.Service.DetectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CapabilitySupport.Unsupported, result[feature].Support);
        Assert.All(new[] { RuntimeFeature.BuildSecret, RuntimeFeature.BuildOutput, RuntimeFeature.BuildProgress, RuntimeFeature.BuildPull }
            .Where(other => other != feature), other => Assert.Equal(CapabilitySupport.Supported, result[other].Support));
    }

    [Fact]
    public async Task DetectAsync_HealthChecksRequireTheCompleteAdvertisedOptionGroup()
    {
        var fixture = new Fixture();
        fixture.Responses["container create --help"] = Success(Help("container create", ["--health-cmd", "--mount"]));

        var result = await fixture.Service.DetectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CapabilitySupport.Unsupported, result[RuntimeFeature.HealthChecks].Support);
        Assert.Equal(CapabilitySupport.Supported, result[RuntimeFeature.CreateMount].Support);
        Assert.Equal(CapabilitySupport.Unsupported, result[RuntimeFeature.CreatePullPolicy].Support);
        Assert.Equal(CapabilitySupport.Unsupported, result[RuntimeFeature.CreateStopTimeout].Support);
    }

    [Fact]
    public async Task DetectAsync_ConnectOptionsRequireTheParentCommand()
    {
        var fixture = new Fixture();
        fixture.Responses["network --help"] = Success(Help("network", ["create", "disconnect"]));

        var result = await fixture.Service.DetectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CapabilitySupport.Unsupported, result[RuntimeFeature.NetworkConnect].Support);
        Assert.Equal(CapabilitySupport.Unsupported, result[RuntimeFeature.NetworkConnectIp].Support);
        Assert.Equal(CapabilitySupport.Unsupported, result[RuntimeFeature.NetworkConnectAlias].Support);
        Assert.Equal(CapabilitySupport.Supported, result[RuntimeFeature.NetworkDisconnect].Support);
    }

    [Fact]
    public async Task DetectAsync_DoesNotInferRestartOrEventsFromNewerVersionNumbers()
    {
        var fixture = new Fixture();
        fixture.Responses["version --format json"] = Success("""{"Client":{"Version":"99.0.0"}}""");

        var result = await fixture.Service.DetectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CapabilitySupport.Unsupported, result[RuntimeFeature.NativeRestart].Support);
        Assert.Equal(CapabilitySupport.Unsupported, result[RuntimeFeature.Events].Support);
    }

    [Theory]
    [InlineData("")]
    [InlineData("system")]
    public async Task DetectAsync_EventsAndRestartFollowAdvertisedCommandsInsteadOfVersionThresholds(string eventsParent)
    {
        var fixture = new Fixture();
        fixture.Responses["version --format json"] = Success("""{"Client":{"Version":"2.9.3"}}""");
        fixture.Responses["container --help"] = Success(Help("container", ["cp", "restart"]));
        fixture.Responses[(eventsParent + " --help").TrimStart()] = Success(Help(eventsParent, ["events"]));

        var result = await fixture.Service.DetectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CapabilitySupport.Supported, result[RuntimeFeature.NativeRestart].Support);
        Assert.Equal(CapabilitySupport.Supported, result[RuntimeFeature.Events].Support);
        Assert.DoesNotContain(fixture.Commands, command => command.Contains("restart") || command.Contains("events"));
    }

    [Fact]
    public async Task DetectAsync_OneFailedEventsHelpCannotProveEventsUnsupported()
    {
        var fixture = new Fixture();
        fixture.Responses["--help"] = Failure();

        var result = await fixture.Service.DetectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CapabilitySupport.Unknown, result[RuntimeFeature.Events].Support);
    }

    [Fact]
    public async Task DetectAsync_MissingNativeSdkDoesNotDisableCliOrMislabelServiceVersion()
    {
        var fixture = new Fixture();
        fixture.Sdk.Setup(value => value.GetMissingComponents()).Throws(new DllNotFoundException("native SDK absent"));

        var result = await fixture.Service.DetectAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsAvailable);
        Assert.Equal(CapabilitySupport.Unsupported, result.SdkAvailability);
        Assert.Equal(CapabilitySupport.Unknown, result.ServiceAvailability);
        Assert.Empty(result.ServiceVersion);
        Assert.NotEmpty(result.SdkPackageVersion);
        Assert.Equal("SdkUnavailable", result.MessageKey);
        fixture.Sdk.Verify(value => value.GetServiceVersion(), Times.Never);
    }

    [Fact]
    public async Task DetectAsync_UnknownSdkErrorRemainsUnknown()
    {
        var fixture = new Fixture();
        fixture.Sdk.Setup(value => value.GetMissingComponents()).Throws(new InvalidOperationException("unrecognized SDK error"));

        var result = await fixture.Service.DetectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CapabilitySupport.Unknown, result.SdkAvailability);
        Assert.True(result.IsAvailable);
        Assert.False(result.CanInstallComponents);
    }

    [Fact]
    public async Task DetectAsync_ServiceFailurePreservesKnownMissingComponentsAndInstallationAvailability()
    {
        var fixture = new Fixture();
        fixture.Sdk.Setup(value => value.GetMissingComponents()).Returns(["VirtualMachinePlatform"]);
        fixture.Sdk.Setup(value => value.GetServiceVersion()).Throws(new InvalidOperationException("service absent"));

        var result = await fixture.Service.DetectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CapabilitySupport.Supported, result.SdkAvailability);
        Assert.Equal(CapabilitySupport.Unknown, result.ServiceAvailability);
        Assert.Equal(["VirtualMachinePlatform"], result.MissingComponents);
        Assert.False(result.IsAvailable);
        Assert.True(result.CanInstallComponents);
        Assert.Equal("MissingRuntimeComponents", result.MessageKey);
        Assert.Equal(["VirtualMachinePlatform"], result.MessageArguments);
    }

    [Fact]
    public async Task DetectAsync_SdkNeedsUpdateRequiresAnApplicationUpdate()
    {
        var fixture = new Fixture();
        fixture.Sdk.Setup(value => value.GetMissingComponents()).Returns(["SdkNeedsUpdate"]);

        var result = await fixture.Service.DetectAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsAvailable);
        Assert.False(result.CanInstallComponents);
        Assert.Equal(CapabilitySupport.Unsupported, result.SdkAvailability);
        Assert.Equal("SdkUpdateRequired", result.MessageKey);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.InstallMissingComponentsAsync(cancellationToken: TestContext.Current.CancellationToken));
        fixture.Sdk.Verify(value => value.InstallMissingComponentsAsync(
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DetectAsync_CachesOneSnapshotUntilExplicitRefresh()
    {
        var fixture = new Fixture();
        var first = await fixture.Service.DetectAsync(TestContext.Current.CancellationToken);
        var commandsAfterFirst = fixture.Commands.Count;

        var second = await fixture.Service.DetectAsync(TestContext.Current.CancellationToken);

        Assert.Same(first, second);
        Assert.Equal(commandsAfterFirst, fixture.Commands.Count);
        fixture.Responses["version --format json"] = Success("""{"Client":{"Version":"2.9.11.0"}}""");

        var refreshed = await fixture.Service.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.NotSame(first, refreshed);
        Assert.Equal("2.9.11.0", refreshed.CliVersion);
        Assert.Equal(commandsAfterFirst * 2, fixture.Commands.Count);
    }

    [Fact]
    public async Task DetectAsync_AlreadyCancelledTokenDoesNotProbeOrReturnCachedResults()
    {
        var fixture = new Fixture();
        await fixture.Service.DetectAsync(TestContext.Current.CancellationToken);
        var count = fixture.Commands.Count;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.DetectAsync(cancellation.Token));

        Assert.Equal(count, fixture.Commands.Count);
    }

    [Fact]
    public async Task DetectAsync_CancellingOneCallerDoesNotPoisonAnotherWaitingCallerOrCache()
    {
        var fixture = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        fixture.Runner.Setup(value => value.ExecuteAsync("wslc.exe",
                It.Is<IReadOnlyList<string>>(arguments => string.Join(" ", arguments) == "version --format json"),
                null, null, It.IsAny<CancellationToken>()))
            .Returns(async (string _, IReadOnlyList<string> arguments, string? input, IProgress<string>? progress, CancellationToken token) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    entered.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                return fixture.Responses["version --format json"];
            });
        using var cancellation = new CancellationTokenSource();
        var first = fixture.Service.DetectAsync(cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var second = fixture.Service.DetectAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        var result = await second;

        Assert.Equal("2.9.10.0", result.CliVersion);
        Assert.Equal(2, calls);
        Assert.Same(result, await fixture.Service.DetectAsync(TestContext.Current.CancellationToken));
        fixture.Sdk.Verify(value => value.GetMissingComponents(), Times.Once);
    }

    [Fact]
    public async Task DetectAsync_TimeoutIsUnknownAndCanBeRetriedByExplicitRefresh()
    {
        var fixture = new Fixture(TimeSpan.FromMilliseconds(20));
        fixture.Runner.Setup(value => value.ExecuteAsync("wslc.exe",
                It.Is<IReadOnlyList<string>>(arguments => string.Join(" ", arguments) == "image build --help"),
                null, null, It.IsAny<CancellationToken>()))
            .Returns(async (string _, IReadOnlyList<string> arguments, string? input, IProgress<string>? progress, CancellationToken token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Success("");
            });

        var timedOut = await fixture.Service.DetectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CapabilitySupport.Unknown, timedOut[RuntimeFeature.BuildSecret].Support);
        Assert.Same(timedOut, await fixture.Service.DetectAsync(TestContext.Current.CancellationToken));
        fixture.Runner.Setup(value => value.ExecuteAsync("wslc.exe",
                It.Is<IReadOnlyList<string>>(arguments => string.Join(" ", arguments) == "image build --help"),
                null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixture.Responses["image build --help"]);

        var refreshed = await fixture.Service.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CapabilitySupport.Supported, refreshed[RuntimeFeature.BuildSecret].Support);
    }

    [Fact]
    public async Task InstallMissingComponentsAsync_RechecksAndPassesOnlyCurrentlyInstallableComponents()
    {
        var fixture = new Fixture();
        fixture.Sdk.SetupSequence(value => value.GetMissingComponents())
            .Returns(["WslPackage"])
            .Returns(["VirtualMachinePlatform", "UnknownFutureComponent"])
            .Returns([]);
        var old = await fixture.Service.DetectAsync(TestContext.Current.CancellationToken);
        var progress = Mock.Of<IProgress<string>>();
        using var cancellation = new CancellationTokenSource();

        await fixture.Service.InstallMissingComponentsAsync(progress, cancellation.Token);
        var current = await fixture.Service.DetectAsync(TestContext.Current.CancellationToken);

        fixture.Sdk.Verify(value => value.InstallMissingComponentsAsync(
            It.Is<IReadOnlyList<string>>(components => components.SequenceEqual(new[] { "VirtualMachinePlatform" })),
            progress, cancellation.Token), Times.Once);
        Assert.NotSame(old, current);
        Assert.Empty(current.MissingComponents);
    }

    [Fact]
    public async Task InstallMissingComponentsAsync_NoMissingComponentsDoesNotInvokeInstaller()
    {
        var fixture = new Fixture();

        await fixture.Service.InstallMissingComponentsAsync(cancellationToken: TestContext.Current.CancellationToken);

        fixture.Sdk.Verify(value => value.InstallMissingComponentsAsync(
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InstallMissingComponentsAsync_FailurePropagatesAndInvalidatesPossiblyStaleSnapshot()
    {
        var fixture = new Fixture();
        fixture.Sdk.Setup(value => value.GetMissingComponents()).Returns(["WslPackage"]);
        fixture.Sdk.Setup(value => value.InstallMissingComponentsAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("installation failed"));
        var before = await fixture.Service.DetectAsync(TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.InstallMissingComponentsAsync(cancellationToken: TestContext.Current.CancellationToken));
        var after = await fixture.Service.DetectAsync(TestContext.Current.CancellationToken);

        Assert.Equal("installation failed", error.Message);
        Assert.NotSame(before, after);
    }

    [Fact]
    public async Task InstallMissingComponentsAsync_CancellationIsPassedToSdkAndDoesNotCacheOldState()
    {
        var fixture = new Fixture();
        fixture.Sdk.Setup(value => value.GetMissingComponents()).Returns(["WslPackage"]);
        var before = await fixture.Service.DetectAsync(TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        fixture.Sdk.Setup(value => value.InstallMissingComponentsAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<IProgress<string>>(), cancellation.Token))
            .Returns((IReadOnlyList<string> components, IProgress<string>? progress, CancellationToken token) =>
            {
                cancellation.Cancel();
                return Task.FromCanceled(token);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.InstallMissingComponentsAsync(cancellationToken: cancellation.Token));
        var after = await fixture.Service.DetectAsync(TestContext.Current.CancellationToken);

        Assert.NotSame(before, after);
        fixture.Sdk.Verify(value => value.InstallMissingComponentsAsync(
            It.IsAny<IReadOnlyList<string>>(), null, cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task InstallMissingComponentsAsync_AlreadyCancelledDoesNotReadSdkOrStartInstall()
    {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.InstallMissingComponentsAsync(cancellationToken: cancellation.Token));

        fixture.Sdk.VerifyNoOtherCalls();
        fixture.Runner.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DetectAsync_UnexpectedFailureDoesNotCacheIncompleteDetection()
    {
        var fixture = new Fixture();
        fixture.Runner.SetupSequence(value => value.ExecuteAsync("wslc.exe",
                It.Is<IReadOnlyList<string>>(arguments => string.Join(" ", arguments) == "version --format json"),
                null, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("runner failed"))
            .ReturnsAsync(fixture.Responses["version --format json"]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.DetectAsync(TestContext.Current.CancellationToken));
        var result = await fixture.Service.DetectAsync(TestContext.Current.CancellationToken);

        Assert.Equal("2.9.10.0", result.CliVersion);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("zh-CN")]
    public void CapabilityReasonAndEnvironmentMessageKeysExistInBothLanguages(string language)
    {
        var document = XDocument.Load(Path.Combine(TestPaths.SourceDirectory, "Resources", $"Strings.{language}.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var resources = document.Root!.Elements().ToDictionary(element => (string)element.Attribute(x + "Key")!, element => element.Value);
        string[] keys =
        [
            "CapabilityNotChecked", "CapabilityProbeFailed", "CapabilityHelpUnrecognized", "CapabilityAdvertised", "CapabilityNotAdvertised",
            "CliUnavailable", "SdkUpdateRequired", "SdkUnavailable", "MissingRuntimeComponents", "RuntimeDetectionIncomplete", "RuntimeReady"
        ];
        Assert.All(keys, key => Assert.False(string.IsNullOrWhiteSpace(resources[key])));
    }

    [Fact]
    public void RuntimeCapabilities_UnrecognizedFeatureDefaultsToUnknown()
    {
        var result = new RuntimeCapabilities()[(RuntimeFeature)int.MaxValue];
        Assert.Equal(CapabilitySupport.Unknown, result.Support);
        Assert.Equal("CapabilityNotChecked", result.ReasonKey);
    }

    private static OperationResult Success(string output) => new(true, 0, output, "", "");

    [Fact]
    public async Task NetworkOptions_AreDetectedIndependentlyAndCached()
    {
        var fixture = new Fixture();
        fixture.Responses["network create --help"] = Success(Help("network create", ["--subnet", "--gateway"]));
        fixture.Responses["network connect --help"] = Success(Help("network connect", ["--ip", "--network-alias-extra"]));
        var first = await fixture.Service.DetectAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CapabilitySupport.Supported, first[RuntimeFeature.NetworkCreateSubnet].Support);
        Assert.Equal(CapabilitySupport.Supported, first[RuntimeFeature.NetworkCreateGateway].Support);
        Assert.Equal(CapabilitySupport.Unsupported, first[RuntimeFeature.NetworkCreateIpRange].Support);
        Assert.Equal(CapabilitySupport.Unsupported, first[RuntimeFeature.NetworkConnectAlias].Support);
        Assert.Equal(CapabilitySupport.Unsupported, first[RuntimeFeature.NetworkConnectDriverOptions].Support);
        Assert.Same(first, await fixture.Service.DetectAsync(TestContext.Current.CancellationToken));
        Assert.Single(fixture.Commands, command => command == "network create --help");
    }
    private static OperationResult Failure(int exitCode = 1) => new(false, exitCode, "", "probe failed", "");

    private static string Help(string path, string[] tokens) =>
        $"使用情况: wslc{(path.Length == 0 ? "" : $" {path}")} [<选项>]\n" +
        string.Join("\n", tokens.Select(token => $"  {token}  描述")) + "\n  -?  --help  帮助\n";

    private sealed class Fixture
    {
        public Mock<IProcessRunner> Runner { get; } = new(MockBehavior.Strict);
        public Mock<IWslcSdkService> Sdk { get; } = new(MockBehavior.Strict);
        public RuntimeCapabilityService Service { get; }
        public Dictionary<string, OperationResult> Responses { get; } = new()
        {
            ["version --format json"] = Success("""{"Client":{"Version":"2.9.10.0"}}"""),
            ["version"] = Success("wslc 2.9.10.0"),
            ["--version"] = Success("wslc 2.9.10.0"),
            ["--help"] = Success(Help("", ["container", "image", "network", "system", "version"])),
            ["container --help"] = Success(Help("container", ["cp", "create", "start", "stop"])),
            ["container create --help"] = Success(Help("container create",
                ["--health-cmd", "--health-interval", "--health-retries", "--health-start-period", "--health-timeout", "--no-healthcheck",
                 "--mount", "--pull", "--stop-timeout", "--stop-signal", "--ip", "--network-alias"])),
            ["network --help"] = Success(Help("network", ["create", "connect", "disconnect"])),
            ["network connect --help"] = Success(Help("network connect", ["--ip", "--network-alias", "--driver-opt"])),
            ["network create --help"] = Success(Help("network create", ["--subnet", "--gateway", "--ip-range"])),
            ["image build --help"] = Success(Help("image build", ["--secret", "--output", "--progress", "--pull"])),
            ["system --help"] = Success(Help("system", ["info", "session"]))
        };

        public IReadOnlyList<string> Commands => Runner.Invocations
            .Select(invocation => string.Join(" ", (IReadOnlyList<string>)invocation.Arguments[1])).ToArray();

        public Fixture(TimeSpan? timeout = null)
        {
            Runner.Setup(value => value.ExecuteAsync(
                    "wslc.exe", It.IsAny<IReadOnlyList<string>>(), null, null, It.IsAny<CancellationToken>()))
                .Returns((string _, IReadOnlyList<string> arguments, string? input, IProgress<string>? progress, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    return Task.FromResult(Responses[string.Join(" ", arguments)]);
                });
            Sdk.Setup(value => value.GetMissingComponents()).Returns([]);
            Sdk.Setup(value => value.GetServiceVersion()).Returns("2.9.10");
            Sdk.Setup(value => value.InstallMissingComponentsAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Service = new RuntimeCapabilityService(Runner.Object, Sdk.Object, timeout);
        }
    }
}
