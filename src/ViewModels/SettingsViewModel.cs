using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExWSLC.Models;
using ExWSLC.Services;
using System.ComponentModel;

namespace ExWSLC.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private const string DefaultRegistryServer = "docker.io";

    public SettingsViewModel(RuntimeWorkspace workspace, ImagesViewModel? imagesPage = null)
    {
        Workspace = workspace;
        ImagesPage = imagesPage;
        Workspace.PropertyChanged += OnWorkspacePropertyChanged;
        SelectedLanguage = Workspace.SettingsService.Current.Language;
        SelectedTheme = Workspace.SettingsService.Current.Theme;
        RefreshIntervalSeconds = Workspace.SettingsService.Current.RefreshIntervalSeconds;
    }

    public RuntimeWorkspace Workspace { get; }
    public ImagesViewModel? ImagesPage { get; set; }
    public RuntimeCapabilities Capabilities => Workspace.Capabilities;

    [ObservableProperty] public partial string RegistryServer { get; set; } = DefaultRegistryServer;
    [ObservableProperty] public partial string RegistryUsername { get; set; } = string.Empty;
    [ObservableProperty] public partial string RegistryPassword { get; set; } = string.Empty;
    [ObservableProperty] public partial string SelectedLanguage { get; set; }
    [ObservableProperty] public partial string SelectedTheme { get; set; }
    [ObservableProperty] public partial int RefreshIntervalSeconds { get; set; }

    [RelayCommand] private void OpenNativeSettings() => Workspace.Runtime.OpenNativeSettings();

    [RelayCommand]
    private async Task LoginRegistryAsync()
    {
        if (string.IsNullOrWhiteSpace(RegistryServer) || string.IsNullOrWhiteSpace(RegistryUsername) || string.IsNullOrEmpty(RegistryPassword)) return;
        var result = await Workspace.Runtime.RegistryLoginAsync(RegistryServer, RegistryUsername, RegistryPassword, Workspace.Lifetime.Token);
        RegistryPassword = string.Empty;
        Workspace.ShowResult(result);
    }

    [RelayCommand]
    private async Task ResetNativeSettingsAsync()
    {
        if (!Workspace.Interaction.Confirm("Reset WSLC settings", "Reset the native WSLC YAML settings to built-in defaults?")) return;
        Workspace.ShowResult(await Workspace.Runtime.ResetNativeSettingsAsync(Workspace.Lifetime.Token));
    }

    [RelayCommand]
    private async Task InstallComponentsAsync()
    {
        if (!Workspace.Interaction.Confirm("Install components", "Install missing WSL Container components using the Microsoft preview SDK?")) return;
        Workspace.IsBusy = true;
        try
        {
            await Workspace.InstallMissingComponentsAsync(new Progress<string>(line => Workspace.StatusMessage = line));
        }
        catch (Exception exception)
        {
            Workspace.Interaction.ShowError("Installation failed", exception.Message);
        }
        finally
        {
            Workspace.IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        Workspace.SettingsService.Current.Language = SelectedLanguage;
        Workspace.SettingsService.Current.Theme = SelectedTheme;
        Workspace.SettingsService.Current.RefreshIntervalSeconds = Math.Clamp(RefreshIntervalSeconds, 2, 300);
        await Workspace.SettingsService.SaveAsync(Workspace.Lifetime.Token);
        LocalizationService.ApplyLanguage(SelectedLanguage);
        LocalizationService.ApplyTheme(SelectedTheme);
        Workspace.StatusMessage = "Settings saved.";
        ImagesPage?.RaiseLanguageChanged();
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(RuntimeWorkspace.Capabilities))
        {
            OnPropertyChanged(nameof(Capabilities));
        }
    }
}
