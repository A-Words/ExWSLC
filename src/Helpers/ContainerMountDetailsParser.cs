using System.Text.Json;
using ExWSLC.Models;

namespace ExWSLC.Helpers;

/// <summary>
/// Parses the mount fields from the Docker-compatible inspect payload produced by WSLC.
/// Unknown mount types are retained and unknown fields are ignored without making the payload unsupported.
/// </summary>
internal static class ContainerMountDetailsParser
{
    public static bool TryParse(string inspectOutput, out ContainerMountDetails details)
    {
        details = new ContainerMountDetails([]);
        if (string.IsNullOrWhiteSpace(inspectOutput)) return false;

        try
        {
            using var document = JsonDocument.Parse(inspectOutput);
            var root = GetInspectRoot(document.RootElement);
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetPropertyIgnoreCase("Mounts", out var mounts) ||
                mounts.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var parsedMounts = new List<ContainerMount>();
            foreach (var mount in mounts.EnumerateArray())
            {
                if (mount.ValueKind != JsonValueKind.Object) continue;

                parsedMounts.Add(new ContainerMount(
                    mount.ReadString("Type"),
                    mount.ReadString("Source"),
                    mount.ReadString("Destination"),
                    ReadNullableBoolean(mount, "ReadWrite")));
            }

            details = new ContainerMountDetails(parsedMounts);
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
}
