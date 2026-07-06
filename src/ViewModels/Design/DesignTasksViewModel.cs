namespace ExWSLC.ViewModels.Design;

public sealed class DesignTasksViewModel : TasksViewModel
{
    public DesignTasksViewModel() : base(DesignWorkspaceFactory.CreateWorkspace())
    {
    }
}
