namespace ExWSLC.Models;

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
