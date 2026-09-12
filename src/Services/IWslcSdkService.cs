namespace ExWSLC.Services;

/// <summary>The small public SDK boundary; inventory remains owned by IContainerRuntime.</summary>
public interface IWslcSdkService
{
    IReadOnlyList<string> GetMissingComponents();
    string GetServiceVersion();
    Task InstallMissingComponentsAsync(
        IReadOnlyList<string> components,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
