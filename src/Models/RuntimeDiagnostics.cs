namespace ExWSLC.Models;

// Only allowlisted, privacy-filtered values cross the diagnostic runtime boundary.
public sealed record RuntimeSessionInfo(string Name, uint? Id, uint? CreatorPid);

public sealed record RuntimeSystemInfo
{
    public string ClientVersion { get; init; } = string.Empty;
    public string ServiceVersion { get; init; } = string.Empty;
    public string WindowsVersion { get; init; } = string.Empty;
    public string KernelVersion { get; init; } = string.Empty;
    public string Direct3DVersion { get; init; } = string.Empty;
    public string DxCoreVersion { get; init; } = string.Empty;
    public string SettingsFile { get; init; } = string.Empty;
    // null means unavailable; an empty list means the query returned no sessions.
    public IReadOnlyList<RuntimeSessionInfo>? Sessions { get; init; }
}

public sealed record RuntimeDiagnostics
{
    public DateTimeOffset CollectedAt { get; init; } = DateTimeOffset.UtcNow;
    public RuntimeSystemInfo? SystemInfo { get; init; }
    public string BasicCliVersion { get; init; } = string.Empty;
    public string BasicServiceVersion { get; init; } = string.Empty;
    public string SdkPackageVersion { get; init; } = string.Empty;
    public string StatusKey { get; init; } = "DiagnosticsNotCollected";
    public string CapabilityReasonKey { get; init; } = string.Empty;
    public int? ExitCode { get; init; }
}
