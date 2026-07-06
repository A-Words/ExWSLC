namespace ExWSLC.Models;

public sealed record RuntimeCapabilities(
    bool IsAvailable,
    string CliVersion,
    string SdkVersion,
    IReadOnlyList<string> MissingComponents,
    string Message)
{
    public static RuntimeCapabilities Unavailable(string message) =>
        new(false, "Unavailable", "Unavailable", Array.Empty<string>(), message);
}
