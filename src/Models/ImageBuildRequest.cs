namespace ExWSLC.Models;

public enum ImageBuildOutput { LocalImage, Tar }
public enum ImageBuildProgress { Auto, Plain, Quiet }
public enum BuildSecretSource { File, Environment }

// References only: secret values are never form fields or application preferences.
public sealed record ImageBuildSecret(string Id, BuildSecretSource SourceType, string Source);

public sealed record ImageBuildRequest
{
    public string ContextPath { get; init; } = string.Empty;
    public string Tag { get; init; } = string.Empty;
    public string Dockerfile { get; init; } = string.Empty;
    public IReadOnlyList<string> BuildArguments { get; init; } = [];
    public string Target { get; init; } = string.Empty;
    public bool NoCache { get; init; }
    public bool Pull { get; init; }
    public ImageBuildOutput Output { get; init; }
    public string OutputPath { get; init; } = string.Empty;
    public ImageBuildProgress? Progress { get; init; }
    public IReadOnlyList<ImageBuildSecret> Secrets { get; init; } = [];
}
