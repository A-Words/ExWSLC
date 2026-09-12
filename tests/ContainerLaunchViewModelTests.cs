using ExWSLC.Models;
using ExWSLC.Services;
using ExWSLC.ViewModels;
using Moq;

namespace ExWSLC.Tests;

public class ContainerLaunchViewModelTests
{
    [Fact]
    public void CreateForm_PreservesSpecialPathsAndHealth_RejectsConflicts()
    {
        using var fixture = new Fixture();
        var vm = fixture.ViewModel;
        vm.NewImage = "image";
        vm.NewStopTimeout = "-1";
        vm.NewStopSignal = "SIGINT";
        vm.NewPullPolicyIndex = 3;
        vm.NewVolumes = "C:\\a,b:/old:ro\r\ncache:/cache";
        vm.NewHealthModeIndex = (int)HealthCheckMode.Custom;
        vm.NewHealthCommand = "true";
        vm.NewMounts.Add(new() { KindIndex = 2, Source = "ignored for tmpfs", Target = "/memory", ReadOnly = true });
        var spec = vm.BuildCreateSpec();
        Assert.Equal(-1, spec.StopTimeoutSeconds);
        Assert.Equal(ContainerPullPolicy.Never, spec.PullPolicy);
        Assert.Equal(HealthCheckMode.Custom, spec.HealthMode);
        Assert.Equal([@"C:\a,b:/old:ro", "cache:/cache"], spec.Volumes);
        Assert.Equal(new(ContainerMountKind.Tmpfs, "", "/memory", true), Assert.Single(spec.Mounts));
        vm.NewMounts[0].Target = "/old/";
        Assert.Throws<ArgumentException>(() => vm.BuildCreateSpec());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManualStop_CapturesTargetAndOptionsBeforeConfirmation(bool fromList)
    {
        using var fixture = new Fixture();
        fixture.ViewModel.Select("first");
        var target = fixture.ViewModel.SelectedContainer;
        fixture.Interaction.Setup(x => x.PickContainerStopOptionsAsync("first", It.IsAny<RuntimeCapabilities>())).Returns(() =>
        {
            fixture.ViewModel.Select("second");
            return Task.FromResult<ContainerStopOptions?>(new(-1, "SIGINT"));
        });
        if (fromList) await fixture.ViewModel.StopContainerFromListCommand.ExecuteAsync(target);
        else await fixture.ViewModel.StopContainerCommand.ExecuteAsync(null);
        fixture.Runtime.Verify(x => x.StopContainerAsync("first", new ContainerStopOptions(-1, "SIGINT"), It.IsAny<CancellationToken>()), Times.Once);
        fixture.Runtime.Verify(x => x.StopContainerAsync("second", It.IsAny<ContainerStopOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Tasks.Verify(x => x.RunAsync("Stop container", It.IsAny<Func<IProgress<string>, CancellationToken, Task<OperationResult>>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DismissedStopDialog_DoesNotStop()
    {
        using var fixture = new Fixture();
        fixture.ViewModel.Select("first");
        await fixture.ViewModel.StopContainerCommand.ExecuteAsync(null);
        fixture.Runtime.Verify(x => x.StopContainerAsync(It.IsAny<string>(), It.IsAny<ContainerStopOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void StopDialog_ValidatesWithoutLosingInput_AndDefaultsToInheritance()
    {
        var vm = new ContainerStopOptionsViewModel("test", NetworkOperationTests.AllCapabilities(CapabilitySupport.Supported));
        Assert.Equal(new ContainerStopOptions(), vm.Build());
        vm.Timeout = "-2";
        Assert.False(vm.IsValid);
        Assert.Equal("-2", vm.Timeout);
        vm.Timeout = "-1";
        vm.Signal = "bad";
        Assert.False(vm.IsValid);
        vm.Signal = "TERM";
        Assert.True(vm.IsValid);
        var unknown = new ContainerStopOptionsViewModel("test", new());
        Assert.False(unknown.CanSetTimeout);
        Assert.False(unknown.CanSetSignal);
        Assert.Equal(new ContainerStopOptions(), unknown.Build());
    }

    [Fact]
    public async Task Export_CapturesRunningContainerBeforePickerChangesSelection()
    {
        using var fixture = new Fixture();
        fixture.ViewModel.Select("first");
        fixture.Interaction.Setup(x => x.PickSaveFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(() =>
        {
            fixture.ViewModel.Select("second");
            return "out.tar";
        });
        fixture.Interaction.Setup(x => x.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        var ok = new OperationResult(true, 0, "", "", "");
        fixture.Runtime.Setup(x => x.StopContainerAsync("first", It.IsAny<CancellationToken>())).ReturnsAsync(ok);
        fixture.Runtime.Setup(x => x.ExportContainerAsync("first", "out.tar", It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(ok);
        fixture.Runtime.Setup(x => x.StartContainerAsync("first", It.IsAny<CancellationToken>())).ReturnsAsync(ok);
        await fixture.ViewModel.ExportContainerCommand.ExecuteAsync(null);
        fixture.Runtime.Verify(x => x.StopContainerAsync("first", It.IsAny<CancellationToken>()), Times.Once);
        fixture.Runtime.Verify(x => x.StartContainerAsync("first", It.IsAny<CancellationToken>()), Times.Once);
        fixture.Runtime.Verify(x => x.ExportContainerAsync("second", It.IsAny<string>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Stop_UsesWorkspaceCancellationAndReturnsWithoutUnhandledException()
    {
        using var fixture = new Fixture();
        fixture.ViewModel.Select("first");
        fixture.Interaction.Setup(x => x.PickContainerStopOptionsAsync(It.IsAny<string>(), It.IsAny<RuntimeCapabilities>())).ReturnsAsync(new ContainerStopOptions(-1));
        fixture.Runtime.Setup(x => x.StopContainerAsync("first", It.IsAny<ContainerStopOptions>(), It.IsAny<CancellationToken>()))
            .Returns<string, ContainerStopOptions, CancellationToken>(async (_, _, token) =>
            {
                fixture.Workspace.CancelCurrentOperation();
                Assert.True(fixture.ViewModel.IsLifecycleOperationInProgress);
                await Task.Delay(Timeout.Infinite, token);
                throw new InvalidOperationException("Cancellation was ignored.");
            });
        await fixture.ViewModel.StopContainerCommand.ExecuteAsync(null);
        Assert.False(fixture.Workspace.IsBusy);
        Assert.False(fixture.ViewModel.IsLifecycleOperationInProgress);
        Assert.True(fixture.ViewModel.HasLifecycleStatus);
    }

    private sealed class Fixture : IDisposable
    {
        public Mock<IContainerRuntime> Runtime { get; } = new();
        public Mock<IUserInteractionService> Interaction { get; } = new();
        public Mock<ITaskService> Tasks { get; } = new();
        public RuntimeWorkspace Workspace { get; }
        public TestViewModel ViewModel { get; }
        public Fixture()
        {
            Runtime.Setup(x => x.StopContainerAsync(It.IsAny<string>(), It.IsAny<ContainerStopOptions>(), It.IsAny<CancellationToken>())).ReturnsAsync(new OperationResult(true, 0, "", "", ""));
            var settings = new Mock<ISettingsService>();
            settings.SetupGet(x => x.Current).Returns(new AppSettings());
            Tasks.Setup(x => x.RunAsync(It.IsAny<string>(), It.IsAny<Func<IProgress<string>, CancellationToken, Task<OperationResult>>>(), It.IsAny<CancellationToken>()))
                .Returns<string, Func<IProgress<string>, CancellationToken, Task<OperationResult>>, CancellationToken>((_, operation, token) => operation(new Progress<string>(), token));
            Workspace = new(Runtime.Object, ContainerCopyTests.Capability(CapabilitySupport.Supported), settings.Object, Tasks.Object, Interaction.Object)
            { Capabilities = NetworkOperationTests.AllCapabilities(CapabilitySupport.Supported) };
            ViewModel = new(Workspace);
        }
        public void Dispose() => Workspace.Dispose();
    }

    private sealed class TestViewModel(RuntimeWorkspace workspace) : ContainersViewModel(workspace)
    {
        public void Select(string id)
        {
            IsDesignMode = true;
            SelectedContainer = new(id, id, "image", "running", "Up", "", "");
            SelectedDetailTabIndex = 7;
            IsDesignMode = false;
        }
    }
}
