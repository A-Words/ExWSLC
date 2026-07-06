namespace ExWSLC.ViewModels.Design;

public sealed class DesignSettingsViewModel : SettingsViewModel
{
    public DesignSettingsViewModel() : base(DesignWorkspaceFactory.CreateWorkspace())
    {
        RegistryUsername = "developer";
    }
}
