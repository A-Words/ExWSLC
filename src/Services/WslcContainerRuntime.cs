using System.Diagnostics;
using System.IO;
using System.Text.Json;
using ExWSLC.Helpers;
using ExWSLC.Models;

namespace ExWSLC.Services;

public sealed class WslcContainerRuntime(IProcessRunner processRunner, IRuntimeCapabilityService? capabilityService = null) : IContainerRuntime
{
    private const string Executable = "wslc.exe";

    public async Task<IReadOnlyList<ContainerSummary>> GetContainersAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["container", "list", "--all", "--no-trunc", "--format", "json"], cancellationToken: cancellationToken);
        return ParseList(result, element => new ContainerSummary(
            ReadIdentifier(element, "Id", "ContainerId"),
            ReadListValue(element, "Name", "Names"),
            ReadListValue(element, "Image"),
            NormalizeContainerState(ReadListValue(element, "State")),
            ReadListValue(element, "Status"),
            ReadPorts(element),
            ReadListValue(element, "Created", "CreatedAt", "CreatedSince"),
            ContainerHealthParser.ReadListStatus(element)), "container list", "containers", cancellationToken);
    }

    public async Task<IReadOnlyList<ImageSummary>> GetImagesAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["image", "list", "--no-trunc", "--format", "json"], cancellationToken: cancellationToken);
        return ParseList(result, element => new ImageSummary(
            ReadIdentifier(element, "Id", "ImageId"),
            ReadListValue(element, "Repository", "Name"),
            ReadListValue(element, "Tag"),
            ReadListValue(element, "Size"),
            ReadListValue(element, "Created", "CreatedAt", "CreatedSince")), "image list", "images", cancellationToken);
    }

    public async Task<IReadOnlyList<NetworkSummary>> GetNetworksAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["network", "list", "--format", "json"], cancellationToken: cancellationToken);
        return ParseList(result, ParseNetworkSummary, "network list", "networks", cancellationToken);
    }

    public async Task<IReadOnlyList<VolumeSummary>> GetVolumesAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["volume", "list", "--format", "json"], cancellationToken: cancellationToken);
        return ParseList(result, ParseVolumeSummary, "volume list", "volumes", cancellationToken);
    }

    public async Task<IReadOnlyList<ContainerStats>> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["stats", "--all", "--no-trunc", "--format", "json"], cancellationToken: cancellationToken);
        return ParseList(result, element => new ContainerStats(
            ReadIdentifier(element, "Id", "ContainerId"),
            ReadListValue(element, "Name"),
            ReadListValue(element, "Cpu", "CpuPercent", "CPUPerc", "CPU %"),
            ReadListValue(element, "Memory", "MemUsage", "MemoryUsage"),
            ReadListValue(element, "NetworkIo", "NetIO", "Network I/O"),
            ReadListValue(element, "BlockIo", "Block I/O"),
            ReadListValue(element, "Pids")), "stats", "stats", cancellationToken);
    }

    public Task<OperationResult> StartContainerAsync(string id, CancellationToken cancellationToken = default) =>
        RunAsync(["container", "start", id], cancellationToken: cancellationToken);

    public Task<OperationResult> StopContainerAsync(string id, CancellationToken cancellationToken = default) =>
        StopContainerAsync(id, new ContainerStopOptions(), cancellationToken);

    public async Task<OperationResult> StopContainerAsync(string id, ContainerStopOptions options, CancellationToken cancellationToken = default)
    {
        var arguments = ContainerLaunchOptions.BuildStopArguments(id, options);
        var features = new List<RuntimeFeature>();
        if (options.TimeoutSeconds is not null) features.Add(RuntimeFeature.StopTimeout);
        if (options.Signal is not null) features.Add(RuntimeFeature.StopSignal);
        if (features.Count > 0)
            ContainerLaunchOptions.RequireSupport(capabilityService is null ? null : await capabilityService.DetectAsync(cancellationToken), features);
        cancellationToken.ThrowIfCancellationRequested();
        var result = await RunAsync(arguments, cancellationToken: cancellationToken);
        if (result.ExitCode == -2) throw new OperationCanceledException(cancellationToken);
        return result;
    }

    public Task<OperationResult> KillContainerAsync(string id, CancellationToken cancellationToken = default) =>
        RunAsync(["container", "kill", id], cancellationToken: cancellationToken);

    public async Task<OperationResult> RestartContainerAsync(string id, CancellationToken cancellationToken = default)
    {
        var stop = await StopContainerAsync(id, cancellationToken);
        return stop.Success ? await StartContainerAsync(id, cancellationToken) : stop;
    }

    public Task<OperationResult> RemoveContainerAsync(string id, bool force, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "container", "remove" };
        if (force) arguments.Add("--force");
        arguments.Add(id);
        return RunAsync(arguments, cancellationToken: cancellationToken);
    }

    public async Task<OperationResult> RunContainerAsync(ContainerCreateSpec spec, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var arguments = BuildRunArguments(spec);
        var required = ContainerLaunchOptions.RequiredFeatures(spec).ToArray();
        if (required.Length > 0)
            ContainerLaunchOptions.RequireSupport(capabilityService is null ? null : await capabilityService.DetectAsync(cancellationToken), required);
        if (spec.HealthMode != HealthCheckMode.Inherit)
        {
            var capabilities = capabilityService is null ? null : await capabilityService.DetectAsync(cancellationToken);
            HealthCheckOptions.RequireSupport(capabilities?[RuntimeFeature.HealthChecks].Support ?? CapabilitySupport.Unknown);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return await RunAsync(arguments, progress: progress, cancellationToken: cancellationToken);
    }

    public Task<OperationResult> ExportContainerAsync(string id, string path, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        RunAsync(["container", "export", id, "--output", path], progress: progress, cancellationToken: cancellationToken);

    public Task<OperationResult> InspectContainerAsync(string id, CancellationToken cancellationToken = default) =>
        RunAsync(["container", "inspect", id], cancellationToken: cancellationToken);

    public async Task<OperationResult> CopyContainerPathAsync(ContainerCopyRequest request, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var arguments = ContainerCopyOptions.BuildArguments(request);
        var capabilities = capabilityService is null ? null : await capabilityService.DetectAsync(cancellationToken);
        if (capabilities?[RuntimeFeature.ContainerCopy].Support != CapabilitySupport.Supported)
            throw new ArgumentException(ContainerCopyOptions.Text("CopyUnavailable", "File transfer is unavailable or unverified. Recheck runtime capabilities in Settings."));
        cancellationToken.ThrowIfCancellationRequested();
        var result = await RunAsync(arguments, progress: progress, cancellationToken: cancellationToken);
        // TaskService marks thrown cancellation as Cancelled, rather than Failed.
        if (result.ExitCode == -2) throw new OperationCanceledException(cancellationToken);
        return result;
    }

    public Task<OperationResult> FollowLogsAsync(string id, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        RunAsync(["container", "logs", "--follow", id], progress: progress, cancellationToken: cancellationToken);

    public Task<OperationResult> ExecAsync(string id, string command, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        RunAsync(["exec", id, "/bin/sh", "-lc", command], progress: progress, cancellationToken: cancellationToken);

    public Task<OperationResult> PullImageAsync(string image, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        RunAsync(["image", "pull", image], progress: progress, cancellationToken: cancellationToken);

    public async Task<OperationResult> BuildImageAsync(ImageBuildRequest request, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var arguments = ImageBuildOptions.BuildArguments(request);
        var redactor = await BuildOutputRedactor.CreateAsync(request.Secrets, cancellationToken);
        try
        {
            var result = await RunAsync(arguments, progress: new BuildProgress(progress, redactor), cancellationToken: cancellationToken);
            // The task service recognizes cancellation through an exception, not exit code -2.
            if (result.ExitCode == -2) throw new OperationCanceledException(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return result with
            {
                Output = redactor.Clean(result.Output), Error = redactor.Clean(result.Error),
                DisplayCommand = "wslc image build"
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) when (request.Secrets.Count > 0)
        {
            // An exception from a runner must not bypass output redaction.
            return new OperationResult(false, -1, string.Empty,
                LocalizationService.GetString("BuildSecretOperationFailed", "Build failed. Check secret sources and build options."), "wslc image build");
        }
    }

    private sealed class BuildProgress(IProgress<string>? target, BuildOutputRedactor redactor) : IProgress<string>
    {
        public void Report(string value) => target?.Report(redactor.Clean(value));
    }

    public Task<OperationResult> ImportImageAsync(string path, string name, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        RunAsync(["image", "import", path, name], progress: progress, cancellationToken: cancellationToken);

    public Task<OperationResult> LoadImageAsync(string path, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        RunAsync(["image", "load", "--input", path], progress: progress, cancellationToken: cancellationToken);

    public Task<OperationResult> SaveImageAsync(string image, string path, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        RunAsync(["image", "save", image, "--output", path], progress: progress, cancellationToken: cancellationToken);

    public Task<OperationResult> TagImageAsync(string image, string tag, CancellationToken cancellationToken = default) =>
        RunAsync(["image", "tag", image, tag], cancellationToken: cancellationToken);

    public Task<OperationResult> PushImageAsync(string image, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        RunAsync(["image", "push", image], progress: progress, cancellationToken: cancellationToken);

    public Task<OperationResult> RemoveImageAsync(string image, bool force, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "image", "remove" };
        if (force) arguments.Add("--force");
        arguments.Add(image);
        return RunAsync(arguments, cancellationToken: cancellationToken);
    }

    public Task<OperationResult> InspectImageAsync(string image, CancellationToken cancellationToken = default) =>
        RunAsync(["image", "inspect", image], cancellationToken: cancellationToken);

    public Task<OperationResult> PruneAsync(string resource, CancellationToken cancellationToken = default) =>
        RunAsync(BuildPruneArguments(resource), cancellationToken: cancellationToken);

    public async Task<OperationResult> CreateNetworkAsync(NetworkCreateSpec spec, CancellationToken cancellationToken = default)
    {
        var arguments = BuildCreateNetworkArguments(spec);
        var required = new List<RuntimeFeature>();
        if (spec.Subnet is not null) required.Add(RuntimeFeature.NetworkCreateSubnet);
        if (spec.Gateway is not null) required.Add(RuntimeFeature.NetworkCreateGateway);
        if (spec.IpRange is not null) required.Add(RuntimeFeature.NetworkCreateIpRange);
        if (required.Count > 0) await RequireNetworkSupportAsync(required, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return await RunAsync(arguments, cancellationToken: cancellationToken);
    }

    public async Task<OperationResult> ConnectNetworkAsync(NetworkConnectionSpec spec, CancellationToken cancellationToken = default)
    {
        var arguments = BuildConnectNetworkArguments(spec);
        var required = new List<RuntimeFeature> { RuntimeFeature.NetworkConnect };
        if (spec.Ipv4Address is not null) required.Add(RuntimeFeature.NetworkConnectIp);
        if (spec.Aliases?.Count > 0) required.Add(RuntimeFeature.NetworkConnectAlias);
        if (spec.DriverOptions?.Count > 0) required.Add(RuntimeFeature.NetworkConnectDriverOptions);
        await RequireNetworkSupportAsync(required, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return await RunAsync(arguments, cancellationToken: cancellationToken);
    }

    public async Task<OperationResult> DisconnectNetworkAsync(NetworkDisconnectionSpec spec, CancellationToken cancellationToken = default)
    {
        NetworkOptions.ValidateTarget(spec.ContainerId, spec.NetworkName);
        await RequireNetworkSupportAsync([RuntimeFeature.NetworkDisconnect], cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return await RunAsync(["network", "disconnect", spec.NetworkName, spec.ContainerId], cancellationToken: cancellationToken);
    }

    private async Task RequireNetworkSupportAsync(IReadOnlyList<RuntimeFeature> features, CancellationToken token)
    {
        var capabilities = capabilityService is null ? null : await capabilityService.DetectAsync(token);
        NetworkOptions.RequireSupport(capabilities, features.ToArray());
    }

    internal static IReadOnlyList<string> BuildConnectNetworkArguments(NetworkConnectionSpec spec)
    {
        NetworkOptions.Validate(spec);
        var arguments = new List<string> { "network", "connect" };
        AddOption(arguments, "--ip", spec.Ipv4Address ?? string.Empty);
        foreach (var alias in spec.Aliases ?? []) arguments.AddRange(["--network-alias", alias]);
        foreach (var option in spec.DriverOptions ?? []) arguments.AddRange(["--driver-opt", option]);
        arguments.AddRange([spec.NetworkName, spec.ContainerId]);
        return arguments;
    }

    public Task<OperationResult> RemoveNetworkAsync(string name, CancellationToken cancellationToken = default) =>
        RunAsync(["network", "remove", name], cancellationToken: cancellationToken);

    public Task<OperationResult> CreateVolumeAsync(VolumeCreateSpec spec, CancellationToken cancellationToken = default) =>
        RunAsync(BuildCreateVolumeArguments(spec), cancellationToken: cancellationToken);

    public Task<OperationResult> RemoveVolumeAsync(string name, bool force, CancellationToken cancellationToken = default) =>
        RunAsync(BuildRemoveVolumeArguments(name, force), cancellationToken: cancellationToken);

    public Task<OperationResult> PruneVolumesAsync(VolumePruneSpec spec, CancellationToken cancellationToken = default) =>
        RunAsync(BuildPruneVolumeArguments(spec), cancellationToken: cancellationToken);

    public Task<OperationResult> InspectResourceAsync(string resource, string name, CancellationToken cancellationToken = default) =>
        RunAsync([resource, "inspect", name], cancellationToken: cancellationToken);

    public Task<OperationResult> RegistryLoginAsync(string server, string username, string password, CancellationToken cancellationToken = default) =>
        processRunner.ExecuteAsync(Executable, ["registry", "login", server, "--username", username, "--password-stdin"], password, cancellationToken: cancellationToken);

    public void OpenInteractiveTerminal(string containerId)
    {
        Process.Start(BuildInteractiveTerminalStartInfo(containerId));
    }

    public void OpenNativeSettings() => Process.Start(new ProcessStartInfo(Executable, "settings") { UseShellExecute = true });

    public Task<OperationResult> ResetNativeSettingsAsync(CancellationToken cancellationToken = default) =>
        RunAsync(["settings", "reset"], cancellationToken: cancellationToken);

    internal static IReadOnlyList<string> BuildRunArguments(ContainerCreateSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Image)) throw new ArgumentException("Image is required.", nameof(spec));
        var arguments = new List<string> { "run", "--detach" };
        ContainerLaunchOptions.AddCreateArguments(spec, arguments);
        HealthCheckOptions.Validate(spec);
        if (spec.HealthMode == HealthCheckMode.Disabled) arguments.Add("--no-healthcheck");
        if (spec.HealthMode == HealthCheckMode.Custom)
        {
            AddOption(arguments, "--health-cmd", spec.HealthCommand ?? string.Empty);
            AddOption(arguments, "--health-interval", spec.HealthInterval ?? string.Empty);
            AddOption(arguments, "--health-timeout", spec.HealthTimeout ?? string.Empty);
            AddOption(arguments, "--health-start-period", spec.HealthStartPeriod ?? string.Empty);
            AddOption(arguments, "--health-retries", spec.HealthRetries ?? string.Empty);
        }
        AddOption(arguments, "--name", spec.Name);
        AddOption(arguments, "--cpus", spec.CpuLimit);
        AddOption(arguments, "--memory", spec.MemoryLimit);
        AddOption(arguments, "--network", spec.Network);
        AddOption(arguments, "--user", spec.User);
        AddOption(arguments, "--workdir", spec.WorkingDirectory);
        if (spec.UseAllGpus) arguments.AddRange(["--gpus", "all"]);
        if (spec.RemoveWhenStopped) arguments.Add("--rm");
        foreach (var pair in spec.Environment.Where(pair => !string.IsNullOrWhiteSpace(pair.Key)))
            arguments.AddRange(["--env", $"{pair.Key}={pair.Value}"]);
        foreach (var port in spec.Ports.Where(value => !string.IsNullOrWhiteSpace(value)))
            arguments.AddRange(["--publish", port]);
        foreach (var volume in spec.Volumes.Where(value => !string.IsNullOrWhiteSpace(value)))
            arguments.AddRange(["--volume", volume]);
        arguments.Add(spec.Image);
        if (!string.IsNullOrWhiteSpace(spec.Command)) arguments.AddRange(["/bin/sh", "-lc", spec.Command]);
        return arguments;
    }

    internal static IReadOnlyList<string> BuildCreateNetworkArguments(NetworkCreateSpec spec)
    {
        NetworkOptions.Validate(spec);

        var arguments = new List<string> { "network", "create" };
        AddOption(arguments, "--driver", spec.Driver);
        AddOption(arguments, "--subnet", spec.Subnet ?? string.Empty);
        AddOption(arguments, "--gateway", spec.Gateway ?? string.Empty);
        AddOption(arguments, "--ip-range", spec.IpRange ?? string.Empty);
        foreach (var option in spec.DriverOptions.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            arguments.AddRange(["--opt", option.Trim()]);
        }

        foreach (var label in spec.Labels.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            arguments.AddRange(["--label", label.Trim()]);
        }

        arguments.Add(spec.Name.Trim());
        return arguments;
    }

    internal static IReadOnlyList<string> BuildCreateVolumeArguments(VolumeCreateSpec spec)
    {
        var arguments = new List<string> { "volume", "create" };
        AddOption(arguments, "--driver", spec.Driver);
        foreach (var option in spec.DriverOptions.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            arguments.AddRange(["--opt", option.Trim()]);
        }

        foreach (var label in spec.Labels.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            arguments.AddRange(["--label", label.Trim()]);
        }

        if (!string.IsNullOrWhiteSpace(spec.Name)) arguments.Add(spec.Name.Trim());
        return arguments;
    }

    internal static IReadOnlyList<string> BuildRemoveVolumeArguments(string name, bool force)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Volume name is required.", nameof(name));
        var arguments = new List<string> { "volume", "remove" };
        if (force) arguments.Add("--force");
        arguments.Add(name.Trim());
        return arguments;
    }

    internal static IReadOnlyList<string> BuildPruneVolumeArguments(VolumePruneSpec spec)
    {
        var arguments = new List<string> { "volume", "prune" };
        if (spec.All) arguments.Add("--all");
        foreach (var filter in spec.Filters.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            arguments.AddRange(["--filter", filter.Trim()]);
        }

        return arguments;
    }

    internal static IReadOnlyList<string> BuildPruneArguments(string resource)
    {
        if (string.IsNullOrWhiteSpace(resource)) throw new ArgumentException("Resource is required.", nameof(resource));
        return [resource.Trim(), "prune"];
    }

    private static IReadOnlyList<T> ParseList<T>(
        OperationResult result,
        Func<JsonElement, T> selector,
        string operation,
        string collectionName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (result.ExitCode == -2)
        {
            throw new OperationCanceledException($"WSLC {operation} was cancelled.", cancellationToken);
        }

        if (!result.Success || result.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(result.Error) ? string.Empty : $" {result.Error.Trim()}";
            throw new InvalidOperationException($"WSLC {operation} failed with exit code {result.ExitCode}.{detail}");
        }

        // Since WSL 2.9.8 an empty list prints nothing. Never treat failed commands as empty lists.
        if (string.IsNullOrWhiteSpace(result.Output)) return [];

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(result.Output);
        }
        catch (JsonException)
        {
            // A complete legacy document is tried first so pretty-printed arrays/objects remain valid.
            // In the new format each non-empty line must be one complete object.
            return ParseJsonLines(result.Output, selector, operation, cancellationToken);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                !new[] { "Id", "ContainerId", "ImageId", "NetworkId", "Name", "Names" }
                    .Any(name => root.TryGetPropertyIgnoreCase(name, out _)))
            {
                foreach (var name in new[] { collectionName, "items", "data" })
                {
                    if (!root.TryGetPropertyIgnoreCase(name, out var collection)) continue;
                    if (collection.ValueKind != JsonValueKind.Array)
                        throw new InvalidOperationException($"WSLC {operation} returned a non-array '{name}' collection.");
                    root = collection;
                    break;
                }
            }

            if (root.ValueKind != JsonValueKind.Array)
                return [ParseListRecord(root, selector, operation, "record 1")];

            var records = new List<T>();
            foreach (var element in root.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                records.Add(ParseListRecord(element, selector, operation, $"record {records.Count + 1}"));
            }
            return records;
        }
    }

    private static IReadOnlyList<T> ParseJsonLines<T>(
        string output,
        Func<JsonElement, T> selector,
        string operation,
        CancellationToken cancellationToken)
    {
        var records = new List<T>();
        using var reader = new StringReader(output);
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var document = JsonDocument.Parse(line);
                records.Add(ParseListRecord(document.RootElement, selector, operation, $"line {lineNumber}"));
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException($"WSLC {operation} returned invalid JSON at line {lineNumber}.", exception);
            }
        }
        return records;
    }

    private static T ParseListRecord<T>(JsonElement element, Func<JsonElement, T> selector, string operation, string location)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"WSLC {operation} returned a non-object at {location}.");
        try
        {
            return selector(element);
        }
        catch (FormatException exception)
        {
            // Report only the field and location; inventory JSON can contain user data.
            throw new InvalidOperationException($"WSLC {operation} returned an invalid {location}: {exception.Message}", exception);
        }
    }

    private static string ReadIdentifier(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetPropertyIgnoreCase(name, out var value) || value.ValueKind == JsonValueKind.Null) continue;
            if (value.ValueKind != JsonValueKind.String) throw new FormatException($"'{name}' must be a string.");
            if (!string.IsNullOrWhiteSpace(value.GetString())) return value.GetString()!;
        }
        throw new FormatException($"Missing non-empty '{names[0]}' identifier.");
    }

    private static string ReadListValue(JsonElement element, params string[] names)
    {
        // Prefer the requested alias order, independent of property order in the CLI JSON.
        foreach (var name in names)
        {
            if (!element.TryGetPropertyIgnoreCase(name, out var value) || value.ValueKind == JsonValueKind.Null) continue;
            if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
                throw new FormatException($"'{name}' must be a string or number.");
            var text = value.ToString();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        return string.Empty;
    }

    private static string ReadPorts(JsonElement element)
    {
        if (!element.TryGetPropertyIgnoreCase("Ports", out var ports) || ports.ValueKind == JsonValueKind.Null) return string.Empty;
        if (ports.ValueKind is not (JsonValueKind.String or JsonValueKind.Array or JsonValueKind.Object))
            throw new FormatException("'Ports' must be a string, array or object.");
        return ports.ToString();
    }

    internal static string NormalizeContainerState(string state) => state switch
    {
        ContainerState.CodeInvalid => ContainerState.Invalid,
        ContainerState.CodeCreated => ContainerState.Created,
        ContainerState.CodeRunning => ContainerState.Running,
        ContainerState.CodeExited => ContainerState.Exited,
        ContainerState.CodeDeleted => ContainerState.Deleted,
        _ => state
    };

    internal static NetworkSummary ParseNetworkSummary(JsonElement element)
    {
        var subnet = ReadListValue(element, "Subnet");
        var gateway = ReadListValue(element, "Gateway");

        if (element.TryGetPropertyIgnoreCase("IPAM", out var ipam) && ipam.ValueKind != JsonValueKind.Null)
        {
            if (ipam.ValueKind != JsonValueKind.Object) throw new FormatException("'IPAM' must be an object.");
            subnet = FirstNonEmpty(subnet, ReadListValue(ipam, "Subnet"));
            gateway = FirstNonEmpty(gateway, ReadListValue(ipam, "Gateway"));

            if (ipam.TryGetPropertyIgnoreCase("Config", out var config) && config.ValueKind == JsonValueKind.Array)
            {
                var firstConfiguration = config.EnumerateArray()
                    .FirstOrDefault(item => item.ValueKind == JsonValueKind.Object);
                subnet = FirstNonEmpty(subnet, ReadListValue(firstConfiguration, "Subnet"));
                gateway = FirstNonEmpty(gateway, ReadListValue(firstConfiguration, "Gateway"));
            }
        }

        return new NetworkSummary(
            ReadListValue(element, "Id", "NetworkId"),
            ReadIdentifier(element, "Name"),
            ReadListValue(element, "Driver"),
            ReadListValue(element, "Scope"),
            subnet,
            gateway);
    }

    internal static VolumeSummary ParseVolumeSummary(JsonElement element) => new(
        ReadIdentifier(element, "Name"),
        ReadListValue(element, "Driver"),
        ReadListValue(element, "Mountpoint"),
        ReadListValue(element, "Size"));

    internal static ProcessStartInfo BuildInteractiveTerminalStartInfo(string containerId, string? executablePath = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "wt.exe",
            UseShellExecute = true
        };
        startInfo.ArgumentList.Add(string.IsNullOrWhiteSpace(executablePath) ? ResolveExecutablePath(Executable) : executablePath);
        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add("--interactive");
        startInfo.ArgumentList.Add("--tty");
        startInfo.ArgumentList.Add(containerId);
        startInfo.ArgumentList.Add("/bin/sh");
        return startInfo;
    }

    private Task<OperationResult> RunAsync(IReadOnlyList<string> arguments, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        processRunner.ExecuteAsync(Executable, arguments, progress: progress, cancellationToken: cancellationToken);

    private static void AddOption(List<string> arguments, string option, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) arguments.AddRange([option, value]);
    }

    private static string FirstNonEmpty(string current, string fallback) =>
        string.IsNullOrWhiteSpace(current) ? fallback : current;

    private static string ResolveExecutablePath(string fileName)
    {
        if (Path.IsPathFullyQualified(fileName) && File.Exists(fileName)) return fileName;

        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var entry in pathEntries)
        {
            var candidate = Path.Combine(entry.Trim('"'), fileName);
            if (File.Exists(candidate)) return candidate;
        }

        return fileName;
    }
}
