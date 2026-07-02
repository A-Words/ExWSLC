using ExWSLC.Models;

namespace ExWSLC.Services;

public interface IProcessRunner
{
    Task<OperationResult> ExecuteAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? standardInput = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IContainerRuntime
{
    Task<IReadOnlyList<ContainerSummary>> GetContainersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ImageSummary>> GetImagesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NetworkSummary>> GetNetworksAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VolumeSummary>> GetVolumesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ContainerStats>> GetStatsAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> StartContainerAsync(string id, CancellationToken cancellationToken = default);
    Task<OperationResult> StopContainerAsync(string id, CancellationToken cancellationToken = default);
    Task<OperationResult> KillContainerAsync(string id, CancellationToken cancellationToken = default);
    Task<OperationResult> RestartContainerAsync(string id, CancellationToken cancellationToken = default);
    Task<OperationResult> RemoveContainerAsync(string id, bool force, CancellationToken cancellationToken = default);
    Task<OperationResult> RunContainerAsync(ContainerCreateSpec spec, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task<OperationResult> ExportContainerAsync(string id, string path, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task<OperationResult> InspectContainerAsync(string id, CancellationToken cancellationToken = default);
    Task<OperationResult> GetLogsAsync(string id, int tail = 300, CancellationToken cancellationToken = default);
    Task<OperationResult> FollowLogsAsync(string id, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task<OperationResult> ExecAsync(string id, string command, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task<OperationResult> PullImageAsync(string image, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task<OperationResult> BuildImageAsync(string path, string tag, string dockerfile, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task<OperationResult> ImportImageAsync(string path, string name, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task<OperationResult> LoadImageAsync(string path, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task<OperationResult> SaveImageAsync(string image, string path, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task<OperationResult> TagImageAsync(string image, string tag, CancellationToken cancellationToken = default);
    Task<OperationResult> PushImageAsync(string image, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task<OperationResult> RemoveImageAsync(string image, bool force, CancellationToken cancellationToken = default);
    Task<OperationResult> InspectImageAsync(string image, CancellationToken cancellationToken = default);
    Task<OperationResult> PruneAsync(string resource, CancellationToken cancellationToken = default);
    Task<OperationResult> CreateNetworkAsync(string name, CancellationToken cancellationToken = default);
    Task<OperationResult> RemoveNetworkAsync(string name, CancellationToken cancellationToken = default);
    Task<OperationResult> CreateVolumeAsync(string name, CancellationToken cancellationToken = default);
    Task<OperationResult> RemoveVolumeAsync(string name, CancellationToken cancellationToken = default);
    Task<OperationResult> InspectResourceAsync(string resource, string name, CancellationToken cancellationToken = default);
    Task<OperationResult> RegistryLoginAsync(string server, string username, string password, CancellationToken cancellationToken = default);
    void OpenInteractiveTerminal(string containerId);
    void OpenNativeSettings();
    Task<OperationResult> ResetNativeSettingsAsync(CancellationToken cancellationToken = default);
}

public interface IRuntimeCapabilityService
{
    Task<RuntimeCapabilities> DetectAsync(CancellationToken cancellationToken = default);
    Task InstallMissingComponentsAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}

public interface ISettingsService
{
    AppSettings Current { get; }
    Task LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(CancellationToken cancellationToken = default);
}

public interface ITaskService
{
    IReadOnlyList<RuntimeTaskItem> Tasks { get; }
    event EventHandler? TasksChanged;
    Task<OperationResult> RunAsync(string title, Func<IProgress<string>, CancellationToken, Task<OperationResult>> operation, CancellationToken cancellationToken = default);
    void ClearCompleted();
}

public interface IUserInteractionService
{
    bool Confirm(string title, string message);
    void ShowError(string title, string message);
    string? PickOpenFile(string title, string filter);
    string? PickSaveFile(string title, string filter, string defaultName);
    string? PickFolder(string title);
}
