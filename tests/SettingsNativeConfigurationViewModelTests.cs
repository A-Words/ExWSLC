using CommunityToolkit.Mvvm.Messaging;
using ExWSLC.Models;
using ExWSLC.Services;
using ExWSLC.ViewModels;
using Moq;

namespace ExWSLC.Tests;

public sealed class SettingsNativeConfigurationViewModelTests
{
    [Fact]
    public async Task LoadAndSaveUseNativeRuntimeAndTrackDirtyState()
    {
        using var fixture = new Fixture();
        Assert.False(fixture.ViewModel.CanSaveNativeSettings);
        await fixture.ViewModel.LoadNativeSettingsCommand.ExecuteAsync(null);
        Assert.True(fixture.ViewModel.CanEditNativeSettings);
        Assert.False(fixture.ViewModel.CanSaveNativeSettings);
        fixture.Cpu.UseDefault = false;
        fixture.Cpu.NumericValue = 4;
        Assert.True(fixture.ViewModel.CanSaveNativeSettings);
        await fixture.ViewModel.SaveNativeSettingsCommand.ExecuteAsync(null);
        Assert.False(fixture.ViewModel.CanSaveNativeSettings);
        fixture.Runtime.Verify(runtime => runtime.SaveNativeSettingsAsync(
            fixture.Original, It.Is<string>(text => text.Contains("cpuCount:")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("NativeConfigConflict")]
    [InlineData("NativeConfigInvalid")]
    public async Task FailedSaveRetainsEditsAndOriginalSnapshot(string status)
    {
        using var fixture = new Fixture();
        fixture.Runtime.Setup(runtime => runtime.SaveNativeSettingsAsync(It.IsAny<NativeSettingsDocument>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);
        await fixture.ViewModel.LoadNativeSettingsCommand.ExecuteAsync(null);
        fixture.Cpu.UseDefault = false;
        fixture.Cpu.NumericValue = 4;
        await fixture.ViewModel.SaveNativeSettingsCommand.ExecuteAsync(null);
        Assert.Equal(4, fixture.Cpu.NumericValue);
        Assert.True(fixture.ViewModel.CanSaveNativeSettings);
        Assert.Equal(LocalizationService.GetString(status, status), fixture.ViewModel.NativeConfigStatus);
    }

    [Fact]
    public async Task DecliningReloadPreservesUnsavedEdits()
    {
        using var fixture = new Fixture();
        await fixture.ViewModel.LoadNativeSettingsCommand.ExecuteAsync(null);
        fixture.Cpu.UseDefault = false;
        fixture.Cpu.NumericValue = 4;
        await fixture.ViewModel.LoadNativeSettingsCommand.ExecuteAsync(null);
        fixture.Runtime.Verify(runtime => runtime.ReadNativeSettingsAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(4, fixture.Cpu.NumericValue);
        Assert.False(fixture.ViewModel.IsNativeSettingsBusy);
    }

    [Fact]
    public async Task SaveDisablesConcurrentReadResetAndEditingAndRecoversAfterFailure()
    {
        using var fixture = new Fixture();
        var completion = new TaskCompletionSource<string>();
        fixture.Runtime.Setup(runtime => runtime.SaveNativeSettingsAsync(It.IsAny<NativeSettingsDocument>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(completion.Task);
        await fixture.ViewModel.LoadNativeSettingsCommand.ExecuteAsync(null);
        fixture.Cpu.UseDefault = false;
        fixture.Cpu.NumericValue = 4;
        var save = fixture.ViewModel.SaveNativeSettingsCommand.ExecuteAsync(null);
        Assert.False(fixture.ViewModel.CanEditNativeSettings);
        Assert.False(fixture.ViewModel.LoadNativeSettingsCommand.CanExecute(null));
        Assert.False(fixture.ViewModel.ResetNativeSettingsCommand.CanExecute(null));
        completion.SetException(new UnauthorizedAccessException("private details"));
        await save;
        Assert.True(fixture.ViewModel.CanEditNativeSettings);
        Assert.True(fixture.ViewModel.CanSaveNativeSettings);
        Assert.Equal(LocalizationService.GetString("NativeConfigFailed", "NativeConfigFailed"), fixture.ViewModel.NativeConfigStatus);
    }

    private sealed class Fixture : IDisposable
    {
        public NativeSettingsDocument Original { get; } = new("session: {}", true);
        public Mock<IContainerRuntime> Runtime { get; } = new(MockBehavior.Strict);
        public RuntimeWorkspace Workspace { get; }
        public SettingsViewModel ViewModel { get; }
        public NativeSettingViewModel Cpu => ViewModel.NativeSettingsFields.Single(setting => setting.Definition.Key == "Cpu");

        public Fixture()
        {
            Runtime.Setup(runtime => runtime.ReadNativeSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Original);
            Runtime.Setup(runtime => runtime.SaveNativeSettingsAsync(It.IsAny<NativeSettingsDocument>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("NativeConfigSaved");
            var settings = new Mock<ISettingsService>();
            settings.SetupGet(service => service.Current).Returns(new AppSettings());
            Workspace = new(Runtime.Object, Mock.Of<IRuntimeCapabilityService>(), settings.Object,
                Mock.Of<ITaskService>(), Mock.Of<IUserInteractionService>());
            ViewModel = new(Workspace);
        }

        public void Dispose()
        {
            WeakReferenceMessenger.Default.UnregisterAll(ViewModel);
            Workspace.Dispose();
        }
    }
}
