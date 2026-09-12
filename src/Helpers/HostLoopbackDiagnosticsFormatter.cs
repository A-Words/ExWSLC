using System.Globalization;
using ExWSLC.Models;
using ExWSLC.Services;

namespace ExWSLC.Helpers;

public static class HostLoopbackDiagnosticsFormatter
{
    public static string Text(string key) => LocalizationService.GetString(key, key);
    public static string Outcome(HostLoopbackProbeResult result) => Text($"HostProbe{result.Outcome}");
    public static string NextStep(HostLoopbackProbeResult result) => Text($"HostProbe{result.Outcome}Hint");
    public static string Target(HostLoopbackProbeResult result)
    {
        var id = HostLoopbackTargetValidator.IsContainerId(result.Target.ContainerId) ? result.Target.ContainerId : "[redacted]";
        var host = result.Target.HostName.Equals(HostLoopbackConfiguration.DefaultHostName, StringComparison.OrdinalIgnoreCase)
            ? HostLoopbackConfiguration.DefaultHostName : "[custom host]";
        return $"{id} → {host}:{result.Target.Port.ToString(CultureInfo.InvariantCulture)}";
    }

    public static string Summary(HostLoopbackProbeResult result) => string.Join(Environment.NewLine,
        "ExWSLC — " + Text("HostLoopbackTitle"),
        Text("HostProbeCopyPrivacy"),
        result.CollectedAt.ToString("u", CultureInfo.InvariantCulture),
        Target(result),
        "CLI: " + RuntimeDiagnosticsFormatter.Display(RuntimeSystemInfoParser.SafeVersion(result.CliVersion)),
        Text("HostProbeDnsLabel") + ": " + Text(result.DnsSucceeded ? "HostProbeDnsSucceeded" : "HostProbeDnsNotConfirmed"),
        Outcome(result), NextStep(result));
}
