using ExWSLC.Models;

namespace ExWSLC.ViewModels.Design;

public sealed class DesignContainersViewModel : ContainersViewModel
{
    public DesignContainersViewModel() : base(DesignWorkspaceFactory.CreateWorkspace())
    {
        IsDesignMode = true;
        SearchText = "api";
        SelectedContainer = VisibleContainerItems.FirstOrDefault()?.Container;
        NetworkDetails = new ContainerNetworkDetails(
            "bridge",
            "api-gateway",
            true,
            [
                new ContainerNetworkAttachment(
                    "bridge",
                    "network-8d3a1b7c",
                    "endpoint-4fa2c1de",
                    "172.20.0.2",
                    16,
                    "172.20.0.1",
                    "02:42:ac:14:00:02",
                    ["api-gateway"])
            ],
            [
                new ContainerPortBinding("127.0.0.1", "8080", "80", "tcp"),
                new ContainerPortBinding("0.0.0.0", "8443", "443", "tcp")
            ]);

        LogLines.Add(new LogLine("2026-07-09 10:14:22 [info]  Starting nginx 1.27.0 (main process)"));
        LogLines.Add(new LogLine("2026-07-09 10:14:22 [info]  Loading configuration from /etc/nginx/nginx.conf"));
        LogLines.Add(new LogLine("2026-07-09 10:14:23 [info]  Listening on 0.0.0.0:8080"));
        LogLines.Add(new LogLine("2026-07-09 10:14:24 [warn]  Upstream 'cache' took 312ms to respond"));
        LogLines.Add(new LogLine("2026-07-09 10:14:25 [info]  GET /health 200 - 4 ms"));
        LogLines.Add(new LogLine("2026-07-09 10:14:26 [error] Upstream timed out (110: Connection timed out)"));
        LogLines.Add(new LogLine("2026-07-09 10:14:27 [info]  Retrying upstream 'cache' (attempt 2/3)"));
    }
}
