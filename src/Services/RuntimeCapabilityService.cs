using System.Text.Json;
using System.Text.RegularExpressions;
using ExWSLC.Models;

namespace ExWSLC.Services;

public sealed class RuntimeCapabilityService(
    IProcessRunner processRunner,
    IWslcSdkService sdk,
    TimeSpan? probeTimeout = null) : IRuntimeCapabilityService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _probeTimeout = probeTimeout ?? TimeSpan.FromSeconds(5);
    private RuntimeCapabilities? _cached;

    public Task<RuntimeCapabilities> DetectAsync(CancellationToken cancellationToken = default) =>
        DetectAsync(false, cancellationToken);

    public Task<RuntimeCapabilities> RefreshAsync(CancellationToken cancellationToken = default) =>
        DetectAsync(true, cancellationToken);

    private async Task<RuntimeCapabilities> DetectAsync(bool refresh, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (refresh) _cached = null;
            if (_cached is not null) return _cached;

            var result = await DetectCoreAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return _cached = result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task InstallMissingComponentsAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Installation can partially succeed even if it later fails or is cancelled.
            _cached = null;
            var missing = await Task.Run(sdk.GetMissingComponents, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (missing.Contains("SdkNeedsUpdate"))
            {
                throw new InvalidOperationException(LocalizationService.GetString(
                    "SdkUpdateRequired", "Update ExWSLC to obtain a compatible bundled SDK."));
            }

            var installable = missing.Where(RuntimeCapabilities.IsInstallableComponent).Distinct().ToArray();
            if (installable.Length == 0) return;
            await sdk.InstallMissingComponentsAsync(installable, progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<RuntimeCapabilities> DetectCoreAsync(CancellationToken cancellationToken)
    {
        var versionResult = await ProbeAsync(["version", "--format", "json"], cancellationToken);
        var results = new List<OperationResult> { versionResult };
        var cliVersion = versionResult.Success ? ParseJsonVersion(versionResult.Output) : string.Empty;
        if (cliVersion.Length == 0)
        {
            foreach (var arguments in new string[][] { ["version"], ["--version"] })
            {
                var fallback = await ProbeAsync(arguments, cancellationToken);
                results.Add(fallback);
                if (fallback.Success) cliVersion = ParseTextVersion(fallback.Output);
                if (cliVersion.Length != 0) break;
            }
        }

        // These are independent, read-only requests. Never call a feature command to probe it.
        var paths = new[] { "", "container", "container create", "container stop", "network", "network connect", "network create", "image build", "system" };
        var helpTasks = paths.Select(async path =>
        {
            var arguments = path.Split(' ', StringSplitOptions.RemoveEmptyEntries).Append("--help").ToArray();
            var result = await ProbeAsync(arguments, cancellationToken);
            return new HelpSnapshot(path, result);
        }).ToArray();
        var help = (await Task.WhenAll(helpTasks)).ToDictionary(value => value.Path);
        results.AddRange(help.Values.Select(value => value.Result));

        var sdkStatus = await Task.Run(() => ReadSdkStatus(cancellationToken), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var cliAvailability = results.Any(result => result.Success || result.ExitCode is not (-1 or -2))
            ? CapabilitySupport.Supported
            : results.All(result => result.ExitCode == -1)
                ? CapabilitySupport.Unsupported
                : CapabilitySupport.Unknown;

        var features = new Dictionary<RuntimeFeature, RuntimeFeatureCapability>
        {
            [RuntimeFeature.HealthChecks] = help["container create"].Options(
                "--health-cmd", "--health-interval", "--health-retries", "--health-start-period", "--health-timeout", "--no-healthcheck"),
            [RuntimeFeature.NetworkConnect] = help["network"].Command("connect"),
            [RuntimeFeature.NetworkDisconnect] = help["network"].Command("disconnect"),
            [RuntimeFeature.NetworkConnectIp] = WithParent(help["network"].Command("connect"), help["network connect"].Options("--ip")),
            [RuntimeFeature.NetworkConnectAlias] = WithParent(help["network"].Command("connect"), help["network connect"].Options("--network-alias")),
            [RuntimeFeature.NetworkConnectDriverOptions] = WithParent(help["network"].Command("connect"), help["network connect"].Options("--driver-opt")),
            [RuntimeFeature.NetworkCreateSubnet] = WithParent(help["network"].Command("create"), help["network create"].Options("--subnet")),
            [RuntimeFeature.NetworkCreateGateway] = WithParent(help["network"].Command("create"), help["network create"].Options("--gateway")),
            [RuntimeFeature.NetworkCreateIpRange] = WithParent(help["network"].Command("create"), help["network create"].Options("--ip-range")),
            [RuntimeFeature.ContainerCopy] = help["container"].Command("cp"),
            [RuntimeFeature.BuildSecret] = help["image build"].Options("--secret"),
            [RuntimeFeature.BuildOutput] = help["image build"].Options("--output"),
            [RuntimeFeature.BuildProgress] = help["image build"].Options("--progress"),
            [RuntimeFeature.BuildPull] = help["image build"].Options("--pull"),
            [RuntimeFeature.CreateMount] = help["container create"].Options("--mount"),
            [RuntimeFeature.CreatePullPolicy] = help["container create"].Options("--pull"),
            [RuntimeFeature.CreateStopTimeout] = help["container create"].Options("--stop-timeout"),
            [RuntimeFeature.CreateStopSignal] = help["container create"].Options("--stop-signal"),
            [RuntimeFeature.CreateTmpfs] = help["container create"].Options("--tmpfs"),
            [RuntimeFeature.StopTimeout] = help["container stop"].Options("--time"),
            [RuntimeFeature.StopSignal] = help["container stop"].Options("--signal"),
            [RuntimeFeature.CreateIp] = help["container create"].Options("--ip"),
            [RuntimeFeature.CreateNetworkAlias] = help["container create"].Options("--network-alias"),
            [RuntimeFeature.SystemInfo] = help["system"].Command("info"),
            // Neither a CLI version nor help proves configuration of an existing session.
            [RuntimeFeature.HostLoopback] = new(CapabilitySupport.Unknown, "HostLoopbackSessionUnknown", "session.hostLoopback"),
            // SDK 2.9.9 exposes neither native restart nor a global events stream.
            // These gates describe the CLI route; backend COM support is not a public entry point.
            [RuntimeFeature.NativeRestart] = help["container"].Command("restart"),
            [RuntimeFeature.Events] = Either(help[""].Command("events"), help["system"].Command("events"))
        };

        var messageKey = cliAvailability == CapabilitySupport.Unsupported
            ? "CliUnavailable"
            : sdkStatus.MissingComponents.Contains("SdkNeedsUpdate")
                ? "SdkUpdateRequired"
                : sdkStatus.MissingComponents.Count > 0
                    ? "MissingRuntimeComponents"
                    : sdkStatus.SdkAvailability == CapabilitySupport.Unsupported
                        ? "SdkUnavailable"
                        : cliVersion.Length == 0 || cliAvailability == CapabilitySupport.Unknown || sdkStatus.SdkAvailability == CapabilitySupport.Unknown ||
                      sdkStatus.ServiceAvailability == CapabilitySupport.Unknown
                            ? "RuntimeDetectionIncomplete"
                            : "RuntimeReady";

        return sdkStatus with
        {
            CliAvailability = cliAvailability,
            CliVersion = cliVersion,
            Features = features.AsReadOnly(),
            MessageKey = messageKey,
            MessageArguments = messageKey == "MissingRuntimeComponents"
                ? Array.AsReadOnly(new[] { string.Join(", ", sdkStatus.MissingComponents) })
                : []
        };
    }

    private RuntimeCapabilities ReadSdkStatus(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> missing;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            missing = Array.AsReadOnly(sdk.GetMissingComponents().ToArray());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is DllNotFoundException or System.IO.FileNotFoundException or
                                          BadImageFormatException or TypeLoadException)
        {
            return new RuntimeCapabilities { SdkAvailability = CapabilitySupport.Unsupported };
        }
        catch (Exception)
        {
            return new RuntimeCapabilities();
        }

        var status = new RuntimeCapabilities
        {
            MissingComponents = missing,
            SdkAvailability = missing.Contains("SdkNeedsUpdate") ? CapabilitySupport.Unsupported : CapabilitySupport.Supported
        };
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var version = sdk.GetServiceVersion();
            return status with
            {
                ServiceVersion = version,
                ServiceAvailability = string.IsNullOrWhiteSpace(version) ? CapabilitySupport.Unknown : CapabilitySupport.Supported
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            // The SDK can load while the service cannot answer (for example, missing WSL components).
            return status;
        }
    }

    private async Task<OperationResult> ProbeAsync(string[] arguments, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_probeTimeout);
        try
        {
            var result = await processRunner.ExecuteAsync("wslc.exe", arguments, cancellationToken: timeout.Token);
            cancellationToken.ThrowIfCancellationRequested();
            return timeout.IsCancellationRequested
                ? new OperationResult(false, -2, string.Empty, string.Empty, string.Empty)
                : result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new OperationResult(false, -2, string.Empty, string.Empty, string.Empty);
        }
    }

    private static string ParseJsonVersion(string output)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("Client", out var client) && client.ValueKind == JsonValueKind.Object &&
                client.TryGetProperty("Version", out var version) && version.ValueKind == JsonValueKind.String)
            {
                return NormalizeVersion(version.GetString());
            }
        }
        catch (JsonException) { }
        return string.Empty;
    }

    private static string ParseTextVersion(string output)
    {
        var match = Regex.Match(output, @"(?im)^\s*wslc\s+(\d+\.\d+\.\d+(?:\.\d+)?)\s*$", RegexOptions.CultureInvariant);
        return match.Success ? NormalizeVersion(match.Groups[1].Value) : string.Empty;
    }

    private static string NormalizeVersion(string? value) =>
        Version.TryParse(value, out var version) && version.Build >= 0 ? version.ToString() : string.Empty;

    private static RuntimeFeatureCapability WithParent(RuntimeFeatureCapability parent, RuntimeFeatureCapability option) =>
        parent.Support == CapabilitySupport.Supported ? option : parent;

    private static RuntimeFeatureCapability Either(RuntimeFeatureCapability first, RuntimeFeatureCapability second)
    {
        if (first.Support == CapabilitySupport.Supported) return first;
        if (second.Support == CapabilitySupport.Supported) return second;
        if (first.Support == CapabilitySupport.Unknown) return first;
        if (second.Support == CapabilitySupport.Unknown) return second;
        return first with { Source = $"{first.Source}; {second.Source}" };
    }

    private sealed class HelpSnapshot(string path, OperationResult result)
    {
        public string Path { get; } = path;
        public OperationResult Result { get; } = result;

        public RuntimeFeatureCapability Command(string command) =>
            Check([command], false);

        public RuntimeFeatureCapability Options(params string[] options) =>
            Check(options, true);

        private RuntimeFeatureCapability Check(string[] tokens, bool options)
        {
            var source = $"wslc{(Path.Length == 0 ? "" : $" {Path}")} --help";
            if (!Result.Success) return new(CapabilitySupport.Unknown, "CapabilityProbeFailed", source);

            var commandPath = Path.Length == 0 ? "" : $@"\s+{Regex.Escape(Path).Replace("\\ ", @"\s+")}";
            // Only accept recognizable command usage plus the help option. Headings may be localized.
            var usage = $@"(?m)^[^\r\n]*[:：]\s*wslc(?:\.exe)?{commandPath}\s+[\[<]";
            var optionTokens = Regex.Matches(Result.Output,
                    @"(?m)^[ \t]+(?:-[^\s-]\s+)?(?<token>--[a-z][a-z0-9-]*)(?=\s|$)", RegexOptions.CultureInvariant)
                .Select(match => match.Groups["token"].Value).ToHashSet(StringComparer.Ordinal);
            if (!Regex.IsMatch(Result.Output, usage, RegexOptions.CultureInvariant) || !optionTokens.Contains("--help"))
                return new(CapabilitySupport.Unknown, "CapabilityHelpUnrecognized", source);

            var found = options ? optionTokens : Regex.Matches(Result.Output,
                    @"(?m)^[ \t]{2,}(?<token>[a-z][a-z0-9-]*)[ \t]{2,}\S", RegexOptions.CultureInvariant)
                .Select(match => match.Groups["token"].Value).ToHashSet(StringComparer.Ordinal);
            return tokens.All(found.Contains)
                ? new(CapabilitySupport.Supported, "CapabilityAdvertised", source)
                : new(CapabilitySupport.Unsupported, "CapabilityNotAdvertised", source);
        }
    }
}
