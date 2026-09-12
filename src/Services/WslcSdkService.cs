using Microsoft.WSL.Containers;

namespace ExWSLC.Services;

public sealed class WslcSdkService : IWslcSdkService
{
    public IReadOnlyList<string> GetMissingComponents() =>
        WslcService.GetMissingComponents().Select(component => component.ToString()).ToArray();

    public string GetServiceVersion()
    {
        var version = WslcService.GetVersion();
        return $"{version.Major}.{version.Minor}.{version.Revision}";
    }

    public async Task InstallMissingComponentsAsync(
        IReadOnlyList<string> components,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selected = components.Select(component => component switch
        {
            "VirtualMachinePlatform" => Component.VirtualMachinePlatform,
            "WslPackage" => Component.WslPackage,
            _ => throw new ArgumentException("Only missing Windows / WSL components can be installed.", nameof(components))
        }).ToArray();
        if (selected.Length == 0) return;

        var operation = WslcService.InstallWithDependenciesAsync(new InstallOptions
        {
            Components = selected,
            Repair = false
        });
        operation.Progress = (_, value) => progress?.Report($"{value.Component}: {value.Progress}/{value.Total}");
        // WinRT cancellation is a request; the underlying native installer may finish a step first.
        await operation.AsTask(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
    }
}
