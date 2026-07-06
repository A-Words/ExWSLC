using System.Windows;
using ExWSLC.Services;
using ExWSLC.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ExWSLC;

public partial class App : Application
{
    public static new App Current => (App)Application.Current;

    public IServiceProvider Services { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        var settings = Services.GetRequiredService<ISettingsService>();
        await settings.LoadAsync();
        LocalizationService.ApplyLanguage(settings.Current.Language);

        MainWindow = Services.GetRequiredService<MainWindow>();
        MainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Services
        services.AddSingleton<IProcessRunner, WslcProcessRunner>();
        services.AddSingleton<IContainerRuntime, WslcContainerRuntime>();
        services.AddSingleton<IRuntimeCapabilityService, RuntimeCapabilityService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ITaskService, TaskService>();
        services.AddSingleton<IUserInteractionService, UserInteractionService>();

        // ViewModels
        services.AddSingleton<RuntimeWorkspace>();
        services.AddSingleton<MainViewModel>();

        // Window
        services.AddSingleton<MainWindow>();
    }
}
