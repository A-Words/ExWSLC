namespace ExWSLC.Models;

public enum ContainerCopyDirection
{
    Upload,
    Download
}

/// <summary>Copies one file or directory, retaining its name, into an existing destination directory.</summary>
public sealed record ContainerCopyRequest(
    ContainerCopyDirection Direction,
    string ContainerId,
    string LocalPath,
    string ContainerPath);
