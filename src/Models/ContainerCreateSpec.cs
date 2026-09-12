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
    public ContainerPullPolicy PullPolicy { get; set; }
    public string? StopSignal { get; set; }
    public int? StopTimeoutSeconds { get; set; }
    public List<ContainerMountSpec> Mounts { get; } = [];
    public HealthCheckMode HealthMode { get; set; }
    public string? HealthCommand { get; set; }
    public string? HealthInterval { get; set; }
    public string? HealthTimeout { get; set; }
    public string? HealthStartPeriod { get; set; }
    public string? HealthRetries { get; set; }
    public List<KeyValuePair<string, string>> Environment { get; } = [];
    public List<string> Ports { get; } = [];
    public List<string> Volumes { get; } = [];
}

public enum ContainerPullPolicy { Inherit, Always, Missing, Never }
