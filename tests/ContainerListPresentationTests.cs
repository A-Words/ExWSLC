using ExWSLC.Models;
using ExWSLC.Views.Pages.Containers;

namespace ExWSLC.Tests;

public class ContainerListPresentationTests
{
    [Theory]
    [InlineData("nginx", "nginx", "")]
    [InlineData("nginx:alpine", "nginx:alpine", "")]
    [InlineData("docker.1ms.run/nginx", "nginx", "docker.1ms.run")]
    [InlineData("localhost:5000/team/api:v2", "api:v2", "localhost:5000/team")]
    [InlineData("ghcr.io/team/api@sha256:abc123", "api@sha256:abc123", "ghcr.io/team")]
    public void ImageHierarchy_PreservesTagDigestAndRepository(string image, string name, string source)
    {
        var item = CreateItem(image: image);
        Assert.Equal(name, item.ImageName);
        Assert.Equal(source, item.ImageSource);
        Assert.Equal(image, item.Image);
    }

    [Theory]
    [InlineData("127.0.0.1:8081->80/tcp", "8081 → 80/tcp", "127.0.0.1")]
    [InlineData("[::1]:8081->80/tcp", "8081 → 80/tcp", "[::1]")]
    [InlineData(":::5353->53/udp", "5353 → 53/udp", "::")]
    [InlineData("0.0.0.0:8000-8005->80-85/tcp", "8000-8005 → 80-85/tcp", "0.0.0.0")]
    [InlineData("8080:80", "8080 → 80", "")]
    [InlineData("80/tcp", "80/tcp", "")]
    [InlineData("unrecognized payload", "unrecognized payload", "")]
    [InlineData("[{broken", "[{broken", "")]
    public void PortSummary_SeparatesBindingWithoutLosingOriginalText(string ports, string summary, string address)
    {
        var display = CreateItem(ports: ports).ListPorts;
        Assert.Equal(summary, display.Summary);
        Assert.Equal(address, display.BindingAddress);
        Assert.Equal(ports, display.FullText);
        Assert.False(display.HasAdditionalMappings);
    }

    [Fact]
    public void PortSummary_PreservesAllDistinctMappingsIncludingDifferentBindings()
    {
        var display = CreateItem(ports: "0.0.0.0:8080->80/tcp, [::]:8080->80/tcp, 53/udp").ListPorts;
        Assert.Equal("8080 → 80/tcp", display.Summary);
        Assert.Equal(2, display.AdditionalCount);
        Assert.True(display.HasAdditionalMappings);
        Assert.Equal(3, display.Mappings.Count);
        Assert.Contains("[::]:8080->80/tcp", display.FullText);
        Assert.Contains("53/udp", display.FullText);
    }

