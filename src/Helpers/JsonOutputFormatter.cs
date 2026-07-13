using System.Text.Json;

namespace ExWSLC.Helpers;

internal static class JsonOutputFormatter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public static string Format(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        try
        {
            using var document = JsonDocument.Parse(value);
            return JsonSerializer.Serialize(document.RootElement, SerializerOptions);
        }
        catch (JsonException)
        {
            return value.Trim();
        }
    }
}
