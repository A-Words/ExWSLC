namespace ExWSLC.ViewModels.Design;

public sealed class DesignSettingsViewModel : SettingsViewModel
{
    public DesignSettingsViewModel() : base(DesignWorkspaceFactory.CreateWorkspace())
    {
        RegistryUsername = "developer";
        Diagnostics = new DesignContainerRuntime().GetSystemInfoAsync(Workspace.Capabilities).GetAwaiter().GetResult();
        HostLoopbackConfiguration = Models.HostLoopbackConfiguration.Default;
    }
}
