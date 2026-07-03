using System.Windows;
using ExWSLC.Services;
using ExWSLC.ViewModels;

namespace ExWSLC;

public partial class App : Application
{
    public static new App Current => (App)Application.Current;
    public MainViewModel ViewModel { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var settings = new SettingsService();
        await settings.LoadAsync();
        LocalizationService.ApplyLanguage(settings.Current.Language);

        var runner = new WslcProcessRunner();
        var runtime = new WslcContainerRuntime(runner);
        ViewModel = new MainViewModel(
            runtime,
            new RuntimeCapabilityService(runner),
            settings,
            new TaskService(),
            new UserInteractionService());

        MainWindow = new MainWindow(ViewModel);
        MainWindow.Show();
    }
}
