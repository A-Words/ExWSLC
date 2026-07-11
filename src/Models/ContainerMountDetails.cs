namespace ExWSLC.Models;

/// <summary>
/// The mounts reported by <c>wslc container inspect</c> for one container.
/// </summary>
public sealed record ContainerMountDetails(IReadOnlyList<ContainerMount> Mounts)
{
    public bool HasMounts => Mounts.Count > 0;
}

/// <summary>
/// One mount from a container inspect response. WSLC 2.9.3 reports bind and tmpfs mounts;
/// the volume kind is retained for forward compatibility.
/// </summary>
public sealed record ContainerMount(
    string Type,
    string Source,
    string Destination,
    bool? ReadWrite)
{
    public ContainerMountKind Kind => Type.Trim().ToLowerInvariant() switch
    {
        "bind" => ContainerMountKind.Bind,
        "tmpfs" => ContainerMountKind.Tmpfs,
        "volume" => ContainerMountKind.Volume,
        _ => ContainerMountKind.Unknown
    };

    public ContainerMountAccess Access => ReadWrite switch
    {
        true => ContainerMountAccess.ReadWrite,
        false => ContainerMountAccess.ReadOnly,
        null => ContainerMountAccess.Unknown
    };

    public bool HasSource => !string.IsNullOrWhiteSpace(Source);
    public bool HasDestination => !string.IsNullOrWhiteSpace(Destination);
    public string DisplaySource => HasSource ? Source : "-";
    public string DisplayDestination => HasDestination ? Destination : "-";
}

public enum ContainerMountKind
{
    Unknown,
    Bind,
    Tmpfs,
    Volume
}

public enum ContainerMountAccess
{
    Unknown,
    ReadOnly,
    ReadWrite
}
