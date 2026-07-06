using ExWSLC.Models;
using ExWSLC.Services;

namespace ExWSLC.ViewModels.Design;

internal sealed class DesignSettingsService : ISettingsService
{
    public AppSettings Current { get; } = new() { Language = "zh-CN", Theme = "System", RefreshIntervalSeconds = 5 };
    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
