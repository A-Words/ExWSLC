using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExWSLC.Models;
using System.Collections.ObjectModel;

namespace ExWSLC.ViewModels;

public class TasksPageViewModel(RuntimeWorkspace workspace) : ObservableObject
{
    public RuntimeWorkspace Workspace { get; } = workspace;
    public ObservableCollection<RuntimeTaskItem> Tasks => Workspace.Tasks;
    public IRelayCommand ClearTasksCommand => Workspace.ClearTasksCommand;
    public IRelayCommand CancelCurrentOperationCommand => Workspace.CancelCurrentOperationCommand;
}
