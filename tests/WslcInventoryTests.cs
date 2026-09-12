using ExWSLC.Models;
using ExWSLC.Services;
using ExWSLC.ViewModels;
using Moq;

namespace ExWSLC.Tests;

public class WslcInventoryTests
{
    private const string Record = """{"ID":"id-one","Name":"name-one","State":"running","FutureField":{"items":[]}}""";

    public static TheoryData<string, string[]> Commands => new()
    {
        { "containers", ["container", "list", "--all", "--no-trunc", "--format", "json"] },
        { "images", ["image", "list", "--no-trunc", "--format", "json"] },
        { "networks", ["network", "list", "--format", "json"] },
        { "volumes", ["volume", "list", "--format", "json"] },
        { "stats", ["stats", "--all", "--no-trunc", "--format", "json"] }
    };

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task List_AcceptsLegacyDocumentsAndNewJsonLines(string resource, string[] arguments)
    {
        string[] payloads =
        [
            $"[\n  {Record}\n]",
            $"{{\"{resource}\":[\n  {Record}\n],\"total\":1}}",
            $"{{\"Items\":[{Record}]}}",
            $"{{\"data\":[{Record}]}}",
            Record,
            """{ "ID": "id-one", "Name": "name-one", "Ports": [], "data": [42] }"""
        ];

        foreach (var payload in payloads)
        {
            var runner = CreateRunner(Success(payload));
            var rows = await ReadAsync(new WslcContainerRuntime(runner.Object), resource, TestContext.Current.CancellationToken);

            Assert.Single(rows);
            runner.Verify(value => value.ExecuteAsync("wslc.exe",
                It.Is<IReadOnlyList<string>>(actual => actual.SequenceEqual(arguments)), null, null,
                TestContext.Current.CancellationToken), Times.Once);
            runner.VerifyNoOtherCalls();
        }

        var second = Record.Replace("id-one", "id-two").Replace("name-one", "name-two");
        var linesRunner = CreateRunner(Success($"\r\n{Record}\r\n \t\r\n{second}\r\n"));
        var lines = await ReadAsync(new WslcContainerRuntime(linesRunner.Object), resource, TestContext.Current.CancellationToken);
        Assert.Equal(2, lines.Length);
        Assert.NotEqual(lines[0], lines[1]);
    }

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task List_AcceptsSuccessfulEmptyOutputAndCollections(string resource, string[] _)
    {
        foreach (var payload in new[] { "", " \r\n\t", "[]", $"{{\"{resource}\":[]}}" })
        {
            var runtime = new WslcContainerRuntime(CreateRunner(Success(payload)).Object);
            Assert.Empty(await ReadAsync(runtime, resource, TestContext.Current.CancellationToken));
        }
    }

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task List_RejectsFailedCommandsEvenWithValidOrEmptyOutput(string resource, string[] _)
    {
        foreach (var payload in new[] { "", Record, "[]" })
        {
            var runtime = new WslcContainerRuntime(CreateRunner(new OperationResult(
                false, 7, payload, "service unavailable", "ignored")).Object);
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ReadAsync(runtime, resource, TestContext.Current.CancellationToken));
            Assert.Contains("exit code 7", failure.Message);
            Assert.Contains("service unavailable", failure.Message);
        }
    }

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task List_RejectsMalformedOrInvalidRecordsWithoutReturningPartialResults(string resource, string[] _)
    {
        string[] payloads =
        [
            "not-json", "null", "42", "{}", "{\"error\":\"unavailable\"}",
            $"{{\"{resource}\":{{}}}}",
            $"[{Record},null]",
            $"[{Record},{{}}]",
            $"{Record}\n{{\"Name\":",
            $"{Record}\n[]",
            $"{Record}\n{{\"ID\":{{}},\"Name\":{{}}}}",
            """{"ID":"id-one","Name":"name-one","Driver":{},"Image":{},"Memory":{},"Size":{}}"""
        ];
        foreach (var payload in payloads)
        {
            var runtime = new WslcContainerRuntime(CreateRunner(Success(payload)).Object);
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ReadAsync(runtime, resource, TestContext.Current.CancellationToken));
            Assert.Contains("WSLC", failure.Message);
            Assert.DoesNotContain(Record, failure.Message);
        }
    }

    [Fact]
    public async Task List_ReportsTheBrokenLineAndDoesNotIncludeInventoryValues()
    {
        var runtime = new WslcContainerRuntime(CreateRunner(Success($"{Record}\n\n{{secret-value" )).Object);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.GetContainersAsync(TestContext.Current.CancellationToken));

        Assert.Contains("container list", failure.Message);
        Assert.Contains("line 3", failure.Message);
        Assert.DoesNotContain("secret-value", failure.Message);
    }

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task List_PropagatesCancellationInsteadOfReturningAnEmptyInventory(string resource, string[] _)
    {
        var runtime = new WslcContainerRuntime(CreateRunner(new OperationResult(
            false, -2, Record, "Operation cancelled.", "ignored")).Object);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            ReadAsync(runtime, resource, TestContext.Current.CancellationToken));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        runtime = new WslcContainerRuntime(CreateRunner(Success("[]")).Object);
        var failure = await Assert.ThrowsAsync<OperationCanceledException>(() => ReadAsync(runtime, resource, cancellation.Token));
        Assert.Equal(cancellation.Token, failure.CancellationToken);
    }

    [Fact]
    public async Task Containers_MapsLegacyNumericStateStructuredPortsAndCurrentDockerFields()
    {
        const string legacy = """
            [{"Id":"old-id","Name":"old-name","Image":"sample:1","State":2,"CreatedAt":1780000000,
              "Ports":[{"ContainerPort":80,"HostPort":8080,"Protocol":6}]}]
            """;
        var oldRuntime = new WslcContainerRuntime(CreateRunner(Success(legacy)).Object);
        var old = Assert.Single(await oldRuntime.GetContainersAsync(TestContext.Current.CancellationToken));
        Assert.Equal("old-id", old.Id);
        Assert.Equal("old-name", old.Name);
        Assert.Equal("Running", old.State);
        Assert.Equal("1780000000", old.Created);
        Assert.Equal("8080:80", old.DisplayPorts);
        Assert.Empty(old.Status);

        const string current = """
            {"CreatedSince":"a minute ago","CreatedAt":"2026-09-12T09:00:00Z","ID":"new-id","Names":"new-name",
             "Image":"sample:2","Ports":"0.0.0.0:8080->80/tcp","State":"running","Status":"Up a minute","Platform":{"OS":"linux"}}
            """;
        var newRuntime = new WslcContainerRuntime(CreateRunner(Success(current)).Object);
        var currentContainer = Assert.Single(await newRuntime.GetContainersAsync(TestContext.Current.CancellationToken));
        Assert.Equal("new-id", currentContainer.Id);
        Assert.Equal("new-name", currentContainer.Name);
        Assert.Equal("sample:2", currentContainer.Image);
        Assert.Equal("running", currentContainer.State);
        Assert.Equal("Up a minute", currentContainer.Status);
        Assert.Equal("2026-09-12T09:00:00Z", currentContainer.Created);
        Assert.Equal("0.0.0.0:8080->80/tcp", currentContainer.DisplayPorts);
    }

    [Fact]
    public async Task Images_MapsNumericLegacyValuesAndPrefersAbsoluteDateWithEmptyAliasFallback()
    {
        const string payload = """
            {"ImageId":"legacy-id","Repository":null,"Tag":null,"Created":1780000000,"Size":2048}
            {"CreatedSince":"yesterday","CreatedAt":"2026-09-11T09:00:00Z","Created":null,"ID":"current-id","Repository":"example","Tag":"latest","Size":"2 KiB"}
            """;
        var runtime = new WslcContainerRuntime(CreateRunner(Success(payload)).Object);
        var images = await runtime.GetImagesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new ImageSummary("legacy-id", "", "", "2048", "1780000000"), images[0]);
        Assert.Equal(new ImageSummary("current-id", "example", "latest", "2 KiB", "2026-09-11T09:00:00Z"), images[1]);
    }

    [Fact]
    public async Task Stats_MapsAliasesAndNumericPids()
    {
        const string payload = """
            {"ContainerId":"legacy-id","Name":"old","CPU":"1%","MemoryUsage":"1 MiB / 2 MiB","NetworkIo":"3 B / 4 B","BlockIo":"5 B / 6 B","Pids":"7"}
            {"ID":"current-id","Name":"new","CPUPerc":"2%","MemUsage":"2 MiB / 3 MiB","NetIO":"4 B / 5 B","BlockIO":"6 B / 7 B","PIDs":8}
            """;
        var runtime = new WslcContainerRuntime(CreateRunner(Success(payload)).Object);
        var stats = await runtime.GetStatsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new ContainerStats("legacy-id", "old", "1%", "1 MiB / 2 MiB", "3 B / 4 B", "5 B / 6 B", "7"), stats[0]);
        Assert.Equal(new ContainerStats("current-id", "new", "2%", "2 MiB / 3 MiB", "4 B / 5 B", "6 B / 7 B", "8"), stats[1]);
    }

    [Fact]
    public async Task NetworksAndVolumes_KeepMissingNewListDetailsEmpty()
    {
        var networksRuntime = new WslcContainerRuntime(CreateRunner(Success("""
            {"ID":"new-network","Name":"example","Driver":"bridge","Scope":"local","IPv4":"true","Labels":""}
            """)).Object);
        var network = Assert.Single(await networksRuntime.GetNetworksAsync(TestContext.Current.CancellationToken));
        Assert.Equal(new NetworkSummary("new-network", "example", "bridge", "local", "", ""), network);

        var volumesRuntime = new WslcContainerRuntime(CreateRunner(Success("""
            {"Name":"example-volume","Driver":"guest","Scope":"local","Mountpoint":""}
            """)).Object);
        var volume = Assert.Single(await volumesRuntime.GetVolumesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(new VolumeSummary("example-volume", "guest", "", ""), volume);
    }

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task RefreshAll_KeepsTheLastSnapshotOnInventoryFailureAndRecovers(string resource, string[] failedArguments)
    {
        var mode = "initial";
        var runner = new Mock<IProcessRunner>();
        runner.Setup(value => value.ExecuteAsync("wslc.exe", It.IsAny<IReadOnlyList<string>>(), null, null, It.IsAny<CancellationToken>()))
            .Returns((string _, IReadOnlyList<string> arguments, string? _, IProgress<string>? _, CancellationToken _) =>
                Task.FromResult(mode switch
                {
                    "failed" when arguments.SequenceEqual(failedArguments) => new OperationResult(false, 7, "", "service unavailable", "ignored"),
                    "malformed" when arguments.SequenceEqual(failedArguments) => Success($"{Record}\n{{broken"),
                    "cancelled" when arguments.SequenceEqual(failedArguments) => new OperationResult(false, -2, "", "cancelled", "ignored"),
                    "initial" => Success(Record),
                    _ => Success("")
                }));
        using var workspace = new RuntimeWorkspace(new WslcContainerRuntime(runner.Object),
            Mock.Of<IRuntimeCapabilityService>(), Mock.Of<ISettingsService>(), new TaskService(), Mock.Of<IUserInteractionService>());
        await workspace.RefreshAllAsync();
        Assert.False(workspace.HasRefreshError);
        var snapshot = Snapshot(workspace);

        foreach (var failure in new[] { "failed", "malformed", "cancelled" })
        {
            mode = failure;
            await workspace.RefreshAllAsync();
            Assert.Equal(snapshot, Snapshot(workspace));
            Assert.True(workspace.HasRefreshError);
            Assert.Contains(resource == "containers" ? "container list" : resource.TrimEnd('s'), workspace.RefreshError);
            Assert.False(workspace.IsBusy);
        }

        mode = "recovered";
        await workspace.RefreshAllAsync();
        Assert.Empty(Snapshot(workspace));
        Assert.False(workspace.HasRefreshError);
    }

    private static object[] Snapshot(RuntimeWorkspace workspace) =>
        workspace.Containers.Cast<object>().Concat(workspace.ActiveContainers).Concat(workspace.Images)
            .Concat(workspace.Networks).Concat(workspace.Volumes).Concat(workspace.Stats).ToArray();

    private static OperationResult Success(string output) => new(true, 0, output, "", "ignored");

    private static Mock<IProcessRunner> CreateRunner(OperationResult result)
    {
        var runner = new Mock<IProcessRunner>(MockBehavior.Strict);
        runner.Setup(value => value.ExecuteAsync("wslc.exe", It.IsAny<IReadOnlyList<string>>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return runner;
    }

    private static async Task<object[]> ReadAsync(IContainerRuntime runtime, string resource, CancellationToken cancellationToken) => resource switch
    {
        "containers" => (await runtime.GetContainersAsync(cancellationToken)).Cast<object>().ToArray(),
        "images" => (await runtime.GetImagesAsync(cancellationToken)).Cast<object>().ToArray(),
        "networks" => (await runtime.GetNetworksAsync(cancellationToken)).Cast<object>().ToArray(),
        "volumes" => (await runtime.GetVolumesAsync(cancellationToken)).Cast<object>().ToArray(),
        "stats" => (await runtime.GetStatsAsync(cancellationToken)).Cast<object>().ToArray(),
        _ => throw new ArgumentOutOfRangeException(nameof(resource))
    };
}
