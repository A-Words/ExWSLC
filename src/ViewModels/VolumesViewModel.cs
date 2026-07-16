using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExWSLC.Helpers;
using ExWSLC.Models;
using ExWSLC.Services;

namespace ExWSLC.ViewModels;

public partial class VolumesViewModel : WorkspaceViewModel
{
    public VolumesViewModel(RuntimeWorkspace workspace) : base(workspace)
    {
    }

    public ObservableCollection<VolumeSummary> Volumes => Workspace.Volumes;

    [ObservableProperty] public partial VolumeSummary? SelectedVolume { get; set; }
    [ObservableProperty] public partial string VolumeName { get; set; } = string.Empty;
    [ObservableProperty] public partial string VolumeDriver { get; set; } = "guest";
    [ObservableProperty] public partial string VolumeOptions { get; set; } = string.Empty;
    [ObservableProperty] public partial string VolumeLabels { get; set; } = string.Empty;

    [RelayCommand]
    private async Task CreateVolumeAsync()
    {
        var spec = new VolumeCreateSpec
        {
            Name = VolumeName.Trim(),
            Driver = VolumeDriver.Trim()
        };
        spec.DriverOptions.AddRange(StringSplitter.SplitLines(VolumeOptions));
        spec.Labels.AddRange(StringSplitter.SplitLines(VolumeLabels));

        var result = await Workspace.Runtime.CreateVolumeAsync(spec, Workspace.Lifetime.Token);
        await ShowOperationErrorAsync(result);
        if (!result.Success) return;

        VolumeName = string.Empty;
        await Workspace.RefreshAllAsync();
    }

    [RelayCommand]
    private async Task RemoveVolumeAsync(VolumeSummary? volume)
    {
        volume ??= SelectedVolume;
        if (volume is null) return;

        var title = LocalizationService.GetString("RemoveVolumeTitle", "Remove volume");
        var template = LocalizationService.GetString("RemoveVolumeConfirmation", "Remove volume {0}?");
        if (!await Workspace.Interaction.ConfirmAsync(title, string.Format(template, volume.Name))) return;

        var result = await Workspace.Runtime.RemoveVolumeAsync(volume.Name, false, Workspace.Lifetime.Token);
        await ShowOperationErrorAsync(result);
        if (result.Success) await Workspace.RefreshAllAsync();
    }

    [RelayCommand]
    private async Task PruneVolumesAsync()
    {
        var title = LocalizationService.GetString("PruneVolumesTitle", "Prune volumes");
        var message = LocalizationService.GetString("PruneVolumesConfirmation", "Remove every unused volume?");
        if (!await Workspace.Interaction.ConfirmAsync(title, message)) return;

        var result = await Workspace.Runtime.PruneVolumesAsync(new VolumePruneSpec(), Workspace.Lifetime.Token);
        await ShowOperationErrorAsync(result);
        if (result.Success) await Workspace.RefreshAllAsync();
    }

    private async Task ShowOperationErrorAsync(OperationResult result)
    {
        if (!result.Success && !string.IsNullOrWhiteSpace(result.Error))
        {
            await Workspace.Interaction.ShowErrorAsync(OperationFailedTitle, result.Error);
        }
    }

    private static string OperationFailedTitle =>
        LocalizationService.GetString("OperationFailed", "WSLC operation failed");
}
