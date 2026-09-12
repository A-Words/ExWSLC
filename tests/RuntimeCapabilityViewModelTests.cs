using CommunityToolkit.Mvvm.Messaging;
using ExWSLC.Models;
using ExWSLC.Services;
using ExWSLC.ViewModels;
using ExWSLC.ViewModels.Messages;
using Moq;

namespace ExWSLC.Tests;

public class RuntimeCapabilityViewModelTests
{
    [Fact]
    public void UpdatingCapabilitiesNotifiesAllVersionAndAvailabilityBindings()
    {
        using var fixture = new Fixture();
        var changes = new List<string?>();
        var workspaceChanges = new List<string?>();
        fixture.Settings.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        fixture.Workspace.PropertyChanged += (_, args) => workspaceChanges.Add(args.PropertyName);

        fixture.Workspace.Capabilities = Ready with
        {
            CliVersion = "2.9.10.0",
            ServiceVersion = "2.9.10",
            SdkPackageVersion = "2.9.9"
        };

        Assert.Equal("2.9.10.0", fixture.Settings.CliVersionText);
        Assert.Equal("2.9.10", fixture.Settings.ServiceVersionText);
        Assert.True(fixture.Settings.IsSdkAvailable);
        Assert.Contains("CLI: 2.9.10.0", fixture.Workspace.VersionSummary);
        Assert.Contains("Service: 2.9.10", fixture.Workspace.VersionSummary);
        Assert.Contains("SDK: 2.9.9", fixture.Workspace.VersionSummary);
        Assert.Contains(nameof(RuntimeWorkspace.VersionSummary), workspaceChanges);
        Assert.Contains(nameof(SettingsViewModel.CliVersionText), changes);
        Assert.Contains(nameof(SettingsViewModel.ServiceVersionText), changes);
        Assert.Contains(nameof(SettingsViewModel.EnvironmentMessage), changes);
        Assert.Contains(nameof(SettingsViewModel.IsSdkAvailable), changes);
    }

    [Fact]
    public void LanguageChangesRefreshLocalizedEnvironmentTextWithoutProbingAgain()
    {
        using var fixture = new Fixture();
        fixture.Workspace.Capabilities = new RuntimeCapabilities { MessageKey = "RuntimeDetectionIncomplete" };
        var snapshot = fixture.Workspace.Capabilities;
        var changes = new List<string?>();
        fixture.Settings.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        WeakReferenceMessenger.Default.Send(new LanguageChangedMessage("en-US"));

        Assert.Contains(nameof(SettingsViewModel.CliVersionText), changes);
        Assert.Contains(nameof(SettingsViewModel.ServiceVersionText), changes);
        Assert.Contains(nameof(SettingsViewModel.EnvironmentMessage), changes);
        Assert.Same(snapshot, fixture.Workspace.Capabilities);
        fixture.Capabilities.VerifyNoOtherCalls();
    }

    [Fact]
    public void MissingSdkUpdateDoesNotEnableTheWindowsComponentInstaller()
    {
        using var fixture = new Fixture();
        fixture.Workspace.Capabilities = Ready with
        {
            SdkAvailability = CapabilitySupport.Unsupported,
            MissingComponents = ["SdkNeedsUpdate"]
        };

        Assert.False(fixture.Settings.IsSdkAvailable);
        Assert.False(fixture.Settings.InstallComponentsCommand.CanExecute(null));
        Assert.True(fixture.Workspace.Capabilities.IsAvailable);
    }

    [Fact]
    public void BusyWorkspaceDisablesEnvironmentDetectionAndInstallation()
    {
        using var fixture = new Fixture();
        fixture.Workspace.Capabilities = Missing;
        Assert.True(fixture.Settings.InstallComponentsCommand.CanExecute(null));

        fixture.Workspace.IsBusy = true;

        Assert.False(fixture.Settings.InstallComponentsCommand.CanExecute(null));
        Assert.False(fixture.Settings.RefreshCapabilitiesCommand.CanExecute(null));
        fixture.Workspace.IsBusy = false;
        Assert.True(fixture.Settings.InstallComponentsCommand.CanExecute(null));
        Assert.True(fixture.Settings.RefreshCapabilitiesCommand.CanExecute(null));
    }

