using System.Text.Json;

namespace ExWSLC.Helpers;

internal static class ContainerPortFormatter
{
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
