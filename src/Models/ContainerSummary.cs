using ExWSLC.Helpers;

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
    public string ShortId => Id.Length <= 12 ? Id : Id[..12];
    public string DisplayPorts => ContainerPortFormatter.Format(Ports);
}

public sealed class ContainerListItem
{
    public required ContainerSummary Container { get; init; }
    public ContainerStats? Stats { get; init; }
    public string Name => Container.Name;
    public string Image => Container.Image;
    public string Ports => Container.DisplayPorts;
    public bool IsRunning => Container.IsRunning;
    public string Cpu => string.IsNullOrWhiteSpace(Stats?.Cpu) ? "--" : Stats.Cpu;
    public string Memory => FormatUsedMemory(Stats?.Memory);

    private static string FormatUsedMemory(string? memory)
    {
        if (string.IsNullOrWhiteSpace(memory)) return "--";

        var separatorIndex = memory.IndexOf('/');
        return separatorIndex < 0 ? memory.Trim() : memory[..separatorIndex].Trim();
    }
}
