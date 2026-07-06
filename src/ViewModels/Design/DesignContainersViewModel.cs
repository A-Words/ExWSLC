namespace ExWSLC.ViewModels.Design;

public sealed class DesignContainersViewModel : ContainersViewModel
{
    public DesignContainersViewModel() : base(DesignWorkspaceFactory.CreateWorkspace())
    {
        SearchText = "api";
        SelectedContainer = VisibleContainerItems.FirstOrDefault()?.Container;
    }
}
