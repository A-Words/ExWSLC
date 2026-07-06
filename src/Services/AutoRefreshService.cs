namespace ExWSLC.Services;

public sealed class AutoRefreshService
{
    private readonly Func<Task> _refreshAction;
    private readonly Func<bool> _canRefresh;
    private readonly Func<int> _intervalProvider;

    public AutoRefreshService(Func<Task> refreshAction, Func<bool> canRefresh, Func<int> intervalProvider)
    {
        _refreshAction = refreshAction;
        _canRefresh = canRefresh;
        _intervalProvider = intervalProvider;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(Math.Clamp(_intervalProvider(), 2, 300)),
                    cancellationToken);
                if (_canRefresh())
                {
                    await _refreshAction();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Swallow unexpected errors to keep the auto-refresh loop alive.
            }
        }
    }
}
