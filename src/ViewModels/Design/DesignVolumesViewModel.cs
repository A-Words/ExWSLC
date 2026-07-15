namespace ExWSLC.ViewModels.Design;

public sealed class DesignVolumesViewModel : VolumesViewModel
{
    public DesignVolumesViewModel() : base(DesignWorkspaceFactory.CreateWorkspace())
    {
        VolumeName = "dev-volume";
        SelectedVolume = Volumes.FirstOrDefault();
    }
}
