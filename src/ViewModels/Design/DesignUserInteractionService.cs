using ExWSLC.Services;

namespace ExWSLC.ViewModels.Design;

internal sealed class DesignUserInteractionService : IUserInteractionService
{
    public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
    public Task ShowErrorAsync(string title, string message) => Task.CompletedTask;
    public string? PickOpenFile(string title, string filter) => null;
    public string? PickSaveFile(string title, string filter, string defaultName) => null;
    public string? PickFolder(string title) => null;
}
