using ExWSLC.Helpers;

namespace ExWSLC.Models;

public sealed record ContainerSummary(
    string Id,
    string Name,
    string Image,
    string State,
    string Status,
    string Ports,
    string Created,
    ContainerHealthStatus HealthStatus = ContainerHealthStatus.Unknown)
{
    public bool IsRunning => State.Equals(ContainerState.Running, StringComparison.OrdinalIgnoreCase) ||
                             State == ContainerState.CodeRunning ||
                             Status.StartsWith("Up", StringComparison.OrdinalIgnoreCase);
    public string ShortId => Id.Length <= 12 ? Id : Id[..12];
    public string DisplayPorts => ContainerPortFormatter.Format(Ports);
}

public sealed class ContainerListItem
{
    private ContainerListPorts? _listPorts;

    public required ContainerSummary Container { get; init; }
    public ContainerStats? Stats { get; init; }
    public string Name => Container.Name;
    public string Image => Container.Image;
    public string ImageName => Image[(Image.LastIndexOf('/') + 1)..];
    public string ImageSource => Image.LastIndexOf('/') is var separator && separator >= 0
        ? Image[..separator] : string.Empty;
    public string Ports => Container.DisplayPorts;
    public ContainerListPorts ListPorts => _listPorts ??= ContainerPortFormatter.FormatList(Container.Ports);
    public bool IsRunning => Container.IsRunning;
    public ContainerListStatus StatusKind => IsRunning
        ? Container.HealthStatus switch
        {
            ContainerHealthStatus.NotConfigured => ContainerListStatus.RunningWithoutHealthCheck,
            ContainerHealthStatus.Healthy => ContainerListStatus.Healthy,
            ContainerHealthStatus.Unhealthy => ContainerListStatus.Unhealthy,
            ContainerHealthStatus.Starting => ContainerListStatus.Checking,
            _ => ContainerListStatus.HealthUnknown
        }
        : Container.State.ToLowerInvariant() switch
        {
            "created" or ContainerState.CodeCreated => ContainerListStatus.Created,
            "exited" or ContainerState.CodeExited => ContainerListStatus.Exited,
            "deleted" or ContainerState.CodeDeleted => ContainerListStatus.Deleted,
            "invalid" or ContainerState.CodeInvalid => ContainerListStatus.Invalid,
            "stopped" => ContainerListStatus.Stopped,
            _ => ContainerListStatus.Other
        };
    public string Cpu => string.IsNullOrWhiteSpace(Stats?.Cpu) ? "--" : Stats.Cpu;
    public string Memory => FormatUsedMemory(Stats?.Memory);

    private static string FormatUsedMemory(string? memory)
    {
        if (string.IsNullOrWhiteSpace(memory)) return "--";

        var separatorIndex = memory.IndexOf('/');
        return separatorIndex < 0 ? memory.Trim() : memory[..separatorIndex].Trim();
    }
}

public enum ContainerListStatus
{
    Other, Created, Exited, Deleted, Invalid, Stopped,
    RunningWithoutHealthCheck, Healthy, Unhealthy, Checking, HealthUnknown
}

public sealed record ContainerListPort(string Mapping, string BindingAddress, string FullText);

public sealed record ContainerListPorts(IReadOnlyList<ContainerListPort> Mappings)
{
    public string Summary => Mappings.Count == 0 ? "-" : Mappings[0].Mapping;
    public string BindingAddress => Mappings.Count == 0 ? string.Empty : Mappings[0].BindingAddress;
    public int AdditionalCount => Math.Max(0, Mappings.Count - 1);
    public bool HasAdditionalMappings => AdditionalCount > 0;
    public string FullText => Mappings.Count == 0 ? "-" : string.Join(Environment.NewLine, Mappings.Select(port => port.FullText));
}
