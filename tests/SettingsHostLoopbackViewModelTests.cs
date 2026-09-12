using System.IO;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.Messaging;
using ExWSLC.Models;
using ExWSLC.Services;
using ExWSLC.ViewModels;
using ExWSLC.ViewModels.Messages;
using Moq;

namespace ExWSLC.Tests;

public class SettingsHostLoopbackViewModelTests
{
    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("1;id")]
    [InlineData("+80")]
    [InlineData("80 ")]
    public void ProbeRequiresAnExplicitIntegerPort(string port)
    {
        using var fixture = new Fixture();
        fixture.Settings.HostProbePort = port;
        Assert.False(fixture.Settings.ProbeHostLoopbackCommand.CanExecute(null));
        Assert.Equal("HostProbePortValidation", fixture.Settings.HostProbeValidation);
        fixture.Runtime.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReadConfigurationNeverExecutesAProbeAndDisabledSettingsPreventIt()
    {
        using var fixture = new Fixture();
        fixture.Runtime.Setup(value => value.GetHostLoopbackConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HostLoopbackConfiguration(CapabilitySupport.Unsupported, "", "HostConfigDisabled"));
        await fixture.Settings.ReadHostLoopbackCommand.ExecuteAsync(null);
        Assert.Equal("HostConfigDisabled", fixture.Settings.HostLoopbackConfigStatus);
        Assert.False(fixture.Settings.ProbeHostLoopbackCommand.CanExecute(null));
        fixture.Runtime.Verify(value => value.GetHostLoopbackConfigurationAsync(It.IsAny<CancellationToken>()), Times.Once);
        fixture.Runtime.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EditingTheFormDoesNotRetargetTheInFlightRequestOrItsCopy()
    {
        using var fixture = new Fixture();
        var started = new TaskCompletionSource<HostLoopbackProbeRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Runtime.Setup(value => value.ProbeHostLoopbackAsync(It.IsAny<HostLoopbackProbeRequest>(), It.IsAny<RuntimeCapabilities>(), It.IsAny<CancellationToken>()))
            .Returns(async (HostLoopbackProbeRequest request, RuntimeCapabilities _, CancellationToken token) =>
            {
                started.SetResult(request);
                await finished.Task;
                return new(request, DateTimeOffset.UtcNow, HostLoopbackOutcome.TcpFailed, true);
            });
        var probe = fixture.Settings.ProbeHostLoopbackCommand.ExecuteAsync(null);
        var target = await started.Task;
        Assert.False(fixture.Settings.ReadHostLoopbackCommand.CanExecute(null));
        Assert.False(fixture.Settings.ProbeHostLoopbackCommand.CanExecute(null));
        fixture.Settings.HostProbePort = "443";
        fixture.Settings.HostProbeContainer = Running with { Id = new string('1', 64) };
        fixture.Settings.HostLoopbackConfiguration = new(CapabilitySupport.Supported, "changed.internal", "HostConfigCustom");
        finished.SetResult();
        await probe;
        Assert.Equal(HostLoopbackDiagnosticsTests.ContainerId, target.ContainerId);
        Assert.Equal(8080, target.Port);
        Assert.Equal(HostLoopbackConfiguration.DefaultHostName, target.HostName);
        Assert.Same(target, fixture.Settings.HostProbeResult!.Target);
        Assert.Contains(":8080", fixture.Settings.HostProbeSummary);
        Assert.DoesNotContain(":443", fixture.Settings.HostProbeSummary);
        fixture.Settings.CopyHostProbeCommand.Execute(null);
        Assert.Equal(fixture.Settings.HostProbeSummary, fixture.Copied);
        Assert.DoesNotContain("private-user", fixture.Copied!);
        Assert.False(fixture.Workspace.IsBusy);
    }

    [Fact]
    public async Task CancelKeepsTheOriginalTargetAndReenablesTheForm()
    {
        using var fixture = new Fixture();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Runtime.Setup(value => value.ProbeHostLoopbackAsync(It.IsAny<HostLoopbackProbeRequest>(), It.IsAny<RuntimeCapabilities>(), It.IsAny<CancellationToken>()))
            .Returns(async (HostLoopbackProbeRequest request, RuntimeCapabilities _, CancellationToken token) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, token);
                return new(request, DateTimeOffset.UtcNow, HostLoopbackOutcome.Connected);
            });
        var probe = fixture.Settings.ProbeHostLoopbackCommand.ExecuteAsync(null);
        await started.Task;
        fixture.Settings.HostProbePort = "9000";
        fixture.Workspace.CancelCurrentOperationCommand.Execute(null);
        await probe;
        Assert.Equal(HostLoopbackOutcome.Cancelled, fixture.Settings.HostProbeResult!.Outcome);
        Assert.Equal(8080, fixture.Settings.HostProbeResult.Target.Port);
        Assert.True(fixture.Settings.CopyHostProbeCommand.CanExecute(null));
        Assert.False(fixture.Workspace.IsBusy);
    }

    [Fact]
    public async Task RuntimeExceptionDoesNotLeakIntoTaskHistoryOrSummary()
    {
        using var fixture = new Fixture();
        fixture.Runtime.Setup(value => value.ProbeHostLoopbackAsync(It.IsAny<HostLoopbackProbeRequest>(), It.IsAny<RuntimeCapabilities>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("private-user secret-token"));
        await fixture.Settings.ProbeHostLoopbackCommand.ExecuteAsync(null);
        Assert.Equal(HostLoopbackOutcome.RuntimeFailed, fixture.Settings.HostProbeResult!.Outcome);
        Assert.DoesNotContain("secret", fixture.TaskResult!.CombinedOutput);
        Assert.DoesNotContain("private-user", fixture.Settings.HostProbeSummary);
    }

    [Fact]
    public void EveryOutcomeHasLocalizedStatusAndNextStepsAndLanguageChangeDoesNotProbe()
    {
        foreach (var language in new[] { "en-US", "zh-CN" })
        {
            var document = XDocument.Load(Path.Combine(TestPaths.SourceDirectory, "Resources", $"Strings.{language}.xaml"));
            var keys = document.Root!.Elements().Select(element => (string?)element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))).ToHashSet();
            foreach (var outcome in Enum.GetValues<HostLoopbackOutcome>())
            {
                Assert.Contains($"HostProbe{outcome}", keys);
                Assert.Contains($"HostProbe{outcome}Hint", keys);
            }
        }
        using var fixture = new Fixture();
        var changes = new List<string?>();
        fixture.Settings.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        WeakReferenceMessenger.Default.Send(new LanguageChangedMessage("zh-CN"));
        Assert.Contains(nameof(SettingsViewModel.HostProbeSummary), changes);
        Assert.Contains(nameof(SettingsViewModel.HostLoopbackConfigStatus), changes);
        fixture.Runtime.VerifyNoOtherCalls();
    }

    private static ContainerSummary Running => new(HostLoopbackDiagnosticsTests.ContainerId, "private-user", "test", "running", "Up", "", "");

    private sealed class Fixture : IDisposable
    {
        public Mock<IContainerRuntime> Runtime { get; } = new(MockBehavior.Strict);
        public RuntimeWorkspace Workspace { get; }
        public SettingsViewModel Settings { get; }
        public string? Copied { get; private set; }
        public OperationResult? TaskResult { get; private set; }

        public Fixture()
        {
            var settings = new Mock<ISettingsService>();
            settings.SetupGet(value => value.Current).Returns(new AppSettings());
            var tasks = new Mock<ITaskService>();
            tasks.Setup(value => value.RunAsync(It.IsAny<string>(), It.IsAny<Func<IProgress<string>, CancellationToken, Task<OperationResult>>>(), It.IsAny<CancellationToken>()))
                .Returns(async (string _, Func<IProgress<string>, CancellationToken, Task<OperationResult>> action, CancellationToken token) =>
                    TaskResult = await action(new Progress<string>(), token));
            var interaction = new Mock<IUserInteractionService>();
            interaction.Setup(value => value.SetClipboardText(It.IsAny<string>())).Callback<string>(text => Copied = text);
            Workspace = new(Runtime.Object, Mock.Of<IRuntimeCapabilityService>(), settings.Object, tasks.Object, interaction.Object);
            Settings = new(Workspace) { HostLoopbackConfiguration = Models.HostLoopbackConfiguration.Default, HostProbeContainer = Running, HostProbePort = "8080" };
        }

        public void Dispose()
        {
            WeakReferenceMessenger.Default.UnregisterAll(Settings);
            Workspace.Dispose();
        }
    }
}
