using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExWSLC.Models;
using ExWSLC.Services;

namespace ExWSLC.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IContainerRuntime _runtime;
    private readonly IRuntimeCapabilityService _capabilityService;
    private readonly ISettingsService _settingsService;
    private readonly ITaskService _taskService;
    private readonly IUserInteractionService _interaction;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _currentOperation;
    private CancellationTokenSource? _logFollow;

    public ObservableCollection<ContainerSummary> Containers { get; } = [];
    public ObservableCollection<ContainerSummary> ActiveContainers { get; } = [];
    public ObservableCollection<ContainerSummary> VisibleContainers { get; } = [];
    public ObservableCollection<ImageSummary> Images { get; } = [];
    public ObservableCollection<ImageSummary> VisibleImages { get; } = [];
    public ObservableCollection<NetworkSummary> Networks { get; } = [];
    public ObservableCollection<VolumeSummary> Volumes { get; } = [];
    public ObservableCollection<ContainerStats> Stats { get; } = [];
    public ObservableCollection<RuntimeTaskItem> Tasks { get; } = [];
    public ObservableCollection<RuntimeTaskItem> RecentTasks { get; } = [];

    [ObservableProperty] private int _selectedPageIndex;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Initializing…";
    [ObservableProperty] private string _detailOutput = string.Empty;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _imageSearchText = string.Empty;
    [ObservableProperty] private bool _isCreatingContainer;
    [ObservableProperty] private ContainerSummary? _selectedContainer;
    [ObservableProperty] private ImageSummary? _selectedImage;
    [ObservableProperty] private NetworkSummary? _selectedNetwork;
    [ObservableProperty] private VolumeSummary? _selectedVolume;
    [ObservableProperty] private RuntimeCapabilities _capabilities = RuntimeCapabilities.Unavailable("Not checked");
    [ObservableProperty] private RuntimeTaskItem? _activeTask;

    [ObservableProperty] private string _newImage = "hello-world:latest";
    [ObservableProperty] private string _newContainerName = string.Empty;
    [ObservableProperty] private string _newCommand = string.Empty;
    [ObservableProperty] private string _newCpuLimit = string.Empty;
    [ObservableProperty] private string _newMemoryLimit = string.Empty;
    [ObservableProperty] private string _newNetwork = string.Empty;
    [ObservableProperty] private string _newUser = string.Empty;
    [ObservableProperty] private string _newWorkingDirectory = string.Empty;
    [ObservableProperty] private string _newEnvironment = string.Empty;
    [ObservableProperty] private string _newPorts = string.Empty;
    [ObservableProperty] private string _newVolumes = string.Empty;
    [ObservableProperty] private bool _newUseAllGpus;
    [ObservableProperty] private bool _newRemoveWhenStopped;
    [ObservableProperty] private string _execText = "uname -a";

    [ObservableProperty] private string _imageReference = "ubuntu:latest";
    [ObservableProperty] private string _imagePath = string.Empty;
    [ObservableProperty] private string _imageTag = string.Empty;
    [ObservableProperty] private string _dockerfilePath = string.Empty;
    [ObservableProperty] private string _resourceName = string.Empty;

    [ObservableProperty] private string _registryServer = "docker.io";
    [ObservableProperty] private string _registryUsername = string.Empty;
    [ObservableProperty] private string _registryPassword = string.Empty;
    [ObservableProperty] private string _selectedLanguage;
    [ObservableProperty] private string _selectedTheme;
    [ObservableProperty] private int _refreshIntervalSeconds;

    public int RunningContainerCount => Containers.Count(container => container.IsRunning);
    public int StoppedContainerCount => Containers.Count - RunningContainerCount;
    public int ImageCount => Images.Count;
    public int NetworkCount => Networks.Count;
    public int VolumeCount => Volumes.Count;
    public int ActiveTaskCount => Tasks.Count(task => task.State is RuntimeTaskState.Running or RuntimeTaskState.Queued);
    public bool HasSelectedImage => SelectedImage is not null;
    public ContainerStats? SelectedContainerStats => SelectedContainer is null
        ? null
        : Stats.FirstOrDefault(stats =>
            stats.Id.Equals(SelectedContainer.Id, StringComparison.OrdinalIgnoreCase) ||
            stats.Name.Equals(SelectedContainer.Name, StringComparison.OrdinalIgnoreCase));
    public string SelectedImageDisplayName => SelectedImage?.DisplayName ??
        (SelectedLanguage.Equals("en-US", StringComparison.OrdinalIgnoreCase) ? "Select an image" : "请选择镜像");
    public string VersionSummary => $"CLI: {Capabilities.CliVersion}  ·  SDK: {Capabilities.SdkVersion}";

    public MainViewModel(
        IContainerRuntime runtime,
        IRuntimeCapabilityService capabilityService,
        ISettingsService settingsService,
        ITaskService taskService,
        IUserInteractionService interaction)
    {
        _runtime = runtime;
        _capabilityService = capabilityService;
        _settingsService = settingsService;
        _taskService = taskService;
        _interaction = interaction;
        _selectedLanguage = settingsService.Current.Language;
        _selectedTheme = settingsService.Current.Theme;
        _refreshIntervalSeconds = settingsService.Current.RefreshIntervalSeconds;
        _taskService.TasksChanged += OnTasksChanged;
    }

    public async Task InitializeAsync()
    {
        Capabilities = await _capabilityService.DetectAsync(_lifetime.Token);
        OnPropertyChanged(nameof(VersionSummary));
        if (!Capabilities.IsAvailable)
        {
            StatusMessage = Capabilities.Message;
            return;
        }

        await RefreshAllAsync();
        _ = AutoRefreshAsync(_lifetime.Token);
    }

    [RelayCommand]
    private async Task RefreshAllAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "Refreshing WSLC state…";
        try
        {
            var containersTask = _runtime.GetContainersAsync(_lifetime.Token);
            var imagesTask = _runtime.GetImagesAsync(_lifetime.Token);
            var networksTask = _runtime.GetNetworksAsync(_lifetime.Token);
            var volumesTask = _runtime.GetVolumesAsync(_lifetime.Token);
            var statsTask = _runtime.GetStatsAsync(_lifetime.Token);
            await Task.WhenAll(containersTask, imagesTask, networksTask, volumesTask, statsTask);
            Replace(Containers, containersTask.Result);
            Replace(ActiveContainers, containersTask.Result.Where(container => container.IsRunning).Take(4));
            ApplyContainerFilter();
            Replace(Images, imagesTask.Result);
            ApplyImageFilter();
            Replace(Networks, networksTask.Result);
            Replace(Volumes, volumesTask.Result);
            Replace(Stats, statsTask.Result);
            RaiseCounts();
            OnPropertyChanged(nameof(SelectedContainerStats));
            StatusMessage = $"Updated {DateTime.Now:T}";
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand] private Task StartContainerAsync() => RunContainerActionAsync("Start container", id => _runtime.StartContainerAsync(id, _lifetime.Token));
    [RelayCommand] private Task StopContainerAsync() => RunContainerActionAsync("Stop container", id => _runtime.StopContainerAsync(id, _lifetime.Token));
    [RelayCommand] private Task RestartContainerAsync() => RunContainerActionAsync("Restart container", id => _runtime.RestartContainerAsync(id, _lifetime.Token));

    [RelayCommand]
    private async Task KillContainerAsync()
    {
        if (SelectedContainer is null || !_interaction.Confirm("Force stop", $"Send SIGKILL to {SelectedContainer.Name}?")) return;
        await RunContainerActionAsync("Kill container", id => _runtime.KillContainerAsync(id, _lifetime.Token));
    }

    [RelayCommand]
    private async Task RemoveContainerAsync()
    {
        if (SelectedContainer is null || !_interaction.Confirm("Remove container", $"Permanently remove {SelectedContainer.Name}?")) return;
        await RunContainerActionAsync("Remove container", id => _runtime.RemoveContainerAsync(id, true, _lifetime.Token));
    }

    [RelayCommand]
    private async Task RunNewContainerAsync()
    {
        try
        {
            var spec = BuildCreateSpec();
            var result = await RunTrackedAsync($"Run {spec.Image}", (progress, token) => _runtime.RunContainerAsync(spec, progress, token));
            ShowResult(result);
            if (result.Success)
            {
                IsCreatingContainer = false;
                await RefreshAllAsync();
            }
        }
        catch (ArgumentException exception)
        {
            _interaction.ShowError("Invalid container", exception.Message);
        }
    }

    [RelayCommand]
    private async Task ShowLogsAsync()
    {
        if (SelectedContainer is null) return;
        ShowResult(await _runtime.GetLogsAsync(SelectedContainer.Id, cancellationToken: _lifetime.Token));
    }

    [RelayCommand]
    private async Task FollowLogsAsync()
    {
        if (SelectedContainer is null || _logFollow is not null) return;
        _logFollow = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        DetailOutput = string.Empty;
        var progress = new Progress<string>(line => DetailOutput += line + Environment.NewLine);
        try
        {
            var result = await _runtime.FollowLogsAsync(SelectedContainer.Id, progress, _logFollow.Token);
            if (result.ExitCode != -2) ShowResult(result);
        }
        finally
        {
            _logFollow.Dispose();
            _logFollow = null;
        }
    }

    [RelayCommand] private void StopFollowingLogs() => _logFollow?.Cancel();

    [RelayCommand]
    private async Task InspectContainerAsync()
    {
        if (SelectedContainer is null) return;
        ShowResult(await _runtime.InspectContainerAsync(SelectedContainer.Id, _lifetime.Token));
    }

    [RelayCommand]
    private async Task ExecAsync()
    {
        if (SelectedContainer is null || string.IsNullOrWhiteSpace(ExecText)) return;
        var result = await RunTrackedAsync("Execute command", (progress, token) => _runtime.ExecAsync(SelectedContainer.Id, ExecText, progress, token));
        ShowResult(result);
    }

    [RelayCommand]
    private void OpenTerminal()
    {
        if (SelectedContainer is not null) _runtime.OpenInteractiveTerminal(SelectedContainer.Id);
    }

    [RelayCommand]
    private async Task ExportContainerAsync()
    {
        if (SelectedContainer is null) return;
        var path = _interaction.PickSaveFile("Export container", "Tar archive (*.tar)|*.tar", $"{SelectedContainer.Name}.tar");
        if (path is null) return;
        var restartAfterExport = SelectedContainer.IsRunning;
        if (restartAfterExport)
        {
            if (!_interaction.Confirm("Export container", "WSLC requires the container to be stopped for export. Stop it temporarily and restart it afterwards?")) return;
            var stop = await _runtime.StopContainerAsync(SelectedContainer.Id, _lifetime.Token);
            if (!stop.Success) { ShowResult(stop); return; }
        }

        var export = await RunTrackedAsync("Export container", (progress, token) => _runtime.ExportContainerAsync(SelectedContainer.Id, path, progress, token));
        ShowResult(export);
        if (restartAfterExport)
        {
            var start = await _runtime.StartContainerAsync(SelectedContainer.Id, _lifetime.Token);
            if (!start.Success) ShowResult(start);
            await RefreshAllAsync();
        }
    }

    [RelayCommand]
    private async Task PullImageAsync()
    {
        if (string.IsNullOrWhiteSpace(ImageReference)) return;
        ShowResult(await RunTrackedAsync($"Pull {ImageReference}", (progress, token) => _runtime.PullImageAsync(ImageReference, progress, token)));
        await RefreshAllAsync();
    }

    [RelayCommand]
    private async Task BuildImageAsync()
    {
        var folder = string.IsNullOrWhiteSpace(ImagePath) ? _interaction.PickFolder("Choose build context") : ImagePath;
        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(ImageTag)) return;
        ShowResult(await RunTrackedAsync($"Build {ImageTag}", (progress, token) => _runtime.BuildImageAsync(folder, ImageTag, DockerfilePath, progress, token)));
        await RefreshAllAsync();
    }

    [RelayCommand]
    private async Task ImportImageAsync()
    {
        var path = string.IsNullOrWhiteSpace(ImagePath) ? _interaction.PickOpenFile("Import image", "Tar archive (*.tar)|*.tar|All files (*.*)|*.*") : ImagePath;
        if (path is null || string.IsNullOrWhiteSpace(ImageTag)) return;
        ShowResult(await RunTrackedAsync("Import image", (progress, token) => _runtime.ImportImageAsync(path, ImageTag, progress, token)));
        await RefreshAllAsync();
    }

    [RelayCommand]
    private async Task LoadImageAsync()
    {
        var path = string.IsNullOrWhiteSpace(ImagePath) ? _interaction.PickOpenFile("Load image", "Tar archive (*.tar)|*.tar|All files (*.*)|*.*") : ImagePath;
        if (path is null) return;
        ShowResult(await RunTrackedAsync("Load image", (progress, token) => _runtime.LoadImageAsync(path, progress, token)));
        await RefreshAllAsync();
    }

    [RelayCommand]
    private async Task SaveImageAsync()
    {
        var image = SelectedImage?.DisplayName ?? ImageReference;
        if (string.IsNullOrWhiteSpace(image)) return;
        var path = _interaction.PickSaveFile("Save image", "Tar archive (*.tar)|*.tar", image.Replace('/', '_').Replace(':', '_') + ".tar");
        if (path is null) return;
        ShowResult(await RunTrackedAsync("Save image", (progress, token) => _runtime.SaveImageAsync(image, path, progress, token)));
    }

    [RelayCommand]
    private async Task TagImageAsync()
    {
        if (SelectedImage is null || string.IsNullOrWhiteSpace(ImageTag)) return;
        ShowResult(await _runtime.TagImageAsync(SelectedImage.DisplayName, ImageTag, _lifetime.Token));
        await RefreshAllAsync();
    }

    [RelayCommand]
    private async Task PushImageAsync()
    {
        var image = SelectedImage?.DisplayName ?? ImageReference;
        if (string.IsNullOrWhiteSpace(image)) return;
        ShowResult(await RunTrackedAsync("Push image", (progress, token) => _runtime.PushImageAsync(image, progress, token)));
    }

    [RelayCommand]
    private async Task RemoveImageAsync()
    {
        if (SelectedImage is null || !_interaction.Confirm("Remove image", $"Permanently remove {SelectedImage.DisplayName}?")) return;
        ShowResult(await _runtime.RemoveImageAsync(SelectedImage.DisplayName, true, _lifetime.Token));
        await RefreshAllAsync();
    }

    [RelayCommand]
    private async Task InspectImageAsync()
    {
        if (SelectedImage is null) return;
        ShowResult(await _runtime.InspectImageAsync(SelectedImage.DisplayName, _lifetime.Token));
    }

    [RelayCommand]
    private async Task CreateNetworkAsync()
    {
        if (string.IsNullOrWhiteSpace(ResourceName)) return;
        ShowResult(await _runtime.CreateNetworkAsync(ResourceName, _lifetime.Token));
        await RefreshAllAsync();
    }

    [RelayCommand]
    private async Task RemoveNetworkAsync()
    {
        if (SelectedNetwork is null || !_interaction.Confirm("Remove network", $"Remove network {SelectedNetwork.Name}?")) return;
        ShowResult(await _runtime.RemoveNetworkAsync(SelectedNetwork.Name, _lifetime.Token));
        await RefreshAllAsync();
    }

    [RelayCommand]
    private async Task CreateVolumeAsync()
    {
        if (string.IsNullOrWhiteSpace(ResourceName)) return;
        ShowResult(await _runtime.CreateVolumeAsync(ResourceName, _lifetime.Token));
        await RefreshAllAsync();
    }

    [RelayCommand]
    private async Task RemoveVolumeAsync()
    {
        if (SelectedVolume is null || !_interaction.Confirm("Remove volume", $"Remove volume {SelectedVolume.Name}?")) return;
        ShowResult(await _runtime.RemoveVolumeAsync(SelectedVolume.Name, _lifetime.Token));
        await RefreshAllAsync();
    }

    [RelayCommand]
    private async Task InspectNetworkAsync()
    {
        if (SelectedNetwork is not null) ShowResult(await _runtime.InspectResourceAsync("network", SelectedNetwork.Name, _lifetime.Token));
    }

    [RelayCommand]
    private async Task InspectVolumeAsync()
    {
        if (SelectedVolume is not null) ShowResult(await _runtime.InspectResourceAsync("volume", SelectedVolume.Name, _lifetime.Token));
    }

    [RelayCommand]
    private async Task PruneAsync(string? resource)
    {
        if (resource is not ("container" or "image" or "network" or "volume")) return;
        if (!_interaction.Confirm("Prune resources", $"Remove every unused {resource} resource?")) return;
        ShowResult(await _runtime.PruneAsync(resource, _lifetime.Token));
        await RefreshAllAsync();
    }

    [RelayCommand]
    private async Task LoginRegistryAsync()
    {
        if (string.IsNullOrWhiteSpace(RegistryServer) || string.IsNullOrWhiteSpace(RegistryUsername) || string.IsNullOrEmpty(RegistryPassword)) return;
        var result = await _runtime.RegistryLoginAsync(RegistryServer, RegistryUsername, RegistryPassword, _lifetime.Token);
        RegistryPassword = string.Empty;
        ShowResult(result);
    }

    [RelayCommand] private void OpenNativeSettings() => _runtime.OpenNativeSettings();

    [RelayCommand]
    private void ShowCreateContainer()
    {
        SelectedContainer = null;
        IsCreatingContainer = true;
    }

    [RelayCommand] private void CancelCreateContainer() => IsCreatingContainer = false;

    [RelayCommand]
    private async Task ResetNativeSettingsAsync()
    {
        if (!_interaction.Confirm("Reset WSLC settings", "Reset the native WSLC YAML settings to built-in defaults?")) return;
        ShowResult(await _runtime.ResetNativeSettingsAsync(_lifetime.Token));
    }

    [RelayCommand]
    private async Task InstallComponentsAsync()
    {
        if (!_interaction.Confirm("Install components", "Install missing WSL Container components using the Microsoft preview SDK?")) return;
        IsBusy = true;
        try
        {
            await _capabilityService.InstallMissingComponentsAsync(new Progress<string>(line => StatusMessage = line), _lifetime.Token);
            Capabilities = await _capabilityService.DetectAsync(_lifetime.Token);
        }
        catch (Exception exception) { _interaction.ShowError("Installation failed", exception.Message); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        _settingsService.Current.Language = SelectedLanguage;
        _settingsService.Current.Theme = SelectedTheme;
        _settingsService.Current.RefreshIntervalSeconds = Math.Clamp(RefreshIntervalSeconds, 2, 300);
        await _settingsService.SaveAsync(_lifetime.Token);
        LocalizationService.ApplyLanguage(SelectedLanguage);
        LocalizationService.ApplyTheme(SelectedTheme);
        StatusMessage = "Settings saved.";
    }

    /// <summary>
    /// Applies the configured theme once the main window is initialized. Mirrors the WPF-UI
    /// FluentWindow pattern of applying the theme from the window constructor (after
    /// <see cref="Window.InitializeComponent"/>) rather than during <c>OnStartup</c>.
    /// </summary>
    public void ApplyConfiguredTheme()
    {
        LocalizationService.ApplyTheme(_settingsService.Current.Theme);
    }

    /// <summary>
    /// Re-applies the theme in response to the OS light/dark preference changing, but only when
    /// the configured theme is "System" — explicit Light/Dark choices are left untouched.
    /// </summary>
    public void RefreshSystemTheme()
    {
        if (string.Equals(_settingsService.Current.Theme, "System", StringComparison.OrdinalIgnoreCase))
        {
            LocalizationService.ApplyTheme("System");
        }
    }

    [RelayCommand] private void ClearTasks() { _taskService.ClearCompleted(); SyncTasks(); }
    [RelayCommand] private void CancelCurrentOperation() => _currentOperation?.Cancel();

    partial void OnSearchTextChanged(string value) => ApplyContainerFilter();
    partial void OnImageSearchTextChanged(string value) => ApplyImageFilter();
    partial void OnSelectedContainerChanged(ContainerSummary? value)
    {
        if (value is not null) IsCreatingContainer = false;
        OnPropertyChanged(nameof(SelectedContainerStats));
    }
    partial void OnSelectedImageChanged(ImageSummary? value)
    {
        OnPropertyChanged(nameof(HasSelectedImage));
        OnPropertyChanged(nameof(SelectedImageDisplayName));
    }
    partial void OnSelectedLanguageChanged(string value) => OnPropertyChanged(nameof(SelectedImageDisplayName));
    partial void OnCapabilitiesChanged(RuntimeCapabilities value) => OnPropertyChanged(nameof(VersionSummary));

    private async Task RunContainerActionAsync(string title, Func<string, Task<OperationResult>> operation)
    {
        if (SelectedContainer is null) return;
        ShowResult(await operation(SelectedContainer.Id));
        await RefreshAllAsync();
    }

    private async Task<OperationResult> RunTrackedAsync(string title, Func<IProgress<string>, CancellationToken, Task<OperationResult>> operation)
    {
        IsBusy = true;
        _currentOperation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        try { return await _taskService.RunAsync(title, operation, _currentOperation.Token); }
        finally { _currentOperation.Dispose(); _currentOperation = null; IsBusy = false; }
    }

    private ContainerCreateSpec BuildCreateSpec()
    {
        if (string.IsNullOrWhiteSpace(NewImage)) throw new ArgumentException("Image is required.");
        var spec = new ContainerCreateSpec
        {
            Image = NewImage.Trim(), Name = NewContainerName.Trim(), Command = NewCommand,
            CpuLimit = NewCpuLimit.Trim(), MemoryLimit = NewMemoryLimit.Trim(), Network = NewNetwork.Trim(),
            User = NewUser.Trim(), WorkingDirectory = NewWorkingDirectory.Trim(), UseAllGpus = NewUseAllGpus,
            RemoveWhenStopped = NewRemoveWhenStopped
        };
        foreach (var line in SplitValues(NewEnvironment))
        {
            var index = line.IndexOf('=');
            if (index > 0) spec.Environment.Add(new(line[..index].Trim(), line[(index + 1)..]));
        }
        spec.Ports.AddRange(SplitValues(NewPorts));
        spec.Volumes.AddRange(SplitValues(NewVolumes));
        return spec;
    }

    private void ShowResult(OperationResult result)
    {
        DetailOutput = result.CombinedOutput;
        StatusMessage = result.Success ? "Operation completed." : $"Failed ({result.ExitCode}): {result.Error}";
        if (!result.Success && !string.IsNullOrWhiteSpace(result.Error)) _interaction.ShowError("WSLC operation failed", result.Error);
    }

    private async Task AutoRefreshAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(RefreshIntervalSeconds, 2, 300)), cancellationToken);
                if (!IsBusy && Capabilities.IsAvailable) await RefreshAllAsync();
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private void ApplyContainerFilter()
    {
        var query = SearchText.Trim();
        Replace(VisibleContainers, Containers.Where(container => string.IsNullOrEmpty(query) ||
            container.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            container.Image.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            container.Id.Contains(query, StringComparison.OrdinalIgnoreCase)));
    }

    private void ApplyImageFilter()
    {
        var query = ImageSearchText.Trim();
        Replace(VisibleImages, Images.Where(image => string.IsNullOrEmpty(query) ||
            image.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            image.Id.Contains(query, StringComparison.OrdinalIgnoreCase)));
    }

    private void OnTasksChanged(object? sender, EventArgs eventArgs) => App.Current.Dispatcher.Invoke(SyncTasks);
    private void SyncTasks()
    {
        Replace(Tasks, _taskService.Tasks);
        Replace(RecentTasks, _taskService.Tasks.Take(5));
        ActiveTask = _taskService.Tasks.FirstOrDefault(task => task.State is RuntimeTaskState.Running or RuntimeTaskState.Queued);
        OnPropertyChanged(nameof(ActiveTaskCount));
    }
    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(RunningContainerCount)); OnPropertyChanged(nameof(StoppedContainerCount));
        OnPropertyChanged(nameof(ImageCount)); OnPropertyChanged(nameof(NetworkCount)); OnPropertyChanged(nameof(VolumeCount));
    }
    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear(); foreach (var item in source) target.Add(item);
    }
    private static IEnumerable<string> SplitValues(string value) =>
        value.Split(['\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public void Dispose()
    {
        _taskService.TasksChanged -= OnTasksChanged;
        _logFollow?.Cancel();
        _currentOperation?.Cancel();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
