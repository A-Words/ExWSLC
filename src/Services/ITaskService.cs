using ExWSLC.Models;

namespace ExWSLC.Services;

public interface ITaskService
{
    IReadOnlyList<RuntimeTaskItem> Tasks { get; }
    event EventHandler? TasksChanged;
    Task<OperationResult> RunAsync(string title, Func<IProgress<string>, CancellationToken, Task<OperationResult>> operation, CancellationToken cancellationToken = default);
    void ClearCompleted();
}
