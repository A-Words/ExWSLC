namespace ExWSLC.ViewModels.Design;

public sealed class DesignResourcesViewModel : ResourcesViewModel
{
    public DesignResourcesViewModel() : base(DesignWorkspaceFactory.CreateWorkspace())
    {
        NetworkName = "dev-network";
        VolumeName = "dev-volume";
        SelectedNetwork = Networks.FirstOrDefault();
        SelectedVolume = Volumes.FirstOrDefault();
    }
}
