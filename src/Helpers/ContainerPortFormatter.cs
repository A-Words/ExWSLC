using System.Text.Json;

using System.Text.RegularExpressions;
using ExWSLC.Models;

namespace ExWSLC.Helpers;

internal static partial class ContainerPortFormatter
{
    public static ContainerListPorts FormatList(string ports)
    {
        var normalized = ports.Trim();
        if (normalized is "" or "[]" or "{}" or "-") return new([]);
        if ((!normalized.StartsWith('[') && !normalized.StartsWith('{')) ||
            normalized.StartsWith('[') && normalized.Contains("->", StringComparison.Ordinal))
            return new(normalized.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(FormatTextMapping).Distinct().ToArray());

        try
        {
            using var document = JsonDocument.Parse(normalized);
            var mappings = EnumeratePortMappings(document.RootElement)
                .Select(FormatListMapping).Where(mapping => mapping is not null)
                .Cast<ContainerListPort>().Distinct().ToArray();
            // Preserve unfamiliar payloads instead of claiming that no ports exist.
            return mappings.Length == 0 ? new([new(normalized, string.Empty, normalized)]) : new(mappings);
        }
        catch (JsonException)
        {
            return new([new(normalized, string.Empty, normalized)]);
        }
    }

    private static ContainerListPort FormatTextMapping(string text)
    {
        var match = TextMappingPattern().Match(text);
        if (!match.Success) return new(text, string.Empty, text);
        var address = match.Groups["address"].Value;
        return new($"{match.Groups["host"].Value} → {match.Groups["container"].Value}", address, text);
    }

    [GeneratedRegex(@"^(?:(?<address>.+):)?(?<host>\d+(?:-\d+)?)(?:->|:)(?<container>\d+(?:-\d+)?(?:/[a-zA-Z0-9]+)?)$")]
    private static partial Regex TextMappingPattern();

    private static ContainerListPort? FormatListMapping(JsonElement element)
    {
        var containerPort = element.ReadInt("ContainerPort", "PrivatePort", "TargetPort");
        var hostPort = element.ReadInt("HostPort", "PublicPort", "PublishedPort");
        if (containerPort <= 0 && hostPort <= 0) return null;
        var protocol = element.ReadString("Protocol", "Type").ToLowerInvariant() switch
        {
            "6" => "tcp",
            "17" => "udp",
            var value => value
        };
        var address = element.ReadString("BindingAddress", "HostIp", "IP");
        var container = containerPort > 0 ? containerPort.ToString() : string.Empty;
        if (container.Length > 0 && protocol.Length > 0) container += $"/{protocol}";
        var mapping = hostPort <= 0 ? container : container.Length == 0 ? hostPort.ToString() : $"{hostPort} → {container}";
        var fullText = address.Length == 0 ? mapping : $"{address} · {mapping}";
        return new(mapping, address, fullText);
    }

    public static string Format(string ports)
    {
        var normalized = ports.Trim();
        if (normalized is "" or "[]" or "{}" or "-") return "-";
        if (!normalized.StartsWith('[') && !normalized.StartsWith('{')) return normalized;

        try
        {
            using var document = JsonDocument.Parse(normalized);
            var mappings = EnumeratePortMappings(document.RootElement)
                .Select(FormatPortMapping)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct()
                .ToArray();

            return mappings.Length == 0 ? "-" : string.Join(", ", mappings);
        }
        catch (JsonException)
        {
            return normalized;
        }
    }

    private static IEnumerable<JsonElement> EnumeratePortMappings(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object);
        }

        if (root.ValueKind != JsonValueKind.Object) return [];

        if (LooksLikePortMapping(root)) return [root];

        return root.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.Array)
            .SelectMany(property => property.Value.EnumerateArray())
            .Where(item => item.ValueKind == JsonValueKind.Object);
    }

    private static bool LooksLikePortMapping(JsonElement element) =>
        element.TryGetPropertyIgnoreCase("ContainerPort", out _) ||
        element.TryGetPropertyIgnoreCase("HostPort", out _) ||
        element.TryGetPropertyIgnoreCase("PrivatePort", out _) ||
        element.TryGetPropertyIgnoreCase("PublicPort", out _);

    private static string FormatPortMapping(JsonElement element)
    {
        var containerPort = element.ReadInt("ContainerPort", "PrivatePort", "TargetPort");
        var hostPort = element.ReadInt("HostPort", "PublicPort", "PublishedPort");
        if (containerPort <= 0 && hostPort <= 0) return string.Empty;

        var container = containerPort > 0 ? containerPort.ToString() : string.Empty;
        if (hostPort <= 0) return container;

        return string.IsNullOrEmpty(container) ? hostPort.ToString() : $"{hostPort}:{container}";
    }
}
