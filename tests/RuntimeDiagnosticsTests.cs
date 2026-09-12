using System.IO;
using System.Text.Json;
using ExWSLC.Helpers;
using ExWSLC.Models;
using ExWSLC.Services;
using Moq;

namespace ExWSLC.Tests;

public class RuntimeDiagnosticsTests
{
    internal const string CompleteJson = """
        {"Client":{"Version":"2.9.10.0","WindowsVersion":"10.0.26200.9445","KernelVersion":"6.18.40.1-1",
        "Direct3DVersion":"1.611.1-81528511","DxCoreVersion":"10.0.26100.1-240331-1435.ge-release",
        "SettingsFile":"C:\\Users\\private-user\\secret\\settings.yaml","Environment":{"TOKEN":"secret-value"}},
        "Server":{"SessionManagerVersion":"2.9.10","Sessions":[{"Name":"wslc-cli-private-user","ID":1,"CreatorPid":1234,"CommandLine":"password=secret"}]}}
        """;

    [Fact]
    public void Parse_ProjectsCompletePayloadWithoutPersonalOrUnknownFields()
    {
        var info = RuntimeSystemInfoParser.Parse(CompleteJson);
        Assert.Equal("2.9.10.0", info.ClientVersion);
        Assert.Equal("2.9.10", info.ServiceVersion);
        Assert.Equal("10.0.26200.9445", info.WindowsVersion);
        Assert.Equal("6.18.40.1-1", info.KernelVersion);
        Assert.Equal("1.611.1-81528511", info.Direct3DVersion);
        Assert.Equal("10.0.26100.1-240331-1435.ge-release", info.DxCoreVersion);
        Assert.Equal("[redacted]", info.SettingsFile);
        Assert.Equal(new RuntimeSessionInfo("wslc-cli-[user]", 1, 1234), Assert.Single(info.Sessions!));
        var serialized = JsonSerializer.Serialize(info);
        foreach (var secret in new[] { "private-user", "secret", "Environment", "CommandLine", "TOKEN", "Users" })
            Assert.DoesNotContain(secret, serialized);
    }

