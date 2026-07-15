namespace ExWSLC.Models;

public sealed record NetworkSummary(
    string Id,
    string Name,
    string Driver,
    string Scope,
    string Subnet,
    string Gateway)
{
    public string DisplayId => string.IsNullOrWhiteSpace(Id) ? "-" : Id;
    public string DisplayDriver => string.IsNullOrWhiteSpace(Driver) ? "-" : Driver;
    public string DisplayScope => string.IsNullOrWhiteSpace(Scope) ? "-" : Scope;
    public string DisplaySubnet => string.IsNullOrWhiteSpace(Subnet) ? "-" : Subnet;
    public string DisplayGateway => string.IsNullOrWhiteSpace(Gateway) ? "-" : Gateway;
}
