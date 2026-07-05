using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExWSLC.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace ExWSLC.ViewModels;

public class OverviewPageViewModel : ObservableObject
{
    public OverviewPageViewModel(RuntimeWorkspace workspace, ContainersPageViewModel containersPage)
    {
        Workspace = workspace;
        ContainersPage = containersPage;
        Workspace.PropertyChanged += OnWorkspacePropertyChanged;
    }

    public RuntimeWorkspace Workspace { get; }
    public ContainersPageViewModel ContainersPage { get; }
    public ObservableCollection<ContainerSummary> ActiveContainers => Workspace.ActiveContainers;
    public ObservableCollection<RuntimeTaskItem> RecentTasks => Workspace.RecentTasks;
    public RuntimeTaskItem? ActiveTask => Workspace.ActiveTask;
    public RuntimeCapabilities Capabilities => Workspace.Capabilities;
    public int RunningContainerCount => Workspace.RunningContainerCount;
    public int StoppedContainerCount => Workspace.StoppedContainerCount;
    public int ImageCount => Workspace.ImageCount;
    public int NetworkCount => Workspace.NetworkCount;
    public int VolumeCount => Workspace.VolumeCount;
    public int ActiveTaskCount => Workspace.ActiveTaskCount;
    public IAsyncRelayCommand RefreshAllCommand => Workspace.RefreshAllCommand;
    public IRelayCommand CancelCurrentOperationCommand => Workspace.CancelCurrentOperationCommand;

    public void ShowCreateContainer() => ContainersPage.ShowCreateContainerCommand.Execute(null);

    public void SelectContainer(ContainerSummary container)
    {
        ContainersPage.IsCreatingContainer = false;
        ContainersPage.SelectedContainer = container;
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is
            nameof(RuntimeWorkspace.ActiveTask) or
            nameof(RuntimeWorkspace.Capabilities) or
            nameof(RuntimeWorkspace.RunningContainerCount) or
            nameof(RuntimeWorkspace.StoppedContainerCount) or
            nameof(RuntimeWorkspace.ImageCount) or
            nameof(RuntimeWorkspace.NetworkCount) or
            nameof(RuntimeWorkspace.VolumeCount) or
            nameof(RuntimeWorkspace.ActiveTaskCount))
        {
            OnPropertyChanged(eventArgs.PropertyName);
        }
    }
}
