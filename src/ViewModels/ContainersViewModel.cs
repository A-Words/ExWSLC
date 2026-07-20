using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExWSLC.Helpers;
using ExWSLC.Models;

namespace ExWSLC.ViewModels;

public partial class ContainersViewModel : WorkspaceViewModel
{
    private const string DefaultImage = "hello-world:latest";
    private const string DefaultExecCommand = "uname -a";
    private const int MaxLogLines = 5000;
    private const int LogTrimBatch = 500;
    private const int LogsTabIndex = 0;
    private const int NetworkTabIndex = 2;
    private const int MountsTabIndex = 3;
    private const int ConfigurationTabIndex = 4;
    private const int InspectTabIndex = 5;

    private CancellationTokenSource? _logFollow;
    private CancellationTokenSource? _networkDetailsLoad;
    private CancellationTokenSource? _mountDetailsLoad;
    private CancellationTokenSource? _inspectDetailsLoad;
    private string? _networkDetailsLoadContainerId;
    private string? _mountDetailsLoadContainerId;
    private string? _inspectDetailsLoadContainerId;
    private string? _followedContainerId;
    private string? _currentContainerId;
    private readonly Dictionary<string, ContainerNetworkDetails> _networkDetailsCache = [];
    private readonly Dictionary<string, ContainerMountDetails> _mountDetailsCache = [];
    private readonly Dictionary<string, ContainerInspectDetails> _inspectDetailsCache = [];

    public ContainersViewModel(RuntimeWorkspace workspace) : base(workspace)
    {
        Workspace.Refreshed += OnWorkspaceRefreshed;
    }
    public ObservableCollection<ContainerListItem> VisibleContainerItems { get; } = [];

    /// <summary>Log lines for the selected container's Logs tab, one row per line.</summary>
    public ObservableCollection<LogLine> LogLines { get; } = [];

    /// <summary>Index of the active detail tab, two-way bound to the detail SelectorBar.</summary>
    [ObservableProperty] public partial int SelectedDetailTabIndex { get; set; }

    /// <summary>Set by the design-time view model to disable live log following in the designer.</summary>
    protected bool IsDesignMode { get; set; }

    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial int ContainerFilterIndex { get; set; }
    [ObservableProperty] public partial bool IsCreatingContainer { get; set; }
    [ObservableProperty] public partial ContainerSummary? SelectedContainer { get; set; }
    [ObservableProperty] public partial string NewImage { get; set; } = DefaultImage;
    [ObservableProperty] public partial string NewContainerName { get; set; } = string.Empty;
    [ObservableProperty] public partial string NewCommand { get; set; } = string.Empty;
    [ObservableProperty] public partial string NewCpuLimit { get; set; } = string.Empty;
    [ObservableProperty] public partial string NewMemoryLimit { get; set; } = string.Empty;
    [ObservableProperty] public partial string NewNetwork { get; set; } = string.Empty;
    [ObservableProperty] public partial string NewUser { get; set; } = string.Empty;
    [ObservableProperty] public partial string NewWorkingDirectory { get; set; } = string.Empty;
    [ObservableProperty] public partial string NewEnvironment { get; set; } = string.Empty;
    [ObservableProperty] public partial string NewPorts { get; set; } = string.Empty;
    [ObservableProperty] public partial string NewVolumes { get; set; } = string.Empty;
    [ObservableProperty] public partial bool NewUseAllGpus { get; set; }
    [ObservableProperty] public partial bool NewRemoveWhenStopped { get; set; }
    [ObservableProperty] public partial string ExecText { get; set; } = DefaultExecCommand;
    [ObservableProperty] public partial ContainerNetworkDetails? NetworkDetails { get; set; }
    [ObservableProperty] public partial bool IsNetworkDetailsLoading { get; set; }
    [ObservableProperty] public partial string NetworkDetailsError { get; set; } = string.Empty;
    [ObservableProperty] public partial DateTimeOffset? NetworkDetailsUpdatedAt { get; set; }
    [ObservableProperty] public partial ContainerMountDetails? MountDetails { get; set; }
    [ObservableProperty] public partial bool IsMountDetailsLoading { get; set; }
    [ObservableProperty] public partial string MountDetailsError { get; set; } = string.Empty;
    [ObservableProperty] public partial ContainerInspectDetails? InspectDetails { get; set; }
    [ObservableProperty] public partial bool IsInspectDetailsLoading { get; set; }
    [ObservableProperty] public partial string InspectDetailsError { get; set; } = string.Empty;
    [ObservableProperty] public partial DateTimeOffset? InspectDetailsUpdatedAt { get; set; }
    [ObservableProperty] public partial string InspectOutput { get; set; } = string.Empty;

