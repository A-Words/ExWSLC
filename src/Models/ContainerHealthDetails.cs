namespace ExWSLC.Models;

public enum ContainerHealthStatus { Unknown, NotConfigured, Starting, Healthy, Unhealthy }
public enum HealthCheckMode { Inherit, Disabled, Custom }

public sealed record ContainerHealthDetails(
    ContainerHealthStatus Status,
    int? FailingStreak,
    IReadOnlyList<ContainerHealthLog> Logs)
{
    public static ContainerHealthDetails Unknown { get; } = new(ContainerHealthStatus.Unknown, null, []);
    public bool HasLogs => Logs.Count > 0;
}

public sealed record ContainerHealthLog(string Start, string End, int? ExitCode, string Output);
