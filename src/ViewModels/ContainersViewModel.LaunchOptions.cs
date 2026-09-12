using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExWSLC.Models;
using ExWSLC.Services;

namespace ExWSLC.ViewModels;

public partial class ContainersViewModel
{
    [ObservableProperty] public partial int NewPullPolicyIndex { get; set; }
    [ObservableProperty] public partial string NewStopTimeout { get; set; } = string.Empty;
    [ObservableProperty] public partial string NewStopSignal { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsLifecycleOperationInProgress { get; set; }
    [ObservableProperty] public partial string LifecycleStatus { get; set; } = string.Empty;
    public bool HasLifecycleStatus => LifecycleStatus.Length > 0;
    partial void OnLifecycleStatusChanged(string value) => OnPropertyChanged(nameof(HasLifecycleStatus));
    public ObservableCollection<ContainerMountEditorViewModel> NewMounts { get; } = [];
    public bool CanSetCreatePull => SupportsCreate(RuntimeFeature.CreatePullPolicy);
    public bool CanSetCreateTimeout => SupportsCreate(RuntimeFeature.CreateStopTimeout);
    public bool CanSetCreateSignal => SupportsCreate(RuntimeFeature.CreateStopSignal);
    public bool CanAddStructuredMount => SupportsCreate(RuntimeFeature.CreateMount) || SupportsCreate(RuntimeFeature.CreateTmpfs);
    public bool CanAddBindVolume => SupportsCreate(RuntimeFeature.CreateMount);
    public bool CanAddTmpfs => SupportsCreate(RuntimeFeature.CreateTmpfs);
    private bool SupportsCreate(RuntimeFeature feature) => Workspace.Capabilities[feature].Support == CapabilitySupport.Supported;

    private void NotifyCreateOptions()
    {
        foreach (var name in new[] { nameof(CanSetCreatePull), nameof(CanSetCreateTimeout), nameof(CanSetCreateSignal), nameof(CanAddStructuredMount), nameof(CanAddBindVolume), nameof(CanAddTmpfs) })
            OnPropertyChanged(name);
    }

    [RelayCommand] private void AddMount() => NewMounts.Add(new() { KindIndex = CanAddBindVolume ? 0 : 2 });
    [RelayCommand] private void RemoveMount(ContainerMountEditorViewModel? mount) { if (mount is not null) NewMounts.Remove(mount); }

    private async Task StopWithOptionsAsync(ContainerSummary? container)
    {
        if (container is null || Workspace.IsBusy) return;
        var options = await Workspace.Interaction.PickContainerStopOptionsAsync(container.Name, Workspace.Capabilities);
        if (options is null || Workspace.IsBusy) return;
        await RunLifecycleActionAsync(container, "Stop container", token => Workspace.Runtime.StopContainerAsync(container.Id, options, token));
    }

    private async Task RunLifecycleActionAsync(ContainerSummary? container, string title, Func<CancellationToken, Task<OperationResult>> operation)
    {
        if (container is null || Workspace.IsBusy) return;
        IsLifecycleOperationInProgress = true;
        LifecycleStatus = LocalizationService.GetString("StopOptionsRunning", "Container operation in progress…");
        try
        {
            var result = await Workspace.RunTrackedAsync(title, (_, token) => operation(token));
            Workspace.ShowResult(result);
            LifecycleStatus = result.Success ? LocalizationService.GetString("StopOptionsCompleted", "Container operation completed.") : result.CombinedOutput;
        }
        catch (OperationCanceledException)
        {
            LifecycleStatus = LocalizationService.GetString("StopOptionsCancelled", "Cancelled waiting. A stop already sent to the runtime may still finish; check the container state.");
        }
        finally
        {
            IsLifecycleOperationInProgress = false;
            await Workspace.RefreshAllAsync();
            InvalidateNetworkDetails(container.Id);
        }
    }

    private Task RestartTrackedAsync(ContainerSummary? container) =>
        RunLifecycleActionAsync(container, "Restart container", token => Workspace.Runtime.RestartContainerAsync(container!.Id, token));
}
