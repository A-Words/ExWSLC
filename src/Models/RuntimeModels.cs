using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExWSLC.Models;

public sealed record ContainerSummary(
    string Id,
    string Name,
    string Image,
    string State,
    string Status,
    string Ports,
    string Created)
{
    public bool IsRunning => State.Equals("running", StringComparison.OrdinalIgnoreCase) ||
                             State == "2" ||
                             Status.StartsWith("Up", StringComparison.OrdinalIgnoreCase);
}

public sealed record ImageSummary(
    string Id,
    string Repository,
    string Tag,
    string Size,
    string Created)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Tag) || Tag == "<none>"
        ? Repository
        : $"{Repository}:{Tag}";
}

public sealed record NetworkSummary(string Id, string Name, string Driver, string Scope);

public sealed record VolumeSummary(string Name, string Driver, string Mountpoint, string Size);

public sealed record ContainerStats(
    string Id,
    string Name,
    string Cpu,
    string Memory,
    string NetworkIo,
    string BlockIo,
    string Pids);

public sealed class ContainerListItem
{
    public required ContainerSummary Container { get; init; }
    public ContainerStats? Stats { get; init; }
    public string Name => Container.Name;
    public string Image => Container.Image;
    public string Ports => FormatPorts(Container.Ports);
    public bool IsRunning => Container.IsRunning;
    public string Cpu => string.IsNullOrWhiteSpace(Stats?.Cpu) ? "--" : Stats.Cpu;
    public string Memory => FormatUsedMemory(Stats?.Memory);

    private static string FormatPorts(string ports)
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

    private static string FormatUsedMemory(string? memory)
    {
        if (string.IsNullOrWhiteSpace(memory)) return "--";

        var separatorIndex = memory.IndexOf('/');
        return separatorIndex < 0 ? memory.Trim() : memory[..separatorIndex].Trim();
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

public sealed record RuntimeCapabilities(
    bool IsAvailable,
    string CliVersion,
    string SdkVersion,
    IReadOnlyList<string> MissingComponents,
    string Message)
{
    public static RuntimeCapabilities Unavailable(string message) =>
        new(false, "Unavailable", "Unavailable", Array.Empty<string>(), message);
}

public sealed class ContainerCreateSpec
{
    public string Image { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string CpuLimit { get; set; } = string.Empty;
    public string MemoryLimit { get; set; } = string.Empty;
    public string Network { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public bool UseAllGpus { get; set; }
    public bool RemoveWhenStopped { get; set; }
    public List<KeyValuePair<string, string>> Environment { get; } = [];
    public List<string> Ports { get; } = [];
    public List<string> Volumes { get; } = [];
}

public sealed record OperationResult(
    bool Success,
    int ExitCode,
    string Output,
    string Error,
    string DisplayCommand)
{
    public string CombinedOutput => string.Join(Environment.NewLine,
        new[] { Output, Error }.Where(value => !string.IsNullOrWhiteSpace(value)));
}

public sealed class AppSettings
{
    public string Language { get; set; } = "zh-CN";
    public string Theme { get; set; } = "System";
    public int RefreshIntervalSeconds { get; set; } = 5;
}

public enum RuntimeTaskState
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled
}

public sealed class RuntimeTaskItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Title { get; init; }
    public RuntimeTaskState State { get; set; } = RuntimeTaskState.Queued;
    public string Detail { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

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
