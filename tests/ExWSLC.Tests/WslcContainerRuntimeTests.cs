using ExWSLC.Models;
using ExWSLC.Services;

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
}
