namespace ExWSLC.ViewModels.Design;

public sealed class DesignNetworksViewModel : NetworksViewModel
{
    public DesignNetworksViewModel() : base(DesignWorkspaceFactory.CreateWorkspace())
    {
        NetworkName = "dev-network";
        NetworkDriver = "bridge";
        NetworkOptions = "com.example.mtu=1500";
        NetworkLabels = "environment=development";
        SelectedNetwork = Networks.FirstOrDefault();
    }
}
