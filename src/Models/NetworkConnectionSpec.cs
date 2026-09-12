namespace ExWSLC.Models;

public sealed record NetworkConnectionSpec(
    string ContainerId,
    string NetworkName,
    string? Ipv4Address = null,
    IReadOnlyList<string>? Aliases = null,
    IReadOnlyList<string>? DriverOptions = null);

public sealed record NetworkDisconnectionSpec(string ContainerId, string NetworkName);
