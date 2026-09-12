using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExWSLC.Helpers;
using ExWSLC.Models;
using ExWSLC.Services;

namespace ExWSLC.ViewModels;

public partial class RuntimeWorkspace : ObservableObject, IDisposable
{
    private readonly IRuntimeCapabilityService _capabilityService;
    private readonly ITaskService _taskService;
    private CancellationTokenSource? _currentOperation;
    private bool _disposed;
    private bool _autoRefreshStarted;

    public RuntimeWorkspace(
        IContainerRuntime runtime,
        IRuntimeCapabilityService capabilityService,
        ISettingsService settingsService,
        ITaskService taskService,
        IUserInteractionService interaction)
    {
        Runtime = runtime;
        _capabilityService = capabilityService;
        SettingsService = settingsService;
        _taskService = taskService;
        Interaction = interaction;
        _taskService.TasksChanged += OnTasksChanged;
    }

    public IContainerRuntime Runtime { get; }
    public ISettingsService SettingsService { get; }
    public IUserInteractionService Interaction { get; }
    public CancellationTokenSource Lifetime { get; } = new();

    public ObservableCollection<ContainerSummary> Containers { get; } = [];
    public ObservableCollection<ContainerSummary> ActiveContainers { get; } = [];
    public ObservableCollection<ImageSummary> Images { get; } = [];
    public ObservableCollection<NetworkSummary> Networks { get; } = [];
    public ObservableCollection<VolumeSummary> Volumes { get; } = [];
    public ObservableCollection<ContainerStats> Stats { get; } = [];
    public ObservableCollection<RuntimeTaskItem> Tasks { get; } = [];
    public ObservableCollection<RuntimeTaskItem> RecentTasks { get; } = [];

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string StatusMessage { get; set; } = "Initializing...";
    [ObservableProperty] public partial string RefreshError { get; set; } = string.Empty;
    [ObservableProperty] public partial string DetailOutput { get; set; } = string.Empty;
    [ObservableProperty] public partial RuntimeCapabilities Capabilities { get; set; } = new();
    [ObservableProperty] public partial RuntimeTaskItem? ActiveTask { get; set; }

    public int RunningContainerCount => Containers.Count(container => container.IsRunning);
    public int StoppedContainerCount => Containers.Count - RunningContainerCount;
    public int ImageCount => Images.Count;
    public int NetworkCount => Networks.Count;
    public int VolumeCount => Volumes.Count;
    public int ActiveTaskCount => Tasks.Count(task => task.State is RuntimeTaskState.Running or RuntimeTaskState.Queued);
    public string VersionSummary => $"CLI: {DisplayVersion(Capabilities.CliVersion)}  ·  Service: {DisplayVersion(Capabilities.ServiceVersion)}  ·  SDK: {DisplayVersion(Capabilities.SdkPackageVersion)}";
    public string CapabilityMessage => string.Format(
        LocalizationService.GetString(Capabilities.MessageKey, Capabilities.MessageKey),
        Capabilities.MessageArguments.Cast<object>().ToArray());

    private static string DisplayVersion(string version) => string.IsNullOrWhiteSpace(version) ? "—" : version;
    partial void OnCapabilitiesChanged(RuntimeCapabilities value) => OnPropertyChanged(nameof(VersionSummary));

    public event EventHandler? Refreshed;

    public async Task InitializeAsync()
    {
        Capabilities = await _capabilityService.DetectAsync(Lifetime.Token);
        StartAutoRefresh();
        if (!Capabilities.IsAvailable)
        {
            StatusMessage = CapabilityMessage;
            return;
        }

        await RefreshAllAsync();
    }

    private void StartAutoRefresh()
    {
        if (_autoRefreshStarted) return;
        _autoRefreshStarted = true;
        var autoRefresh = new AutoRefreshService(
            RefreshAllAsync,
            () => !IsBusy && Capabilities.IsAvailable,
            () => SettingsService.Current.RefreshIntervalSeconds);
        _ = autoRefresh.RunAsync(Lifetime.Token);
    }

