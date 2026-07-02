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
}
