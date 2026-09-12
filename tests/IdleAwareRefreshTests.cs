using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using ExWSLC.Models;
using ExWSLC.Services;
using ExWSLC.ViewModels;
using Moq;

namespace ExWSLC.Tests;

public class IdleAwareRefreshTests
{
    [Fact]
    public async Task MinimizedPreference_PausesAllFiveQueriesAndRestoresThemTogether()
    {
        using var fixture = new Fixture();
        fixture.Workspace.SetWindowMinimized(true);
        await fixture.Workspace.AutoRefreshTickAsync();
        Assert.Equal(5, fixture.Runtime.Invocations.Count); // Default preserves polling.
        fixture.Runtime.Invocations.Clear();
        fixture.Preferences.PauseAutoRefreshWhenMinimized = true;
        fixture.Workspace.ApplyRefreshPreferences();
        Assert.True(fixture.Workspace.IsAutoRefreshPaused);
        for (var tick = 0; tick < 12; tick++) await fixture.Workspace.AutoRefreshTickAsync();
        fixture.Runtime.VerifyNoOtherCalls();
        fixture.Workspace.SetWindowMinimized(false);
        await fixture.Workspace.AutoRefreshTickAsync();
        Assert.False(fixture.Workspace.IsAutoRefreshPaused);
        Assert.Equal(5, fixture.Runtime.Invocations.Count);
    }

    [Fact]
    public async Task ManualRefresh_RemainsAvailableWhileMinimizedAndDoesNotResumePolling()
    {
        using var fixture = new Fixture();
        fixture.Minimize();
        await fixture.Workspace.RefreshAllCommand.ExecuteAsync(null);
        Assert.Equal(5, fixture.Runtime.Invocations.Count);
        await fixture.Workspace.AutoRefreshTickAsync();
        Assert.Equal(5, fixture.Runtime.Invocations.Count);
        Assert.True(fixture.Workspace.IsAutoRefreshPaused);
    }

