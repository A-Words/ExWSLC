using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExWSLC.Models;

namespace ExWSLC.ViewModels;

public partial class ContainersViewModel : WorkspaceViewModel
{
    private const string DefaultImage = "hello-world:latest";
    private const string DefaultExecCommand = "uname -a";

    private CancellationTokenSource? _logFollow;

    public ContainersViewModel(RuntimeWorkspace workspace) : base(workspace)
    {
        Workspace.Refreshed += OnWorkspaceRefreshed;
    }
    public ObservableCollection<ContainerListItem> VisibleContainerItems { get; } = [];

    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
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

    public ContainerStats? SelectedContainerStats => SelectedContainer is null ? null : Workspace.FindStats(SelectedContainer);

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
        if (container is null || !Workspace.Interaction.Confirm("Remove container", $"Permanently remove {container.Name}?")) return;
        SelectedContainer = null;
        Workspace.ShowResult(await Workspace.Runtime.RemoveContainerAsync(container.Id, true, Workspace.Lifetime.Token));
        await Workspace.RefreshAllAsync();
    }

    [RelayCommand]
    private async Task KillContainerAsync()
    {
        if (SelectedContainer is null || !Workspace.Interaction.Confirm("Force stop", $"Send SIGKILL to {SelectedContainer.Name}?")) return;
        await RunContainerActionAsync("Kill container", id => Workspace.Runtime.KillContainerAsync(id, Workspace.Lifetime.Token));
    }

    [RelayCommand]
    private async Task RemoveContainerAsync()
    {
        if (SelectedContainer is null || !Workspace.Interaction.Confirm("Remove container", $"Permanently remove {SelectedContainer.Name}?")) return;
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
            Workspace.Interaction.ShowError("Invalid container", exception.Message);
        }
    }

    [RelayCommand]
    private async Task ShowLogsAsync()
    {
        if (SelectedContainer is null) return;
        Workspace.ShowResult(await Workspace.Runtime.GetLogsAsync(SelectedContainer.Id, cancellationToken: Workspace.Lifetime.Token));
    }

    [RelayCommand]
    private async Task FollowLogsAsync()
    {
        if (SelectedContainer is null || _logFollow is not null) return;
        _logFollow = CancellationTokenSource.CreateLinkedTokenSource(Workspace.Lifetime.Token);
        Workspace.DetailOutput = string.Empty;
        OnPropertyChanged(nameof(DetailOutput));
        var progress = new Progress<string>(line =>
        {
            Workspace.DetailOutput += line + Environment.NewLine;
            OnPropertyChanged(nameof(DetailOutput));
        });
        try
        {
            var result = await Workspace.Runtime.FollowLogsAsync(SelectedContainer.Id, progress, _logFollow.Token);
            if (result.ExitCode != -2) Workspace.ShowResult(result);
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
        Workspace.ShowResult(await Workspace.Runtime.InspectContainerAsync(SelectedContainer.Id, Workspace.Lifetime.Token));
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
        var path = Workspace.Interaction.PickSaveFile("Export container", "Tar archive (*.tar)|*.tar", $"{SelectedContainer.Name}.tar");
        if (path is null) return;
        var restartAfterExport = SelectedContainer.IsRunning;
        if (restartAfterExport)
        {
            if (!Workspace.Interaction.Confirm("Export container", "WSLC requires the container to be stopped for export. Stop it temporarily and restart it afterwards?")) return;
            var stop = await Workspace.Runtime.StopContainerAsync(SelectedContainer.Id, Workspace.Lifetime.Token);
            if (!stop.Success)
            {
                Workspace.ShowResult(stop);
                return;
            }
        }

        var export = await Workspace.RunTrackedAsync("Export container", (progress, token) => Workspace.Runtime.ExportContainerAsync(SelectedContainer.Id, path, progress, token));
        Workspace.ShowResult(export);
        if (restartAfterExport)
        {
            var start = await Workspace.Runtime.StartContainerAsync(SelectedContainer.Id, Workspace.Lifetime.Token);
            if (!start.Success) Workspace.ShowResult(start);
            await Workspace.RefreshAllAsync();
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

    partial void OnSelectedContainerChanged(ContainerSummary? value)
    {
        if (value is not null) IsCreatingContainer = false;
        OnPropertyChanged(nameof(SelectedContainerStats));
    }

    private async Task RunContainerActionAsync(string title, Func<string, Task<OperationResult>> operation)
    {
        if (SelectedContainer is null) return;
        Workspace.ShowResult(await operation(SelectedContainer.Id));
        await Workspace.RefreshAllAsync();
    }

    private async Task RunContainerActionAsync(ContainerSummary? container, Func<string, Task<OperationResult>> operation)
    {
        if (container is null) return;
        SelectedContainer = null;
        Workspace.ShowResult(await operation(container.Id));
        await Workspace.RefreshAllAsync();
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
        foreach (var line in RuntimeWorkspace.SplitValues(NewEnvironment))
        {
            var index = line.IndexOf('=');
            if (index > 0) spec.Environment.Add(new(line[..index].Trim(), line[(index + 1)..]));
        }

        spec.Ports.AddRange(RuntimeWorkspace.SplitValues(NewPorts));
        spec.Volumes.AddRange(RuntimeWorkspace.SplitValues(NewVolumes));
        return spec;
    }

    private void OnWorkspaceRefreshed(object? sender, EventArgs eventArgs)
    {
        var selectedBeforeRefresh = SelectedContainer;
        ApplyContainerFilter();
        RestoreSelectedContainerIfUnchanged(selectedBeforeRefresh);
        OnPropertyChanged(nameof(SelectedContainerStats));
    }

    private void ApplyContainerFilter()
    {
        var query = SearchText.Trim();
        var visible = Workspace.Containers.Where(container => string.IsNullOrEmpty(query) ||
            container.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            container.Image.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            container.Id.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();

        RuntimeWorkspace.Replace(VisibleContainerItems, visible.Select(container => new ContainerListItem
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
