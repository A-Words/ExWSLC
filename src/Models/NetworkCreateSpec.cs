namespace ExWSLC.Models;

public sealed class NetworkCreateSpec
{
    public string Name { get; set; } = string.Empty;
    public string Driver { get; set; } = "bridge";
    public string? Subnet { get; set; }
    public string? Gateway { get; set; }
    public string? IpRange { get; set; }
    public List<string> DriverOptions { get; } = [];
    public List<string> Labels { get; } = [];
}
