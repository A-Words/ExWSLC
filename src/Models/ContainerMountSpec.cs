namespace ExWSLC.Models;

public sealed record ContainerMountSpec(ContainerMountKind Kind, string Source, string Target, bool ReadOnly = false);
