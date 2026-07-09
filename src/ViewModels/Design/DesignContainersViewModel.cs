using ExWSLC.Models;

namespace ExWSLC.ViewModels.Design;

public sealed class DesignContainersViewModel : ContainersViewModel
{
    public DesignContainersViewModel() : base(DesignWorkspaceFactory.CreateWorkspace())
    {
        IsDesignMode = true;
        SearchText = "api";
        SelectedContainer = VisibleContainerItems.FirstOrDefault()?.Container;

        LogLines.Add(new LogLine("2026-07-09 10:14:22 [info]  Starting nginx 1.27.0 (main process)"));
        LogLines.Add(new LogLine("2026-07-09 10:14:22 [info]  Loading configuration from /etc/nginx/nginx.conf"));
        LogLines.Add(new LogLine("2026-07-09 10:14:23 [info]  Listening on 0.0.0.0:8080"));
        LogLines.Add(new LogLine("2026-07-09 10:14:24 [warn]  Upstream 'cache' took 312ms to respond"));
        LogLines.Add(new LogLine("2026-07-09 10:14:25 [info]  GET /health 200 - 4 ms"));
        LogLines.Add(new LogLine("2026-07-09 10:14:26 [error] Upstream timed out (110: Connection timed out)"));
        LogLines.Add(new LogLine("2026-07-09 10:14:27 [info]  Retrying upstream 'cache' (attempt 2/3)"));
    }
}
