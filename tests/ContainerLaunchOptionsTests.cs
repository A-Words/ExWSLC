using ExWSLC.Helpers;
using ExWSLC.Models;
using ExWSLC.Services;
using Moq;

namespace ExWSLC.Tests;

public class ContainerLaunchOptionsTests
{
    [Fact]
    public void Defaults_OmitEveryOverride()
    {
        Assert.Equal(["run", "--detach", "image"], WslcContainerRuntime.BuildRunArguments(new() { Image = "image" }));
        Assert.Equal(["container", "stop", "id"], ContainerLaunchOptions.BuildStopArguments("id", new()));
    }

    [Theory]
    [InlineData(ContainerPullPolicy.Always, "always")]
    [InlineData(ContainerPullPolicy.Missing, "missing")]
    [InlineData(ContainerPullPolicy.Never, "never")]
    public void PullPolicies_PreserveHealthAndRemoval(ContainerPullPolicy policy, string expected)
    {
        Assert.Equal(["run", "--detach", "--pull", expected, "--no-healthcheck", "--rm", "image"],
            WslcContainerRuntime.BuildRunArguments(new() { Image = "image", PullPolicy = policy, HealthMode = HealthCheckMode.Disabled, RemoveWhenStopped = true }));
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData("0", 0)]
    [InlineData("20", 20)]
    [InlineData("-1", -1)]
    [InlineData("2147483647", int.MaxValue)]
    public void Timeout_UsesDistinctCreateAndStopFlags(string text, int? expected)
    {
        Assert.Equal(expected, ContainerLaunchOptions.ParseTimeout(text));
        var create = WslcContainerRuntime.BuildRunArguments(new() { Image = "image", StopTimeoutSeconds = expected, StopSignal = "SIGINT" });
        var stop = ContainerLaunchOptions.BuildStopArguments("id", new(expected, "SIGQUIT"));
        Assert.Contains("--stop-signal", create);
        Assert.Contains("SIGINT", create);
        Assert.Contains("--signal", stop);
        Assert.Contains("SIGQUIT", stop);
        Assert.Equal(expected.HasValue, create.Contains("--stop-timeout"));
        Assert.Equal(expected.HasValue, stop.Contains("--time"));
        Assert.DoesNotContain("--time", create);
        Assert.DoesNotContain("--stop-timeout", stop);
    }

    [Theory]
    [InlineData("-2")]
    [InlineData("1.5")]
    [InlineData("5s")]
    [InlineData("2147483648")]
    public void InvalidTimeout_IsRejected(string text) => Assert.Throws<ArgumentException>(() => ContainerLaunchOptions.ParseTimeout(text));

    [Theory]
    [InlineData("SIGTERM")]
    [InlineData("int")]
    [InlineData("31")]
    [InlineData("SIGTKFLT")]
    public void VerifiedSignals_AreAccepted(string signal) => ContainerLaunchOptions.ValidateStop(new(Signal: signal));

    [Theory]
    [InlineData("0")]
    [InlineData("32")]
    [InlineData("SIGRTMIN")]
    [InlineData("BOGUS")]
    [InlineData("SIGTERM --time 0")]
    [InlineData("")]
    public void InvalidSignals_AreRejected(string signal) => Assert.Throws<ArgumentException>(() => ContainerLaunchOptions.ValidateStop(new(Signal: signal)));

    [Fact]
    public void Mounts_UseVerifiedCsvAndTmpfsGrammarWithoutShellEscaping()
    {
        var spec = new ContainerCreateSpec { Image = "image" };
        spec.Mounts.Add(new(ContainerMountKind.Bind, @"C:\中文, space & $(literal)", "/data,\"quoted\"", true));
        spec.Mounts.Add(new(ContainerMountKind.Volume, "cache-volume", "/cache"));
        spec.Mounts.Add(new(ContainerMountKind.Tmpfs, "", "/memory", true));
        spec.Volumes.Add(@"C:\legacy, source:/legacy:ro");
        var arguments = WslcContainerRuntime.BuildRunArguments(spec);
        Assert.Contains("type=bind,\"source=C:\\中文, space & $(literal)\",\"target=/data,\"\"quoted\"\"\",readonly=true", arguments);
        Assert.Contains("type=volume,source=cache-volume,target=/cache,readonly=false", arguments);
        Assert.Contains("/memory:ro", arguments);
        Assert.Contains(@"C:\legacy, source:/legacy:ro", arguments);
    }

