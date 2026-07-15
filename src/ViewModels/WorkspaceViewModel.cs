using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ExWSLC.ViewModels;

public abstract class WorkspaceViewModel : ObservableObject
{
    protected WorkspaceViewModel(RuntimeWorkspace workspace)
    {
        Workspace = workspace;
        Workspace.PropertyChanged += OnWorkspacePropertyChanged;
    }

    public RuntimeWorkspace Workspace { get; }
    public string DetailOutput { get => Workspace.DetailOutput; set => Workspace.DetailOutput = value; }
    public string RefreshError => Workspace.RefreshError;
    public bool HasRefreshError => Workspace.HasRefreshError;
    public IAsyncRelayCommand RefreshAllCommand => Workspace.RefreshAllCommand;

    protected virtual void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(RuntimeWorkspace.DetailOutput))
        {
            OnPropertyChanged(nameof(DetailOutput));
        }
        else if (eventArgs.PropertyName is nameof(RuntimeWorkspace.RefreshError))
        {
            OnPropertyChanged(nameof(RefreshError));
            OnPropertyChanged(nameof(HasRefreshError));
        }
    }
}
