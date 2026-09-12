using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.Messaging;
using ExWSLC.Helpers;
using ExWSLC.Models;
using ExWSLC.Services;
using ExWSLC.ViewModels;
using ExWSLC.ViewModels.Messages;
using Moq;

namespace ExWSLC.Tests;

public class SettingsDiagnosticsViewModelTests
{
    [Fact]
    public async Task Refresh_UsesCachedDetectionAndReplacesSnapshotsWithoutInventory()
    {
        using var fixture = new Fixture();
        await fixture.Settings.RefreshDiagnosticsCommand.ExecuteAsync(null);
        Assert.Same(fixture.Snapshot, fixture.Settings.Diagnostics);
        Assert.False(fixture.Workspace.IsBusy);
        Assert.True(fixture.Settings.CopyDiagnosticsCommand.CanExecute(null));
        Assert.True(fixture.Settings.ExportDiagnosticsCommand.CanExecute(null));
        fixture.Snapshot = fixture.Snapshot with { SystemInfo = new RuntimeSystemInfo { Sessions = [] } };
        await fixture.Settings.RefreshDiagnosticsCommand.ExecuteAsync(null);
        Assert.Empty(fixture.Settings.DiagnosticsSessions);
        Assert.Equal("DiagnosticsNoSessions", fixture.Settings.DiagnosticsSessionsStatus);
        fixture.Capabilities.Verify(value => value.DetectAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        fixture.Capabilities.VerifyNoOtherCalls();
        fixture.Runtime.Verify(value => value.GetSystemInfoAsync(It.IsAny<RuntimeCapabilities>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        fixture.Runtime.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Refresh_CancelsThroughWorkspaceAndPreservesPreviousSnapshot()
    {
        using var fixture = new Fixture();
        await fixture.Settings.RefreshDiagnosticsCommand.ExecuteAsync(null);
        var previous = fixture.Settings.Diagnostics;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Runtime.Setup(value => value.GetSystemInfoAsync(It.IsAny<RuntimeCapabilities>(), It.IsAny<CancellationToken>()))
            .Returns(async (RuntimeCapabilities _, CancellationToken token) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, token);
                return fixture.Snapshot;
            });
        var refresh = fixture.Settings.RefreshDiagnosticsCommand.ExecuteAsync(null);
        await started.Task;
        Assert.True(fixture.Workspace.IsBusy);
        Assert.False(fixture.Settings.RefreshDiagnosticsCommand.CanExecute(null));
        Assert.False(fixture.Settings.CopyDiagnosticsCommand.CanExecute(null));
        fixture.Workspace.CancelCurrentOperationCommand.Execute(null);
        await refresh;
        Assert.Same(previous, fixture.Settings.Diagnostics);
        Assert.Equal("DiagnosticsCancelled", fixture.Settings.DiagnosticsActionStatus);
        Assert.False(fixture.Workspace.IsBusy);
        Assert.True(fixture.Settings.RefreshDiagnosticsCommand.CanExecute(null));
        Assert.True(fixture.Settings.CopyDiagnosticsCommand.CanExecute(null));
    }

    [Fact]
    public async Task Refresh_RemainsUsableAfterDetectionFailure()
    {
        using var fixture = new Fixture();
        fixture.Capabilities.Setup(value => value.DetectAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("private error"));
        await fixture.Settings.RefreshDiagnosticsCommand.ExecuteAsync(null);
        Assert.Same(fixture.Snapshot, fixture.Settings.Diagnostics);
        Assert.DoesNotContain("private", fixture.TaskResult!.CombinedOutput);
    }

    [Fact]
    public async Task Refresh_ReplacesStaleInfoWithSafeFailureAndBasicVersions()
    {
        using var fixture = new Fixture();
        await fixture.Settings.RefreshDiagnosticsCommand.ExecuteAsync(null);
        fixture.Runtime.Setup(value => value.GetSystemInfoAsync(It.IsAny<RuntimeCapabilities>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("C:\\Users\\private password=secret"));
        await fixture.Settings.RefreshDiagnosticsCommand.ExecuteAsync(null);
        Assert.Null(fixture.Settings.Diagnostics!.SystemInfo);
        Assert.Equal("2.9.10.0", fixture.Settings.Diagnostics.BasicCliVersion);
        Assert.Equal("DiagnosticsSessionsUnavailable", fixture.Settings.DiagnosticsSessionsStatus);
        Assert.False(fixture.TaskResult!.Success);
        Assert.DoesNotContain("secret", fixture.TaskResult.CombinedOutput);
    }

    [Fact]
    public async Task CopyAndExport_UseTheSameRedactedSnapshotAndHandleIoFailure()
    {
        using var fixture = new Fixture();
        await fixture.Settings.RefreshDiagnosticsCommand.ExecuteAsync(null);
        string? copied = null;
        fixture.Interaction.Setup(value => value.SetClipboardText(It.IsAny<string>())).Callback<string>(value => copied = value);
        fixture.Settings.CopyDiagnosticsCommand.Execute(null);
        Assert.Equal(RuntimeDiagnosticsFormatter.Summary(fixture.Snapshot), copied);
        Assert.DoesNotContain("private-user", copied!);
        Assert.DoesNotContain("secret", copied!);
        var path = Path.GetTempFileName();
        try
        {
            fixture.Interaction.Setup(value => value.PickSaveFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(path);
            await fixture.Settings.ExportDiagnosticsCommand.ExecuteAsync(null);
            Assert.Equal(copied, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            fixture.Interaction.Setup(value => value.PickSaveFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(Path.GetTempPath());
            await fixture.Settings.ExportDiagnosticsCommand.ExecuteAsync(null);
            Assert.Equal("DiagnosticsExportFailed", fixture.Settings.DiagnosticsActionStatus);
            fixture.Interaction.Setup(value => value.SetClipboardText(It.IsAny<string>())).Throws(new IOException("secret"));
            fixture.Settings.CopyDiagnosticsCommand.Execute(null);
            Assert.Equal("DiagnosticsCopyFailed", fixture.Settings.DiagnosticsActionStatus);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task LanguageChange_RaisesEveryDiagnosticBindingWithoutRunningQueries()
    {
        using var fixture = new Fixture();
        await fixture.Settings.RefreshDiagnosticsCommand.ExecuteAsync(null);
        var changes = new List<string?>();
        fixture.Settings.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        WeakReferenceMessenger.Default.Send(new LanguageChangedMessage("zh-CN"));
        foreach (var name in new[] { "DiagnosticsFields", "DiagnosticsSessions", "DiagnosticsStatus", "DiagnosticsSessionsStatus" })
            Assert.Contains(name, changes);
        fixture.Runtime.Verify(value => value.GetSystemInfoAsync(It.IsAny<RuntimeCapabilities>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void DiagnosticStrings_ArePresentInBothLanguagesIncludingCodeOnlyMessages()
    {
        var files = new[] { "Helpers/RuntimeDiagnosticsFormatter.cs", "ViewModels/SettingsViewModel.Diagnostics.cs", "Services/WslcContainerRuntime.cs", "Models/RuntimeDiagnostics.cs" };
        var keys = files.SelectMany(file => Regex.Matches(File.ReadAllText(Path.Combine(TestPaths.SourceDirectory, file)), "\"(Diagnostics[A-Za-z]+)\"")
            .Select(match => match.Groups[1].Value)).Distinct().ToArray();
        foreach (var language in new[] { "en-US", "zh-CN" })
        {
            var doc = XDocument.Load(Path.Combine(TestPaths.SourceDirectory, $"Resources/Strings.{language}.xaml"));
            var entries = doc.Root!.Elements().ToDictionary(element => (string)element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))!, element => element.Value);
            foreach (var key in keys) Assert.True(entries.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text), $"Missing {language}: {key}");
        }
    }

    private sealed class Fixture : IDisposable
    {
        public Mock<IContainerRuntime> Runtime { get; } = new(MockBehavior.Strict);
        public Mock<IRuntimeCapabilityService> Capabilities { get; } = new(MockBehavior.Strict);
        public Mock<IUserInteractionService> Interaction { get; } = new(MockBehavior.Strict);
        public RuntimeWorkspace Workspace { get; }
        public SettingsViewModel Settings { get; }
        public OperationResult? TaskResult { get; private set; }
        public RuntimeDiagnostics Snapshot { get; set; } = new()
        {
            SystemInfo = RuntimeSystemInfoParser.Parse(RuntimeDiagnosticsTests.CompleteJson),
            StatusKey = "DiagnosticsCollected", SdkPackageVersion = "2.9.9"
        };

        public Fixture()
        {
            var settings = new Mock<ISettingsService>();
            settings.SetupGet(value => value.Current).Returns(new AppSettings());
            var tasks = new Mock<ITaskService>();
            tasks.Setup(value => value.RunAsync(It.IsAny<string>(), It.IsAny<Func<IProgress<string>, CancellationToken, Task<OperationResult>>>(), It.IsAny<CancellationToken>()))
                .Returns(async (string _, Func<IProgress<string>, CancellationToken, Task<OperationResult>> operation, CancellationToken token) =>
                    TaskResult = await operation(new Progress<string>(), token));
            Capabilities.Setup(value => value.DetectAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new RuntimeCapabilities { CliVersion = "2.9.10.0" });
            Runtime.Setup(value => value.GetSystemInfoAsync(It.IsAny<RuntimeCapabilities>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => Snapshot);
            Workspace = new(Runtime.Object, Capabilities.Object, settings.Object, tasks.Object, Interaction.Object);
            Settings = new(Workspace);
        }

        public void Dispose()
        {
            WeakReferenceMessenger.Default.UnregisterAll(Settings);
            Workspace.Dispose();
        }
    }
}
