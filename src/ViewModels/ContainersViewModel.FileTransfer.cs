using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExWSLC.Helpers;
using ExWSLC.Models;

namespace ExWSLC.ViewModels;

public partial class ContainersViewModel
{
    [ObservableProperty] public partial int CopyDirectionIndex { get; set; }
    [ObservableProperty] public partial string CopyLocalPath { get; set; } = string.Empty;
    [ObservableProperty] public partial string CopyContainerPath { get; set; } = string.Empty;
    [ObservableProperty] public partial string CopyMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial string CopyOutput { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsCopyPending { get; set; }
    [ObservableProperty] public partial bool IsCopyRunning { get; set; }

    public bool IsCopyUpload => CopyDirectionIndex == (int)ContainerCopyDirection.Upload;
    public bool IsCopyDownload => CopyDirectionIndex == (int)ContainerCopyDirection.Download;
    public bool IsCopyUnavailable => Workspace.Capabilities[RuntimeFeature.ContainerCopy].Support != CapabilitySupport.Supported;
    public bool CanEditCopy => !IsCopyPending && !IsCopyUnavailable;
    private bool CanStartCopy() => CanEditCopy && !Workspace.IsBusy && SelectedContainer is not null &&
        !string.IsNullOrWhiteSpace(CopyLocalPath) && !string.IsNullOrWhiteSpace(CopyContainerPath);

    partial void OnCopyDirectionIndexChanged(int value)
    {
        CopyLocalPath = string.Empty;
        CopyContainerPath = string.Empty;
        OnPropertyChanged(nameof(IsCopyUpload));
        OnPropertyChanged(nameof(IsCopyDownload));
        NotifyCopyCommands();
    }
    partial void OnCopyLocalPathChanged(string value) => NotifyCopyCommands();
    partial void OnCopyContainerPathChanged(string value) => NotifyCopyCommands();
    partial void OnIsCopyPendingChanged(bool value) => NotifyCopyCommands();

    private void NotifyCopyCommands()
    {
        OnPropertyChanged(nameof(IsCopyUnavailable));
        OnPropertyChanged(nameof(CanEditCopy));
        StartCopyCommand.NotifyCanExecuteChanged();
        PickCopyFileCommand.NotifyCanExecuteChanged();
        PickCopyFolderCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanEditCopy))]
    private void PickCopyFile()
    {
        if (!CanEditCopy || !IsCopyUpload) return;
        var path = Workspace.Interaction.PickOpenFile(ContainerCopyOptions.Text("CopyChooseFile", "Choose source file"),
            ContainerCopyOptions.Text("CopyAllFiles", "All files (*.*)|*.*"));
        if (path is not null) CopyLocalPath = path;
    }

    [RelayCommand(CanExecute = nameof(CanEditCopy))]
    private void PickCopyFolder()
    {
        if (!CanEditCopy) return;
        var path = Workspace.Interaction.PickFolder(IsCopyUpload
            ? ContainerCopyOptions.Text("CopyChooseSourceFolder", "Choose source directory")
            : ContainerCopyOptions.Text("CopyChooseDestination", "Choose destination directory"));
        if (path is not null) CopyLocalPath = path;
    }

    [RelayCommand(CanExecute = nameof(CanStartCopy))]
    private async Task StartCopyAsync()
    {
        if (!CanStartCopy() || SelectedContainer is not { } container) return;
        // Capture before confirmation: changing selection or form inputs never retargets this operation.
        var request = new ContainerCopyRequest((ContainerCopyDirection)CopyDirectionIndex, container.Id, CopyLocalPath, CopyContainerPath);
        var remote = $"{container.Name} ({container.ShortId}):{request.ContainerPath}";
        var source = request.Direction == ContainerCopyDirection.Upload ? request.LocalPath : remote;
        var destination = request.Direction == ContainerCopyDirection.Upload ? remote : request.LocalPath;
        var summary = string.Format(ContainerCopyOptions.Text("CopyEndpoints", "Source: {0}\nDestination directory: {1}"), source, destination);
        var title = ContainerCopyOptions.Text("CopyTitle", "File transfer") + " / " + container.Name;
        var acceptingOutput = true;
        IsCopyPending = true;
        CopyOutput = string.Empty;
        CopyMessage = string.Empty;
        try
        {
            ContainerCopyOptions.Validate(request);
            var semantics = ContainerCopyOptions.Text("CopySemantics", "Copies the source with its original name into the destination directory. Existing directories are merged and same-name files may be replaced without another prompt. Links are copied as links; Windows may reject them. Cancellation can leave partial files and does not roll back changes.");
            if (!await Workspace.Interaction.ConfirmAsync(title, summary + "\n\n" + semantics)) return;
            if (Workspace.IsBusy || IsCopyUnavailable)
                throw new ArgumentException(ContainerCopyOptions.Text("CopyBusyOrUnavailable", "Another task is running or copy support changed. Wait or recheck capabilities, then try again."));
            IsCopyRunning = true;
            CopyMessage = summary + "\n" + ContainerCopyOptions.Text("CopyRunning", "Transferring… No percentage is available.");
            var result = await Workspace.RunTrackedAsync(title, async (progress, token) =>
            {
                var copyProgress = new Progress<string>(line =>
                {
                    progress.Report(line);
                    if (IsCopyRunning && acceptingOutput)
                    {
                        var output = CopyOutput + line + Environment.NewLine;
                        CopyOutput = output.Length > 16000 ? output[^16000..] : output;
                    }
                });
                return await Workspace.Runtime.CopyContainerPathAsync(request, copyProgress, token);
            });
            if (result.ExitCode == -2) throw new OperationCanceledException();
            acceptingOutput = false;
            IsCopyRunning = false;
            CopyOutput = result.CombinedOutput;
            CopyMessage = summary + "\n" + (result.Success
                ? ContainerCopyOptions.Text("CopySucceeded", "Transfer completed. Verify the destination files.")
                : ContainerCopyOptions.Text("CopyFailed", "Transfer failed. Check source access and destination directory permissions. Partial destination files may remain."));
        }
        catch (OperationCanceledException)
        {
            CopyMessage = summary + "\n" + ContainerCopyOptions.Text("CopyCancelled", "Transfer cancelled. Partial or overwritten destination files may remain; no rollback was performed.");
        }
        catch (Exception exception)
        {
            CopyMessage = summary + "\n" + exception.Message;
        }
        finally
        {
            acceptingOutput = false;
            IsCopyRunning = false;
            IsCopyPending = false;
        }
    }
}
