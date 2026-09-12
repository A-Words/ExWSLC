using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ExWSLC.Models;
using ExWSLC.Services;
using ExWSLC.ViewModels.Messages;
using System.ComponentModel;

namespace ExWSLC.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private const string DefaultRegistryServer = "docker.io";

    public SettingsViewModel(RuntimeWorkspace workspace)
    {
        Workspace = workspace;
        Workspace.PropertyChanged += OnWorkspacePropertyChanged;
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, (recipient, _) => ((SettingsViewModel)recipient).RaiseLanguageChanged());
        SelectedLanguage = Workspace.SettingsService.Current.Language;
        SelectedTheme = Workspace.SettingsService.Current.Theme;
        RefreshIntervalSeconds = Workspace.SettingsService.Current.RefreshIntervalSeconds;
        RefreshDiagnosticsCommand.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(RefreshDiagnosticsCommand.IsRunning)) RaiseDiagnosticsChanged();
        };
        ProbeHostLoopbackCommand.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ProbeHostLoopbackCommand.IsRunning)) RaiseHostLoopbackChanged();
        };
        ReadHostLoopbackCommand.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ReadHostLoopbackCommand.IsRunning)) RaiseHostLoopbackChanged();
        };
    }

    public RuntimeWorkspace Workspace { get; }
    public RuntimeCapabilities Capabilities => Workspace.Capabilities;
    public bool IsSdkAvailable => Capabilities.SdkAvailability == CapabilitySupport.Supported;
    public string CliVersionText => DisplayVersion(Capabilities.CliVersion);
    public string ServiceVersionText => DisplayVersion(Capabilities.ServiceVersion);
    public string EnvironmentMessage => Workspace.CapabilityMessage;
    public bool CanInstallComponents => !Workspace.IsBusy && Capabilities.CanInstallComponents;
    public bool CanRefreshCapabilities => !Workspace.IsBusy;

    private static string DisplayVersion(string version) => string.IsNullOrWhiteSpace(version)
        ? LocalizationService.GetString("Unknown", "Unknown")
        : version;

    private void RaiseLanguageChanged()
    {
        RaiseDiagnosticsChanged();
        RaiseHostLoopbackChanged();
        OnPropertyChanged(nameof(CliVersionText));
        OnPropertyChanged(nameof(ServiceVersionText));
        OnPropertyChanged(nameof(EnvironmentMessage));
    }

    [ObservableProperty] public partial string RegistryServer { get; set; } = DefaultRegistryServer;
    [ObservableProperty] public partial string RegistryUsername { get; set; } = string.Empty;
    [ObservableProperty] public partial string RegistryPassword { get; set; } = string.Empty;
    [ObservableProperty] public partial string SelectedLanguage { get; set; }
    [ObservableProperty] public partial string SelectedTheme { get; set; }
    [ObservableProperty] public partial int RefreshIntervalSeconds { get; set; }

    [RelayCommand] private void OpenNativeSettings() => Workspace.Runtime.OpenNativeSettings();

    private bool CanLoginRegistry() =>
        !string.IsNullOrWhiteSpace(RegistryServer) &&
        !string.IsNullOrWhiteSpace(RegistryUsername) &&
        !string.IsNullOrEmpty(RegistryPassword);

    [RelayCommand(CanExecute = nameof(CanLoginRegistry))]
    private async Task LoginRegistryAsync()
    {
        var result = await Workspace.Runtime.RegistryLoginAsync(RegistryServer, RegistryUsername, RegistryPassword, Workspace.Lifetime.Token);
        RegistryPassword = string.Empty;
        Workspace.ShowResult(result);
    }

    partial void OnRegistryServerChanged(string value) => LoginRegistryCommand.NotifyCanExecuteChanged();

    partial void OnRegistryUsernameChanged(string value) => LoginRegistryCommand.NotifyCanExecuteChanged();

    partial void OnRegistryPasswordChanged(string value) => LoginRegistryCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private async Task ResetNativeSettingsAsync()
    {
        if (!await Workspace.Interaction.ConfirmAsync(
                LocalizationService.GetString("ResetNativeSettings", "Reset WSLC settings"),
                LocalizationService.GetString("ResetNativeSettingsConfirmation", "Reset the native WSLC YAML settings to built-in defaults?"))) return;
        Workspace.ShowResult(await Workspace.Runtime.ResetNativeSettingsAsync(Workspace.Lifetime.Token));
    }

    [RelayCommand(CanExecute = nameof(CanInstallComponents))]
    private async Task InstallComponentsAsync()
    {
        if (!await Workspace.Interaction.ConfirmAsync(
                LocalizationService.GetString("InstallComponents", "Install missing components"),
                LocalizationService.GetString("InstallComponentsConfirmation", "Install missing WSL Container components using the Microsoft preview SDK?"))) return;
        try
        {
            await Workspace.InstallMissingComponentsAsync(new Progress<string>(line => Workspace.StatusMessage = line));
        }
        catch (OperationCanceledException)
        {
            Workspace.StatusMessage = LocalizationService.GetString("InstallationCancelled", "Installation cancellation requested.");
        }
        catch (Exception exception)
        {
            await Workspace.Interaction.ShowErrorAsync(
                LocalizationService.GetString("InstallationFailed", "Installation failed"),
                exception.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefreshCapabilities))]
    private async Task RefreshCapabilitiesAsync()
    {
        try
        {
            await Workspace.RefreshCapabilitiesAsync();
        }
        catch (OperationCanceledException) when (Workspace.Lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            await Workspace.Interaction.ShowErrorAsync(
                LocalizationService.GetString("RuntimeDetectionFailed", "Environment detection failed"), exception.Message);
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
        Workspace.StatusMessage = LocalizationService.GetString("SettingsSavedStatus", "Settings saved.");
        WeakReferenceMessenger.Default.Send(new LanguageChangedMessage(SelectedLanguage));
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(RuntimeWorkspace.Capabilities))
        {
            OnPropertyChanged(nameof(Capabilities));
            OnPropertyChanged(nameof(IsSdkAvailable));
            RaiseLanguageChanged();
        }
        if (eventArgs.PropertyName is nameof(RuntimeWorkspace.Capabilities) or nameof(RuntimeWorkspace.IsBusy))
        {
            RaiseHostLoopbackChanged();
            OnPropertyChanged(nameof(CanRefreshDiagnostics));
            RefreshDiagnosticsCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanInstallComponents));
            OnPropertyChanged(nameof(CanRefreshCapabilities));
            InstallComponentsCommand.NotifyCanExecuteChanged();
            RefreshCapabilitiesCommand.NotifyCanExecuteChanged();
        }
    }
}
