namespace ExWSLC.Models;

public sealed record VolumeSummary(string Name, string Driver, string Mountpoint, string Size)
{
    public string DisplayDriver => string.IsNullOrWhiteSpace(Driver) ? "-" : Driver;
    public string DisplayMountpoint => string.IsNullOrWhiteSpace(Mountpoint) ? "-" : Mountpoint;
    public string DisplaySize => string.IsNullOrWhiteSpace(Size) ? "-" : Size;
}