    public async Task InstallMissingComponentsAsync(IProgress<string> progress)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await _capabilityService.InstallMissingComponentsAsync(progress, Lifetime.Token);
            Capabilities = await _capabilityService.DetectAsync(Lifetime.Token);
            StatusMessage = CapabilityMessage;
        }
        finally
        {
            IsBusy = false;
        }
        StartAutoRefresh();
        if (Capabilities.IsAvailable) await RefreshAllAsync();
    }

    public async Task RefreshCapabilitiesAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            Capabilities = await _capabilityService.RefreshAsync(Lifetime.Token);
            StatusMessage = CapabilityMessage;
        }
        finally
        {
            IsBusy = false;
        }
        StartAutoRefresh();
        if (Capabilities.IsAvailable) await RefreshAllAsync();
    }

    [RelayCommand]
    public async Task RefreshAllAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        RefreshError = string.Empty;
        StatusMessage = "Refreshing WSLC state...";
        try
        {
            var containersTask = Runtime.GetContainersAsync(Lifetime.Token);
            var imagesTask = Runtime.GetImagesAsync(Lifetime.Token);
            var networksTask = Runtime.GetNetworksAsync(Lifetime.Token);
            var volumesTask = Runtime.GetVolumesAsync(Lifetime.Token);
            var statsTask = Runtime.GetStatsAsync(Lifetime.Token);
            await Task.WhenAll(containersTask, imagesTask, networksTask, volumesTask, statsTask);
            Containers.ReplaceAll(containersTask.Result);
            ActiveContainers.ReplaceAll(containersTask.Result.Where(container => container.IsRunning).Take(4));
            Images.ReplaceAll(imagesTask.Result);
            Networks.ReplaceAll(networksTask.Result);
            Volumes.ReplaceAll(volumesTask.Result);
            Stats.ReplaceAll(statsTask.Result);
            RaiseCounts();
            Refreshed?.Invoke(this, EventArgs.Empty);
            StatusMessage = $"Updated {DateTime.Now:T}";
        }
        catch (Exception exception)
        {
            RefreshError = exception.Message;
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public bool HasRefreshError => !string.IsNullOrWhiteSpace(RefreshError);

    partial void OnRefreshErrorChanged(string value) => OnPropertyChanged(nameof(HasRefreshError));

    public async Task<OperationResult> RunTrackedAsync(
        string title,
        Func<IProgress<string>, CancellationToken, Task<OperationResult>> operation)
    {
        IsBusy = true;
        _currentOperation = CancellationTokenSource.CreateLinkedTokenSource(Lifetime.Token);
        try
        {
            return await _taskService.RunAsync(title, operation, _currentOperation.Token);
        }
        finally
        {
            _currentOperation.Dispose();
            _currentOperation = null;
            IsBusy = false;
        }
    }

    public async void ShowResult(OperationResult result)
    {
        DetailOutput = result.CombinedOutput;
        StatusMessage = result.Success ? "Operation completed." : $"Failed ({result.ExitCode}): {result.Error}";
        if (!result.Success && !string.IsNullOrWhiteSpace(result.Error))
        {
            await Interaction.ShowErrorAsync("WSLC operation failed", result.Error);
        }
    }

    [RelayCommand]
    public void ClearTasks()
    {
        _taskService.ClearCompleted();
        SyncTasks();
    }

    [RelayCommand]
    public void CancelCurrentOperation() => _currentOperation?.Cancel();

    public ContainerStats? FindStats(ContainerSummary container) =>
        Stats.FirstOrDefault(stats =>
            stats.Id.Equals(container.Id, StringComparison.OrdinalIgnoreCase) ||
            stats.Name.Equals(container.Name, StringComparison.OrdinalIgnoreCase));

    public void RaiseSharedPropertyChanges()
    {
        OnPropertyChanged(nameof(DetailOutput));
        OnPropertyChanged(nameof(Capabilities));
        OnPropertyChanged(nameof(VersionSummary));
    }

    private void OnTasksChanged(object? sender, EventArgs eventArgs) => App.Current.Dispatcher.Invoke(SyncTasks);

    private void SyncTasks()
    {
        Tasks.ReplaceAll(_taskService.Tasks);
        RecentTasks.ReplaceAll(_taskService.Tasks.Take(5));
        ActiveTask = _taskService.Tasks.FirstOrDefault(task => task.State is RuntimeTaskState.Running or RuntimeTaskState.Queued);
        OnPropertyChanged(nameof(ActiveTaskCount));
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(RunningContainerCount));
        OnPropertyChanged(nameof(StoppedContainerCount));
        OnPropertyChanged(nameof(ImageCount));
        OnPropertyChanged(nameof(NetworkCount));
        OnPropertyChanged(nameof(VolumeCount));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _taskService.TasksChanged -= OnTasksChanged;
        _currentOperation?.Cancel();
        Lifetime.Cancel();
        Lifetime.Dispose();
    }
}
