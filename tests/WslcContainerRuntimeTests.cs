using ExWSLC.Helpers;
using ExWSLC.Models;
using ExWSLC.Services;
using System.Text.Json;

namespace ExWSLC.Tests;

public class WslcContainerRuntimeTests
{
    [Fact]
    public void BuildRunArguments_MapsEverySupportedOptionAsSeparateArgument()
    {
        var spec = new ContainerCreateSpec
        {
            Image = "nginx:latest", Name = "web app", Command = "nginx -g 'daemon off;'",
            CpuLimit = "1.5", MemoryLimit = "512M", Network = "frontend", User = "1000:1000",
            WorkingDirectory = "/srv/app", UseAllGpus = true, RemoveWhenStopped = true
        };
        spec.Environment.Add(new("GREETING", "hello world; rm -rf /"));
        spec.Ports.Add("8080:80");
        spec.Volumes.Add("cache:/var/cache/nginx");

        var arguments = WslcContainerRuntime.BuildRunArguments(spec);

        Assert.Equal("run", arguments[0]);
        Assert.Contains("web app", arguments);
        Assert.Contains("GREETING=hello world; rm -rf /", arguments);
        Assert.Contains("8080:80", arguments);
        Assert.Contains("cache:/var/cache/nginx", arguments);
        Assert.Equal(["/bin/sh", "-lc", spec.Command], arguments.TakeLast(3));
    }

    [Fact]
    public void BuildRunArguments_RequiresImage()
    {
        Assert.Throws<ArgumentException>(() => WslcContainerRuntime.BuildRunArguments(new ContainerCreateSpec()));
    }

    [Fact]
    public void BuildCreateNetworkArguments_MapsDriverOptionsAndLabelsAsSeparateArguments()
    {
        var spec = new NetworkCreateSpec
        {
            Name = "frontend",
            Driver = "custom-driver"
        };
        spec.DriverOptions.Add("mtu=1500");
        spec.DriverOptions.Add("isolation=strict");
        spec.Labels.Add("environment=development");
        spec.Labels.Add("owner=platform team");

        var arguments = WslcContainerRuntime.BuildCreateNetworkArguments(spec);

        Assert.Equal([
            "network", "create",
            "--driver", "custom-driver",
            "--opt", "mtu=1500",
            "--opt", "isolation=strict",
            "--label", "environment=development",
            "--label", "owner=platform team",
            "frontend"
        ], arguments);
    }

    [Fact]
    public void BuildCreateNetworkArguments_RequiresNameAndOmitsBlankOptionalValues()
    {
        Assert.Throws<ArgumentException>(() =>
            WslcContainerRuntime.BuildCreateNetworkArguments(new NetworkCreateSpec()));

        var spec = new NetworkCreateSpec { Name = "isolated", Driver = string.Empty };
        spec.DriverOptions.Add(" ");
        spec.Labels.Add(string.Empty);

        Assert.Equal(["network", "create", "isolated"], WslcContainerRuntime.BuildCreateNetworkArguments(spec));
    }

