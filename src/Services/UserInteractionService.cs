using System.Windows;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace ExWSLC.Services;

public sealed class UserInteractionService : IUserInteractionService
{
    public async Task<bool> ConfirmAsync(string title, string message)
    {
        var messageBox = new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = message,
            PrimaryButtonText = "Yes",
            CloseButtonText = "No"
        };
        var result = await messageBox.ShowDialogAsync();
        return result == MessageBoxResult.Primary;
    }

    public async Task ShowErrorAsync(string title, string message)
    {
        var messageBox = new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK"
        };
        await messageBox.ShowDialogAsync();
    }

    public string? PickOpenFile(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickSaveFile(string title, string filter, string defaultName)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = defaultName };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
