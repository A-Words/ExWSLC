using ExWSLC.Models;

namespace ExWSLC.Services;

public interface IRuntimeCapabilityService
{
    Task<RuntimeCapabilities> DetectAsync(CancellationToken cancellationToken = default);
    Task InstallMissingComponentsAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}
