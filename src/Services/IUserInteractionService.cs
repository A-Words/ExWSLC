using ExWSLC.Models;

namespace ExWSLC.Services;

public interface IUserInteractionService
{
    Task<ContainerStopOptions?> PickContainerStopOptionsAsync(string containerName, RuntimeCapabilities capabilities);
    Task<bool> ConfirmAsync(string title, string message);
    Task ShowErrorAsync(string title, string message);
    string? PickOpenFile(string title, string filter);
    string? PickSaveFile(string title, string filter, string defaultName);
    string? PickFolder(string title);
}