    [Theory]
    [InlineData(ContainerMountKind.Bind, "", "/data")]
    [InlineData(ContainerMountKind.Bind, "relative", "/data")]
    [InlineData(ContainerMountKind.Bind, "C:relative", "/data")]
    [InlineData(ContainerMountKind.Bind, "C:\\bad\"path", "/data")]
    [InlineData(ContainerMountKind.Bind, "C:\\file:stream", "/data")]
    [InlineData(ContainerMountKind.Bind, "C:\\bad\u0001path", "/data")]
    [InlineData(ContainerMountKind.Volume, "", "/data")]
    [InlineData(ContainerMountKind.Volume, "a", "/data")]
    [InlineData(ContainerMountKind.Volume, "bad/name", "/data")]
    [InlineData(ContainerMountKind.Volume, "valid", "data")]
    [InlineData(ContainerMountKind.Tmpfs, "source", "/data")]
    [InlineData(ContainerMountKind.Tmpfs, "", "/data:ro")]
    public void InvalidMounts_AreRejected(ContainerMountKind kind, string source, string target)
    {
        var spec = new ContainerCreateSpec { Image = "image" };
        spec.Mounts.Add(new(kind, source, target));
        Assert.Throws<ArgumentException>(() => WslcContainerRuntime.BuildRunArguments(spec));
    }

    [Theory]
    [InlineData("/data/")]
    [InlineData("//data/.")]
    [InlineData("/other/../data")]
    public void DuplicateTargets_AreRejectedAcrossSimpleAndStructuredMounts(string target)
    {
        var spec = new ContainerCreateSpec { Image = "image" };
        spec.Volumes.Add(@"C:\source:/data:rw");
        spec.Mounts.Add(new(ContainerMountKind.Tmpfs, "", target));
        Assert.Throws<ArgumentException>(() => WslcContainerRuntime.BuildRunArguments(spec));
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"\\server\share\dir,中文")]
    public void AbsoluteWindowsSources_AreAccepted(string source)
    {
        var spec = new ContainerCreateSpec { Image = "image" };
        spec.Mounts.Add(new(ContainerMountKind.Bind, source, "/data"));
        Assert.Contains("--mount", WslcContainerRuntime.BuildRunArguments(spec));
    }

    [Theory]
    [InlineData(CapabilitySupport.Unknown)]
    [InlineData(CapabilitySupport.Unsupported)]
    public async Task UnverifiedOverrides_AreNotSentToRuntime(CapabilitySupport support)
    {
        var runner = new Mock<IProcessRunner>(MockBehavior.Strict);
        var capability = new Mock<IRuntimeCapabilityService>();
        capability.Setup(x => x.DetectAsync(It.IsAny<CancellationToken>())).ReturnsAsync(NetworkOperationTests.AllCapabilities(support));
        var runtime = new WslcContainerRuntime(runner.Object, capability.Object);
        await Assert.ThrowsAsync<ArgumentException>(() => runtime.RunContainerAsync(new() { Image = "image", PullPolicy = ContainerPullPolicy.Always }, cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => runtime.StopContainerAsync("id", new(-1), TestContext.Current.CancellationToken));
        Assert.Empty(runner.Invocations);
    }

    [Fact]
    public async Task Restart_UsesInheritedStopThenStart_AndStopsOnFailureOrCancellation()
    {
        var runner = new Mock<IProcessRunner>();
        var calls = new List<string>();
        var stopCode = 0;
        runner.Setup(x => x.ExecuteAsync("wslc.exe", It.IsAny<IReadOnlyList<string>>(), null, null, It.IsAny<CancellationToken>()))
            .Returns<string, IReadOnlyList<string>, string?, IProgress<string>?, CancellationToken>((_, args, _, _, _) =>
            {
                calls.Add(string.Join(' ', args));
                return Task.FromResult(new OperationResult(stopCode == 0, stopCode, "", "", ""));
            });
        var runtime = new WslcContainerRuntime(runner.Object);
        Assert.True((await runtime.RestartContainerAsync("id", TestContext.Current.CancellationToken)).Success);
        Assert.Equal(["container stop id", "container start id"], calls);
        calls.Clear();
        stopCode = 1;
        Assert.False((await runtime.RestartContainerAsync("id", TestContext.Current.CancellationToken)).Success);
        Assert.Equal(["container stop id"], calls);
        stopCode = -2;
        await Assert.ThrowsAsync<OperationCanceledException>(() => runtime.RestartContainerAsync("id", TestContext.Current.CancellationToken));
    }
}
