using System.Windows;
using ExWSLC.Services;
using ExWSLC.ViewModels;

namespace ExWSLC;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var settings = new SettingsService();
        await settings.LoadAsync();
        LocalizationService.ApplyLanguage(settings.Current.Language);
        LocalizationService.ApplyTheme(settings.Current.Theme);

        var runner = new WslcProcessRunner();
        var runtime = new WslcContainerRuntime(runner);
        var viewModel = new MainViewModel(
            runtime,
            new RuntimeCapabilityService(runner),
            settings,
            new TaskService(),
            new UserInteractionService());

        MainWindow = new MainWindow(viewModel);
        MainWindow.Show();
    }
}
