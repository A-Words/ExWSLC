using ExWSLC.Models;

namespace ExWSLC.ViewModels.Design;

public sealed class DesignOverviewViewModel : OverviewViewModel
{
    public DesignOverviewViewModel()
        : this(DesignWorkspaceFactory.CreateWorkspace())
    {
    }

    private DesignOverviewViewModel(RuntimeWorkspace workspace)
        : base(workspace, new ContainersViewModel(workspace))
    {
    }
}
