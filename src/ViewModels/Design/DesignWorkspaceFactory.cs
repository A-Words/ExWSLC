using ExWSLC.Helpers;
using ExWSLC.Models;

namespace ExWSLC.ViewModels.Design;

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

        workspace.Containers.ReplaceAll(SampleContainers);
        workspace.ActiveContainers.ReplaceAll(SampleContainers.Where(container => container.IsRunning));
        workspace.Images.ReplaceAll(SampleImages);
        workspace.Networks.ReplaceAll(SampleNetworks);
        workspace.Volumes.ReplaceAll(SampleVolumes);
        workspace.Stats.ReplaceAll(SampleStats);
        workspace.Tasks.ReplaceAll(SampleTasks);
        workspace.RecentTasks.ReplaceAll(SampleTasks.Take(3));
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
        new("net-bridge", "bridge", "bridge", "local", "172.18.0.0/16", "172.18.0.1"),
        new("net-dev", "dev-network", "bridge", "local", "172.19.0.0/16", "172.19.0.1")
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