    [Fact]
    public async Task InFlightRefresh_IsNotCancelledOrReenteredByWindowChanges()
    {
        using var fixture = new Fixture();
        var query = new TaskCompletionSource<IReadOnlyList<ContainerSummary>>();
        CancellationToken queryToken = default;
        fixture.Runtime.Setup(value => value.GetContainersAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken token) => { queryToken = token; return query.Task; });
        var refresh = fixture.Workspace.AutoRefreshTickAsync();
        fixture.Minimize();
        fixture.Workspace.SetWindowMinimized(false);
        await fixture.Workspace.AutoRefreshTickAsync();
        await fixture.Workspace.RefreshAllAsync();
        Assert.False(queryToken.IsCancellationRequested);
        Assert.Equal(5, fixture.Runtime.Invocations.Count);
        query.SetResult([]);
        await refresh;
        Assert.False(fixture.Workspace.IsBusy);
    }

    [Fact]
    public async Task TaskAndLogFollow_KeepTheirTokensAcrossMinimizeAndRestore()
    {
        using var fixture = new Fixture();
        var started = new TaskCompletionSource<CancellationToken>();
        var followDone = new TaskCompletionSource<OperationResult>();
        fixture.Runtime.Setup(value => value.FollowLogsAsync("test-id", It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .Returns((string _, IProgress<string>? progress, CancellationToken token) =>
            { started.SetResult(token); return followDone.Task; });
        var containers = new ContainersViewModel(fixture.Workspace)
        {
            SelectedDetailTabIndex = 0,
            SelectedContainer = new("test-id", "test", "test", "running", "", "", "")
        };
        var logToken = await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        fixture.Runtime.Setup(value => value.GetContainersAsync(It.IsAny<CancellationToken>())).ReturnsAsync([containers.SelectedContainer!]);
        var taskDone = new TaskCompletionSource<OperationResult>();
        CancellationToken taskToken = default;
        var task = fixture.Workspace.RunTrackedAsync("test transfer", (_, token) => { taskToken = token; return taskDone.Task; });
        fixture.Minimize();
        fixture.Workspace.SetWindowMinimized(false);
        await fixture.Workspace.AutoRefreshTickAsync();
        Assert.Single(fixture.Runtime.Invocations); // Only the explicit log follow.
        Assert.False(taskToken.IsCancellationRequested);
        Assert.False(logToken.IsCancellationRequested);
        taskDone.SetResult(Success);
        await task;
        await fixture.Workspace.AutoRefreshTickAsync();
        Assert.Equal(6, fixture.Runtime.Invocations.Count);
        Assert.False(logToken.IsCancellationRequested);
        fixture.Workspace.Dispose();
        Assert.True(logToken.IsCancellationRequested);
        followDone.SetResult(Success);
        GC.KeepAlive(containers);
    }

    [Fact]
    public async Task Closing_CancelsInFlightQueryAndRejectsFutureRefreshes()
    {
        using var fixture = new Fixture();
        fixture.Runtime.Setup(value => value.GetContainersAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken token) => { await Task.Delay(Timeout.Infinite, token); return Array.Empty<ContainerSummary>(); });
        var refresh = fixture.Workspace.AutoRefreshTickAsync();
        fixture.Workspace.Dispose();
        await refresh;
        await fixture.Workspace.AutoRefreshTickAsync();
        await fixture.Workspace.RefreshAllAsync();
        fixture.Workspace.SetWindowMinimized(false);
        Assert.Equal(5, fixture.Runtime.Invocations.Count);
        Assert.Empty(fixture.Workspace.RefreshError);
        Assert.False(fixture.Workspace.IsBusy);
    }

    [Fact]
    public async Task RestoreDuringTask_WakesWorkspaceImmediatelyAfterTaskCompletes()
    {
        using var fixture = new Fixture();
        fixture.Preferences.RefreshIntervalSeconds = 300;
        await fixture.Workspace.InitializeAsync();
        fixture.Runtime.Invocations.Clear();
        var refreshed = new TaskCompletionSource();
        fixture.Workspace.Refreshed += (_, _) => refreshed.TrySetResult();
        var taskDone = new TaskCompletionSource<OperationResult>();
        var task = fixture.Workspace.RunTrackedAsync("test build", (_, _) => taskDone.Task);
        for (var toggle = 0; toggle < 10; toggle++)
        {
            fixture.Minimize();
            fixture.Workspace.SetWindowMinimized(false);
        }
        fixture.Runtime.VerifyNoOtherCalls();
        taskDone.SetResult(Success);
        await task;
        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(5, fixture.Runtime.Invocations.Count);
    }

    [Fact]
    public async Task InitializeWhileMinimized_DetectsCapabilitiesButDefersInventoryUntilRestore()
    {
        using var fixture = new Fixture();
        fixture.Minimize();
        fixture.Preferences.RefreshIntervalSeconds = 300;
        await fixture.Workspace.InitializeAsync();
        fixture.Runtime.VerifyNoOtherCalls();
        var refreshed = new TaskCompletionSource();
        fixture.Workspace.Refreshed += (_, _) => refreshed.TrySetResult();
        fixture.Workspace.SetWindowMinimized(false);
        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(5, fixture.Runtime.Invocations.Count);
    }

    [Fact]
    public async Task Scheduler_CoalescesRestoreRequestsAndWaitsForBusyTaskWithoutIntervalDelay()
    {
        using var lifetime = new CancellationTokenSource();
        var busy = true;
        var calls = 0;
        var refreshed = new TaskCompletionSource();
        var service = new AutoRefreshService(() => { calls++; refreshed.TrySetResult(); return Task.CompletedTask; }, () => !busy, () => 300);
        for (var index = 0; index < 20; index++) service.RequestRefresh();
        await service.TickAsync(lifetime.Token);
        Assert.Equal(0, calls);
        var loop = service.RunAsync(lifetime.Token);
        busy = false;
        service.NotifyEligibilityChanged();
        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1, calls);
        lifetime.Cancel();
        await loop;
        await service.TickAsync(lifetime.Token);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Scheduler_DoesNotReenterAndRecoversAfterFailedRefresh()
    {
        var gate = new TaskCompletionSource();
        var calls = 0;
        var service = new AutoRefreshService(() => { calls++; return calls == 1 ? gate.Task : Task.CompletedTask; }, () => true, () => 5);
        var first = service.TickAsync(TestContext.Current.CancellationToken);
        await service.TickAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, calls);
        gate.SetException(new IOException("test failure"));
        await Assert.ThrowsAsync<IOException>(() => first);
        await service.TickAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task SavedPreferenceChange_AppliesImmediatelyAndLegacySettingsKeepDefault()
    {
        Assert.False(JsonSerializer.Deserialize<AppSettings>("{\"RefreshIntervalSeconds\":10}")!.PauseAutoRefreshWhenMinimized);
        using var fixture = new Fixture();
        fixture.Preferences.PauseAutoRefreshWhenMinimized = true;
        var settings = new SettingsViewModel(fixture.Workspace);
        Assert.True(settings.PauseAutoRefreshWhenMinimized);
        fixture.Preferences.PauseAutoRefreshWhenMinimized = false;
        fixture.Workspace.SetWindowMinimized(true);
        Assert.False(fixture.Workspace.IsAutoRefreshPaused);
        fixture.Preferences.PauseAutoRefreshWhenMinimized = true;
        fixture.Workspace.ApplyRefreshPreferences();
        Assert.True(fixture.Preferences.PauseAutoRefreshWhenMinimized);
        Assert.True(fixture.Workspace.IsAutoRefreshPaused);
        fixture.Preferences.PauseAutoRefreshWhenMinimized = false;
        fixture.Workspace.ApplyRefreshPreferences();
        Assert.False(fixture.Workspace.IsAutoRefreshPaused);
        await fixture.Workspace.AutoRefreshTickAsync();
        Assert.Equal(5, fixture.Runtime.Invocations.Count);
    }

    [Fact]
    public void WindowSubscription_UsesMinimizationOnlyAndIsReleasedOnClose()
    {
        var source = File.ReadAllText(Path.Combine(TestPaths.SourceDirectory, "MainWindow.xaml.cs"));
        Assert.Contains("StateChanged += OnStateChanged", source);
        Assert.Contains("StateChanged -= OnStateChanged", source);
        Assert.Contains("WindowState == WindowState.Minimized", source);
        Assert.DoesNotContain("Deactivated +=", source);
        Assert.DoesNotContain("IsActive", source);
        var xaml = File.ReadAllText(Path.Combine(TestPaths.SourceDirectory, "Views", "Pages", "SettingsPage.xaml"));
        Assert.Contains("IsChecked=\"{Binding PauseAutoRefreshWhenMinimized}\"", xaml);
        foreach (var language in new[] { "en-US", "zh-CN" })
        {
            var resources = XDocument.Load(Path.Combine(TestPaths.SourceDirectory, "Resources", $"Strings.{language}.xaml"));
            var keys = resources.Root!.Elements().ToDictionary(element => (string)element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))!, element => element.Value);
            foreach (var key in new[] { "PauseAutoRefreshWhenMinimized", "IdleRefreshExplanation", "AutoRefreshPaused", "AutoRefreshScheduled" }) Assert.False(string.IsNullOrWhiteSpace(keys[key]));
        }
    }

    private static OperationResult Success => new(true, 0, "", "", "test");

    private sealed class Fixture : IDisposable
    {
        public Mock<IContainerRuntime> Runtime { get; } = new(MockBehavior.Strict);
        public AppSettings Preferences { get; } = new();
        public RuntimeWorkspace Workspace { get; }
        public Fixture()
        {
            var settings = new Mock<ISettingsService>();
            settings.SetupGet(value => value.Current).Returns(Preferences);
            settings.Setup(value => value.SaveAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            var tasks = new Mock<ITaskService>();
            tasks.Setup(value => value.RunAsync(It.IsAny<string>(), It.IsAny<Func<IProgress<string>, CancellationToken, Task<OperationResult>>>(), It.IsAny<CancellationToken>()))
                .Returns((string _, Func<IProgress<string>, CancellationToken, Task<OperationResult>> action, CancellationToken token) => action(new Progress<string>(), token));
            Runtime.Setup(value => value.GetContainersAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Runtime.Setup(value => value.GetImagesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Runtime.Setup(value => value.GetNetworksAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Runtime.Setup(value => value.GetVolumesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Runtime.Setup(value => value.GetStatsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
            var capabilities = new Mock<IRuntimeCapabilityService>();
            capabilities.Setup(value => value.DetectAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new RuntimeCapabilities { CliAvailability = CapabilitySupport.Supported });
            Workspace = new(Runtime.Object, capabilities.Object, settings.Object, tasks.Object, Mock.Of<IUserInteractionService>())
            { Capabilities = new() { CliAvailability = CapabilitySupport.Supported } };
        }
        public void Minimize()
        {
            Preferences.PauseAutoRefreshWhenMinimized = true;
            Workspace.SetWindowMinimized(true);
        }
        public void Dispose() => Workspace.Dispose();
    }
}
