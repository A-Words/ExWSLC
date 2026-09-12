using System.Text.Json;
using ExWSLC.Helpers;
using ExWSLC.Models;
using ExWSLC.Services;
using Moq;

namespace ExWSLC.Tests;

public class ContainerHealthTests
{
    [Theory]
    [InlineData(null, ContainerHealthStatus.Unknown)]
    [InlineData("", ContainerHealthStatus.NotConfigured)]
    [InlineData("starting", ContainerHealthStatus.Starting)]
    [InlineData("HEALTHY", ContainerHealthStatus.Healthy)]
    [InlineData("unhealthy", ContainerHealthStatus.Unhealthy)]
    [InlineData("future-status", ContainerHealthStatus.Unknown)]
    public void ListStatus_DistinguishesKnownMissingAndUnknown(string? status, ContainerHealthStatus expected)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { HealthStatus = status, State = "running", Status = "Up" }));
        Assert.Equal(expected, ContainerHealthParser.ReadListStatus(document.RootElement));
        using var missing = JsonDocument.Parse("""{"State":"running","Status":"Up (healthy)"}""");
        Assert.Equal(ContainerHealthStatus.Unknown, ContainerHealthParser.ReadListStatus(missing.RootElement));
    }

    [Theory]
    [InlineData("{}", ContainerHealthStatus.Unknown)]
    [InlineData("{\"State\":{\"Running\":true}}", ContainerHealthStatus.Unknown)]
    [InlineData("{\"Config\":{\"Healthcheck\":null}}", ContainerHealthStatus.NotConfigured)]
    [InlineData("{\"Config\":{\"Healthcheck\":{\"Test\":[\"NONE\"]}}}", ContainerHealthStatus.NotConfigured)]
    [InlineData("{\"Config\":{\"Healthcheck\":{\"Test\":[\"CMD-SHELL\",\"true\"]}}}", ContainerHealthStatus.Unknown)]
    [InlineData("{\"State\":{\"Health\":{\"Status\":\"starting\"}}}", ContainerHealthStatus.Starting)]
    [InlineData("{\"State\":{\"Health\":{\"Status\":\"healthy\"}}}", ContainerHealthStatus.Healthy)]
    [InlineData("{\"State\":{\"Health\":{\"Status\":\"unhealthy\"}}}", ContainerHealthStatus.Unhealthy)]
    [InlineData("{\"State\":{\"Health\":{\"Status\":\"new\"}}}", ContainerHealthStatus.Unknown)]
    [InlineData("{\"State\":{\"Health\":{}}}", ContainerHealthStatus.Unknown)]
    [InlineData("{\"Config\":{\"Healthcheck\":123},\"State\":{\"Health\":[]}}", ContainerHealthStatus.Unknown)]
    public void Inspect_UsesOnlyHealthEvidence(string payload, ContainerHealthStatus expected)
    {
        Assert.True(ContainerInspectDetailsParser.TryParse(payload, out var details));
        Assert.Equal(expected, details.Health.Status);
        Assert.Null(details.Health.FailingStreak);
    }

    [Fact]
    public void StoppedContainer_EmptyListHealthDoesNotProveItHasNoCheck()
    {
        using var document = JsonDocument.Parse("""{"State":"exited","HealthStatus":""}""");
        Assert.Equal(ContainerHealthStatus.Unknown, ContainerHealthParser.ReadListStatus(document.RootElement));
    }

    [Fact]
    public void Inspect_BoundsRecordsAndOutputAndPreservesMissingExitCode()
    {
        var payload = JsonSerializer.Serialize(new[] { new { Id = "one", State = new { Health = new
        {
            Status = "unhealthy", FailingStreak = 42,
            Log = Enumerable.Range(0, 20).Select(i => new { Start = i.ToString(), End = "end", Output = new string('x', 5000) })
        } } } });
        Assert.True(ContainerInspectDetailsParser.TryParse(payload, out var details));
        Assert.Equal(42, details.Health.FailingStreak);
        Assert.Equal(10, details.Health.Logs.Count);
        Assert.Equal("19", details.Health.Logs[0].Start);
        Assert.Equal("10", details.Health.Logs[^1].Start);
        Assert.All(details.Health.Logs, log => { Assert.Equal(4097, log.Output.Length); Assert.Null(log.ExitCode); });
        Assert.Contains(new string('x', 5000), details.RawJson);
    }

    [Theory]
    [InlineData("0", 0L)] [InlineData("0s", 0L)] [InlineData("1ns", 1L)]
    [InlineData("500ms", 500000000L)] [InlineData("1m30s", 90000000000L)]
    [InlineData("1.5h", 5400000000000L)] [InlineData("100us", 100000L)]
    [InlineData(".5s", 500000000L)] [InlineData("+1s", 1000000000L)]
    [InlineData("1µs", 1000L)] [InlineData("1μs", 1000L)]
    [InlineData("9223372036854775807ns", long.MaxValue)]
    public void Duration_ParsesUnitsAndBoundary(string input, long expected)
    {
        Assert.True(HealthCheckOptions.TryParseDuration(input, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("")] [InlineData("-1s")] [InlineData("30")] [InlineData("30 s")]
    [InlineData("1.2.3s")] [InlineData("1d")] [InlineData("9223372036854775808ns")]
    [InlineData("9223372036854775807ns1ns")] [InlineData("1e3s")]
    public void Duration_RejectsMalformedNegativeAndOverflow(string input) => Assert.False(HealthCheckOptions.TryParseDuration(input, out _));

    [Fact]
    public void RunArguments_PreserveInheritanceDisableAndExplicitZero()
    {
        var spec = new ContainerCreateSpec { Image = "image" };
        Assert.Equal(["run", "--detach", "image"], WslcContainerRuntime.BuildRunArguments(spec));
        spec.HealthMode = HealthCheckMode.Disabled;
        Assert.Equal(["run", "--detach", "--no-healthcheck", "image"], WslcContainerRuntime.BuildRunArguments(spec));
        spec.HealthMode = HealthCheckMode.Custom;
        spec.HealthCommand = "test -f '/tmp/ready file' && echo ready";
        spec.HealthInterval = "1m30s";
        spec.HealthTimeout = "500ms";
        spec.HealthStartPeriod = "0";
        spec.HealthRetries = "0";
        Assert.Equal(["run", "--detach", "--health-cmd", spec.HealthCommand, "--health-interval", "1m30s",
            "--health-timeout", "500ms", "--health-start-period", "0", "--health-retries", "0", "image"], WslcContainerRuntime.BuildRunArguments(spec));
        spec.HealthCommand = null;
        Assert.DoesNotContain("--health-cmd", WslcContainerRuntime.BuildRunArguments(spec));
    }

    [Theory]
    [InlineData(HealthCheckMode.Inherit)] [InlineData(HealthCheckMode.Disabled)]
    public void RunArguments_RejectConflictingOverrides(HealthCheckMode mode) =>
        Assert.Throws<ArgumentException>(() => WslcContainerRuntime.BuildRunArguments(new() { Image = "image", HealthMode = mode, HealthRetries = "0" }));

    [Theory]
    [InlineData("")] [InlineData("-1")] [InlineData("2147483648")] [InlineData("1.5")] [InlineData("3oops")]
    public void RunArguments_RejectInvalidRetries(string retries) =>
        Assert.Throws<ArgumentException>(() => WslcContainerRuntime.BuildRunArguments(new() { Image = "image", HealthMode = HealthCheckMode.Custom, HealthRetries = retries }));

    [Theory]
    [InlineData(CapabilitySupport.Unknown)] [InlineData(CapabilitySupport.Unsupported)] [InlineData(CapabilitySupport.Supported)]
    public async Task Runtime_GatesOverridesOnCachedDetection(CapabilitySupport support)
    {
        var runner = new Mock<IProcessRunner>();
        runner.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult(true, 0, "", "", ""));
        var capabilities = new Mock<IRuntimeCapabilityService>();
        capabilities.Setup(x => x.DetectAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new RuntimeCapabilities
        {
            Features = new Dictionary<RuntimeFeature, RuntimeFeatureCapability> { [RuntimeFeature.HealthChecks] = new(support, "", "") }
        });
        var runtime = new WslcContainerRuntime(runner.Object, capabilities.Object);
        var spec = new ContainerCreateSpec { Image = "image", HealthMode = HealthCheckMode.Disabled };
        if (support == CapabilitySupport.Supported) await runtime.RunContainerAsync(spec, cancellationToken: TestContext.Current.CancellationToken);
        else await Assert.ThrowsAsync<ArgumentException>(() => runtime.RunContainerAsync(spec, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(support == CapabilitySupport.Supported ? 1 : 0, runner.Invocations.Count);
        spec.HealthMode = HealthCheckMode.Inherit;
        await runtime.RunContainerAsync(spec, cancellationToken: TestContext.Current.CancellationToken);
        capabilities.Verify(x => x.DetectAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
