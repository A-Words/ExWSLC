using CommunityToolkit.Mvvm.ComponentModel;
using ExWSLC.Services;

namespace ExWSLC.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    public MainViewModel(RuntimeWorkspace workspace)
    {
        Workspace = workspace;
        Containers = new ContainersViewModel(Workspace);
        ImagesPage = new ImagesViewModel(Workspace);
        NetworksPage = new NetworksViewModel(Workspace);
        VolumesPage = new VolumesViewModel(Workspace);
        SettingsPage = new SettingsViewModel(Workspace);
    }

    public RuntimeWorkspace Workspace { get; }
    public ContainersViewModel Containers { get; }
    public ImagesViewModel ImagesPage { get; }
    public NetworksViewModel NetworksPage { get; }
    public VolumesViewModel VolumesPage { get; }
    public SettingsViewModel SettingsPage { get; }

    public async Task InitializeAsync()
    {
        // Capture the token before awaiting: closing the window disposes the workspace's source.
        var cancellationToken = Workspace.Lifetime.Token;
        try
        {
            await Workspace.InitializeAsync();
        }
        catch (Exception exception)
        {
            if (cancellationToken.IsCancellationRequested) return;
            Workspace.Capabilities = Workspace.Capabilities with { MessageKey = "RuntimeDetectionFailed", MessageArguments = [] };
            Workspace.RefreshError = $"{LocalizationService.GetString("RuntimeDetectionFailed", "Environment detection failed")}: {exception.Message}";
            Workspace.StatusMessage = Workspace.RefreshError;
        }
    }

    public void ApplyConfiguredTheme()
    {
        LocalizationService.ApplyTheme(Workspace.SettingsService.Current.Theme);
    }

    public void RefreshSystemTheme()
    {
        if (string.Equals(Workspace.SettingsService.Current.Theme, "System", StringComparison.OrdinalIgnoreCase))
        {
            LocalizationService.ApplyTheme("System");
        }
    }

    public void Dispose() => Workspace.Dispose();
}
