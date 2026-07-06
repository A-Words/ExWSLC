using ExWSLC.Services;

namespace ExWSLC.ViewModels.Design;

internal sealed class DesignUserInteractionService : IUserInteractionService
{
    public bool Confirm(string title, string message) => true;
    public void ShowError(string title, string message) { }
    public string? PickOpenFile(string title, string filter) => null;
    public string? PickSaveFile(string title, string filter, string defaultName) => null;
    public string? PickFolder(string title) => null;
}
