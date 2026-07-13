using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExWSLC.Helpers;
using ExWSLC.Models;
using System.Collections.ObjectModel;

namespace ExWSLC.ViewModels;

public partial class ResourcesViewModel : WorkspaceViewModel
{
    public ResourcesViewModel(RuntimeWorkspace workspace) : base(workspace)
    {
    }
    public ObservableCollection<NetworkSummary> Networks => Workspace.Networks;
    public ObservableCollection<VolumeSummary> Volumes => Workspace.Volumes;

    [ObservableProperty] public partial NetworkSummary? SelectedNetwork { get; set; }
    [ObservableProperty] public partial VolumeSummary? SelectedVolume { get; set; }
    [ObservableProperty] public partial string NetworkName { get; set; } = string.Empty;
    [ObservableProperty] public partial string VolumeName { get; set; } = string.Empty;
    [ObservableProperty] public partial string ResourceOperationOutput { get; set; } = string.Empty;
    [ObservableProperty] public partial string NetworkInspectOutput { get; set; } = string.Empty;
    [ObservableProperty] public partial string VolumeInspectOutput { get; set; } = string.Empty;

    [RelayCommand]
    private async Task CreateNetworkAsync()
    {
        if (string.IsNullOrWhiteSpace(NetworkName)) return;
        await ShowOperationResultAsync(await Workspace.Runtime.CreateNetworkAsync(NetworkName, Workspace.Lifetime.Token));
        await Workspace.RefreshAllAsync();
    }

    [RelayCommand]
    private async Task RemoveNetworkAsync()
    {
        if (SelectedNetwork is null || !await Workspace.Interaction.ConfirmAsync("Remove network", $"Remove network {SelectedNetwork.Name}?")) return;
        await ShowOperationResultAsync(await Workspace.Runtime.RemoveNetworkAsync(SelectedNetwork.Name, Workspace.Lifetime.Token));
        await Workspace.RefreshAllAsync();
    }

    [RelayCommand]
    private async Task CreateVolumeAsync()
    {
        if (string.IsNullOrWhiteSpace(VolumeName)) return;
        await ShowOperationResultAsync(await Workspace.Runtime.CreateVolumeAsync(VolumeName, Workspace.Lifetime.Token));
        await Workspace.RefreshAllAsync();
    }

    [RelayCommand]
    private async Task RemoveVolumeAsync()
    {
        if (SelectedVolume is null || !await Workspace.Interaction.ConfirmAsync("Remove volume", $"Remove volume {SelectedVolume.Name}?")) return;
        await ShowOperationResultAsync(await Workspace.Runtime.RemoveVolumeAsync(SelectedVolume.Name, Workspace.Lifetime.Token));
        await Workspace.RefreshAllAsync();
    }

    [RelayCommand]
    private async Task InspectNetworkAsync()
    {
        if (SelectedNetwork is not null)
        {
            var result = await Workspace.Runtime.InspectResourceAsync("network", SelectedNetwork.Name, Workspace.Lifetime.Token);
            NetworkInspectOutput = result.Success ? JsonOutputFormatter.Format(result.Output) : result.CombinedOutput;
            if (!result.Success && !string.IsNullOrWhiteSpace(result.Error))
            {
                await Workspace.Interaction.ShowErrorAsync("WSLC operation failed", result.Error);
            }
        }
    }

    [RelayCommand]
    private async Task InspectVolumeAsync()
    {
        if (SelectedVolume is not null)
        {
            var result = await Workspace.Runtime.InspectResourceAsync("volume", SelectedVolume.Name, Workspace.Lifetime.Token);
            VolumeInspectOutput = result.Success ? JsonOutputFormatter.Format(result.Output) : result.CombinedOutput;
            if (!result.Success && !string.IsNullOrWhiteSpace(result.Error))
            {
                await Workspace.Interaction.ShowErrorAsync("WSLC operation failed", result.Error);
            }
        }
    }

    [RelayCommand]
    private async Task PruneAsync(string? resource)
    {
        if (resource is not ("network" or "volume")) return;
        if (!await Workspace.Interaction.ConfirmAsync("Prune resources", $"Remove every unused {resource} resource?")) return;
        await ShowOperationResultAsync(await Workspace.Runtime.PruneAsync(resource, Workspace.Lifetime.Token));
        await Workspace.RefreshAllAsync();
    }

    private async Task ShowOperationResultAsync(OperationResult result)
    {
        ResourceOperationOutput = result.CombinedOutput;
        if (!result.Success && !string.IsNullOrWhiteSpace(result.Error))
        {
            await Workspace.Interaction.ShowErrorAsync("WSLC operation failed", result.Error);
        }
    }

}
