namespace ExWSLC.Models;

/// <summary>Null values inherit the container configuration. -1 waits indefinitely.</summary>
public sealed record ContainerStopOptions(int? TimeoutSeconds = null, string? Signal = null);
