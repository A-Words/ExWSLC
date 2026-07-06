namespace ExWSLC.Models;

public sealed class AppSettings
{
    public string Language { get; set; } = "zh-CN";
    public string Theme { get; set; } = "System";
    public int RefreshIntervalSeconds { get; set; } = 5;
}
