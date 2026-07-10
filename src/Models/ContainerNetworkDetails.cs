using System.Net;

namespace ExWSLC.Models;

/// <summary>
/// The network-related portion of a container inspect response. The list command intentionally
/// remains lightweight, so this model is loaded only when the Network tab is visited.
/// </summary>
public sealed record ContainerNetworkDetails(
    string NetworkMode,
    string HostName,
    bool? IsRunning,
    IReadOnlyList<ContainerNetworkAttachment> Networks,
    IReadOnlyList<ContainerPortBinding> PortBindings)
{
    public bool HasNetworks => Networks.Count > 0;
    public bool HasPortBindings => PortBindings.Count > 0;
    public string DisplayNetworkMode => string.IsNullOrWhiteSpace(NetworkMode) ? "-" : NetworkMode;
    public string DisplayHostName => string.IsNullOrWhiteSpace(HostName) ? "-" : HostName;
    public bool RuntimeAddressesUnavailable => IsRunning == false &&
        Networks.All(network => string.IsNullOrWhiteSpace(network.IpAddress) && string.IsNullOrWhiteSpace(network.MacAddress));
}

/// <summary>One network attachment as reported by <c>wslc container inspect</c>.</summary>
public sealed record ContainerNetworkAttachment(
    string Name,
    string NetworkId,
    string EndpointId,
    string IpAddress,
    int IpPrefixLength,
    string Gateway,
    string MacAddress,
    IReadOnlyList<string> Aliases)
{
    public string DisplayIpAddress => string.IsNullOrWhiteSpace(IpAddress)
        ? "-"
        : IpPrefixLength > 0 ? $"{IpAddress}/{IpPrefixLength}" : IpAddress;

    public string DisplayGateway => string.IsNullOrWhiteSpace(Gateway) ? "-" : Gateway;
    public string DisplayMacAddress => string.IsNullOrWhiteSpace(MacAddress) ? "-" : MacAddress;
    public string DisplayAliases => Aliases.Count == 0 ? "-" : string.Join(", ", Aliases);
    public string DisplayNetworkId => string.IsNullOrWhiteSpace(NetworkId) ? "-" : NetworkId;
    public string DisplayEndpointId => string.IsNullOrWhiteSpace(EndpointId) ? "-" : EndpointId;
    public bool HasIpAddress => !string.IsNullOrWhiteSpace(IpAddress);
    public bool HasMacAddress => !string.IsNullOrWhiteSpace(MacAddress);
    public bool HasNetworkId => !string.IsNullOrWhiteSpace(NetworkId);
    public bool HasEndpointId => !string.IsNullOrWhiteSpace(EndpointId);
}

/// <summary>A published host-to-container port binding.</summary>
public sealed record ContainerPortBinding(
    string BindingAddress,
    string HostPort,
    string ContainerPort,
    string Protocol)
{
    public string DisplayHostEndpoint => string.IsNullOrWhiteSpace(HostPort)
        ? "-"
        : string.IsNullOrWhiteSpace(BindingAddress) ? HostPort : $"{FormatBindingAddress(BindingAddress)}:{HostPort}";

    public string DisplayContainerEndpoint => string.IsNullOrWhiteSpace(ContainerPort)
        ? "-"
        : string.IsNullOrWhiteSpace(Protocol) ? ContainerPort : $"{ContainerPort}/{Protocol.ToUpperInvariant()}";

    public bool HasHostBinding => !string.IsNullOrWhiteSpace(HostPort);

    public ContainerPortExposure Exposure => string.IsNullOrWhiteSpace(BindingAddress)
        ? ContainerPortExposure.Unknown
        : IsLoopbackAddress(BindingAddress)
            ? ContainerPortExposure.LocalOnly
            : ContainerPortExposure.PotentiallyExposed;

    private static bool IsLoopbackAddress(string address) =>
        address.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        IPAddress.TryParse(address, out var ipAddress) && IPAddress.IsLoopback(ipAddress);

    private static string FormatBindingAddress(string address) =>
        address.Contains(":", StringComparison.Ordinal) && !address.StartsWith("[", StringComparison.Ordinal)
            ? $"[{address}]"
            : address;
}

public enum ContainerPortExposure
{
    Unknown,
    LocalOnly,
    PotentiallyExposed
}
