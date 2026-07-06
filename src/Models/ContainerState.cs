namespace ExWSLC.Models;

public static class ContainerState
{
    public const string Invalid = "Invalid";
    public const string Created = "Created";
    public const string Running = "Running";
    public const string Exited = "Exited";
    public const string Deleted = "Deleted";

    // Raw numeric codes from WSLC CLI
    public const string CodeInvalid = "0";
    public const string CodeCreated = "1";
    public const string CodeRunning = "2";
    public const string CodeExited = "3";
    public const string CodeDeleted = "4";
}
