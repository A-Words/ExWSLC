using System.Text.Json;
using ExWSLC.Models;

namespace ExWSLC.Helpers;

/// <summary>Reads the command and environment fields used by the container configuration page.</summary>
internal static class ContainerInspectDetailsParser
{
    public static bool TryParse(string inspectOutput, out ContainerInspectDetails details)
    {
        details = new ContainerInspectDetails(string.Empty, new ContainerInspectConfig([]), [], string.Empty);
        if (string.IsNullOrWhiteSpace(inspectOutput)) return false;

        try
        {
            using var document = JsonDocument.Parse(inspectOutput);
            var root = GetInspectRoot(document.RootElement);
            if (root.ValueKind != JsonValueKind.Object) return false;

            var config = GetObject(root, "Config");
            details = new ContainerInspectDetails(
                root.ReadString("Id", "ID", "ContainerId"),
                new ContainerInspectConfig(ReadStringArray(config, "Cmd", "Command")),
                ReadEnvironment(config),
                JsonOutputFormatter.Format(inspectOutput));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static JsonElement GetInspectRoot(JsonElement root) =>
        root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray().FirstOrDefault(item => item.ValueKind == JsonValueKind.Object)
            : root;

    private static JsonElement GetObject(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetPropertyIgnoreCase(name, out var value) &&
        value.ValueKind == JsonValueKind.Object
            ? value
            : default;

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetPropertyIgnoreCase(name, out var value) || value.ValueKind != JsonValueKind.Array) continue;
            return value.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!)
                .ToArray();
        }

        return [];
    }

    private static IReadOnlyList<ContainerKeyValue> ReadEnvironment(JsonElement config) =>
        ReadStringArray(config, "Env", "Environment")
            .Select(value =>
            {
                var separator = value.IndexOf('=');
                return separator < 0
                    ? new ContainerKeyValue(value, string.Empty)
                    : new ContainerKeyValue(value[..separator], value[(separator + 1)..]);
            })
            .ToArray();
}
