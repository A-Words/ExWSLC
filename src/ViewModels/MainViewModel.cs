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
        ResourcesPage = new ResourcesViewModel(Workspace);
        TasksPage = new TasksViewModel(Workspace);
        SettingsPage = new SettingsViewModel(Workspace);
        OverviewPage = new OverviewViewModel(Workspace, Containers);
    }

    public RuntimeWorkspace Workspace { get; }
    public OverviewViewModel OverviewPage { get; }
    public ContainersViewModel Containers { get; }
    public ImagesViewModel ImagesPage { get; }
    public ResourcesViewModel ResourcesPage { get; }
    public TasksViewModel TasksPage { get; }
    public SettingsViewModel SettingsPage { get; }

    public Task InitializeAsync() => Workspace.InitializeAsync();

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
