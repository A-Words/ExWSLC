namespace ExWSLC.Models;

public sealed class VolumeCreateSpec
{
    public string Name { get; set; } = string.Empty;
    public string Driver { get; set; } = "guest";
    public List<string> DriverOptions { get; } = [];
    public List<string> Labels { get; } = [];
}