    [Fact]
    public void StructuredPorts_PreserveAddressAndReportedProtocolsWithoutInventingDefaults()
    {
        var display = CreateItem(ports: """
            {"ports":[
                {"BindingAddress":"127.0.0.1","ContainerPort":80,"HostPort":8080,"Protocol":6},
                {"IP":"::","PrivatePort":53,"PublicPort":5353,"Type":"udp"},
                {"ContainerPort":443,"HostPort":8443}
            ]}
            """).ListPorts;
        Assert.Equal("8080 → 80/tcp", display.Summary);
        Assert.Equal("127.0.0.1", display.BindingAddress);
        Assert.Equal("5353 → 53/udp", display.Mappings[1].Mapping);
        Assert.Equal("::", display.Mappings[1].BindingAddress);
        Assert.Equal("8443 → 443", display.Mappings[2].Mapping);
        Assert.Equal(2, display.AdditionalCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void MissingPorts_HaveNoAdditionalMappings(string ports)
    {
        var display = CreateItem(ports: ports).ListPorts;
        Assert.Equal("-", display.Summary);
        Assert.Empty(display.Mappings);
        Assert.False(display.HasAdditionalMappings);
    }

    [Theory]
    [InlineData(820)]
    [InlineData(850)]
    [InlineData(1000)]
    [InlineData(1099)]
    [InlineData(1100)]
    [InlineData(1400)]
    public void Layout_FitsViewportAndKeepsCpuAndMemorySeparate(double viewportWidth)
    {
        var layout = ContainerListLayout.FromViewport(viewportWidth);
        Assert.Equal(86, layout.Cpu);
        Assert.Equal(106, layout.Memory);
        Assert.Equal(viewportWidth - 28,
            layout.Name + layout.Image + layout.Ports + layout.Cpu + layout.Memory + layout.Actions, 5);
        Assert.True(layout.Name >= 160);
        Assert.True(layout.Image >= 132);
        Assert.True(layout.Ports >= 144);
        Assert.Equal(88, layout.Actions);
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(632, true)]
    [InlineData(743, true)]
    [InlineData(744, false)]
    [InlineData(820, false)]
    [InlineData(1100, false)]
    public void Layout_ReservesActionsAndScrollsColumnsThatDoNotFit(double viewportWidth, bool overflows)
    {
        var layout = ContainerListLayout.FromViewport(viewportWidth);
        Assert.Equal(88, layout.Actions);
        Assert.Equal(viewportWidth - 28, layout.RowWidth, 5);
        Assert.Equal(overflows, layout.HasHorizontalOverflow);
        Assert.Equal(86, layout.Cpu);
        Assert.Equal(106, layout.Memory);
        Assert.Equal(layout.DataWidth, layout.DataViewport + layout.HorizontalScrollRange, 5);
        Assert.True(layout.Name >= 160);
        Assert.True(layout.Image >= 132);
        Assert.True(layout.Ports >= 144);
    }

    [Theory]
    [InlineData(ContainerHealthStatus.NotConfigured, ContainerListStatus.RunningWithoutHealthCheck)]
    [InlineData(ContainerHealthStatus.Healthy, ContainerListStatus.Healthy)]
    [InlineData(ContainerHealthStatus.Unhealthy, ContainerListStatus.Unhealthy)]
    [InlineData(ContainerHealthStatus.Starting, ContainerListStatus.Checking)]
    [InlineData(ContainerHealthStatus.Unknown, ContainerListStatus.HealthUnknown)]
    public void RunningIndicator_ReflectsHealthWithoutTreatingUnknownAsHealthy(ContainerHealthStatus health, ContainerListStatus expected)
    {
        foreach (var state in new[] { "running", "Running", ContainerState.CodeRunning })
        {
            var item = new ContainerListItem { Container = new("id", "web", "nginx", state, "", "-", "now", health) };
            Assert.Equal(expected, item.StatusKind);
        }
    }

    [Theory]
    [InlineData("created", ContainerListStatus.Created)]
    [InlineData("1", ContainerListStatus.Created)]
    [InlineData("Exited", ContainerListStatus.Exited)]
    [InlineData("3", ContainerListStatus.Exited)]
    [InlineData("stopped", ContainerListStatus.Stopped)]
    [InlineData("deleted", ContainerListStatus.Deleted)]
    [InlineData("4", ContainerListStatus.Deleted)]
    [InlineData("Invalid", ContainerListStatus.Invalid)]
    [InlineData("0", ContainerListStatus.Invalid)]
    [InlineData("future-state", ContainerListStatus.Other)]
    [InlineData("", ContainerListStatus.Other)]
    public void InactiveIndicator_PreservesLifecycleStateAndIgnoresStaleHealth(string state, ContainerListStatus expected)
    {
        foreach (var health in Enum.GetValues<ContainerHealthStatus>())
        {
            var item = new ContainerListItem { Container = new("id", "web", "nginx", state, "", "-", "now", health) };
            Assert.Equal(expected, item.StatusKind);
        }
    }

    [Fact]
    public void RunningStatusFallback_StillUsesHealthWhenTheStateFieldIsMissing()
    {
        var item = new ContainerListItem { Container = new("id", "web", "nginx", "", "Up 2 minutes", "-", "now", ContainerHealthStatus.Unhealthy) };
        Assert.Equal(ContainerListStatus.Unhealthy, item.StatusKind);
    }

    [Theory]
    [InlineData("created")]
    [InlineData(ContainerState.CodeCreated)]
    [InlineData("stopped")]
    [InlineData("Exited")]
    [InlineData(ContainerState.CodeExited)]
    [InlineData("deleted")]
    [InlineData(ContainerState.CodeDeleted)]
    [InlineData("invalid")]
    [InlineData(ContainerState.CodeInvalid)]
    public void InactiveMetrics_ShowDashForMissingZeroOrStaleStats(string state)
    {
        ContainerStats?[] samples = [null,
            new("id", "web", "0.00%", "0B", "", "", ""),
            new("id", "web", "4.2%", "96 MiB / 8 GiB", "", "", "")];
        foreach (var stats in samples)
        {
            var item = new ContainerListItem { Container = new("id", "web", "nginx", state, "", "-", "now"), Stats = stats };
            Assert.Equal("-", item.Cpu);
            Assert.Equal("-", item.Memory);
        }
    }

    [Theory]
    [InlineData("running", "")]
    [InlineData("Running", "")]
    [InlineData(ContainerState.CodeRunning, "")]
    [InlineData("", "Up 2 minutes")]
    public void RunningMetrics_PreserveUsageAndMissingSamplePlaceholder(string state, string status)
    {
        var container = new ContainerSummary("id", "web", "nginx", state, status, "-", "now");
        var item = new ContainerListItem { Container = container, Stats = new("id", "web", "4.2%", "96 MiB / 8 GiB", "", "", "") };
        Assert.Equal("4.2%", item.Cpu);
        Assert.Equal("96 MiB", item.Memory);

        var missing = new ContainerListItem { Container = container };
        Assert.Equal("--", missing.Cpu);
        Assert.Equal("--", missing.Memory);
    }

    private static ContainerListItem CreateItem(string image = "nginx", string ports = "-") => new()
    {
        Container = new ContainerSummary("abc", "web", image, "running", "Up", ports, "now")
    };
}
