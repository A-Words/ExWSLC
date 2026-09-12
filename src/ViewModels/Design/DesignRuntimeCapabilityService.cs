using ExWSLC.Models;
using ExWSLC.Services;

namespace ExWSLC.ViewModels.Design;

internal sealed class DesignRuntimeCapabilityService : IRuntimeCapabilityService
{
    public Task<RuntimeCapabilities> DetectAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateCapabilities());

    public Task<RuntimeCapabilities> RefreshAsync(CancellationToken cancellationToken = default) => DetectAsync(cancellationToken);

    public static RuntimeCapabilities CreateCapabilities() => new()
    {
        CliAvailability = CapabilitySupport.Supported,
        SdkAvailability = CapabilitySupport.Supported,
        ServiceAvailability = CapabilitySupport.Supported,
        CliVersion = "2.9.10.0",
        ServiceVersion = "2.9.10",
        MessageKey = "RuntimeReady"
    };

    public Task InstallMissingComponentsAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("Design install completed");
        return Task.CompletedTask;
    }
}
