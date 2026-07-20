using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExWSLC.Helpers;
using ExWSLC.Models;
using ExWSLC.Services;

namespace ExWSLC.ViewModels;

public partial class NetworksViewModel : WorkspaceViewModel
{
    public NetworksViewModel(RuntimeWorkspace workspace) : base(workspace)
    {
        Workspace.Refreshed += (_, _) => ApplyFilter();
        ApplyFilter();
    }

    public ObservableCollection<NetworkSummary> Networks => Workspace.Networks;
    public ObservableCollection<NetworkSummary> VisibleNetworks { get; } = [];

    [ObservableProperty] public partial NetworkSummary? SelectedNetwork { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial string NetworkName { get; set; } = string.Empty;
    [ObservableProperty] public partial string NetworkDriver { get; set; } = "bridge";
    [ObservableProperty] public partial string NetworkOptions { get; set; } = string.Empty;
    [ObservableProperty] public partial string NetworkLabels { get; set; } = string.Empty;
    [ObservableProperty] public partial string OperationOutput { get; set; } = string.Empty;
    [ObservableProperty] public partial string InspectOutput { get; set; } = string.Empty;

    public bool HasOperationOutput => !string.IsNullOrWhiteSpace(OperationOutput);
    public bool HasInspectOutput => !string.IsNullOrWhiteSpace(InspectOutput);

    [RelayCommand(CanExecute = nameof(CanCreateNetwork))]
    private async Task CreateNetworkAsync()
    {
        var name = NetworkName.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var spec = new NetworkCreateSpec
        {
            Name = name,
            Driver = NetworkDriver.Trim()
        };
        spec.DriverOptions.AddRange(StringSplitter.SplitLines(NetworkOptions));
        spec.Labels.AddRange(StringSplitter.SplitLines(NetworkLabels));

        var result = await Workspace.Runtime.CreateNetworkAsync(spec, Workspace.Lifetime.Token);
        await ShowOperationResultAsync(result);
        if (!result.Success) return;

        NetworkName = string.Empty;
        await Workspace.RefreshAllAsync();
    }

    [RelayCommand]
    private async Task RemoveNetworkAsync(NetworkSummary? network)
    {
        network ??= SelectedNetwork;
        if (network is null) return;

        var title = LocalizationService.GetString("RemoveNetworkTitle", "Remove network");
        var template = LocalizationService.GetString("RemoveNetworkConfirmation", "Remove network {0}?");
        if (!await Workspace.Interaction.ConfirmAsync(title, string.Format(template, network.Name))) return;

        var result = await Workspace.Runtime.RemoveNetworkAsync(network.Name, Workspace.Lifetime.Token);
        await ShowOperationResultAsync(result);
        if (result.Success) await Workspace.RefreshAllAsync();
    }

    [RelayCommand]
    private async Task InspectNetworkAsync(NetworkSummary? network)
    {
        network ??= SelectedNetwork;
        if (network is null) return;

        var result = await Workspace.Runtime.InspectResourceAsync("network", network.Name, Workspace.Lifetime.Token);
        InspectOutput = result.Success ? JsonOutputFormatter.Format(result.Output) : result.CombinedOutput;
        if (!result.Success && !string.IsNullOrWhiteSpace(result.Error))
        {
            await Workspace.Interaction.ShowErrorAsync(OperationFailedTitle, result.Error);
        }
    }

    [RelayCommand]
    private async Task PruneNetworksAsync()
    {
        var title = LocalizationService.GetString("PruneNetworksTitle", "Prune networks");
        var message = LocalizationService.GetString("PruneNetworksConfirmation", "Remove every unused network?");
        if (!await Workspace.Interaction.ConfirmAsync(title, message)) return;

        var result = await Workspace.Runtime.PruneAsync("network", Workspace.Lifetime.Token);
        await ShowOperationResultAsync(result);
        if (result.Success) await Workspace.RefreshAllAsync();
    }

    private async Task ShowOperationResultAsync(OperationResult result)
    {
        OperationOutput = result.CombinedOutput;
        if (!result.Success && !string.IsNullOrWhiteSpace(result.Error))
        {
            await Workspace.Interaction.ShowErrorAsync(OperationFailedTitle, result.Error);
        }
    }

    private bool CanCreateNetwork() => !string.IsNullOrWhiteSpace(NetworkName);

    private static string OperationFailedTitle =>
        LocalizationService.GetString("OperationFailed", "WSLC operation failed");

    partial void OnNetworkNameChanged(string value) => CreateNetworkCommand.NotifyCanExecuteChanged();
    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnOperationOutputChanged(string value) => OnPropertyChanged(nameof(HasOperationOutput));
    partial void OnInspectOutputChanged(string value) => OnPropertyChanged(nameof(HasInspectOutput));

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        VisibleNetworks.ReplaceAll(Networks.Where(network => string.IsNullOrEmpty(query) ||
            network.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            network.DisplayId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            network.DisplayDriver.Contains(query, StringComparison.OrdinalIgnoreCase)));
    }
}
