namespace ExWSLC.Services;

public interface IUserInteractionService
{
    bool Confirm(string title, string message);
    void ShowError(string title, string message);
    string? PickOpenFile(string title, string filter);
    string? PickSaveFile(string title, string filter, string defaultName);
    string? PickFolder(string title);
}
