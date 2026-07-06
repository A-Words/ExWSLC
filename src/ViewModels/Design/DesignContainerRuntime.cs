using ExWSLC.Models;
using ExWSLC.Services;

namespace ExWSLC.ViewModels.Design;

internal sealed class DesignContainerRuntime : IContainerRuntime
{
    private static readonly OperationResult Success = new(true, 0, "Design operation completed.", string.Empty, "design");

    public Task<IReadOnlyList<ContainerSummary>> GetContainersAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ContainerSummary>>([]);
    public Task<IReadOnlyList<ImageSummary>> GetImagesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ImageSummary>>([]);
    public Task<IReadOnlyList<NetworkSummary>> GetNetworksAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<NetworkSummary>>([]);
    public Task<IReadOnlyList<VolumeSummary>> GetVolumesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<VolumeSummary>>([]);
    public Task<IReadOnlyList<ContainerStats>> GetStatsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ContainerStats>>([]);
    public Task<OperationResult> StartContainerAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    public Task<OperationResult> StopContainerAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    public Task<OperationResult> KillContainerAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    public Task<OperationResult> RestartContainerAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    public Task<OperationResult> RemoveContainerAsync(string id, bool force, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    public Task<OperationResult> RunContainerAsync(ContainerCreateSpec spec, IProgress<string>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    public Task<OperationResult> ExportContainerAsync(string id, string path, IProgress<string>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    public Task<OperationResult> InspectContainerAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    public Task<OperationResult> GetLogsAsync(string id, int tail = 300, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    public Task<OperationResult> FollowLogsAsync(string id, IProgress<string>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    public Task<OperationResult> ExecAsync(string id, string command, IProgress<string>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    public Task<OperationResult> PullImageAsync(string image, IProgress<string>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    public Task<OperationResult> BuildImageAsync(string path, string tag, string dockerfile, IProgress<string>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    public Task<OperationResult> ImportImageAsync(string path, string name, IProgress<string>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    public Task<OperationResult> LoadImageAsync(string path, IProgress<string>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    public Task<OperationResult> SaveImageAsync(string image, string path, IProgress<string>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    public Task<OperationResult> TagImageAsync(string image, string tag, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    public Task<OperationResult> PushImageAsync(string image, IProgress<string>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    public Task<OperationResult> RemoveImageAsync(string image, bool force, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    public Task<OperationResult> InspectImageAsync(string image, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    public Task<OperationResult> PruneAsync(string resource, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    public Task<OperationResult> CreateNetworkAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    public Task<OperationResult> RemoveNetworkAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    public Task<OperationResult> CreateVolumeAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    public Task<OperationResult> RemoveVolumeAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    public Task<OperationResult> InspectResourceAsync(string resource, string name, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    public Task<OperationResult> RegistryLoginAsync(string server, string username, string password, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    public void OpenInteractiveTerminal(string containerId) { }
    public void OpenNativeSettings() { }
    public Task<OperationResult> ResetNativeSettingsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Success);
}
