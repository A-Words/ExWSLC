using System.Text.Json;
using ExWSLC.Models;

namespace ExWSLC.Helpers;

/// <summary>
/// Parses the network fields from the Docker-compatible inspect payload produced by WSLC.
/// Unknown fields are ignored deliberately: inspect is a diagnostic payload, not a versioned
/// application contract.
/// </summary>
internal static class ContainerNetworkDetailsParser
{
    public static bool TryParse(string inspectOutput, out ContainerNetworkDetails details)
    {
        details = new ContainerNetworkDetails(string.Empty, string.Empty, null, [], []);
        if (string.IsNullOrWhiteSpace(inspectOutput)) return false;

        try
        {
            using var document = JsonDocument.Parse(inspectOutput);
            var root = GetInspectRoot(document.RootElement);
            if (root.ValueKind != JsonValueKind.Object) return false;

            var hostConfig = GetObject(root, "HostConfig");
            var config = GetObject(root, "Config");
            var state = GetObject(root, "State");
            details = new ContainerNetworkDetails(
                hostConfig.ReadString("NetworkMode"),
                config.ReadString("Hostname", "HostName"),
                ReadNullableBoolean(state, "Running"),
                ReadNetworkAttachments(root),
                ReadPortBindings(root));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static JsonElement GetInspectRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array) return root;

        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object) return item;
        }

        return default;
    }

    private static IReadOnlyList<ContainerNetworkAttachment> ReadNetworkAttachments(JsonElement root)
    {
        var networks = GetObject(GetObject(root, "NetworkSettings"), "Networks");
        if (networks.ValueKind != JsonValueKind.Object) return [];

        var attachments = new List<ContainerNetworkAttachment>();
        foreach (var network in networks.EnumerateObject())
        {
            if (network.Value.ValueKind != JsonValueKind.Object) continue;

            var value = network.Value;
            attachments.Add(new ContainerNetworkAttachment(
                network.Name,
                value.ReadString("NetworkID", "NetworkId", "NetworkIdentifier"),
                value.ReadString("EndpointID", "EndpointId", "EndpointIdentifier"),
                value.ReadString("IPAddress", "IpAddress", "IP"),
                value.ReadInt("IPPrefixLen", "IpPrefixLen", "PrefixLength"),
                value.ReadString("Gateway"),
                value.ReadString("MacAddress", "MACAddress"),
                ReadStringArray(value, "Aliases")));
        }

        return attachments;
    }

    private static IReadOnlyList<ContainerPortBinding> ReadPortBindings(JsonElement root)
    {
        var ports = GetObject(root, "Ports");
        if (ports.ValueKind != JsonValueKind.Object) return [];

        var bindings = new List<ContainerPortBinding>();
        foreach (var port in ports.EnumerateObject())
        {
            var (containerPort, protocol) = SplitPortKey(port.Name);
            if (port.Value.ValueKind == JsonValueKind.Array)
            {
                var foundBinding = false;
                foreach (var binding in port.Value.EnumerateArray())
                {
                    if (binding.ValueKind != JsonValueKind.Object) continue;
                    foundBinding = true;
                    bindings.Add(new ContainerPortBinding(
                        binding.ReadString("HostIp", "HostIP", "BindingAddress"),
                        binding.ReadString("HostPort", "PublicPort", "PublishedPort"),
                        containerPort,
                        protocol));
                }

                if (!foundBinding) bindings.Add(new ContainerPortBinding(string.Empty, string.Empty, containerPort, protocol));
                continue;
            }

            if (port.Value.ValueKind == JsonValueKind.Object)
            {
                bindings.Add(new ContainerPortBinding(
                    port.Value.ReadString("HostIp", "HostIP", "BindingAddress"),
                    port.Value.ReadString("HostPort", "PublicPort", "PublishedPort"),
                    containerPort,
                    protocol));
                continue;
            }

            bindings.Add(new ContainerPortBinding(string.Empty, string.Empty, containerPort, protocol));
        }

        return bindings;
    }

    private static (string ContainerPort, string Protocol) SplitPortKey(string key)
    {
        var separator = key.LastIndexOf('/');
        return separator < 0
            ? (key, string.Empty)
            : (key[..separator], key[(separator + 1)..]);
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string name)
    {
        if (!element.TryGetPropertyIgnoreCase(name, out var value) || value.ValueKind != JsonValueKind.Array) return [];

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToArray();
    }

    private static bool? ReadNullableBoolean(JsonElement element, string name)
    {
        if (!element.TryGetPropertyIgnoreCase(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static JsonElement GetObject(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetPropertyIgnoreCase(name, out var value) &&
        value.ValueKind == JsonValueKind.Object
            ? value
            : default;
}