    [Fact]
    public void Parse_AbbreviatesOnlyTheConfirmedLocalSettingsPath()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "wslc", "settings.yaml");
        var info = RuntimeSystemInfoParser.Parse(JsonSerializer.Serialize(new { Client = new { SettingsFile = path } }));
        Assert.Equal(@"%LOCALAPPDATA%\wslc\settings.yaml", info.SettingsFile);
    }

    [Theory]
    [InlineData("{\"Client\":{\"Version\":\"2.9.10.0\"}}")]
    [InlineData("{\"Client\":{\"Version\":\"2.9.10.0\",\"KernelVersion\":42},\"Server\":false}")]
    public void Parse_PreservesValidFieldsInPartialPayload(string json)
    {
        var info = RuntimeSystemInfoParser.Parse(json);
        Assert.Equal("2.9.10.0", info.ClientVersion);
        Assert.Empty(info.KernelVersion);
        Assert.Null(info.Sessions);
    }

    [Fact]
    public void Parse_DistinguishesEmptyAndMalformedSessionLists()
    {
        Assert.Empty(RuntimeSystemInfoParser.Parse("{\"Server\":{\"Sessions\":[]}}").Sessions!);
        Assert.Null(RuntimeSystemInfoParser.Parse("{\"Server\":{\"Sessions\":false}}").Sessions);
        var info = RuntimeSystemInfoParser.Parse("""
            {"Server":{"Sessions":[{"ID":-1,"CreatorPid":"private-path","Name":"token=secret"},null]}}
            """);
        Assert.Equal(2, info.Sessions!.Count);
        Assert.Null(info.Sessions[0].Id);
        Assert.Null(info.Sessions[0].CreatorPid);
        Assert.Equal("[redacted]", info.Sessions[0].Name);
        Assert.Empty(info.Sessions[1].Name);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"Other\":{\"Client\":{}}}")]
    public void Parse_RejectsUnrecognizedDocuments(string json) =>
        Assert.ThrowsAny<JsonException>(() => RuntimeSystemInfoParser.Parse(json));

    [Theory]
    [InlineData("C:\\Users\\private-user")]
    [InlineData("2.9.10.0\nTOKEN=secret")]
    [InlineData("Bearer-secret")]
    public void Parse_RejectsFreeTextInVersionFields(string value) => Assert.Empty(RuntimeSystemInfoParser.SafeVersion(value));

    [Fact]
    public async Task Query_UsesOnlyTheInfoCommandAndKeepsVersionSourcesSeparate()
    {
        var (runtime, runner) = CreateRuntime(new(true, 0, CompleteJson, "", ""));
        var before = DateTimeOffset.UtcNow;
        var result = await runtime.GetSystemInfoAsync(Baseline, TestContext.Current.CancellationToken);
        Assert.Equal("2.9.8", result.BasicServiceVersion);
        Assert.Equal("2.9.10", result.SystemInfo!.ServiceVersion);
        Assert.Equal("DiagnosticsCollected", result.StatusKey);
        Assert.InRange(result.CollectedAt, before, DateTimeOffset.UtcNow);
        runner.Verify(value => value.ExecuteAsync("wslc.exe",
            It.Is<IReadOnlyList<string>>(args => args.SequenceEqual(new[] { "system", "info", "--format", "json" })),
            null, null, It.IsAny<CancellationToken>()), Times.Once);
        runner.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Query_FallsBackWithoutCallingUnsupportedCli(bool missingCli)
    {
        var runner = new Mock<IProcessRunner>(MockBehavior.Strict);
        var capabilities = Baseline with
        {
            CliAvailability = missingCli ? CapabilitySupport.Unsupported : CapabilitySupport.Supported,
            Features = new Dictionary<RuntimeFeature, RuntimeFeatureCapability>
            {
                [RuntimeFeature.SystemInfo] = new(CapabilitySupport.Unsupported, "CapabilityCommandMissing", "wslc system --help")
            }
        };
        var result = await new WslcContainerRuntime(runner.Object).GetSystemInfoAsync(capabilities, TestContext.Current.CancellationToken);
        Assert.Null(result.SystemInfo);
        Assert.Equal("2.9.8.0", result.BasicCliVersion);
        Assert.Equal("2.9.8", result.BasicServiceVersion);
        Assert.Equal(missingCli ? "DiagnosticsCliUnavailable" : "DiagnosticsUnsupported", result.StatusKey);
        runner.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true, "broken json", "DiagnosticsInvalidJson")]
    [InlineData(false, "broken json", "DiagnosticsQueryFailed")]
    [InlineData(false, "{\"Client\":{\"Version\":\"2.9.10.0\"}}", "DiagnosticsQueryFailed")]
    public async Task Query_PreservesPartialOutputAndNeverExposesCliErrors(bool success, string output, string status)
    {
        var (runtime, _) = CreateRuntime(new(success, success ? 0 : 9, output, "C:\\Users\\private-user token=secret", ""));
        var result = await runtime.GetSystemInfoAsync(Baseline, TestContext.Current.CancellationToken);
        Assert.Equal(status, result.StatusKey);
        Assert.Equal(output.StartsWith('{') ? "2.9.10.0" : null, result.SystemInfo?.ClientVersion);
        var summary = RuntimeDiagnosticsFormatter.Summary(result);
        Assert.Contains("2.9.8.0", summary);
        Assert.DoesNotContain("private-user", summary);
        Assert.DoesNotContain("secret", summary);
    }

    [Fact]
    public async Task Query_PropagatesCancellationEvenWhenRunnerReturnsFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var runner = new Mock<IProcessRunner>();
        runner.Setup(value => value.ExecuteAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(() => { cancellation.Cancel(); return Task.FromResult(new OperationResult(false, -2, CompleteJson, "", "")); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WslcContainerRuntime(runner.Object).GetSystemInfoAsync(Baseline, cancellation.Token));
    }

    [Fact]
    public async Task Query_DoesNotExposeThrownExceptionMessages()
    {
        var runner = new Mock<IProcessRunner>();
        runner.Setup(value => value.ExecuteAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), null, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("private-path password=secret"));
        var result = await new WslcContainerRuntime(runner.Object).GetSystemInfoAsync(Baseline, TestContext.Current.CancellationToken);
        Assert.Equal("DiagnosticsQueryFailed", result.StatusKey);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task Query_ReportsTimeoutSeparatelyFromUserCancellation()
    {
        var runner = new Mock<IProcessRunner>();
        runner.Setup(value => value.ExecuteAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), null, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var result = await new WslcContainerRuntime(runner.Object).GetSystemInfoAsync(Baseline, TestContext.Current.CancellationToken);
        Assert.Equal("DiagnosticsTimedOut", result.StatusKey);
        Assert.Equal("2.9.8.0", result.BasicCliVersion);
    }

    [Fact(Skip = "Set EXWSLC_LIVE_DIAGNOSTICS=1 for read-only WSLC validation.", SkipUnless = nameof(LiveDiagnosticsEnabled))]
    public async Task LiveQuery_MatchesWhitelistedCliFieldsWithoutCreatingResources()
    {
        var token = TestContext.Current.CancellationToken;
        var runner = new WslcProcessRunner();
        var capabilities = await new RuntimeCapabilityService(runner, new WslcSdkService()).DetectAsync(token);
        var result = await new WslcContainerRuntime(runner).GetSystemInfoAsync(capabilities, token);
        var cli = await runner.ExecuteAsync("wslc.exe", ["system", "info", "--format", "json"], cancellationToken: token);
        Assert.True(cli.Success);
        var expected = RuntimeSystemInfoParser.Parse(cli.Output);
        Assert.Equal("DiagnosticsCollected", result.StatusKey);
        Assert.Equal(expected.ClientVersion, result.SystemInfo!.ClientVersion);
        Assert.Equal(expected.ServiceVersion, result.SystemInfo.ServiceVersion);
        Assert.Equal(expected.WindowsVersion, result.SystemInfo.WindowsVersion);
        Assert.Equal(expected.KernelVersion, result.SystemInfo.KernelVersion);
        Assert.Equal(expected.Direct3DVersion, result.SystemInfo.Direct3DVersion);
        Assert.Equal(expected.DxCoreVersion, result.SystemInfo.DxCoreVersion);
        Assert.Equal(expected.SettingsFile, result.SystemInfo.SettingsFile);
        // Sessions can legitimately change between two independent read-only samples.
        var summary = RuntimeDiagnosticsFormatter.Summary(result);
        Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserName, summary, StringComparison.OrdinalIgnoreCase);
    }

    public static bool LiveDiagnosticsEnabled => Environment.GetEnvironmentVariable("EXWSLC_LIVE_DIAGNOSTICS") == "1";

    private static RuntimeCapabilities Baseline => new() { CliVersion = "2.9.8.0", ServiceVersion = "2.9.8", SdkPackageVersion = "2.9.9" };

    private static (WslcContainerRuntime Runtime, Mock<IProcessRunner> Runner) CreateRuntime(OperationResult result)
    {
        var runner = new Mock<IProcessRunner>(MockBehavior.Strict);
        runner.Setup(value => value.ExecuteAsync("wslc.exe", It.IsAny<IReadOnlyList<string>>(), null, null, It.IsAny<CancellationToken>())).ReturnsAsync(result);
        return (new WslcContainerRuntime(runner.Object), runner);
    }
}
