using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    [ObservableProperty] public partial string ResourceName { get; set; } = string.Empty;

    [RelayCommand]
    private async Task CreateNetworkAsync()
    {
        if (string.IsNullOrWhiteSpace(ResourceName)) return;
        Workspace.ShowResult(await Workspace.Runtime.CreateNetworkAsync(ResourceName, Workspace.Lifetime.Token));
        await Workspace.RefreshAllAsync();
    }

    [RelayCommand]
    private async Task RemoveNetworkAsync()
    {
        if (SelectedNetwork is null || !Workspace.Interaction.Confirm("Remove network", $"Remove network {SelectedNetwork.Name}?")) return;
        Workspace.ShowResult(await Workspace.Runtime.RemoveNetworkAsync(SelectedNetwork.Name, Workspace.Lifetime.Token));
        await Workspace.RefreshAllAsync();
    }

    [RelayCommand]
    private async Task CreateVolumeAsync()
    {
        if (string.IsNullOrWhiteSpace(ResourceName)) return;
        Workspace.ShowResult(await Workspace.Runtime.CreateVolumeAsync(ResourceName, Workspace.Lifetime.Token));
        await Workspace.RefreshAllAsync();
    }

    [RelayCommand]
    private async Task RemoveVolumeAsync()
    {
        if (SelectedVolume is null || !Workspace.Interaction.Confirm("Remove volume", $"Remove volume {SelectedVolume.Name}?")) return;
        Workspace.ShowResult(await Workspace.Runtime.RemoveVolumeAsync(SelectedVolume.Name, Workspace.Lifetime.Token));
        await Workspace.RefreshAllAsync();
    }

    [RelayCommand]
    private async Task InspectNetworkAsync()
    {
        if (SelectedNetwork is not null)
        {
            Workspace.ShowResult(await Workspace.Runtime.InspectResourceAsync("network", SelectedNetwork.Name, Workspace.Lifetime.Token));
        }
    }

    [RelayCommand]
    private async Task InspectVolumeAsync()
    {
        if (SelectedVolume is not null)
        {
            Workspace.ShowResult(await Workspace.Runtime.InspectResourceAsync("volume", SelectedVolume.Name, Workspace.Lifetime.Token));
        }
    }

    [RelayCommand]
    private async Task PruneAsync(string? resource)
    {
        if (resource is not ("network" or "volume")) return;
        if (!Workspace.Interaction.Confirm("Prune resources", $"Remove every unused {resource} resource?")) return;
        Workspace.ShowResult(await Workspace.Runtime.PruneAsync(resource, Workspace.Lifetime.Token));
        await Workspace.RefreshAllAsync();
    }

}
