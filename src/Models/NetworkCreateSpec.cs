namespace ExWSLC.Models;

public sealed class NetworkCreateSpec
{
    public string Name { get; set; } = string.Empty;
    public string Driver { get; set; } = "bridge";
    public List<string> DriverOptions { get; } = [];
    public List<string> Labels { get; } = [];
}