    public ContainerStats? SelectedContainerStats => SelectedContainer is null ? null : Workspace.FindStats(SelectedContainer);
    public bool HasNetworkDetails => NetworkDetails is not null;
    public bool HasNetworkDetailsError => !string.IsNullOrWhiteSpace(NetworkDetailsError);
    public bool HasMountDetails => MountDetails is not null;
    public bool HasMountDetailsError => !string.IsNullOrWhiteSpace(MountDetailsError);
    public bool HasInspectDetails => InspectDetails is not null;
    public bool HasInspectDetailsError => !string.IsNullOrWhiteSpace(InspectDetailsError);

    [RelayCommand] private Task StartContainerAsync() => RunContainerActionAsync("Start container", id => Workspace.Runtime.StartContainerAsync(id, Workspace.Lifetime.Token));
    [RelayCommand] private Task StopContainerAsync() => RunContainerActionAsync("Stop container", id => Workspace.Runtime.StopContainerAsync(id, Workspace.Lifetime.Token));
    [RelayCommand] private Task RestartContainerAsync() => RunContainerActionAsync("Restart container", id => Workspace.Runtime.RestartContainerAsync(id, Workspace.Lifetime.Token));

    [RelayCommand] private Task StartContainerFromListAsync(ContainerSummary? container) => RunContainerActionAsync(container, id => Workspace.Runtime.StartContainerAsync(id, Workspace.Lifetime.Token));
    [RelayCommand] private Task StopContainerFromListAsync(ContainerSummary? container) => RunContainerActionAsync(container, id => Workspace.Runtime.StopContainerAsync(id, Workspace.Lifetime.Token));
    [RelayCommand] private Task RestartContainerFromListAsync(ContainerSummary? container) => RunContainerActionAsync(container, id => Workspace.Runtime.RestartContainerAsync(id, Workspace.Lifetime.Token));

    [RelayCommand]
    private void ShowContainerDetailsFromList(ContainerSummary? container)
    {
        if (container is null) return;
        IsCreatingContainer = false;
        SelectedContainer = container;
    }

    [RelayCommand]
    private void OpenTerminalFromList(ContainerSummary? container)
    {
        if (container is not null) Workspace.Runtime.OpenInteractiveTerminal(container.Id);
    }

    [RelayCommand]
    private async Task RemoveContainerFromListAsync(ContainerSummary? container)
    {
        if (container is null || !await Workspace.Interaction.ConfirmAsync("Remove container", $"Permanently remove {container.Name}?")) return;
        SelectedContainer = null;
        var result = await Workspace.Runtime.RemoveContainerAsync(container.Id, true, Workspace.Lifetime.Token);
        Workspace.ShowResult(result);
        if (result.Success)
        {
            _networkDetailsCache.Remove(container.Id);
            _mountDetailsCache.Remove(container.Id);
            _inspectDetailsCache.Remove(container.Id);
        }
        await Workspace.RefreshAllAsync();
    }

    [RelayCommand]
    private async Task KillContainerAsync()
    {
        if (SelectedContainer is null || !await Workspace.Interaction.ConfirmAsync("Force stop", $"Send SIGKILL to {SelectedContainer.Name}?")) return;
        await RunContainerActionAsync("Kill container", id => Workspace.Runtime.KillContainerAsync(id, Workspace.Lifetime.Token));
    }