    [Fact]
    public void BuildCreateVolumeArguments_MapsEverySupportedOptionAndAllowsAnonymousVolumes()
    {
        var spec = new VolumeCreateSpec { Name = " app-data ", Driver = "vhd" };
        spec.DriverOptions.Add("size=2GB");
        spec.DriverOptions.Add("dynamic=true");
        spec.Labels.Add("environment=development");

        Assert.Equal([
            "volume", "create",
            "--driver", "vhd",
            "--opt", "size=2GB",
            "--opt", "dynamic=true",
            "--label", "environment=development",
            "app-data"
        ], WslcContainerRuntime.BuildCreateVolumeArguments(spec));

        Assert.Equal(
            ["volume", "create", "--driver", "guest"],
            WslcContainerRuntime.BuildCreateVolumeArguments(new VolumeCreateSpec()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildRemoveVolumeArguments_MapsForceOption(bool force)
    {
        string[] expected = force
            ? ["volume", "remove", "--force", "app-data"]
            : ["volume", "remove", "app-data"];

        Assert.Equal(expected, WslcContainerRuntime.BuildRemoveVolumeArguments(" app-data ", force));
        Assert.Throws<ArgumentException>(() => WslcContainerRuntime.BuildRemoveVolumeArguments(" ", force));
    }

    [Fact]
    public void BuildPruneVolumeArguments_MapsAllAndRepeatedFilters()
    {
        var spec = new VolumePruneSpec { All = true };
        spec.Filters.Add("label=environment=development");
        spec.Filters.Add("label!=keep");

        Assert.Equal([
            "volume", "prune", "--all",
            "--filter", "label=environment=development",
            "--filter", "label!=keep"
        ], WslcContainerRuntime.BuildPruneVolumeArguments(spec));
    }

    [Theory]
    [InlineData("image")]
    [InlineData("network")]
    [InlineData("volume")]
    public void BuildPruneArguments_OmitsUnsupportedForceOption(string resource)
    {
        Assert.Equal([resource, "prune"], WslcContainerRuntime.BuildPruneArguments(resource));
    }

    [Fact]
    public void BuildPruneArguments_RequiresResource()
    {
        Assert.Throws<ArgumentException>(() => WslcContainerRuntime.BuildPruneArguments(" "));
    }

    [Fact]
    public void ParseArray_AcceptsWrappedPayloadAndUnknownFields()
    {
        const string json = """
            { "containers": [{ "ID": "abc123", "Names": "web", "Image": "nginx", "State": "running", "FutureField": 42 }] }
            """;
        var result = new OperationResult(true, 0, json, string.Empty, "wslc list");

        var containers = WslcContainerRuntime.ParseArray(result, element => new ContainerSummary(
            element.ReadString("Id", "ID"), element.ReadString("Name", "Names"),
            element.ReadString("Image"), element.ReadString("State"), element.ReadString("Status"),
            element.ReadString("Ports"), element.ReadString("Created")));

        var container = Assert.Single(containers);
        Assert.Equal("abc123", container.Id);
        Assert.Equal("web", container.Name);
        Assert.True(container.IsRunning);
    }

    [Fact]
    public void ParseArray_ReturnsEmptyForMalformedJson()
    {
        var result = new OperationResult(true, 0, "not-json", string.Empty, "wslc list");
        Assert.Empty(WslcContainerRuntime.ParseArray(result, element => element.ToString()));
    }

    [Fact]
    public void ParseArrayOrThrow_ReportsFailedAndMalformedInventory()
    {
        var failed = new OperationResult(false, 7, string.Empty, "network service unavailable", "wslc network list");
        var failure = Assert.Throws<InvalidOperationException>(() =>
            WslcContainerRuntime.ParseArrayOrThrow(failed, element => element.ToString(), "network list"));
        Assert.Equal("network service unavailable", failure.Message);

        var malformed = new OperationResult(true, 0, "not-json", string.Empty, "wslc network list");
        var malformedFailure = Assert.Throws<InvalidOperationException>(() =>
            WslcContainerRuntime.ParseArrayOrThrow(malformed, element => element.ToString(), "network list"));
        Assert.Equal("WSLC network list returned invalid JSON.", malformedFailure.Message);

        var empty = new OperationResult(true, 0, "[]", string.Empty, "wslc network list");
        Assert.Empty(WslcContainerRuntime.ParseArrayOrThrow(empty, element => element.ToString(), "network list"));
    }

    [Fact]
    public void ParseNetworkSummary_ReadsNestedIpamConfiguration()
    {
        using var document = JsonDocument.Parse("""
            {
              "ID": "network-id",
              "Name": "frontend",
              "Driver": "bridge",
              "Scope": "local",
              "IPAM": {
                "Config": [
                  { "Subnet": "172.20.0.0/16", "Gateway": "172.20.0.1" }
                ]
              }
            }
            """);

        var network = WslcContainerRuntime.ParseNetworkSummary(document.RootElement);

        Assert.Equal("network-id", network.Id);
        Assert.Equal("frontend", network.Name);
        Assert.Equal("bridge", network.Driver);
        Assert.Equal("local", network.Scope);
        Assert.Equal("172.20.0.0/16", network.Subnet);
        Assert.Equal("172.20.0.1", network.Gateway);
    }

    [Fact]
    public void ParseNetworkSummary_PrefersFlatFieldsAndHandlesMissingValues()
    {
        using var document = JsonDocument.Parse("""
            {
              "Id": "network-id",
              "Name": "isolated",
              "Driver": "bridge",
              "Subnet": "10.0.0.0/24",
              "Gateway": "10.0.0.1",
              "IPAM": { "Subnet": "ignored", "Gateway": "ignored" }
            }
            """);

        var network = WslcContainerRuntime.ParseNetworkSummary(document.RootElement);

        Assert.Equal("10.0.0.0/24", network.Subnet);
        Assert.Equal("10.0.0.1", network.Gateway);
        Assert.Equal("-", network.DisplayScope);
    }

    [Fact]
    public void ParseVolumeSummary_ReadsSizeAndMountPointVariants()
    {
        using var document = JsonDocument.Parse("""
            {
              "Name": "app-data",
              "Driver": "guest",
              "MountPoint": "/var/lib/wslc/volumes/app-data",
              "Size": "256 MB"
            }
            """);

        var volume = WslcContainerRuntime.ParseVolumeSummary(document.RootElement);

        Assert.Equal("app-data", volume.Name);
        Assert.Equal("guest", volume.Driver);
        Assert.Equal("/var/lib/wslc/volumes/app-data", volume.Mountpoint);
        Assert.Equal("256 MB", volume.Size);
    }

    [Theory]
    [InlineData("0", "Invalid")]
    [InlineData("1", "Created")]
    [InlineData("2", "Running")]
    [InlineData("3", "Exited")]
    [InlineData("4", "Deleted")]
    [InlineData("Paused", "Paused")]
    public void NormalizeContainerState_HandlesPreviewNumericEnum(string input, string expected)
    {
        Assert.Equal(expected, WslcContainerRuntime.NormalizeContainerState(input));
    }

    [Fact]
    public void BuildInteractiveTerminalStartInfo_UsesResolvedWslcPath()
    {
        const string wslcPath = @"C:\Program Files\WSL\wslc.exe";
        var startInfo = WslcContainerRuntime.BuildInteractiveTerminalStartInfo("container-id", wslcPath);

        Assert.Equal("wt.exe", startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
        Assert.Equal(wslcPath, startInfo.ArgumentList[0]);
        Assert.Equal(["exec", "--interactive", "--tty", "container-id", "/bin/sh"], startInfo.ArgumentList.Skip(1));
    }
}
