using System.Diagnostics;
using System.Text.Json;
using ExWSLC.Models;

namespace ExWSLC.Services;

public sealed class WslcContainerRuntime(IProcessRunner processRunner) : IContainerRuntime
{
    private const string Executable = "wslc.exe";

    public async Task<IReadOnlyList<ContainerSummary>> GetContainersAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["container", "list", "--all", "--no-trunc", "--format", "json"], cancellationToken: cancellationToken);
        return ParseArray(result, element => new ContainerSummary(
            element.ReadString("Id", "ID", "ContainerId"),
            element.ReadString("Name", "Names"),
            element.ReadString("Image"),
            element.ReadString("State"),
            element.ReadString("Status"),
            element.ReadString("Ports"),
            element.ReadString("Created", "CreatedAt", "CreatedSince")));
    }

    public async Task<IReadOnlyList<ImageSummary>> GetImagesAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["image", "list", "--no-trunc", "--format", "json"], cancellationToken: cancellationToken);
        return ParseArray(result, element => new ImageSummary(
            element.ReadString("Id", "ID", "ImageId"),
            element.ReadString("Repository", "Name"),
            element.ReadString("Tag"),
            element.ReadString("Size"),
            element.ReadString("Created", "CreatedAt", "CreatedSince")));
    }

    public async Task<IReadOnlyList<NetworkSummary>> GetNetworksAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["network", "list", "--format", "json"], cancellationToken: cancellationToken);
        return ParseArray(result, element => new NetworkSummary(
            element.ReadString("Id", "ID", "NetworkId"),
            element.ReadString("Name"),
            element.ReadString("Driver"),
            element.ReadString("Scope")));
    }

    public async Task<IReadOnlyList<VolumeSummary>> GetVolumesAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["volume", "list", "--format", "json"], cancellationToken: cancellationToken);
        return ParseArray(result, element => new VolumeSummary(
            element.ReadString("Name"),
            element.ReadString("Driver"),
            element.ReadString("Mountpoint", "MountPoint"),
            element.ReadString("Size")));
    }

    public async Task<IReadOnlyList<ContainerStats>> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["stats", "--all", "--no-trunc", "--format", "json"], cancellationToken: cancellationToken);
        return ParseArray(result, element => new ContainerStats(
            element.ReadString("Id", "ID", "ContainerId"),
            element.ReadString("Name"),
            element.ReadString("Cpu", "CPU", "CpuPercent", "CPU %"),
            element.ReadString("Memory", "MemUsage", "MemoryUsage"),
            element.ReadString("NetworkIo", "NetIO", "Network I/O"),
            element.ReadString("BlockIo", "BlockIO", "Block I/O"),
            element.ReadString("Pids", "PIDs")));
    }

    public Task<OperationResult> StartContainerAsync(string id, CancellationToken cancellationToken = default) =>
        RunAsync(["container", "start", id], cancellationToken: cancellationToken);

    public Task<OperationResult> StopContainerAsync(string id, CancellationToken cancellationToken = default) =>
        RunAsync(["container", "stop", "--time", "10", id], cancellationToken: cancellationToken);

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

    public Task<OperationResult> RunContainerAsync(ContainerCreateSpec spec, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        RunAsync(BuildRunArguments(spec), progress: progress, cancellationToken: cancellationToken);

    public Task<OperationResult> ExportContainerAsync(string id, string path, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        RunAsync(["container", "export", id, "--output", path], progress: progress, cancellationToken: cancellationToken);

    public Task<OperationResult> InspectContainerAsync(string id, CancellationToken cancellationToken = default) =>
        RunAsync(["container", "inspect", id], cancellationToken: cancellationToken);

    public Task<OperationResult> GetLogsAsync(string id, int tail = 300, CancellationToken cancellationToken = default) =>
        RunAsync(["container", "logs", "--tail", tail.ToString(), "--timestamps", id], cancellationToken: cancellationToken);

    public Task<OperationResult> ExecAsync(string id, string command, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        RunAsync(["exec", id, "/bin/sh", "-lc", command], progress: progress, cancellationToken: cancellationToken);

    public Task<OperationResult> PullImageAsync(string image, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        RunAsync(["image", "pull", image], progress: progress, cancellationToken: cancellationToken);

    public Task<OperationResult> BuildImageAsync(string path, string tag, string dockerfile, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "image", "build", "--tag", tag };
        if (!string.IsNullOrWhiteSpace(dockerfile)) arguments.AddRange(["--file", dockerfile]);
        arguments.Add(path);
        return RunAsync(arguments, progress: progress, cancellationToken: cancellationToken);
    }

    public Task<OperationResult> ImportImageAsync(string path, string name, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        RunAsync(["image", "import", path, name], progress: progress, cancellationToken: cancellationToken);

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

    public Task<OperationResult> PruneAsync(string resource, CancellationToken cancellationToken = default) =>
        RunAsync([resource, "prune", "--force"], cancellationToken: cancellationToken);

    public Task<OperationResult> CreateNetworkAsync(string name, CancellationToken cancellationToken = default) =>
        RunAsync(["network", "create", name], cancellationToken: cancellationToken);

    public Task<OperationResult> RemoveNetworkAsync(string name, CancellationToken cancellationToken = default) =>
        RunAsync(["network", "remove", name], cancellationToken: cancellationToken);

    public Task<OperationResult> CreateVolumeAsync(string name, CancellationToken cancellationToken = default) =>
        RunAsync(["volume", "create", name], cancellationToken: cancellationToken);

    public Task<OperationResult> RemoveVolumeAsync(string name, CancellationToken cancellationToken = default) =>
        RunAsync(["volume", "remove", name], cancellationToken: cancellationToken);

    public Task<OperationResult> RegistryLoginAsync(string server, string username, string password, CancellationToken cancellationToken = default) =>
        processRunner.ExecuteAsync(Executable, ["registry", "login", server, "--username", username, "--password-stdin"], password, cancellationToken: cancellationToken);

    public void OpenInteractiveTerminal(string containerId)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "wt.exe",
            UseShellExecute = true,
            ArgumentList = { "wslc.exe", "exec", "--interactive", "--tty", containerId, "/bin/sh" }
        });
    }

    public void OpenNativeSettings() => Process.Start(new ProcessStartInfo(Executable, "settings") { UseShellExecute = true });

    internal static IReadOnlyList<string> BuildRunArguments(ContainerCreateSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Image)) throw new ArgumentException("Image is required.", nameof(spec));
        var arguments = new List<string> { "run", "--detach" };
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

    internal static IReadOnlyList<T> ParseArray<T>(OperationResult result, Func<JsonElement, T> selector)
    {
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output)) return [];
        try
        {
            using var document = JsonDocument.Parse(result.Output);
            var root = document.RootElement;
            var array = root.ValueKind == JsonValueKind.Array
                ? root
                : root.EnumerateObject().FirstOrDefault(property => property.Value.ValueKind == JsonValueKind.Array).Value;
            if (array.ValueKind != JsonValueKind.Array) return [];
            return array.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).Select(selector).ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private Task<OperationResult> RunAsync(IReadOnlyList<string> arguments, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        processRunner.ExecuteAsync(Executable, arguments, progress: progress, cancellationToken: cancellationToken);

    private static void AddOption(List<string> arguments, string option, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) arguments.AddRange([option, value]);
    }
}
