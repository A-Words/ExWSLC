using ExWSLC.Models;
using ExWSLC.Services;

namespace ExWSLC.ViewModels.Design;

internal sealed class DesignTaskService : ITaskService
{
    public IReadOnlyList<RuntimeTaskItem> Tasks => [];
    public event EventHandler? TasksChanged { add { } remove { } }
    public Task<OperationResult> RunAsync(string title, Func<IProgress<string>, CancellationToken, Task<OperationResult>> operation, CancellationToken cancellationToken = default) =>
        Task.FromResult(new OperationResult(true, 0, "Design operation completed.", string.Empty, title));
    public void ClearCompleted() { }
}
