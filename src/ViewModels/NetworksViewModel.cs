using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExWSLC.Helpers;
using ExWSLC.Models;
using ExWSLC.Services;
using NetworkValidation = ExWSLC.Helpers.NetworkOptions;

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
    [ObservableProperty] public partial string NetworkSubnet { get; set; } = string.Empty;
    [ObservableProperty] public partial string NetworkGateway { get; set; } = string.Empty;
    [ObservableProperty] public partial string NetworkIpRange { get; set; } = string.Empty;
    public bool CanSetNetworkSubnet => Workspace.Capabilities[RuntimeFeature.NetworkCreateSubnet].Support == CapabilitySupport.Supported;
    public bool CanSetNetworkGateway => Workspace.Capabilities[RuntimeFeature.NetworkCreateGateway].Support == CapabilitySupport.Supported;
    public bool CanSetNetworkIpRange => Workspace.Capabilities[RuntimeFeature.NetworkCreateIpRange].Support == CapabilitySupport.Supported;

    protected override void OnWorkspacePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        base.OnWorkspacePropertyChanged(sender, args);
        if (args.PropertyName == nameof(RuntimeWorkspace.Capabilities))
        {
            OnPropertyChanged(nameof(CanSetNetworkSubnet));
            OnPropertyChanged(nameof(CanSetNetworkGateway));
            OnPropertyChanged(nameof(CanSetNetworkIpRange));
        }
        if (args.PropertyName == nameof(RuntimeWorkspace.IsBusy)) CreateNetworkCommand.NotifyCanExecuteChanged();
    }
    [ObservableProperty] public partial string OperationOutput { get; set; } = string.Empty;
    [ObservableProperty] public partial string InspectOutput { get; set; } = string.Empty;

    public bool HasOperationOutput => !string.IsNullOrWhiteSpace(OperationOutput);
    public bool HasInspectOutput => !string.IsNullOrWhiteSpace(InspectOutput);

    [RelayCommand(CanExecute = nameof(CanCreateNetwork))]
    private async Task CreateNetworkAsync()
    {
        if (Workspace.IsBusy) return;
        var name = NetworkName.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var spec = new NetworkCreateSpec
        {
            Name = name,
            Driver = NetworkDriver.Trim(),
            Subnet = OptionalValue(NetworkSubnet),
            Gateway = OptionalValue(NetworkGateway),
            IpRange = OptionalValue(NetworkIpRange)
        };
        spec.DriverOptions.AddRange(StringSplitter.SplitLines(NetworkOptions));
        spec.Labels.AddRange(StringSplitter.SplitLines(NetworkLabels));

        OperationResult result;
        try
        {
            NetworkValidation.Validate(spec);
            if (spec.Subnet is not null) NetworkValidation.RequireSupport(Workspace.Capabilities, RuntimeFeature.NetworkCreateSubnet);
            if (spec.Gateway is not null) NetworkValidation.RequireSupport(Workspace.Capabilities, RuntimeFeature.NetworkCreateGateway);
            if (spec.IpRange is not null) NetworkValidation.RequireSupport(Workspace.Capabilities, RuntimeFeature.NetworkCreateIpRange);
            result = await Workspace.Runtime.CreateNetworkAsync(spec, Workspace.Lifetime.Token);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception exception)
        {
            OperationOutput = exception.Message;
            await Workspace.Interaction.ShowErrorAsync(OperationFailedTitle, exception.Message);
            return;
        }
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

    private static string? OptionalValue(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private bool CanCreateNetwork() => !Workspace.IsBusy && !string.IsNullOrWhiteSpace(NetworkName);

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
