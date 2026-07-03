using Microsoft.WSL.Containers;
using ExWSLC.Models;

namespace ExWSLC.Services;

public sealed class RuntimeCapabilityService(IProcessRunner processRunner) : IRuntimeCapabilityService
{
    public async Task<RuntimeCapabilities> DetectAsync(CancellationToken cancellationToken = default)
    {
        var cli = await processRunner.ExecuteAsync("wslc.exe", ["version"], cancellationToken: cancellationToken);
        if (!cli.Success)
        {
            return RuntimeCapabilities.Unavailable(string.IsNullOrWhiteSpace(cli.Error)
                ? "wslc.exe is not available. Update WSL to continue."
                : cli.Error);
        }

        try
        {
            var missing = WslcService.GetMissingComponents().Select(component => component.ToString()).ToArray();
            var version = WslcService.GetVersion();
            var sdkVersion = $"{version.Major}.{version.Minor}.{version.Revision}";
            return new RuntimeCapabilities(
                missing.Length == 0,
                FirstNonEmptyLine(cli.Output),
                sdkVersion,
                missing,
                missing.Length == 0 ? "WSL Container is ready." : $"Missing: {string.Join(", ", missing)}");
        }
        catch (Exception exception)
        {
            return new RuntimeCapabilities(true, FirstNonEmptyLine(cli.Output), "Preview API unavailable", [], exception.Message);
        }
    }

    public async Task InstallMissingComponentsAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var operation = WslcService.InstallWithDependenciesAsync();
        operation.Progress = (_, value) => progress?.Report($"{value.Component}: {value.Progress}/{value.Total}");
        await operation;
    }

    private static string FirstNonEmptyLine(string value) =>
        value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? value;
}