    [Fact]
    public async Task InstallationAfterUnavailableStartupRefreshesInventoryAndResumesAutomaticRefresh()
    {
        using var fixture = new Fixture();
        fixture.Capabilities.SetupSequence(value => value.DetectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Missing).ReturnsAsync(Ready);
        fixture.Capabilities.Setup(value => value.InstallMissingComponentsAsync(
                It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var automaticallyRefreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inventoryCalls = 0;
        fixture.Runtime.Setup(value => value.GetContainersAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (Interlocked.Increment(ref inventoryCalls) == 2) automaticallyRefreshed.TrySetResult();
                return Task.FromResult<IReadOnlyList<ContainerSummary>>([new("id", "web", "alpine", "running", "Up", "", "")]);
            });
        await fixture.Workspace.InitializeAsync();
        Assert.False(fixture.Workspace.Capabilities.IsAvailable);
        Assert.Equal(0, inventoryCalls);

        await fixture.Settings.InstallComponentsCommand.ExecuteAsync(null);

        Assert.False(fixture.Workspace.IsBusy);
        Assert.True(fixture.Workspace.Capabilities.IsAvailable);
        Assert.Equal("web", Assert.Single(fixture.Workspace.Containers).Name);
        Assert.False(fixture.Settings.InstallComponentsCommand.CanExecute(null));
        await automaticallyRefreshed.Task.WaitAsync(TimeSpan.FromSeconds(6), TestContext.Current.CancellationToken);
        fixture.Capabilities.Verify(value => value.DetectAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        fixture.Capabilities.Verify(value => value.InstallMissingComponentsAsync(
            It.IsAny<IProgress<string>>(), fixture.Workspace.Lifetime.Token), Times.Once);
    }

    [Fact]
    public async Task InstallationFailureResetsBusyStateAndDoesNotPretendInventoryWasRefreshed()
    {
        using var fixture = new Fixture();
        fixture.Workspace.Capabilities = Missing;
        fixture.Capabilities.Setup(value => value.InstallMissingComponentsAsync(
                It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("installation failed"));

        await fixture.Settings.InstallComponentsCommand.ExecuteAsync(null);

        Assert.False(fixture.Workspace.IsBusy);
        fixture.Interaction.Verify(value => value.ShowErrorAsync(It.IsAny<string>(), "installation failed"), Times.Once);
        fixture.Runtime.VerifyNoOtherCalls();
        fixture.Capabilities.Verify(value => value.DetectAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InstallationCancellationDoesNotShowAnInstallationFailureDialog()
    {
        using var fixture = new Fixture();
        fixture.Workspace.Capabilities = Missing;
        fixture.Capabilities.Setup(value => value.InstallMissingComponentsAsync(
                It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await fixture.Settings.InstallComponentsCommand.ExecuteAsync(null);

        Assert.False(fixture.Workspace.IsBusy);
        Assert.Equal(LocalizationService.GetString("InstallationCancelled", "Installation cancellation requested."), fixture.Workspace.StatusMessage);
        fixture.Interaction.Verify(value => value.ShowErrorAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        fixture.Runtime.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DecliningInstallationConfirmationDoesNotCallInstaller()
    {
        using var fixture = new Fixture();
        fixture.Workspace.Capabilities = Missing;
        fixture.Interaction.Setup(value => value.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

        await fixture.Settings.InstallComponentsCommand.ExecuteAsync(null);

        fixture.Capabilities.VerifyNoOtherCalls();
        Assert.False(fixture.Workspace.IsBusy);
    }

    [Fact]
    public async Task CheckAgainExplicitlyRefreshesCapabilitiesAndThenInventory()
    {
        using var fixture = new Fixture();
        fixture.Capabilities.Setup(value => value.RefreshAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Ready);

        await fixture.Settings.RefreshCapabilitiesCommand.ExecuteAsync(null);

        fixture.Capabilities.Verify(value => value.RefreshAsync(fixture.Workspace.Lifetime.Token), Times.Once);
        fixture.Capabilities.Verify(value => value.DetectAsync(It.IsAny<CancellationToken>()), Times.Never);
        fixture.Runtime.Verify(value => value.GetContainersAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(fixture.Workspace.IsBusy);
    }

    [Fact]
    public async Task OrdinaryInventoryRefreshDoesNotRepeatCapabilityProbes()
    {
        using var fixture = new Fixture();
        fixture.Workspace.Capabilities = Ready;

        await fixture.Workspace.RefreshAllAsync();
        await fixture.Workspace.RefreshAllAsync();

        fixture.Capabilities.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InitializationCancelledByWindowDisposalDoesNotEscapeTheUiAwait()
    {
        using var fixture = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Capabilities.Setup(value => value.DetectAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken token) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Ready;
            });
        var main = new MainViewModel(fixture.Workspace);
        var initialize = main.InitializeAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        main.Dispose();
        await initialize;

        Assert.Empty(fixture.Workspace.RefreshError);
        fixture.Runtime.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InitializationFailureIsVisibleAndCanRecoverThroughCheckAgain()
    {
        using var fixture = new Fixture();
        fixture.Capabilities.Setup(value => value.DetectAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("temporary detection failure"));
        fixture.Capabilities.Setup(value => value.RefreshAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Ready);
        using var main = new MainViewModel(fixture.Workspace);

        await main.InitializeAsync();

        Assert.Equal("RuntimeDetectionFailed", fixture.Workspace.Capabilities.MessageKey);
        Assert.Contains("temporary detection failure", fixture.Workspace.RefreshError);
        Assert.True(main.SettingsPage.RefreshCapabilitiesCommand.CanExecute(null));

        await main.SettingsPage.RefreshCapabilitiesCommand.ExecuteAsync(null);

        Assert.Equal("RuntimeReady", fixture.Workspace.Capabilities.MessageKey);
        Assert.Empty(fixture.Workspace.RefreshError);
        fixture.Runtime.Verify(value => value.GetContainersAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static RuntimeCapabilities Ready => new()
    {
        CliAvailability = CapabilitySupport.Supported,
        SdkAvailability = CapabilitySupport.Supported,
        ServiceAvailability = CapabilitySupport.Supported,
        MessageKey = "RuntimeReady"
    };

    private static RuntimeCapabilities Missing => Ready with
    {
        MissingComponents = ["WslPackage"],
        MessageKey = "MissingRuntimeComponents",
        MessageArguments = ["WslPackage"]
    };

    private sealed class Fixture : IDisposable
    {
        public Mock<IContainerRuntime> Runtime { get; } = new(MockBehavior.Strict);
        public Mock<IRuntimeCapabilityService> Capabilities { get; } = new(MockBehavior.Strict);
        public Mock<IUserInteractionService> Interaction { get; } = new(MockBehavior.Strict);
        public RuntimeWorkspace Workspace { get; }
        public SettingsViewModel Settings { get; }

        public Fixture()
        {
            var settings = new Mock<ISettingsService>();
            settings.SetupGet(value => value.Current).Returns(new AppSettings { RefreshIntervalSeconds = 2 });
            Interaction.Setup(value => value.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            Interaction.Setup(value => value.ShowErrorAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
            Runtime.Setup(value => value.GetContainersAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Runtime.Setup(value => value.GetImagesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Runtime.Setup(value => value.GetNetworksAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Runtime.Setup(value => value.GetVolumesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Runtime.Setup(value => value.GetStatsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Workspace = new RuntimeWorkspace(Runtime.Object, Capabilities.Object, settings.Object, new TaskService(), Interaction.Object);
            Settings = new SettingsViewModel(Workspace);
        }

        public void Dispose()
        {
            WeakReferenceMessenger.Default.UnregisterAll(Settings);
            Workspace.Dispose();
        }
    }
}
