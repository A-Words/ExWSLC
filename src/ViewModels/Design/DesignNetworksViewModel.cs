namespace ExWSLC.ViewModels.Design;

public sealed class DesignNetworksViewModel : NetworksViewModel
{
    public DesignNetworksViewModel() : base(DesignWorkspaceFactory.CreateWorkspace())
    {
        NetworkName = "dev-network";
        NetworkDriver = "bridge";
        NetworkSubnet = "172.30.0.0/24";
        NetworkGateway = "172.30.0.1";
        NetworkIpRange = "172.30.0.128/25";
        NetworkOptions = "com.example.mtu=1500";
        NetworkLabels = "environment=development";
        SelectedNetwork = Networks.FirstOrDefault();
    }
}
