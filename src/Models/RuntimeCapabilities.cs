namespace ExWSLC.Models;

using System.Reflection;

public enum CapabilitySupport
{
    Unknown,
    Supported,
    Unsupported
}

public enum RuntimeFeature
{
    HealthChecks,
    NetworkConnect,
    NetworkDisconnect,
    NetworkConnectIp,
    NetworkConnectAlias,
    ContainerCopy,
    BuildSecret,
    BuildOutput,
    BuildProgress,
    BuildPull,
    CreateMount,
    CreatePullPolicy,
    CreateStopTimeout,
    CreateStopSignal,
    CreateIp,
    CreateNetworkAlias,
    SystemInfo,
    HostLoopback,
    NativeRestart,
    Events,
    NetworkConnectDriverOptions,
    NetworkCreateSubnet,
    NetworkCreateGateway,
    NetworkCreateIpRange,
    CreateTmpfs,
    StopTimeout,
    StopSignal
}

public sealed record RuntimeFeatureCapability(CapabilitySupport Support, string ReasonKey, string Source)
{
    public static RuntimeFeatureCapability NotChecked { get; } =
        new(CapabilitySupport.Unknown, "CapabilityNotChecked", string.Empty);
}

public sealed record RuntimeCapabilities
{
    public static string BundledSdkPackageVersion { get; } = typeof(RuntimeCapabilities).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "WslContainersSdkPackageVersion")?.Value ?? string.Empty;

    public CapabilitySupport CliAvailability { get; init; }
    public CapabilitySupport SdkAvailability { get; init; }
    public CapabilitySupport ServiceAvailability { get; init; }
    public string CliVersion { get; init; } = string.Empty;
    public string ServiceVersion { get; init; } = string.Empty;
    public string SdkPackageVersion { get; init; } = BundledSdkPackageVersion;
    public IReadOnlyList<string> MissingComponents { get; init; } = [];
    public string MessageKey { get; init; } = "CapabilityNotChecked";
    public IReadOnlyList<string> MessageArguments { get; init; } = [];
    public IReadOnlyDictionary<RuntimeFeature, RuntimeFeatureCapability> Features { get; init; } =
        new Dictionary<RuntimeFeature, RuntimeFeatureCapability>().AsReadOnly();

    // An inconclusive probe must not prevent CLI inventory from reporting its own result.
    public bool IsAvailable => CliAvailability != CapabilitySupport.Unsupported &&
                               !MissingComponents.Any(IsInstallableComponent);
    public bool CanInstallComponents => SdkAvailability == CapabilitySupport.Supported &&
                                        MissingComponents.Any(IsInstallableComponent);
    public RuntimeFeatureCapability this[RuntimeFeature feature] =>
        Features.TryGetValue(feature, out var capability) ? capability : RuntimeFeatureCapability.NotChecked;

    public static bool IsInstallableComponent(string component) =>
        component is "WslPackage" or "VirtualMachinePlatform";
}
