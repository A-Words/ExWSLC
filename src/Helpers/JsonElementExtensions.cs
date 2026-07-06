using System.Text.Json;

namespace ExWSLC.Helpers;

internal static class JsonElementExtensions
{
    public static string ReadString(this JsonElement element, params string[] names)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Any(name => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            return property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
                _ => property.Value.ToString()
            };
        }

        return string.Empty;
    }

    public static int ReadInt(this JsonElement element, params string[] names)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Any(name => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var number))
            {
                return number;
            }

            if (property.Value.ValueKind == JsonValueKind.String &&
                int.TryParse(property.Value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return 0;
    }

    public static bool TryGetPropertyIgnoreCase(this JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
