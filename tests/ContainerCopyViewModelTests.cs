using System.IO;
using System.Xml.Linq;
using ExWSLC.Models;
using ExWSLC.Services;
using ExWSLC.ViewModels;
using Moq;

namespace ExWSLC.Tests;

public class ContainerCopyViewModelTests
{
    [Theory]
    [InlineData(ContainerCopyDirection.Upload)] [InlineData(ContainerCopyDirection.Download)]
    public async Task Confirmation_ShowsCapturedEndpointsAndOverwriteSemantics(ContainerCopyDirection direction)
    {
        using var fixture = new Fixture(direction);
        var confirmation = new TaskCompletionSource<bool>();
        fixture.Interaction.Setup(x => x.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(confirmation.Task);
        var original = fixture.ViewModel.CopyLocalPath;
        var operation = fixture.ViewModel.StartCopyCommand.ExecuteAsync(null);
        Assert.True(fixture.ViewModel.IsCopyPending);
        Assert.False(fixture.ViewModel.StartCopyCommand.CanExecute(null));
        fixture.ViewModel.Select("container-two");
        fixture.ViewModel.CopyLocalPath = Path.Combine(fixture.Root, "changed");
        fixture.ViewModel.CopyContainerPath = "/changed";
        confirmation.SetResult(true);
        await operation;
        fixture.Runtime.Verify(x => x.CopyContainerPathAsync(new(direction, "container-one", original, "/data"), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        fixture.Interaction.Verify(x => x.ConfirmAsync(It.IsAny<string>(), It.Is<string>(text =>
            text.Contains(original) && text.Contains("container-one") && text.Contains("/data") && ContainsMessage(text, "CopySemantics"))), Times.Once);
        Assert.Contains("container-one", fixture.ViewModel.CopyMessage);
        Assert.DoesNotContain("container-two", fixture.ViewModel.CopyMessage);
    }

    [Fact]
    public async Task DeclinedOverwrite_LeavesExistingDestinationAndDoesNotExecute()
    {
        using var fixture = new Fixture();
        fixture.Interaction.Setup(x => x.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);
        await fixture.ViewModel.StartCopyCommand.ExecuteAsync(null);
        fixture.Runtime.Verify(x => x.CopyContainerPathAsync(It.IsAny<ContainerCopyRequest>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal("keep", File.ReadAllText(fixture.Sentinel));
        Assert.False(fixture.ViewModel.IsCopyPending);
    }

    [Fact]
    public async Task Cancellation_AfterSelectionChangesStopsOriginalTransferAndKeepsDestination()
    {
        using var fixture = new Fixture();
        fixture.Runtime.Setup(x => x.CopyContainerPathAsync(It.IsAny<ContainerCopyRequest>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .Returns<ContainerCopyRequest, IProgress<string>, CancellationToken>(async (_, _, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Success();
            });
        var operation = fixture.ViewModel.StartCopyCommand.ExecuteAsync(null);
        Assert.True(fixture.ViewModel.IsCopyRunning);
        fixture.ViewModel.Select("container-two");
        fixture.Workspace.CancelCurrentOperationCommand.Execute(null);
        await operation;
        Assert.Contains("container-one", fixture.ViewModel.CopyMessage);
        Assert.True(ContainsMessage(fixture.ViewModel.CopyMessage, "CopyCancelled"));
        Assert.Equal("keep", File.ReadAllText(fixture.Sentinel));
        Assert.False(fixture.Workspace.IsBusy);
        Assert.False(fixture.ViewModel.IsCopyRunning);
        Assert.False(fixture.ViewModel.IsCopyPending);
    }

    [Fact]
    public async Task PermissionFailure_ReportsActualOutputAndPreservesExistingFiles()
    {
        using var fixture = new Fixture();
        fixture.Runtime.Setup(x => x.CopyContainerPathAsync(It.IsAny<ContainerCopyRequest>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult(false, 1, "partial", "permission denied", "wslc container cp"));
        await fixture.ViewModel.StartCopyCommand.ExecuteAsync(null);
        Assert.Contains("permission denied", fixture.ViewModel.CopyOutput);
        Assert.True(ContainsMessage(fixture.ViewModel.CopyMessage, "CopyFailed"));
        Assert.Equal("keep", File.ReadAllText(fixture.Sentinel));
    }

    [Fact]
    public async Task MissingSource_FailsBeforeConfirmation()
    {
        using var fixture = new Fixture(ContainerCopyDirection.Upload);
        fixture.ViewModel.CopyLocalPath = Path.Combine(fixture.Root, "missing");
        await fixture.ViewModel.StartCopyCommand.ExecuteAsync(null);
        fixture.Interaction.Verify(x => x.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.True(ContainsMessage(fixture.ViewModel.CopyMessage, "CopySourceMissing"));
    }

    [Fact]
    public async Task CapabilityChangeDuringConfirmation_PreventsExecution()
    {
        using var fixture = new Fixture();
        fixture.Interaction.Setup(x => x.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(() =>
        {
            fixture.Workspace.Capabilities = new();
            return Task.FromResult(true);
        });
        await fixture.ViewModel.StartCopyCommand.ExecuteAsync(null);
        fixture.Runtime.Verify(x => x.CopyContainerPathAsync(It.IsAny<ContainerCopyRequest>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.True(fixture.ViewModel.IsCopyUnavailable);
    }

    [Fact]
    public void InventoryRefresh_KeepsFormEditableButPreventsStartingAnotherTask()
    {
        using var fixture = new Fixture();
        fixture.Workspace.IsBusy = true;
        Assert.True(fixture.ViewModel.CanEditCopy);
        Assert.False(fixture.ViewModel.StartCopyCommand.CanExecute(null));
        fixture.Workspace.IsBusy = false;
        Assert.True(fixture.ViewModel.StartCopyCommand.CanExecute(null));
    }

    [Fact]
    public void Pickers_ReuseInteractionServiceAndCancellationKeepsCurrentPath()
    {
        using var fixture = new Fixture(ContainerCopyDirection.Upload);
        fixture.Interaction.Setup(x => x.PickOpenFile(It.IsAny<string>(), It.IsAny<string>())).Returns(fixture.Sentinel);
        fixture.ViewModel.PickCopyFileCommand.Execute(null);
        Assert.Equal(fixture.Sentinel, fixture.ViewModel.CopyLocalPath);
        fixture.Interaction.Setup(x => x.PickFolder(It.IsAny<string>())).Returns((string?)null);
        fixture.ViewModel.PickCopyFolderCommand.Execute(null);
        Assert.Equal(fixture.Sentinel, fixture.ViewModel.CopyLocalPath);
        fixture.ViewModel.CopyDirectionIndex = 1;
        Assert.Empty(fixture.ViewModel.CopyLocalPath);
        fixture.Interaction.Setup(x => x.PickFolder(It.IsAny<string>())).Returns(fixture.Root);
        fixture.ViewModel.PickCopyFolderCommand.Execute(null);
        Assert.Equal(fixture.Root, fixture.ViewModel.CopyLocalPath);
    }

    private static OperationResult Success() => new(true, 0, "", "", "");

    // WPF template tests can load either language into Application.Current while
    // these tests run. Assert the complete resource text without changing global UI state.
    private static bool ContainsMessage(string actual, string key) => new[] { "en-US", "zh-CN" }.Any(language =>
        actual.Contains(XDocument.Load(Path.Combine(TestPaths.SourceDirectory, "Resources", $"Strings.{language}.xaml"))
            .Root!.Elements().Single(element => (string?)element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == key).Value,
            StringComparison.Ordinal));

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "exwslc-05-vm-" + Guid.NewGuid().ToString("N"))).FullName;
        public string Sentinel => Path.Combine(Root, "existing.txt");
        public Mock<IContainerRuntime> Runtime { get; } = new();
        public Mock<IUserInteractionService> Interaction { get; } = new();
        public RuntimeWorkspace Workspace { get; }
        public TestViewModel ViewModel { get; }
        public Fixture(ContainerCopyDirection direction = ContainerCopyDirection.Download)
        {
            File.WriteAllText(Sentinel, "keep");
            Interaction.Setup(x => x.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            Runtime.Setup(x => x.CopyContainerPathAsync(It.IsAny<ContainerCopyRequest>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(Success());
            var settings = new Mock<ISettingsService>();
            settings.SetupGet(x => x.Current).Returns(new AppSettings());
            var tasks = new Mock<ITaskService>();
            tasks.Setup(x => x.RunAsync(It.IsAny<string>(), It.IsAny<Func<IProgress<string>, CancellationToken, Task<OperationResult>>>(), It.IsAny<CancellationToken>()))
                .Returns<string, Func<IProgress<string>, CancellationToken, Task<OperationResult>>, CancellationToken>((_, operation, token) => operation(new Progress<string>(), token));
            Workspace = new(Runtime.Object, ContainerCopyTests.Capability(CapabilitySupport.Supported), settings.Object, tasks.Object, Interaction.Object)
            { Capabilities = NetworkOperationTests.AllCapabilities(CapabilitySupport.Supported) };
            ViewModel = new(Workspace);
            ViewModel.Select("container-one");
            ViewModel.CopyDirectionIndex = (int)direction;
            ViewModel.CopyLocalPath = direction == ContainerCopyDirection.Upload ? Sentinel : Root;
            ViewModel.CopyContainerPath = "/data";
        }
        public void Dispose()
        {
            Workspace.Dispose();
            Directory.Delete(Root, true);
        }
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