    [RelayCommand]
    private async Task RemoveContainerAsync()
    {
        if (SelectedContainer is null || !await Workspace.Interaction.ConfirmAsync("Remove container", $"Permanently remove {SelectedContainer.Name}?")) return;
        await RunContainerActionAsync("Remove container", id => Workspace.Runtime.RemoveContainerAsync(id, true, Workspace.Lifetime.Token));
    }

    [RelayCommand]
    private async Task RunNewContainerAsync()
    {
        try
        {
            var spec = BuildCreateSpec();
            var result = await Workspace.RunTrackedAsync($"Run {spec.Image}", (progress, token) => Workspace.Runtime.RunContainerAsync(spec, progress, token));
            Workspace.ShowResult(result);
            if (result.Success)
            {
                IsCreatingContainer = false;
                await Workspace.RefreshAllAsync();
            }
        }
        catch (ArgumentException exception)
        {
            await Workspace.Interaction.ShowErrorAsync("Invalid container", exception.Message);
        }
    }

    private bool ShouldFollowLogs => SelectedContainer is not null && SelectedDetailTabIndex == LogsTabIndex;

    partial void OnSelectedDetailTabIndexChanged(int value)
    {
        EvaluateFollow();
        switch (value)
        {
            case NetworkTabIndex:
                _mountDetailsLoad?.Cancel();
                _inspectDetailsLoad?.Cancel();
                _ = LoadNetworkDetailsAsync(force: false);
                break;
            case MountsTabIndex:
                _networkDetailsLoad?.Cancel();
                _inspectDetailsLoad?.Cancel();
                _ = LoadMountDetailsAsync(force: false);
                break;
            case ConfigurationTabIndex:
            case InspectTabIndex:
                _networkDetailsLoad?.Cancel();
                _mountDetailsLoad?.Cancel();
                _ = LoadInspectDetailsAsync(force: false);
                break;
            default:
                _networkDetailsLoad?.Cancel();
                _mountDetailsLoad?.Cancel();
                _inspectDetailsLoad?.Cancel();
                break;
        }
    }

    /// <summary>
    /// Starts or stops live log following based on whether the Logs tab is active and a container is
    /// selected. The Logs tab always follows; following restarts when the selected container changes.
    /// </summary>
    private void EvaluateFollow()
    {
        if (IsDesignMode) return;

        var container = SelectedContainer;
        if (container is not null && SelectedDetailTabIndex == LogsTabIndex)
        {
            if (_logFollow is not null && container.Id == _followedContainerId)
                return; // already following this container
            _logFollow?.Cancel(); // following a different container, or winding down: restart
            StartFollow();
        }
        else
        {
            _logFollow?.Cancel();
        }
    }

    private async void StartFollow()
    {
        var container = SelectedContainer;
        if (container is null || _logFollow is not null) return; // nothing to follow, or a previous follow is still winding down

        // Yield first so the body never runs synchronously. If FollowLogsAsync completes immediately
        // (a fast-exiting follow, e.g. a stopped/nonexistent container), the restart in the finally
        // is posted to a fresh message-loop iteration instead of recursing on the call stack.
        await Task.Yield();

        // Conditions may have changed during the yield; re-check before committing.
        container = SelectedContainer;
        if (container is null || !ShouldFollowLogs || _logFollow is not null) return;

        _logFollow = CancellationTokenSource.CreateLinkedTokenSource(Workspace.Lifetime.Token);
        _followedContainerId = container.Id;
        LogLines.Clear();
        // Progress<T> marshals callbacks to the UI thread, so appending here is safe.
        var progress = new Progress<string>(AppendLogLine);
        try
        {
            var result = await Workspace.Runtime.FollowLogsAsync(container.Id, progress, _logFollow.Token);
            if (result.ExitCode != -2 && !result.Success && !string.IsNullOrWhiteSpace(result.Error))
            {
                await Workspace.Interaction.ShowErrorAsync("WSLC operation failed", result.Error);
            }
        }
        catch (Exception exception)
        {
            // StartFollow is async void, so swallow to avoid crashing the app; report instead.
            _ = Workspace.Interaction.ShowErrorAsync("WSLC operation failed", exception.Message);
        }
        finally
        {
            // Restart only when WE cancelled to switch containers. A follow that ended on its own
            // (stopped/nonexistent container) or due to shutdown must not restart, or it would spin.
            var cancelledByUs = _logFollow.IsCancellationRequested;
            _logFollow.Dispose();
            _logFollow = null;
            _followedContainerId = null;
            if (cancelledByUs && ShouldFollowLogs && !Workspace.Lifetime.IsCancellationRequested)
            {
                StartFollow();
            }
        }
    }

