using System.Text.Json;
using ExWSLC.Models;

namespace ExWSLC.Helpers;

internal static class ContainerHealthParser
{
    public const int MaxRecords = 10;
    public const int MaxOutputCharacters = 4096;

    public static ContainerHealthStatus ParseStatus(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "starting" => ContainerHealthStatus.Starting,
        "healthy" => ContainerHealthStatus.Healthy,
        "unhealthy" => ContainerHealthStatus.Unhealthy,
        "" or "none" => ContainerHealthStatus.NotConfigured,
        _ => ContainerHealthStatus.Unknown
    };

    public static ContainerHealthStatus ReadListStatus(JsonElement root)
    {
        if (!root.TryGetPropertyIgnoreCase("HealthStatus", out var value) || value.ValueKind != JsonValueKind.String)
            return ContainerHealthStatus.Unknown;
        var status = value.GetString();
        // CLI derives this field from Docker's status description. A stopped/created container
        // can have an image health check even though its list HealthStatus is empty.
        if (string.IsNullOrWhiteSpace(status) && !root.ReadString("State").Equals("running", StringComparison.OrdinalIgnoreCase))
            return ContainerHealthStatus.Unknown;
        return ParseStatus(status);
    }

    public static ContainerHealthDetails ReadInspect(JsonElement root)
    {
        var state = Object(root, "State");
        var health = Object(state, "Health");
        var status = ContainerHealthStatus.Unknown;
        if (health.ValueKind == JsonValueKind.Object)
        {
            var value = String(health, "Status");
            // Empty or omitted inspect status is inconclusive, unlike the explicit list field.
            if (!string.IsNullOrWhiteSpace(value)) status = ParseStatus(value);
        }
        else
        {
            var config = Object(root, "Config");
            if (config.TryGetPropertyIgnoreCase("Healthcheck", out var check))
            {
                if (check.ValueKind == JsonValueKind.Null) status = ContainerHealthStatus.NotConfigured;
                else if (check.TryGetPropertyIgnoreCase("Test", out var test) && test.ValueKind == JsonValueKind.Array &&
                         test.GetArrayLength() == 1 && test[0].ValueKind == JsonValueKind.String && test[0].GetString() == "NONE")
                    status = ContainerHealthStatus.NotConfigured;
            }
        }

        var logs = new List<ContainerHealthLog>();
        if (health.TryGetPropertyIgnoreCase("Log", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            // WSLC returns oldest first. Only materialize the bounded tail, newest first.
            for (var i = entries.GetArrayLength() - 1; i >= 0 && logs.Count < MaxRecords; i--)
            {
                var entry = entries[i];
                if (entry.ValueKind != JsonValueKind.Object) continue;
                logs.Add(new(String(entry, "Start"), String(entry, "End"), Integer(entry, "ExitCode"), String(entry, "Output")));
            }
        }
        var failures = Integer(health, "FailingStreak");
        return new(status, failures >= 0 ? failures : null, logs);
    }

    private static JsonElement Object(JsonElement root, string name) =>
        root.TryGetPropertyIgnoreCase(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : default;

    private static int? Integer(JsonElement root, string name) =>
        root.TryGetPropertyIgnoreCase(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number : null;

    private static string String(JsonElement root, string name)
    {
        var text = root.TryGetPropertyIgnoreCase(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : string.Empty;
        return text.Length <= MaxOutputCharacters ? text : text[..MaxOutputCharacters] + "…";
    }
}
