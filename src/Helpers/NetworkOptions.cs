using System.Globalization;
using System.Net;
using System.Net.Sockets;
using ExWSLC.Models;
using ExWSLC.Services;

namespace ExWSLC.Helpers;

internal static class NetworkOptions
{
    public static string Text(string key, string fallback) => LocalizationService.GetString(key, fallback);

    public static void RequireSupport(RuntimeCapabilities? capabilities, params RuntimeFeature[] features)
    {
        if (features.Any(feature => capabilities?[feature].Support != CapabilitySupport.Supported))
            throw new ArgumentException(Text("NetworkOptionUnsupported", "This network operation or option is unavailable. Clear optional fields or re-detect the runtime in Settings."));
    }

    public static void ValidateTarget(string containerId, string networkName)
    {
        ValidateName(containerId);
        ValidateName(networkName);
    }

    private static void ValidateName(string value)
    {
        // Positional identifiers must not become CLI options; shell metacharacters remain literal.
        if (string.IsNullOrWhiteSpace(value) || value.TrimStart().StartsWith('-') || value.Any(char.IsControl))
            throw new ArgumentException(Text("NetworkInvalidTarget", "Choose a valid container and network name (not an option beginning with '-')."));
    }

    public static void Validate(NetworkConnectionSpec spec)
    {
        ValidateTarget(spec.ContainerId, spec.NetworkName);
        if (spec.Ipv4Address is not null && (!TryAddress(spec.Ipv4Address, out var address) || address.AddressFamily != AddressFamily.InterNetwork))
            throw new ArgumentException(Text("NetworkInvalidIpv4", "Enter an IPv4 address such as 172.30.0.10, without a CIDR suffix."));
        if (spec.Aliases?.Any(value => string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl)) == true)
            throw new ArgumentException(Text("NetworkInvalidAlias", "Enter one nonempty network alias per line."));
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var option in spec.DriverOptions ?? [])
        {
            var separator = option.IndexOf('=');
            if (separator <= 0 || option.Any(char.IsControl) || !keys.Add(option[..separator]))
                throw new ArgumentException(Text("NetworkInvalidDriverOptions", "Use one key=value endpoint option per line, with no repeated keys."));
        }
    }

    public static void Validate(NetworkCreateSpec spec)
    {
        ValidateName(spec.Name);
        if (spec.Subnet is null && (spec.Gateway is not null || spec.IpRange is not null))
            throw new ArgumentException(Text("NetworkSubnetRequired", "Enter a subnet before specifying a gateway or IP range."));
        if (spec.Subnet is null) return;
        if (!TrySubnet(spec.Subnet, out var subnet, out var prefix))
            throw new ArgumentException(Text("NetworkInvalidSubnet", "Enter a network CIDR with no host bits, such as 172.30.0.0/24."));
        if (spec.Gateway is not null && (!TryAddress(spec.Gateway, out var gateway) || !Contains(subnet, prefix, gateway)))
            throw new ArgumentException(Text("NetworkInvalidGateway", "The gateway must be an IP address inside the subnet, using the same address family."));
        if (spec.IpRange is not null && (!TrySubnet(spec.IpRange, out var range, out var rangePrefix) || rangePrefix < prefix || !Contains(subnet, prefix, range)))
            throw new ArgumentException(Text("NetworkInvalidRange", "The IP range must be a network CIDR contained within the subnet."));
    }

    private static bool TryAddress(string value, out IPAddress address)
    {
        if (!IPAddress.TryParse(value, out address!) || value.Contains('%')) return false;
        return address.AddressFamily != AddressFamily.InterNetwork || address.ToString() == value;
    }

    private static bool TrySubnet(string value, out IPAddress address, out int prefix)
    {
        address = IPAddress.None;
        prefix = 0;
        var parts = value.Split('/');
        if (parts.Length != 2 || !TryAddress(parts[0], out address) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out prefix)) return false;
        var bytes = address.GetAddressBytes();
        if (prefix < 0 || prefix > bytes.Length * 8) return false;
        for (var bit = prefix; bit < bytes.Length * 8; bit++)
            if ((bytes[bit / 8] & (1 << (7 - bit % 8))) != 0) return false;
        return true;
    }

    private static bool Contains(IPAddress subnet, int prefix, IPAddress address)
    {
        var network = subnet.GetAddressBytes();
        var candidate = address.GetAddressBytes();
        if (network.Length != candidate.Length) return false;
        for (var bit = 0; bit < prefix; bit++)
            if (((network[bit / 8] ^ candidate[bit / 8]) & (1 << (7 - bit % 8))) != 0) return false;
        return true;
    }
}
