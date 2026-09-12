using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExWSLC.Helpers;
using ExWSLC.Models;

namespace ExWSLC.ViewModels;

public partial class ContainersViewModel
{
    private readonly HashSet<string> _staleNetworkContainers = new(StringComparer.OrdinalIgnoreCase);
    public ObservableCollection<NetworkSummary> AvailableConnectionNetworks { get; } = [];
    [ObservableProperty] public partial string? ConnectionNetworkName { get; set; }
    [ObservableProperty] public partial string ConnectionIpv4 { get; set; } = string.Empty;
    [ObservableProperty] public partial string ConnectionAliases { get; set; } = string.Empty;
    [ObservableProperty] public partial string ConnectionDriverOptions { get; set; } = string.Empty;
    [ObservableProperty] public partial string NetworkOperationMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsNetworkOperationInProgress { get; set; }
    [ObservableProperty] public partial bool IsNetworkDetailsStale { get; set; }

    public bool CanSetConnectionIpv4 => SupportsNetwork(RuntimeFeature.NetworkConnectIp);
    public bool CanSetConnectionAliases => SupportsNetwork(RuntimeFeature.NetworkConnectAlias);
    public bool CanSetConnectionDriverOptions => SupportsNetwork(RuntimeFeature.NetworkConnectDriverOptions);
    public bool HasAvailableConnectionNetworks => AvailableConnectionNetworks.Count > 0;
    public bool SupportsNetworkConnect => SupportsNetwork(RuntimeFeature.NetworkConnect);
    public bool CanEditNetworkConnection => !IsNetworkOperationInProgress;

    private bool SupportsNetwork(RuntimeFeature feature) => Workspace.Capabilities[feature].Support == CapabilitySupport.Supported;
    private bool CanChangeNetwork() => CanEditNetworkConnection && !Workspace.IsBusy && SelectedContainer is not null &&
        NetworkDetails is not null && !IsNetworkDetailsLoading && !IsNetworkDetailsStale;
    private bool CanConnectNetwork() => CanChangeNetwork() && SupportsNetworkConnect && !string.IsNullOrWhiteSpace(ConnectionNetworkName);
    private bool CanDisconnectNetwork(ContainerNetworkAttachment? attachment) => CanChangeNetwork() &&
        attachment is not null && SupportsNetwork(RuntimeFeature.NetworkDisconnect);

    partial void OnConnectionNetworkNameChanged(string? value) => NotifyNetworkCommands();
    partial void OnIsNetworkOperationInProgressChanged(bool value) => NotifyNetworkCommands();
    partial void OnIsNetworkDetailsStaleChanged(bool value) => NotifyNetworkCommands();
    partial void OnIsNetworkDetailsLoadingChanged(bool value) => NotifyNetworkCommands();

    private void NotifyNetworkCommands()
    {
        OnPropertyChanged(nameof(CanEditNetworkConnection));
        OnPropertyChanged(nameof(SupportsNetworkConnect));
        OnPropertyChanged(nameof(CanSetConnectionIpv4));
        OnPropertyChanged(nameof(CanSetConnectionAliases));
        OnPropertyChanged(nameof(CanSetConnectionDriverOptions));
        ConnectNetworkCommand.NotifyCanExecuteChanged();
        DisconnectNetworkCommand.NotifyCanExecuteChanged();
    }

    private void UpdateConnectionNetworks()
    {
        var selected = ConnectionNetworkName;
        AvailableConnectionNetworks.ReplaceAll(Workspace.Networks.Where(network =>
            network.Driver is not ("host" or "null") && network.Name is not ("host" or "none") &&
            NetworkDetails?.Networks.Any(attached => attached.Name == network.Name) != true));
        ConnectionNetworkName = AvailableConnectionNetworks.Any(network => network.Name == selected) ? selected : null;
        OnPropertyChanged(nameof(HasAvailableConnectionNetworks));
        NotifyNetworkCommands();
    }

    [RelayCommand(CanExecute = nameof(CanConnectNetwork))]
    private Task ConnectNetworkAsync() => ChangeNetworkAsync(null);

    [RelayCommand(CanExecute = nameof(CanDisconnectNetwork))]
    private Task DisconnectNetworkAsync(ContainerNetworkAttachment? attachment) => ChangeNetworkAsync(attachment);

