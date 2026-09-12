using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using ExWSLC.Models;

namespace ExWSLC.Helpers;

public static partial class RuntimeSystemInfoParser
{
    public static RuntimeSystemInfo Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException();
        var client = Property(root, "Client");
        var server = Property(root, "Server");
        if (client.ValueKind != JsonValueKind.Object && server.ValueKind != JsonValueKind.Object)
            throw new JsonException();

        var sessions = Property(server, "Sessions");
        return new RuntimeSystemInfo
        {
            ClientVersion = SafeVersion(Text(client, "Version")),
            ServiceVersion = SafeVersion(Text(server, "SessionManagerVersion")),
            WindowsVersion = SafeVersion(Text(client, "WindowsVersion")),
            KernelVersion = SafeVersion(Text(client, "KernelVersion")),
            Direct3DVersion = SafeVersion(Text(client, "Direct3DVersion")),
            DxCoreVersion = SafeVersion(Text(client, "DxCoreVersion")),
            SettingsFile = SafeSettingsPath(Text(client, "SettingsFile")),
            Sessions = sessions.ValueKind == JsonValueKind.Array
                ? Array.AsReadOnly(sessions.EnumerateArray().Select(session => new RuntimeSessionInfo(
                    SafeSessionName(Text(session, "Name")), Number(session, "ID"), Number(session, "CreatorPid"))).ToArray())
                : null
        };
    }

    public static string SafeVersion(string value) => VersionPattern().IsMatch(value) ? value : string.Empty;

    private static string SafeSettingsPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return value.Replace('/', '\\').Equals(Path.Combine(local, "wslc", "settings.yaml"), StringComparison.OrdinalIgnoreCase)
            ? @"%LOCALAPPDATA%\wslc\settings.yaml"
            : "[redacted]";
    }

    private static string SafeSessionName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        // Default CLI names contain the account name. Arbitrary user-defined names can
        // contain paths or secrets, so they are never copied verbatim into diagnostics.
        if (value.StartsWith("wslc-cli-", StringComparison.OrdinalIgnoreCase)) return "wslc-cli-[user]";
        return Guid.TryParse(value, out var id) ? id.ToString() : "[redacted]";
    }

    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    private static string Text(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() ?? string.Empty : string.Empty;

    private static uint? Number(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetUInt32(out var number) ? number : null;

    [GeneratedRegex(@"\A[0-9]+(?:\.[0-9]+){1,3}(?:-[0-9]+)*(?:\.[a-z]+(?:-[a-z]+)*)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
