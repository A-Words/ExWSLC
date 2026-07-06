namespace ExWSLC.Models;

public sealed record ContainerStats(
    string Id,
    string Name,
    string Cpu,
    string Memory,
    string NetworkIo,
    string BlockIo,
    string Pids);
