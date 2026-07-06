using ExWSLC.Models;
using ExWSLC.Services;

namespace ExWSLC.ViewModels.Design;

internal sealed class DesignRuntimeCapabilityService : IRuntimeCapabilityService
{
    public Task<RuntimeCapabilities> DetectAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new RuntimeCapabilities(true, "2.9.3", "2.9.3", [], "Design data ready"));

    public Task InstallMissingComponentsAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("Design install completed");
        return Task.CompletedTask;
    }
}
