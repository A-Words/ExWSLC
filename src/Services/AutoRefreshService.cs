namespace ExWSLC.Services;

public sealed class AutoRefreshService
{
    private readonly Func<Task> _refreshAction;
    private readonly Func<bool> _canRefresh;
    private readonly Func<int> _intervalProvider;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private bool _requested;
    private int _refreshing;

    public AutoRefreshService(Func<Task> refreshAction, Func<bool> canRefresh, Func<int> intervalProvider)
    {
        _refreshAction = refreshAction;
        _canRefresh = canRefresh;
        _intervalProvider = intervalProvider;
    }

    // All state notifications come from the owning UI context. A single queued
    // request survives a busy task or a minimized window without starting a timer per event.
    public void RequestRefresh()
    {
        _requested = true;
        NotifyEligibilityChanged();
    }

    public void NotifyEligibilityChanged()
    {
        if (_requested && _wake.CurrentCount == 0) _wake.Release();
    }

    public void AcknowledgeRefresh() => _requested = false;

    // Controllable scheduling entry point: tests advance a tick without wall-clock waits.
    public async Task TickAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0) return;
        try
        {
            if (!_canRefresh()) return;
            _requested = false;
            await _refreshAction();
        }
        finally { Volatile.Write(ref _refreshing, 0); }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var requested = await _wake.WaitAsync(
                    TimeSpan.FromSeconds(Math.Clamp(_intervalProvider(), 2, 300)),
                    cancellationToken);
                if (!requested || _requested) await TickAsync(cancellationToken);
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
