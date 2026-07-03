using ExWSLC.Models;

namespace ExWSLC.Services;

public sealed class TaskService : ITaskService
{
    private readonly List<RuntimeTaskItem> _tasks = [];
    private readonly object _gate = new();

    public IReadOnlyList<RuntimeTaskItem> Tasks
    {
        get
        {
            lock (_gate) return _tasks.ToArray();
        }
    }

    public event EventHandler? TasksChanged;

    public async Task<OperationResult> RunAsync(
        string title,
        Func<IProgress<string>, CancellationToken, Task<OperationResult>> operation,
        CancellationToken cancellationToken = default)
    {
        var item = new RuntimeTaskItem { Title = title, State = RuntimeTaskState.Running, StartedAt = DateTimeOffset.Now };
        lock (_gate) _tasks.Insert(0, item);
        RaiseChanged();
        var progress = new Progress<string>(line =>
        {
            item.Detail = line;
            RaiseChanged();
        });

        try
        {
            var result = await operation(progress, cancellationToken);
            item.State = result.Success ? RuntimeTaskState.Succeeded : RuntimeTaskState.Failed;
            item.Detail = result.Success ? "Completed" : result.CombinedOutput;
            return result;
        }
        catch (OperationCanceledException)
        {
            item.State = RuntimeTaskState.Cancelled;
            item.Detail = "Cancelled";
            throw;
        }
        catch (Exception exception)
        {
            item.State = RuntimeTaskState.Failed;
            item.Detail = exception.Message;
            return new OperationResult(false, -1, string.Empty, exception.Message, title);
        }
        finally
        {
            item.FinishedAt = DateTimeOffset.Now;
            RaiseChanged();
        }
    }

    public void ClearCompleted()
    {
        lock (_gate) _tasks.RemoveAll(item => item.State is not (RuntimeTaskState.Running or RuntimeTaskState.Queued));
        RaiseChanged();
    }

    private void RaiseChanged() => TasksChanged?.Invoke(this, EventArgs.Empty);
}