    [RelayCommand]
    private void CopyLogs()
    {
        if (LogLines.Count == 0) return;
        Clipboard.SetText(string.Join(Environment.NewLine, LogLines.Select(line => line.Text)));
    }

    [RelayCommand] private void ClearLogs() => LogLines.Clear();

    [RelayCommand]
    private void CopyNetworkValue(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) Clipboard.SetText(value);
    }

    [RelayCommand]
    private Task RefreshNetworkDetailsAsync() => LoadNetworkDetailsAsync(force: true);

    [RelayCommand]
    private void CopyMountValue(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) Clipboard.SetText(value);
    }

    [RelayCommand]
    private Task RefreshMountDetailsAsync() => LoadMountDetailsAsync(force: true);

    private void AppendLogLine(string line)
    {
        LogLines.Add(new LogLine(line));
        TrimLogLines();
    }

    private void TrimLogLines()
    {
        if (LogLines.Count <= MaxLogLines) return;
        var excess = LogLines.Count - MaxLogLines + LogTrimBatch;
        for (var i = 0; i < excess && LogLines.Count > 0; i++)
        {
            LogLines.RemoveAt(0);
        }
    }

    [RelayCommand]
    private async Task InspectContainerAsync()
    {
        await LoadInspectDetailsAsync(force: true);
    }

    [RelayCommand]
    private void CopyConfigurationValue(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && value != "--") Clipboard.SetText(value);
    }

    [RelayCommand]
    private async Task ExecAsync()
    {
        if (SelectedContainer is null || string.IsNullOrWhiteSpace(ExecText)) return;
        var result = await Workspace.RunTrackedAsync("Execute command", (progress, token) => Workspace.Runtime.ExecAsync(SelectedContainer.Id, ExecText, progress, token));
        Workspace.ShowResult(result);
    }

    [RelayCommand]
    private void OpenTerminal()
    {
        if (SelectedContainer is not null) Workspace.Runtime.OpenInteractiveTerminal(SelectedContainer.Id);
    }

    [RelayCommand]
    private async Task ExportContainerAsync()
    {
        if (SelectedContainer is null) return;
        var containerId = SelectedContainer.Id;
        var path = Workspace.Interaction.PickSaveFile("Export container", "Tar archive (*.tar)|*.tar", $"{SelectedContainer.Name}.tar");
        if (path is null) return;
        var restartAfterExport = SelectedContainer.IsRunning;
        if (restartAfterExport)
        {
            if (!await Workspace.Interaction.ConfirmAsync("Export container", "WSLC requires the container to be stopped for export. Stop it temporarily and restart it afterwards?")) return;
            var stop = await Workspace.Runtime.StopContainerAsync(containerId, Workspace.Lifetime.Token);
            if (!stop.Success)
            {
                Workspace.ShowResult(stop);
                return;
            }
        }

        var export = await Workspace.RunTrackedAsync("Export container", (progress, token) => Workspace.Runtime.ExportContainerAsync(containerId, path, progress, token));
        Workspace.ShowResult(export);
        if (restartAfterExport)
        {
            var start = await Workspace.Runtime.StartContainerAsync(containerId, Workspace.Lifetime.Token);
            if (!start.Success) Workspace.ShowResult(start);
            await Workspace.RefreshAllAsync();
            InvalidateNetworkDetails(containerId);
        }
    }

    [RelayCommand]
    private void ShowCreateContainer()
    {
        SelectedContainer = null;
        IsCreatingContainer = true;
    }

    [RelayCommand]
    private void ShowContainerList()
    {
        SelectedContainer = null;
        IsCreatingContainer = false;
    }

    [RelayCommand] private void CancelCreateContainer() => IsCreatingContainer = false;

    partial void OnSearchTextChanged(string value) => ApplyContainerFilter();
    partial void OnContainerFilterIndexChanged(int value) => ApplyContainerFilter();

    partial void OnSelectedContainerChanged(ContainerSummary? value)
    {
        if (value is not null) IsCreatingContainer = false;

        // Auto-refresh replaces the selected container with a fresh instance of the same container;
        // treat that as "no real change" so live logs aren't wiped and following isn't restarted on
        // every refresh cycle.
        var isSameContainer = value is not null && value.Id == _currentContainerId;
        _currentContainerId = value?.Id;
        if (!isSameContainer)
        {
            LogLines.Clear();
            SelectedDetailTabIndex = LogsTabIndex;
            _networkDetailsLoad?.Cancel();
            _mountDetailsLoad?.Cancel();
            _inspectDetailsLoad?.Cancel();
            NetworkDetailsError = string.Empty;
            NetworkDetailsUpdatedAt = null;
            MountDetailsError = string.Empty;
            InspectDetailsError = string.Empty;
            InspectDetailsUpdatedAt = null;
            if (value is not null && _networkDetailsCache.TryGetValue(value.Id, out var cachedDetails))
            {
                NetworkDetails = cachedDetails;
            }
            else
            {
                NetworkDetails = null;
            }

            if (value is not null && _mountDetailsCache.TryGetValue(value.Id, out var cachedMountDetails))
            {
                MountDetails = cachedMountDetails;
            }
            else
            {
                MountDetails = null;
            }

            if (value is not null && _inspectDetailsCache.TryGetValue(value.Id, out var cachedInspectDetails))
            {
                InspectDetails = cachedInspectDetails;
                InspectOutput = cachedInspectDetails.RawJson;
            }
            else
            {
                InspectDetails = null;
                InspectOutput = string.Empty;
            }
        }
        EvaluateFollow();
        OnPropertyChanged(nameof(SelectedContainerStats));
    }

    partial void OnNetworkDetailsChanged(ContainerNetworkDetails? value) => OnPropertyChanged(nameof(HasNetworkDetails));

    partial void OnNetworkDetailsErrorChanged(string value) => OnPropertyChanged(nameof(HasNetworkDetailsError));

    partial void OnMountDetailsChanged(ContainerMountDetails? value) => OnPropertyChanged(nameof(HasMountDetails));

    partial void OnMountDetailsErrorChanged(string value) => OnPropertyChanged(nameof(HasMountDetailsError));

    partial void OnInspectDetailsChanged(ContainerInspectDetails? value) => OnPropertyChanged(nameof(HasInspectDetails));

    partial void OnInspectDetailsErrorChanged(string value) => OnPropertyChanged(nameof(HasInspectDetailsError));

    private async Task LoadInspectDetailsAsync(bool force)
    {
        if (IsDesignMode || SelectedContainer is not { } container) return;

        if (_inspectDetailsLoad is not null &&
            string.Equals(_inspectDetailsLoadContainerId, container.Id, StringComparison.OrdinalIgnoreCase))
        {
            if (!force && !_inspectDetailsLoad.IsCancellationRequested) return;
            _inspectDetailsLoad.Cancel();
        }

        if (!force && _inspectDetailsCache.TryGetValue(container.Id, out var cachedDetails))
        {
            InspectDetails = cachedDetails;
            InspectOutput = cachedDetails.RawJson;
            InspectDetailsError = string.Empty;
            return;
        }

        var load = CancellationTokenSource.CreateLinkedTokenSource(Workspace.Lifetime.Token);
        _inspectDetailsLoad = load;
        _inspectDetailsLoadContainerId = container.Id;
        IsInspectDetailsLoading = true;
        InspectDetailsError = string.Empty;
        try
        {
            var result = await Workspace.Runtime.InspectContainerAsync(container.Id, load.Token);
            if (load.IsCancellationRequested || !IsCurrentInspectContainer(container.Id)) return;

            if (!result.Success)
            {
                InspectDetailsError = string.IsNullOrWhiteSpace(result.Error)
                    ? "WSLC could not load container configuration."
                    : result.Error;
                return;
            }

            if (!ContainerInspectDetailsParser.TryParse(result.Output, out var details))
            {
                InspectDetailsError = "WSLC returned unsupported container configuration data.";
                return;
            }

            _inspectDetailsCache[container.Id] = details;
            InspectDetails = details;
            InspectOutput = details.RawJson;
            InspectDetailsUpdatedAt = DateTimeOffset.Now;
        }
        catch (OperationCanceledException) when (load.IsCancellationRequested)
        {
            // The user selected a different container or left Configuration/Inspect.
        }
        catch (Exception exception)
        {
            if (!load.IsCancellationRequested &&
                ReferenceEquals(_inspectDetailsLoad, load) &&
                IsCurrentInspectContainer(container.Id))
            {
                InspectDetailsError = exception.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(_inspectDetailsLoad, load))
            {
                _inspectDetailsLoad = null;
                _inspectDetailsLoadContainerId = null;
                IsInspectDetailsLoading = false;
            }

            load.Dispose();
        }
    }

    private bool IsCurrentInspectContainer(string containerId) =>
        SelectedDetailTabIndex is ConfigurationTabIndex or InspectTabIndex &&
        SelectedContainer is { } container &&
        container.Id.Equals(containerId, StringComparison.OrdinalIgnoreCase);

    private async Task LoadNetworkDetailsAsync(bool force)
    {
        if (IsDesignMode || SelectedContainer is not { } container) return;

        if (_networkDetailsLoad is not null &&
            string.Equals(_networkDetailsLoadContainerId, container.Id, StringComparison.OrdinalIgnoreCase))
        {
            if (!force && !_networkDetailsLoad.IsCancellationRequested) return;
            _networkDetailsLoad.Cancel();
        }

        if (!force && _networkDetailsCache.TryGetValue(container.Id, out var cachedDetails))
        {
            NetworkDetails = cachedDetails;
            NetworkDetailsError = string.Empty;
            return;
        }

        var load = CancellationTokenSource.CreateLinkedTokenSource(Workspace.Lifetime.Token);
        _networkDetailsLoad = load;
        _networkDetailsLoadContainerId = container.Id;
        IsNetworkDetailsLoading = true;
        NetworkDetailsError = string.Empty;
        try
        {
            var result = await Workspace.Runtime.InspectContainerAsync(container.Id, load.Token);
            if (load.IsCancellationRequested || !IsCurrentNetworkContainer(container.Id)) return;

            if (!result.Success)
            {
                NetworkDetailsError = string.IsNullOrWhiteSpace(result.Error)
                    ? "WSLC could not load network details."
                    : result.Error;
                return;
            }

            if (!ContainerNetworkDetailsParser.TryParse(result.Output, out var details))
            {
                NetworkDetailsError = "WSLC returned unsupported network detail data.";
                return;
            }

            _networkDetailsCache[container.Id] = details;
            NetworkDetails = details;
            NetworkDetailsUpdatedAt = DateTimeOffset.Now;
        }
        catch (OperationCanceledException) when (load.IsCancellationRequested)
        {
            // The user selected a different container or left the Network tab.
        }
        catch (Exception exception)
        {
            if (!load.IsCancellationRequested &&
                ReferenceEquals(_networkDetailsLoad, load) &&
                IsCurrentNetworkContainer(container.Id))
            {
                NetworkDetailsError = exception.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(_networkDetailsLoad, load))
            {
                _networkDetailsLoad = null;
                _networkDetailsLoadContainerId = null;
                IsNetworkDetailsLoading = false;
            }

            load.Dispose();
        }
    }

    private bool IsCurrentNetworkContainer(string containerId) =>
        SelectedDetailTabIndex == NetworkTabIndex &&
        SelectedContainer is { } container &&
        container.Id.Equals(containerId, StringComparison.OrdinalIgnoreCase);

    private async Task LoadMountDetailsAsync(bool force)
    {
        if (IsDesignMode || SelectedContainer is not { } container) return;

        if (_mountDetailsLoad is not null &&
            string.Equals(_mountDetailsLoadContainerId, container.Id, StringComparison.OrdinalIgnoreCase))
        {
            if (!force && !_mountDetailsLoad.IsCancellationRequested) return;
            _mountDetailsLoad.Cancel();
        }

        if (!force && _mountDetailsCache.TryGetValue(container.Id, out var cachedDetails))
        {
            // A cancelled refresh can still be winding down if the runtime ignores cancellation.
            // The cached value is already usable, so do not leave the page in a loading state.
            IsMountDetailsLoading = false;
            MountDetails = cachedDetails;
            MountDetailsError = string.Empty;
            return;
        }

        var load = CancellationTokenSource.CreateLinkedTokenSource(Workspace.Lifetime.Token);
        _mountDetailsLoad = load;
        _mountDetailsLoadContainerId = container.Id;
        IsMountDetailsLoading = true;
        MountDetailsError = string.Empty;
        try
        {
            var result = await Workspace.Runtime.InspectContainerAsync(container.Id, load.Token);
            if (load.IsCancellationRequested || !IsCurrentMountContainer(container.Id)) return;

            if (!result.Success)
            {
                MountDetailsError = string.IsNullOrWhiteSpace(result.Error)
                    ? "WSLC could not load mount details."
                    : result.Error;
                return;
            }

            if (!ContainerMountDetailsParser.TryParse(result.Output, out var details))
            {
                MountDetailsError = "WSLC returned unsupported mount detail data.";
                return;
            }

            _mountDetailsCache[container.Id] = details;
            MountDetails = details;
        }
        catch (OperationCanceledException) when (load.IsCancellationRequested)
        {
            // The user selected a different container or left the Mounts tab.
        }
        catch (Exception exception)
        {
            if (!load.IsCancellationRequested &&
                ReferenceEquals(_mountDetailsLoad, load) &&
                IsCurrentMountContainer(container.Id))
            {
                MountDetailsError = exception.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(_mountDetailsLoad, load))
            {
                _mountDetailsLoad = null;
                _mountDetailsLoadContainerId = null;
                IsMountDetailsLoading = false;
            }

            load.Dispose();
        }
    }

    private bool IsCurrentMountContainer(string containerId) =>
        SelectedDetailTabIndex == MountsTabIndex &&
        SelectedContainer is { } container &&
        container.Id.Equals(containerId, StringComparison.OrdinalIgnoreCase);

    private async Task RunContainerActionAsync(string title, Func<string, Task<OperationResult>> operation)
    {
        if (SelectedContainer is null) return;
        var containerId = SelectedContainer.Id;
        var result = await operation(containerId);
        Workspace.ShowResult(result);
        await Workspace.RefreshAllAsync();
        if (result.Success) InvalidateNetworkDetails(containerId);
    }

    private async Task RunContainerActionAsync(ContainerSummary? container, Func<string, Task<OperationResult>> operation)
    {
        if (container is null) return;
        SelectedContainer = null;
        var result = await operation(container.Id);
        Workspace.ShowResult(result);
        await Workspace.RefreshAllAsync();
        if (result.Success) _networkDetailsCache.Remove(container.Id);
    }

    private ContainerCreateSpec BuildCreateSpec()
    {
        if (string.IsNullOrWhiteSpace(NewImage)) throw new ArgumentException("Image is required.");
        var spec = new ContainerCreateSpec
        {
            Image = NewImage.Trim(),
            Name = NewContainerName.Trim(),
            Command = NewCommand,
            CpuLimit = NewCpuLimit.Trim(),
            MemoryLimit = NewMemoryLimit.Trim(),
            Network = NewNetwork.Trim(),
            User = NewUser.Trim(),
            WorkingDirectory = NewWorkingDirectory.Trim(),
            UseAllGpus = NewUseAllGpus,
            RemoveWhenStopped = NewRemoveWhenStopped
        };
        foreach (var line in StringSplitter.SplitValues(NewEnvironment))
        {
            var index = line.IndexOf('=');
            if (index > 0) spec.Environment.Add(new(line[..index].Trim(), line[(index + 1)..]));
        }

        spec.Ports.AddRange(StringSplitter.SplitValues(NewPorts));
        spec.Volumes.AddRange(StringSplitter.SplitValues(NewVolumes));
        return spec;
    }

    private void OnWorkspaceRefreshed(object? sender, EventArgs eventArgs)
    {
        var selectedBeforeRefresh = SelectedContainer;
        ApplyContainerFilter();
        RestoreSelectedContainerIfUnchanged(selectedBeforeRefresh);
        var existingContainerIds = Workspace.Containers.Select(container => container.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var containerId in _networkDetailsCache.Keys.Where(containerId => !existingContainerIds.Contains(containerId)).ToArray())
        {
            _networkDetailsCache.Remove(containerId);
        }
        foreach (var containerId in _mountDetailsCache.Keys.Where(containerId => !existingContainerIds.Contains(containerId)).ToArray())
        {
            _mountDetailsCache.Remove(containerId);
        }
        foreach (var containerId in _inspectDetailsCache.Keys.Where(containerId => !existingContainerIds.Contains(containerId)).ToArray())
        {
            _inspectDetailsCache.Remove(containerId);
        }
        OnPropertyChanged(nameof(SelectedContainerStats));
    }

    private void InvalidateNetworkDetails(string containerId)
    {
        _networkDetailsCache.Remove(containerId);
        if (SelectedContainer is not { } container || !container.Id.Equals(containerId, StringComparison.OrdinalIgnoreCase)) return;

        NetworkDetails = null;
        NetworkDetailsUpdatedAt = null;
        NetworkDetailsError = string.Empty;
        if (SelectedDetailTabIndex == NetworkTabIndex) _ = LoadNetworkDetailsAsync(force: true);
    }

    private void ApplyContainerFilter()
    {
        var query = SearchText.Trim();
        var visible = Workspace.Containers.Where(container =>
            (ContainerFilterIndex == 0 ||
             ContainerFilterIndex == 1 && container.IsRunning ||
             ContainerFilterIndex == 2 && !container.IsRunning) &&
            (string.IsNullOrEmpty(query) ||
             container.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             container.Image.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             container.Id.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();

        VisibleContainerItems.ReplaceAll(visible.Select(container => new ContainerListItem
        {
            Container = container,
            Stats = Workspace.FindStats(container)
        }));
    }

    private void RestoreSelectedContainerIfUnchanged(ContainerSummary? previousSelection)
    {
        if (previousSelection is null) return;
        if (!ReferenceEquals(SelectedContainer, previousSelection)) return;

        SelectedContainer = VisibleContainerItems.Select(item => item.Container).FirstOrDefault(container =>
            MatchesContainerIdentity(container, previousSelection));
    }

    private static bool MatchesContainerIdentity(ContainerSummary candidate, ContainerSummary selection)
    {
        if (!string.IsNullOrWhiteSpace(selection.Id) &&
            candidate.Id.Equals(selection.Id, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(selection.Name) &&
               candidate.Name.Equals(selection.Name, StringComparison.OrdinalIgnoreCase);
    }
}
