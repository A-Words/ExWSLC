using ExWSLC.Models;
using ExWSLC.Services;

namespace ExWSLC.ViewModels.Design;

public sealed class DesignContainersPageViewModel : ContainersPageViewModel
{
    public DesignContainersPageViewModel() : base(DesignWorkspaceFactory.CreateWorkspace())
    {
        SearchText = "api";
        SelectedContainer = VisibleContainerItems.FirstOrDefault()?.Container;
    }
}

public sealed class DesignImagesPageViewModel : ImagesPageViewModel
{
    public DesignImagesPageViewModel() : base(DesignWorkspaceFactory.CreateWorkspace())
    {
        ImageSearchText = "ubuntu";
        SelectedImage = VisibleImages.FirstOrDefault();
    }
}

public sealed class DesignResourcesPageViewModel : ResourcesPageViewModel
{
    public DesignResourcesPageViewModel() : base(DesignWorkspaceFactory.CreateWorkspace())
    {
        ResourceName = "dev-network";
        SelectedNetwork = Networks.FirstOrDefault();
        SelectedVolume = Volumes.FirstOrDefault();
    }
}

public sealed class DesignTasksPageViewModel : TasksPageViewModel
{
    public DesignTasksPageViewModel() : base(DesignWorkspaceFactory.CreateWorkspace())
    {
    }
}

public sealed class DesignSettingsPageViewModel : SettingsPageViewModel
{
    public DesignSettingsPageViewModel() : base(DesignWorkspaceFactory.CreateWorkspace())
    {
        RegistryUsername = "developer";
    }
}

public sealed class DesignOverviewPageViewModel : OverviewPageViewModel
{
    public DesignOverviewPageViewModel()
        : this(DesignWorkspaceFactory.CreateWorkspace())
    {
    }

    private DesignOverviewPageViewModel(RuntimeWorkspace workspace)
        : base(workspace, new ContainersPageViewModel(workspace))
    {
    }
}

internal static class DesignWorkspaceFactory
{
    public static RuntimeWorkspace CreateWorkspace()
    {
        var workspace = new RuntimeWorkspace(
            new DesignContainerRuntime(),
            new DesignRuntimeCapabilityService(),
            new DesignSettingsService(),
            new DesignTaskService(),
            new DesignUserInteractionService())
        {
            Capabilities = new RuntimeCapabilities(true, "2.9.3", "2.9.3", [], "Design data ready"),
            StatusMessage = "Design data ready",
            DetailOutput = """
                $ wslc ps
                api-gateway     running    8080:80
                worker-cache    running
                """,
            ActiveTask = new RuntimeTaskItem
            {
                Title = "Pull ubuntu:latest",
                State = RuntimeTaskState.Running,
                Detail = "Downloading layer 3 of 5",
                StartedAt = DateTimeOffset.Now.AddMinutes(-2)
            }
        };

        RuntimeWorkspace.Replace(workspace.Containers, SampleContainers);
        RuntimeWorkspace.Replace(workspace.ActiveContainers, SampleContainers.Where(container => container.IsRunning));
        RuntimeWorkspace.Replace(workspace.Images, SampleImages);
        RuntimeWorkspace.Replace(workspace.Networks, SampleNetworks);
        RuntimeWorkspace.Replace(workspace.Volumes, SampleVolumes);
        RuntimeWorkspace.Replace(workspace.Stats, SampleStats);
        RuntimeWorkspace.Replace(workspace.Tasks, SampleTasks);
        RuntimeWorkspace.Replace(workspace.RecentTasks, SampleTasks.Take(3));
        return workspace;
    }

    private static readonly ContainerSummary[] SampleContainers =
    [
        new("1234567890abcdef", "api-gateway", "nginx:latest", "running", "Up 12 minutes", "8080:80", "now"),
        new("abcdef1234567890", "worker-cache", "redis:7", "running", "Up 8 minutes", string.Empty, "now"),
        new("fedcba0987654321", "demo-shell", "ubuntu:latest", "stopped", "Exited", string.Empty, "yesterday")
    ];

    private static readonly ImageSummary[] SampleImages =
    [
        new("sha256:ubuntu", "ubuntu", "latest", "78 MB", "2 days ago"),
        new("sha256:nginx", "nginx", "latest", "192 MB", "4 days ago"),
        new("sha256:redis", "redis", "7", "117 MB", "1 week ago")
    ];

    private static readonly NetworkSummary[] SampleNetworks =
    [
        new("net-bridge", "bridge", "nat", "local"),
        new("net-dev", "dev-network", "bridge", "local")
    ];

    private static readonly VolumeSummary[] SampleVolumes =
    [
        new("cache-data", "local", "/var/lib/wslc/volumes/cache-data", "256 MB"),
        new("logs", "local", "/var/lib/wslc/volumes/logs", "42 MB")
    ];

    private static readonly ContainerStats[] SampleStats =
    [
        new("1234567890abcdef", "api-gateway", "4.2%", "96 MiB / 8 GiB", "1.2 MB / 800 kB", "4 MB / 1 MB", "12"),
        new("abcdef1234567890", "worker-cache", "1.1%", "54 MiB / 8 GiB", "300 kB / 200 kB", "2 MB / 512 kB", "5")
    ];

    private static readonly RuntimeTaskItem[] SampleTasks =
    [
        new()
        {
            Title = "Pull ubuntu:latest",
            State = RuntimeTaskState.Running,
            Detail = "Downloading layer 3 of 5",
            StartedAt = DateTimeOffset.Now.AddMinutes(-2)
        },
        new()
        {
            Title = "Start api-gateway",
            State = RuntimeTaskState.Succeeded,
            Detail = "Completed",
            StartedAt = DateTimeOffset.Now.AddMinutes(-12),
            FinishedAt = DateTimeOffset.Now.AddMinutes(-11)
        },
        new()
        {
            Title = "Inspect bridge",
            State = RuntimeTaskState.Succeeded,
            Detail = "Completed",
            StartedAt = DateTimeOffset.Now.AddMinutes(-20),
            FinishedAt = DateTimeOffset.Now.AddMinutes(-20)
        }
    ];
}

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

internal sealed class DesignRuntimeCapabilityService : IRuntimeCapabilityService
{
    public Task<RuntimeCapabilities> DetectAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new RuntimeCapabilities(true, "2.9.3", "2.9.3", [], "Design data ready"));

    public Task InstallMissingComponentsAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("Design install completed");
        return Task.CompletedTask;
    }
}

internal sealed class DesignSettingsService : ISettingsService
{
    public AppSettings Current { get; } = new() { Language = "zh-CN", Theme = "System", RefreshIntervalSeconds = 5 };
    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class DesignTaskService : ITaskService
{
    public IReadOnlyList<RuntimeTaskItem> Tasks => [];
    public event EventHandler? TasksChanged { add { } remove { } }
    public Task<OperationResult> RunAsync(string title, Func<IProgress<string>, CancellationToken, Task<OperationResult>> operation, CancellationToken cancellationToken = default) =>
        Task.FromResult(new OperationResult(true, 0, "Design operation completed.", string.Empty, title));
    public void ClearCompleted() { }
}

internal sealed class DesignUserInteractionService : IUserInteractionService
{
    public bool Confirm(string title, string message) => true;
    public void ShowError(string title, string message) { }
    public string? PickOpenFile(string title, string filter) => null;
    public string? PickSaveFile(string title, string filter, string defaultName) => null;
    public string? PickFolder(string title) => null;
}
