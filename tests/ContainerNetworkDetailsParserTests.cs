using ExWSLC.Helpers;
using ExWSLC.Models;

namespace ExWSLC.Tests;

public class ContainerNetworkDetailsParserTests
{
    [Fact]
    public void TryParse_ReadsDynamicNetworkAndPortKeys()
    {
        const string inspect = """
            {
              "Config": { "Hostname": "api-gateway" },
              "HostConfig": { "NetworkMode": "bridge" },
              "State": { "Running": true },
              "NetworkSettings": {
                "Networks": {
                  "edge": {
                    "NetworkID": "network-123",
                    "EndpointID": "endpoint-456",
                    "Gateway": "172.20.0.1",
                    "IPAddress": "172.20.0.2",
                    "IPPrefixLen": 16,
                    "MacAddress": "02:42:ac:14:00:02",
                    "Aliases": ["api", "gateway"]
                  }
                }
              },
              "Ports": {
                "80/tcp": [
                  { "HostIp": "127.0.0.1", "HostPort": "8080" }
                ],
                "443/tcp": [
                  { "HostIp": "0.0.0.0", "HostPort": "8443" }
                ]
              }
            }
            """;

        var parsed = ContainerNetworkDetailsParser.TryParse(inspect, out var details);

        Assert.True(parsed);
        Assert.Equal("bridge", details.NetworkMode);
        Assert.Equal("api-gateway", details.HostName);
        Assert.True(details.IsRunning is true);
        var network = Assert.Single(details.Networks);
        Assert.Equal("edge", network.Name);
        Assert.Equal("172.20.0.2/16", network.DisplayIpAddress);
        Assert.Equal("172.20.0.1", network.Gateway);
        Assert.Equal("network-123", network.NetworkId);
        Assert.Equal("endpoint-456", network.EndpointId);
        Assert.Equal(["api", "gateway"], network.Aliases);
        Assert.Equal(2, details.PortBindings.Count);
        Assert.Equal("127.0.0.1:8080", details.PortBindings[0].DisplayHostEndpoint);
        Assert.Equal("80/TCP", details.PortBindings[0].DisplayContainerEndpoint);
        Assert.Equal(ContainerPortExposure.LocalOnly, details.PortBindings[0].Exposure);
        Assert.Equal(ContainerPortExposure.PotentiallyExposed, details.PortBindings[1].Exposure);
    }

    [Fact]
    public void TryParse_PreservesConfiguredNetworkWhenStoppedAddressesAreUnavailable()
    {
        const string inspect = """
            {
              "HostConfig": { "NetworkMode": "bridge" },
              "State": { "Running": false },
              "NetworkSettings": {
                "Networks": {
                  "bridge": {
                    "Gateway": "",
                    "IPAddress": "",
                    "MacAddress": ""
                  }
                }
              },
              "Ports": {}
            }
            """;

        var parsed = ContainerNetworkDetailsParser.TryParse(inspect, out var details);

        Assert.True(parsed);
        Assert.True(details.HasNetworks);
        Assert.False(details.HasPortBindings);
        Assert.True(details.RuntimeAddressesUnavailable);
        var network = Assert.Single(details.Networks);
        Assert.Equal("bridge", network.Name);
        Assert.Equal("-", network.DisplayIpAddress);
        Assert.Equal("-", network.DisplayNetworkId);
        Assert.Equal("-", network.DisplayEndpointId);
    }

    [Fact]
    public void TryParse_ReturnsFalseForMalformedInspectPayload()
    {
        Assert.False(ContainerNetworkDetailsParser.TryParse("not-json", out _));
    }

    [Fact]
    public void ContainerPortBinding_FormatsIpv6HostEndpointsUnambiguously()
    {
        var binding = new ContainerPortBinding("::1", "8080", "80", "tcp");
        var loopbackBinding = new ContainerPortBinding("127.0.0.2", "8081", "80", "tcp");

        Assert.Equal("[::1]:8080", binding.DisplayHostEndpoint);
        Assert.Equal(ContainerPortExposure.LocalOnly, binding.Exposure);
        Assert.Equal(ContainerPortExposure.LocalOnly, loopbackBinding.Exposure);
    }
}
