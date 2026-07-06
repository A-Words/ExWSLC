namespace ExWSLC.Models;

public sealed class ContainerCreateSpec
{
    public string Image { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string CpuLimit { get; set; } = string.Empty;
    public string MemoryLimit { get; set; } = string.Empty;
    public string Network { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public bool UseAllGpus { get; set; }
    public bool RemoveWhenStopped { get; set; }
    public List<KeyValuePair<string, string>> Environment { get; } = [];
    public List<string> Ports { get; } = [];
    public List<string> Volumes { get; } = [];
}
