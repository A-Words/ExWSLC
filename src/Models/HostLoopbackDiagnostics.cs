namespace ExWSLC.Models;

// Support describes the native configuration only, never an existing session's behavior.
public sealed record HostLoopbackConfiguration(CapabilitySupport Support, string HostName, string ReasonKey)
{
    public const string DefaultHostName = "host.wslc.internal";
    public static HostLoopbackConfiguration Unknown { get; } = new(CapabilitySupport.Unknown, string.Empty, "HostConfigUnknown");
    public static HostLoopbackConfiguration Default { get; } = new(CapabilitySupport.Unknown, DefaultHostName, "HostConfigDefault");
}

public sealed record HostLoopbackProbeRequest(string ContainerId, string HostName, int Port);

public enum HostLoopbackOutcome
{
    Connected,
    DnsFailed,
    TcpFailed,
    CapabilityDisabled,
    CapabilityUnknown,
    ContainerNotRunning,
    ContainerUnavailable,
    ToolsMissing,
    TimedOut,
    Cancelled,
    InvalidTarget,
    ConfigurationChanged,
    RuntimeFailed
}

public sealed record HostLoopbackProbeResult(
    HostLoopbackProbeRequest Target,
    DateTimeOffset CollectedAt,
    HostLoopbackOutcome Outcome,
    bool DnsSucceeded = false,
    string CliVersion = "");
