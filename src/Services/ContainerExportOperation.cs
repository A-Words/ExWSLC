using ExWSLC.Models;

namespace ExWSLC.Services;

/// <summary>Keep temporary stop, export and restoration within one tracked operation.</summary>
internal static class ContainerExportOperation
{
    public static async Task<OperationResult> RunAsync(IContainerRuntime runtime, string id, string path, bool restore,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var stopRequested = false;
        string? failure = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (restore)
            {
                stopRequested = true;
                var stop = await runtime.StopContainerAsync(id, cancellationToken);
                if (!stop.Success) { failure = stop.CombinedOutput; return stop; }
            }
            cancellationToken.ThrowIfCancellationRequested();
            var export = await runtime.ExportContainerAsync(id, path, progress, cancellationToken);
            if (export.ExitCode == -2) throw new OperationCanceledException(cancellationToken);
            if (!export.Success) failure = export.CombinedOutput;
            return export;
        }
        catch (Exception exception) { failure = exception.Message; throw; }
        finally
        {
            if (stopRequested)
            {
                // Cancelling the export must not skip recovery while the application is running.
                // Never recreate a container removed by --rm or change its removal policy.
                using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try
                {
                    progress?.Report(LocalizationService.GetString("ExportOptionsRestoring", "Restoring container after export…"));
                    var start = await runtime.StartContainerAsync(id, recovery.Token);
                    if (!start.Success) throw new InvalidOperationException(start.CombinedOutput);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(LocalizationService.GetString("ExportOptionsRestoreFailed",
                        "Could not restore the container after export. Check its state; containers removed automatically cannot be restarted.")
                        + Environment.NewLine + failure + Environment.NewLine + exception.Message, exception);
                }
            }
        }
    }
}
