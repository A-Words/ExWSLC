using CommunityToolkit.Mvvm.ComponentModel;
using ExWSLC.Services;

namespace ExWSLC.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    public MainViewModel(
        IContainerRuntime runtime,
        IRuntimeCapabilityService capabilityService,
        ISettingsService settingsService,
        ITaskService taskService,
        IUserInteractionService interaction)
    {
        Workspace = new RuntimeWorkspace(runtime, capabilityService, settingsService, taskService, interaction);
        Containers = new ContainersViewModel(Workspace);
        ImagesPage = new ImagesPageViewModel(Workspace);
        ResourcesPage = new ResourcesPageViewModel(Workspace);
        TasksPage = new TasksPageViewModel(Workspace);
        SettingsPage = new SettingsPageViewModel(Workspace, ImagesPage);
        OverviewPage = new OverviewPageViewModel(Workspace, Containers);
    }

    public RuntimeWorkspace Workspace { get; }
    public OverviewPageViewModel OverviewPage { get; }
    public ContainersViewModel Containers { get; }
    public ImagesPageViewModel ImagesPage { get; }
    public ResourcesPageViewModel ResourcesPage { get; }
    public TasksPageViewModel TasksPage { get; }
    public SettingsPageViewModel SettingsPage { get; }

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