    private async Task ChangeNetworkAsync(ContainerNetworkAttachment? attachment)
    {
        // Lock before confirmation, and capture all user inputs before any await.
        if (IsNetworkOperationInProgress || Workspace.IsBusy || SelectedContainer is not { } container) return;
        IsNetworkOperationInProgress = true;
        var networkName = attachment?.Name ?? ConnectionNetworkName ?? string.Empty;
        var target = $"{container.Name} / {networkName}";
        var connect = attachment is null;
        var attempted = false;
        NetworkOperationMessage = string.Empty;
        try
        {
            if (NetworkDetails is null || IsNetworkDetailsLoading || IsNetworkDetailsStale)
                throw new ArgumentException(NetworkOptions.Text("NetworkRefreshFirst", "Refresh network details before changing connections."));
            var attached = NetworkDetails.Networks.Any(network => network.Name == networkName);
            if (connect && attached || !connect && !attached)
                throw new ArgumentException(NetworkOptions.Text("NetworkAttachmentChanged", "This network is already connected or no longer attached. Refresh and choose again."));
            if (NetworkDetails.NetworkMode is "host" or "none" || NetworkDetails.NetworkMode.StartsWith("container:", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(NetworkOptions.Text("NetworkModeUnsupported", "This container's host, none, or shared network mode cannot be changed here."));
            var spec = new NetworkConnectionSpec(container.Id, networkName, string.IsNullOrWhiteSpace(ConnectionIpv4) ? null : ConnectionIpv4.Trim(),
                StringSplitter.SplitLines(ConnectionAliases).ToArray(), StringSplitter.SplitLines(ConnectionDriverOptions).ToArray());
            NetworkOptions.ValidateTarget(container.Id, networkName);
            if (connect)
            {
                if (!AvailableConnectionNetworks.Any(network => network.Name == networkName))
                    throw new ArgumentException(NetworkOptions.Text("NetworkChooseAvailable", "Choose an available network from the list, or refresh the inventory."));
                NetworkOptions.Validate(spec);
                NetworkOptions.RequireSupport(Workspace.Capabilities, RuntimeFeature.NetworkConnect);
                if (spec.Ipv4Address is not null) NetworkOptions.RequireSupport(Workspace.Capabilities, RuntimeFeature.NetworkConnectIp);
                if (spec.Aliases?.Count > 0) NetworkOptions.RequireSupport(Workspace.Capabilities, RuntimeFeature.NetworkConnectAlias);
                if (spec.DriverOptions?.Count > 0) NetworkOptions.RequireSupport(Workspace.Capabilities, RuntimeFeature.NetworkConnectDriverOptions);
            }
            else
            {
                NetworkOptions.RequireSupport(Workspace.Capabilities, RuntimeFeature.NetworkDisconnect);
                if (!await Workspace.Interaction.ConfirmAsync(NetworkOptions.Text("NetworkDisconnect", "Disconnect"),
                    string.Format(NetworkOptions.Text("NetworkDisconnectConfirmation", "Disconnect {0} from {1}? Existing connections may be interrupted."), container.Name, networkName))) return;
            }
            if (Workspace.IsBusy) throw new ArgumentException(NetworkOptions.Text("NetworkWorkspaceBusy", "Another operation is running. Wait and try again."));
            attempted = true;
            var result = await Workspace.RunTrackedAsync(target, (_, token) => connect
                ? Workspace.Runtime.ConnectNetworkAsync(spec, token)
                : Workspace.Runtime.DisconnectNetworkAsync(new(container.Id, networkName), token));
            if (result.ExitCode == -2) throw new OperationCanceledException();
            if (!result.Success)
            {
                NetworkOperationMessage = target + ": " + NetworkOptions.Text("NetworkOperationFailedHint", "Operation failed. Check address conflicts, driver options and network mode; details are unchanged.") + "\n" + result.Error;
                return;
            }
            await RefreshAfterNetworkChangeAsync(container.Id);
            NetworkOperationMessage = target + ": " + NetworkOptions.Text("NetworkOperationSucceeded", "Operation succeeded.") + " " +
                (Workspace.HasRefreshError || IsCurrentNetworkContainer(container.Id) && IsNetworkDetailsStale
                    ? NetworkOptions.Text("NetworkRefreshIncomplete", "Refresh is incomplete. Retry before making another change.")
                    : NetworkOptions.Text("NetworkRefreshCompleted", "Inventory refreshed; reopen or refresh the target's network details to verify."));
        }
        catch (OperationCanceledException)
        {
            // Cancellation can race with a server-side mutation; invalidate even without a success receipt.
            if (attempted && !Workspace.Lifetime.IsCancellationRequested) await RefreshAfterNetworkChangeAsync(container.Id);
            NetworkOperationMessage = target + ": " + NetworkOptions.Text("NetworkOperationCancelled", "Cancellation requested. The runtime may have applied the change; refresh the target to verify.");
        }
        catch (Exception exception)
        {
            NetworkOperationMessage = target + ": " + exception.Message;
        }
        finally
        {
            IsNetworkOperationInProgress = false;
        }
    }

    private async Task RefreshAfterNetworkChangeAsync(string containerId)
    {
        _networkDetailsCache.Remove(containerId);
        _inspectDetailsCache.Remove(containerId);
        _staleNetworkContainers.Add(containerId);
        if (_networkDetailsLoadContainerId == containerId) _networkDetailsLoad?.Cancel();
        if (SelectedContainer?.Id == containerId)
        {
            IsNetworkDetailsStale = true;
            NetworkDetailsUpdatedAt = null;
        }
        await Workspace.RefreshAllAsync();
        if (IsCurrentNetworkContainer(containerId)) await LoadNetworkDetailsAsync(force: true);
        UpdateConnectionNetworks();
    }
}
