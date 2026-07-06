namespace ExWSLC.ViewModels.Design;

public sealed class DesignResourcesViewModel : ResourcesViewModel
{
    public DesignResourcesViewModel() : base(DesignWorkspaceFactory.CreateWorkspace())
    {
        ResourceName = "dev-network";
        SelectedNetwork = Networks.FirstOrDefault();
        SelectedVolume = Volumes.FirstOrDefault();
    }
}
