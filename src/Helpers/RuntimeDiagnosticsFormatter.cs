using System.Globalization;
using System.Text;
using ExWSLC.Models;
using ExWSLC.Services;

namespace ExWSLC.Helpers;

public sealed record DiagnosticField(string Label, string Value);

public static class RuntimeDiagnosticsFormatter
{
    private static string Localize(string key) => LocalizationService.GetString(key, key);
    public static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? Localize("Unknown") : value;

    public static IReadOnlyList<DiagnosticField> Fields(RuntimeDiagnostics snapshot) =>
    [
        new(Localize("DiagnosticsCollectedAt"), snapshot.CollectedAt.ToString("u", CultureInfo.InvariantCulture)),
        new(Localize("DiagnosticsClientVersion"), Display(snapshot.SystemInfo?.ClientVersion)),
        new(Localize("DiagnosticsServiceVersion"), Display(snapshot.SystemInfo?.ServiceVersion)),
        new(Localize("DiagnosticsBasicCliVersion"), Display(snapshot.BasicCliVersion)),
        new(Localize("DiagnosticsBasicServiceVersion"), Display(snapshot.BasicServiceVersion)),
        new(Localize("BundledSdkPackage"), Display(snapshot.SdkPackageVersion)),
        new(Localize("DiagnosticsWindowsVersion"), Display(snapshot.SystemInfo?.WindowsVersion)),
        new(Localize("DiagnosticsKernelVersion"), Display(snapshot.SystemInfo?.KernelVersion)),
        new("Direct3D", Display(snapshot.SystemInfo?.Direct3DVersion)),
        new("DXCore", Display(snapshot.SystemInfo?.DxCoreVersion)),
        new(Localize("DiagnosticsSettingsPath"), Display(snapshot.SystemInfo?.SettingsFile))
    ];

    public static string Status(RuntimeDiagnostics snapshot) => Localize(snapshot.StatusKey) +
        (snapshot.ExitCode is { } code ? $" ({code.ToString(CultureInfo.InvariantCulture)})" : string.Empty) +
        (snapshot.StatusKey == "DiagnosticsUnsupported" ? $" {Localize(snapshot.CapabilityReasonKey)}" : string.Empty);

    public static string SessionsStatus(RuntimeDiagnostics snapshot) => snapshot.SystemInfo?.Sessions is { } sessions
        ? sessions.Count == 0 ? Localize("DiagnosticsNoSessions") : string.Empty
        : Localize("DiagnosticsSessionsUnavailable");

    public static string Summary(RuntimeDiagnostics snapshot)
    {
        var text = new StringBuilder();
        text.AppendLine("ExWSLC — " + Localize("DiagnosticsTitle"));
        text.AppendLine(Localize("DiagnosticsPrivacyHint"));
        text.AppendLine(Status(snapshot));
        foreach (var field in Fields(snapshot)) text.AppendLine($"{field.Label}: {field.Value}");
        text.AppendLine(Localize("DiagnosticsSessions"));
        text.AppendLine(SessionsStatus(snapshot));
        foreach (var session in snapshot.SystemInfo?.Sessions ?? [])
        {
            text.AppendLine($"{Localize("Name")}: {Display(session.Name)}; ID: {Display(session.Id?.ToString(CultureInfo.InvariantCulture))}; " +
                            $"{Localize("DiagnosticsCreatorPid")}: {Display(session.CreatorPid?.ToString(CultureInfo.InvariantCulture))}");
        }
        return text.ToString();
    }
}
